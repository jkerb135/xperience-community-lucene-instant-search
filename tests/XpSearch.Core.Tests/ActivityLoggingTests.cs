using CMS.Activities;
using CMS.Helpers;
using CMS.Websites.Routing;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
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
    public void ConsentedClickAndConversion_PutTheQueryInTheValueAndTheRestInTheOtherFields()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);

        logger.LogClick("mugs", "doc-1", 3);
        logger.LogConversion("mugs", "doc-1");

        activities.Received(1).Log(
            XpSearchActivityTypes.Click,
            Arg.Is<CustomActivityData>(data =>
                data.ActivityValue == "mugs" && data.ActivityComment == "doc-1" && data.ActivityItemDetailID == 3));
        activities.Received(1).Log(
            XpSearchActivityTypes.Conversion,
            Arg.Is<CustomActivityData>(data =>
                data.ActivityValue == "mugs" && data.ActivityComment == "doc-1"));
    }

    [Test]
    public void ConsentedSearch_CarriesNoResultIdOrPosition()
    {
        cookieLevel.GetCurrentCookieLevel().Returns(Visitor);

        logger.LogSearch("mugs", 7);

        activities.Received(1).Log(
            XpSearchActivityTypes.Query,
            Arg.Is<CustomActivityData>(data => data.ActivityComment == null && data.ActivityItemDetailID == 0));
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
    public async Task CacheMiss_LogsTheActivityAndQueuesTheQueryLogRowUnderTheReturnedQueryId()
    {
        var journaled = BuildJournaled();

        var response = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("Lucene "), CancellationToken.None);

        journaled.Activities.Received(1).LogSearch("lucene", StubPipeline.Total);

        var entry = journaled.Queue.Items.Single().Entry!;

        Expect.Multiple(() =>
        {
            Assert.That(entry.QueryText, Is.EqualTo("lucene"), "the journal records the normalized query");
            Assert.That(entry.QueryId, Is.EqualTo(response.QueryId));
            Assert.That(entry.IndexName, Is.EqualTo(TestCorpus.IndexName));
            Assert.That(entry.ResultCount, Is.EqualTo(StubPipeline.Total));
            Assert.That(entry.ChannelName, Is.EqualTo("Store"));
            Assert.That(
                journaled.Contexts.Get(response.QueryId!)?.Query,
                Is.EqualTo("lucene"),
                "a click on this response has to resolve the query text");
        });
    }

    /// <summary>
    /// The defect this seam exists for: a search answered from the cache never enters the pipeline, so
    /// while the logging lived in a stage it was invisible to the analytics and its clicks could not be
    /// attributed.
    /// </summary>
    [Test]
    public async Task CacheHit_IsJournaledToo_UnderItsOwnQueryId()
    {
        var journaled = BuildJournaled();

        var miss = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);
        var hit = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);

        journaled.Activities.Received(2).LogSearch("lucene", StubPipeline.Total);

        Expect.Multiple(() =>
        {
            Assert.That(journaled.Inner.Calls, Is.EqualTo(1), "the second search must have been a cache hit");
            Assert.That(hit.QueryId, Is.Not.EqualTo(miss.QueryId));
            Assert.That(
                journaled.Queue.Items.Select(item => item.Entry!.QueryId),
                Is.EqualTo(new[] { miss.QueryId, hit.QueryId }),
                "exactly one query log row per request, hit or miss, under the id the caller was given");
            Assert.That(journaled.Contexts.Get(hit.QueryId!)?.Query, Is.EqualTo("lucene"));
        });
    }

    [Test]
    public async Task ClickAfterACacheHit_ResolvesTheQueryTextOfTheActivity()
    {
        var journaled = BuildJournaled();

        await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);
        var hit = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);

        var clicks = Substitute.For<ISearchActivityLogger>();
        var sink = new ActivitySearchEventSink(
            clicks,
            journaled.Contexts,
            journaled.Queue,
            NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = hit.QueryId!, ResultId = "doc-1", Position = 2 },
            CancellationToken.None);

        clicks.Received(1).LogClick("lucene", "doc-1", 2);
    }

    [Test]
    public async Task WithCachingDisabled_EachSearchIsJournaledExactlyOnce()
    {
        var journaled = BuildJournaled(new XpSearchOptions { CacheTtl = TimeSpan.Zero });

        var one = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);
        var two = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(journaled.Inner.Calls, Is.EqualTo(2));
            Assert.That(
                journaled.Queue.Items.Select(item => item.Entry!.QueryId),
                Is.EqualTo(new[] { one.QueryId, two.QueryId }));
        });
    }

    /// <summary>
    /// Stamping the query log is what splits every existing metric by variant (XP-1). The activity is
    /// deliberately not stamped: it is consent-gated and carries no experiment.
    /// </summary>
    [Test]
    public async Task TheQueryLogIsStampedWithTheExperimentOnlyWhileOneIsRunning()
    {
        var running = BuildJournaled(experiment: new ExperimentAssignment(7, SearchVariant.B));
        var none = BuildJournaled();

        await running.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);
        await none.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);

        var stamped = running.Queue.Items.Single().Entry!;
        var plain = none.Queue.Items.Single().Entry!;

        Expect.Multiple(() =>
        {
            Assert.That(stamped.ExperimentId, Is.EqualTo(7));
            Assert.That(stamped.Variant, Is.EqualTo("B"));
            Assert.That(plain.ExperimentId, Is.Null);
            Assert.That(plain.Variant, Is.Null);
        });
    }

    private static JournaledPipeline BuildJournaled(XpSearchOptions? options = null, ExperimentAssignment? experiment = null)
    {
        var activities = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();
        var channel = Substitute.For<IWebsiteChannelContext>();
        channel.WebsiteChannelName.Returns("Store");
        var inner = new StubPipeline();

        var pipeline = new CachedSearchPipeline(
            inner,
            new MemorySearchCache(),
            Microsoft.Extensions.Options.Options.Create(options ?? new XpSearchOptions()),
            new StubContactGroupResolver(),
            new StubExperimentResolver(experiment),
            new SearchRequestJournal(activities, contexts, queue, channel, NullLogger<SearchRequestJournal>.Instance),
            new FakePopularitySignalStore());

        return new JournaledPipeline(pipeline, inner, queue, contexts, activities);
    }

    private sealed record JournaledPipeline(
        ISearchPipeline Pipeline,
        StubPipeline Inner,
        RecordingQueryLogQueue Queue,
        QueryContextMap Contexts,
        ISearchActivityLogger Activities);

    /// <summary>Stands in for the real pipeline, and counts how often the cache let it run.</summary>
    private sealed class StubPipeline : ISearchPipeline
    {
        internal const int Total = 3;

        internal int Calls { get; private set; }

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new SearchResponse
            {
                Results = [],
                Total = Total,
                QueryId = request.QueryId ?? Guid.NewGuid().ToString()
            });
        }
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
