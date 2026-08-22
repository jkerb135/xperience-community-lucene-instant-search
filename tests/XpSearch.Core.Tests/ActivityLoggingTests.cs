using CMS.Activities;
using CMS.Helpers;
using CMS.Websites.Routing;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the consent gate of spec §9.1: the right activity for a consenting visitor, nothing at all
/// and no exception for a visitor who has not consented.
/// </summary>
[TestFixture]
internal sealed class ActivityLoggingTests
{
    private ICustomActivityLogger activities = null!;
    private ICurrentCookieLevelProvider cookieLevel = null!;
    private SearchActivityLogger logger = null!;

    /// <summary>Cookie level <em>Visitor</em>, the lowest level activities are logged at.</summary>
    private static int Visitor => Kentico.Web.Mvc.CookieLevel.Visitor.Level;

    /// <summary>Cookie level <em>Essential</em>, which means the visitor has not consented to tracking.</summary>
    private static int Essential => Kentico.Web.Mvc.CookieLevel.Essential.Level;

    [SetUp]
    public void CreateLogger()
    {
        activities = Substitute.For<ICustomActivityLogger>();
        cookieLevel = Substitute.For<ICurrentCookieLevelProvider>();
        logger = new SearchActivityLogger(activities, cookieLevel, NullLogger<SearchActivityLogger>.Instance);
    }

    [Test]
    public void ConsentedSearch_LogsTheQueryActivityWithTheQueryAsItsValue()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);

        logger.LogSearch("mugs", 7);

        activities.Received(1).Log(
            XpSearchActivityTypes.Query,
            Arg.Is<CustomActivityData>(data => data.ActivityValue == "mugs"));
    }

    [Test]
    public void ConsentedSearchWithoutResults_LogsTheNoResultsActivity()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);

        logger.LogSearch("nothing here", 0);

        activities.Received(1).Log(
            XpSearchActivityTypes.NoResults,
            Arg.Is<CustomActivityData>(data => data.ActivityValue == "nothing here"));
    }

    [Test]
    public void ConsentedClickAndConversion_LogTheDocumentedValueFormat()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);

        logger.LogClick("mugs", "doc-1", 3);
        logger.LogConversion("mugs", "doc-1");

        activities.Received(1).Log(XpSearchActivityTypes.Click, Arg.Is<CustomActivityData>(data => data.ActivityValue == "mugs | doc-1 | 3"));
        activities.Received(1).Log(XpSearchActivityTypes.Conversion, Arg.Is<CustomActivityData>(data => data.ActivityValue == "mugs | doc-1"));
    }

    [Test]
    public void VisitorWithoutConsent_IsNotLoggedAndNothingThrows()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Essential);

        // Calling them at all is the assertion: any exception here fails the test.
        logger.LogSearch("mugs", 7);
        logger.LogSearch("mugs", 0);
        logger.LogClick("mugs", "doc-1", 3);
        logger.LogConversion("mugs", "doc-1");

        activities.DidNotReceiveWithAnyArgs().Log(default!, default!);
    }

    [Test]
    public void NoRequestContext_IsNotLoggedAndNothingThrows()
    {
        // Outside a request - a worker thread, a startup task - the cookie level cannot be read at all.
        cookieLevel.GetCurrentCookieLevel().Returns(_ => throw new InvalidOperationException("No request."));

        logger.LogSearch("mugs", 7);

        activities.DidNotReceiveWithAnyArgs().Log(default!, default!);
    }

    [Test]
    public void FailingActivityLogger_DoesNotSurfaceToTheCaller()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);
        activities.WhenForAnyArgs(logger => logger.Log(default!, default!)).Do(_ => throw new InvalidOperationException("Boom."));

        logger.LogSearch("mugs", 7);
    }

    [Test]
    public async Task Pipeline_LogsTheActivityAndQueuesTheQueryLogRow()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();
        var channel = Substitute.For<IWebsiteChannelContext>();
        channel.WebsiteChannelName.Returns("Store");

        using var harness = new TestHarness(
            null,
            true,
            new SearchTimingStage(),
            new LogActivityStage(activityLogger, contexts, queue, channel, NullLogger<LogActivityStage>.Instance));

        var response = await harness.Search(TestHarness.Request("lucene"));

        activityLogger.Received(1).LogSearch("lucene", (int)response.Total);

        var entry = queue.Items.Single().Entry!;

        Assert.That(entry.QueryText, Is.EqualTo("lucene"));
        Assert.That(entry.QueryId, Is.EqualTo(response.QueryId));
        Assert.That(entry.IndexName, Is.EqualTo(TestCorpus.IndexName));
        Assert.That(entry.ResultCount, Is.EqualTo((int)response.Total));
        Assert.That(entry.ChannelName, Is.EqualTo("Store"));
        Assert.That(contexts.Get(response.QueryId!)!.Query, Is.EqualTo("lucene"));
    }

    [Test]
    public async Task EventSink_LogsTheClickAndRecordsItsPositionOnTheQueryLogRow()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();

        contexts.Set("q-1", new QueryContext("mugs", TestCorpus.IndexName));

        var sink = new ActivitySearchEventSink(activityLogger, contexts, queue, NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 2 },
            CancellationToken.None);
        await sink.HandleAsync(
            new EventRequest { Type = EventType.Conversion, QueryId = "q-1", ResultId = "doc-1" },
            CancellationToken.None);

        activityLogger.Received(1).LogClick("mugs", "doc-1", 2);
        activityLogger.Received(1).LogConversion("mugs", "doc-1");

        var click = queue.Items.Single();

        Assert.That(click.ClickedQueryId, Is.EqualTo("q-1"));
        Assert.That(click.ClickedPosition, Is.EqualTo(2));
    }

    [Test]
    public async Task EventSink_WithAnUnknownQueryId_StillRecordsTheEvent()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var queue = new RecordingQueryLogQueue();
        var sink = new ActivitySearchEventSink(activityLogger, new QueryContextMap(), queue, NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "gone", ResultId = "doc-1", Position = 1 },
            CancellationToken.None);

        activityLogger.Received(1).LogClick(string.Empty, "doc-1", 1);
        Assert.That(queue.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void QueryContextMap_ForgetsAnEntryOnceItIsOlderThanItsRetention()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var map = new QueryContextMap(() => now);

        map.Set("q-1", new QueryContext("mugs", "TestIndex"));

        Assert.That(map.Get("q-1"), Is.Not.Null);

        now = now.Add(QueryContextMap.Retention).AddMinutes(1);

        Assert.That(map.Get("q-1"), Is.Null);
    }
}
