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
    /// A quoted phrase (PH-1) reaches the journal verbatim: reports and the suggestion miner group by
    /// what the visitor typed, and "french press" is not the same search as french press.
    /// </summary>
    [Test]
    public async Task AQuotedQuery_IsJournaledWithItsQuotes()
    {
        var journaled = BuildJournaled();

        await journaled.Pipeline.ExecuteAsync(TestHarness.Request("\"French Press\""), CancellationToken.None);

        Assert.That(journaled.Queue.Items.Single().Entry!.QueryText, Is.EqualTo("\"french press\""));
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

    /// <summary>
    /// The first-load handoff (PB-6): the results widget renders the first paint server-side and the
    /// hydrating client repeats it under the same <c>queryId</c>. One page load must stay one row.
    /// </summary>
    [Test]
    public async Task ASearchRepeatedUnderAQueryIdThatWasAlreadyJournaled_IsNotJournaledTwice()
    {
        var journaled = BuildJournaled();
        var request = TestHarness.Request("lucene");
        request.QueryId = "server-1";

        var first = await journaled.Pipeline.ExecuteAsync(request, CancellationToken.None);
        var second = await journaled.Pipeline.ExecuteAsync(request, CancellationToken.None);

        journaled.Activities.Received(1).LogSearch("lucene", StubPipeline.Total);

        Expect.Multiple(() =>
        {
            Assert.That(first.QueryId, Is.EqualTo("server-1"));
            Assert.That(second.QueryId, Is.EqualTo("server-1"), "the caller still gets the id it sent");
            Assert.That(
                journaled.Queue.Items.Select(item => item.Entry!.QueryId),
                Is.EqualTo(new[] { "server-1" }),
                "the repeat of an already journaled search must not add a second query log row");
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
            new StaticOptionsMonitor<XpSearchOptions>(new XpSearchOptions()),
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

    /// <summary>
    /// ES-1: a probe is a count the client asked for on the visitor's behalf - the sheet's
    /// "Show N results", the empty state's unfiltered count - and must be invisible to every report,
    /// suggestion miner and popularity signal. They all read the journal's outputs, so the query log
    /// enqueue is asserted specifically: no row, no activity, no click-attribution context.
    /// </summary>
    [TestCase(false, TestName = "AProbeRequestOnACacheMiss_IsAnsweredButNeverJournaled")]
    [TestCase(true, TestName = "AProbeRequestWithCachingDisabled_IsAnsweredButNeverJournaled")]
    public async Task AProbeRequestOnACacheMiss_IsAnsweredButNeverJournaled(bool cachingDisabled)
    {
        var journaled = BuildJournaled(cachingDisabled ? new XpSearchOptions { CacheTtl = TimeSpan.Zero } : null);
        var request = TestHarness.Request("lucene");
        request.Probe = true;

        var response = await journaled.Pipeline.ExecuteAsync(request, CancellationToken.None);

        journaled.Activities.DidNotReceiveWithAnyArgs().LogSearch(default!, default);

        Expect.Multiple(() =>
        {
            Assert.That(journaled.Inner.Calls, Is.EqualTo(1), "a probe is still answered by the pipeline");
            Assert.That(response.Total, Is.EqualTo(StubPipeline.Total), "the count is what the caller came for");
            Assert.That(journaled.Queue.Items, Is.Empty, "a probe must add no query log row");
            Assert.That(journaled.Contexts.Get(response.QueryId!), Is.Null);
        });
    }

    [Test]
    public async Task AProbeRequestServedFromTheCache_IsNotJournaledEither()
    {
        var journaled = BuildJournaled();
        var probe = TestHarness.Request("lucene");
        probe.Probe = true;

        var search = await journaled.Pipeline.ExecuteAsync(TestHarness.Request("lucene"), CancellationToken.None);
        await journaled.Pipeline.ExecuteAsync(probe, CancellationToken.None);

        journaled.Activities.Received(1).LogSearch("lucene", StubPipeline.Total);

        Expect.Multiple(() =>
        {
            Assert.That(journaled.Inner.Calls, Is.EqualTo(1), "the probe shares the cache entry of the same search");
            Assert.That(
                journaled.Queue.Items.Select(item => item.Entry!.QueryId),
                Is.EqualTo(new[] { search.QueryId }),
                "only the real search is journaled");
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
            new PerIndexSettings(options ?? new XpSearchOptions()),
            TestIndexRegistry.Of(TestCorpus.IndexName),
            new StubContactGroupResolver(),
            new StubExperimentResolver(experiment),
            new SearchRequestJournal(activities, contexts, queue, channel, NullLogger<SearchRequestJournal>.Instance),
            new FakePopularitySignalStore(),
            new Fixtures.FixedTypoToleranceSource(false));

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

        var sink = new ActivitySearchEventSink(activityLogger, contexts, queue, Settings(), NullLogger<ActivitySearchEventSink>.Instance);

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

    /// <summary>Builds the sink's options; SC-1 reads the event budget and the page size ceiling off them.</summary>
    private static StaticOptionsMonitor<XpSearchOptions> Settings(XpSearchOptions? options = null) =>
        new(options ?? new XpSearchOptions());

    [Test]
    public async Task EventSink_WithAnUnknownQueryId_DropsTheEvent()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var queue = new RecordingQueryLogQueue();
        var sink = new ActivitySearchEventSink(activityLogger, new QueryContextMap(), queue, Settings(), NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "gone", ResultId = "doc-1", Position = 1 },
            CancellationToken.None);

        Assert.That(activityLogger.ReceivedCalls(), Is.Empty);
        Assert.That(queue.Items, Is.Empty);
    }

    [Test]
    public async Task EventSink_BeyondTheQueryIdBudget_DropsTheEvent()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();

        contexts.Set("q-1", new QueryContext("mugs", TestCorpus.IndexName, 10));

        var sink = new ActivitySearchEventSink(
            activityLogger,
            contexts,
            queue,
            Settings(new XpSearchOptions { MaxEventsPerQuery = 3 }),
            NullLogger<ActivitySearchEventSink>.Instance);

        for (int i = 0; i < 5; i++)
        {
            await sink.HandleAsync(
                new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 1 },
                CancellationToken.None);
        }

        activityLogger.Received(3).LogClick("mugs", "doc-1", 1);
        Assert.That(queue.Items, Has.Count.EqualTo(3), "the replayed clicks past the budget never reach the popularity signal");
    }

    [Test]
    public async Task EventSink_WithAPositionTheSearchNeverShowed_DropsTheEvent()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();

        contexts.Set("q-1", new QueryContext("mugs", TestCorpus.IndexName, 20));

        var sink = new ActivitySearchEventSink(activityLogger, contexts, queue, Settings(), NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 21 },
            CancellationToken.None);
        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 20 },
            CancellationToken.None);

        activityLogger.Received(1).LogClick("mugs", "doc-1", 20);
        Assert.That(queue.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task EventSink_WithAQueryOfUnknownPageSize_BoundsThePositionByTheIndexCeiling()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var contexts = new QueryContextMap();
        var queue = new RecordingQueryLogQueue();

        contexts.Set("q-1", new QueryContext("mugs", TestCorpus.IndexName));

        var sink = new ActivitySearchEventSink(
            activityLogger,
            contexts,
            queue,
            Settings(new XpSearchOptions { MaxPageSize = 25 }),
            NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 26 },
            CancellationToken.None);

        Assert.That(queue.Items, Is.Empty);
    }

    [Test]
    public void QueryContextMap_ForgetsAnEntryOnceItIsOlderThanItsRetention()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var map = new QueryContextMap(null, () => now);

        map.Set("q-1", new QueryContext("mugs", "TestIndex"));

        Assert.That(map.Get("q-1"), Is.Not.Null);

        now = now.Add(QueryContextMap.Retention).AddMinutes(1);

        Assert.That(map.Get("q-1"), Is.Null);
    }

    /// <summary>
    /// The second tier (WF-1): what the local map has never seen - because another instance of the web
    /// farm answered the search - is resolved from the query log row that search wrote.
    /// </summary>
    [Test]
    public async Task QueryContextMap_ResolvesAQueryIdItNeverSaw_FromTheQueryLog()
    {
        var store = new InMemoryQueryLogStore();
        var map = new QueryContextMap(store);

        await store.AppendAsync(
            new QueryLogEntry("elsewhere", TestCorpus.IndexName, "mugs", 7, DateTime.UtcNow, "Store", "en", 3),
            CancellationToken.None);

        map.Set("here", new QueryContext("kettle", TestCorpus.IndexName));

        var local = await map.GetAsync("here", CancellationToken.None);
        var remote = await map.GetAsync("elsewhere", CancellationToken.None);
        var unknown = await map.GetAsync("neither", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(local?.Query, Is.EqualTo("kettle"), "the local tier answers first");
            Assert.That(remote?.Query, Is.EqualTo("mugs"));
            Assert.That(remote?.IndexName, Is.EqualTo(TestCorpus.IndexName));
            Assert.That(remote?.ResultCount, Is.EqualTo(7), "the row carries what the search found (SC-1)");
            Assert.That(unknown, Is.Null, "a miss in both tiers is still a miss");
        });
    }

    /// <summary>
    /// The sink goes through the two-tier lookup, so a click that lands on the instance which did not
    /// answer the search still names the query on its activity (WF-1).
    /// </summary>
    [Test]
    public async Task EventSink_ResolvesTheQueryOfASearchAnsweredByAnotherInstance()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var store = new InMemoryQueryLogStore();
        var queue = new RecordingQueryLogQueue();

        await store.AppendAsync(
            new QueryLogEntry("q-2", TestCorpus.IndexName, "mugs", 7, DateTime.UtcNow, "Store", "en", 3),
            CancellationToken.None);

        var sink = new ActivitySearchEventSink(
            activityLogger,
            new QueryContextMap(store),
            queue,
            Settings(),
            NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-2", ResultId = "doc-1", Position = 2 },
            CancellationToken.None);

        activityLogger.Received(1).LogClick("mugs", "doc-1", 2);
    }

    /// <summary>
    /// The bound of a context resolved from the log: the row knows no page window, so its result count
    /// is what a click's position may not exceed (WF-1 + SC-1).
    /// </summary>
    [Test]
    public async Task EventSink_ForAContextResolvedFromTheQueryLog_BoundsThePositionByTheResultCount()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var store = new InMemoryQueryLogStore();
        var queue = new RecordingQueryLogQueue();

        await store.AppendAsync(
            new QueryLogEntry("q-5", TestCorpus.IndexName, "mugs", 7, DateTime.UtcNow, "Store", "en", 3),
            CancellationToken.None);

        var sink = new ActivitySearchEventSink(
            activityLogger,
            new QueryContextMap(store),
            queue,
            Settings(new XpSearchOptions { MaxPageSize = 100 }),
            NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-5", ResultId = "doc-1", Position = 8 },
            CancellationToken.None);

        Assert.That(queue.Items, Is.Empty, "the row found 7 documents, so position 8 was never shown");
    }

    /// <summary>
    /// A context resolved from the database is kept locally, which is what gives SC-1's per-query event
    /// budget something to count on (WF-1).
    /// </summary>
    [Test]
    public async Task EventSink_ForAContextResolvedFromTheQueryLog_StillEnforcesTheEventBudget()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var store = new InMemoryQueryLogStore();
        var queue = new RecordingQueryLogQueue();

        await store.AppendAsync(
            new QueryLogEntry("q-6", TestCorpus.IndexName, "mugs", 7, DateTime.UtcNow, "Store", "en", 3),
            CancellationToken.None);

        var sink = new ActivitySearchEventSink(
            activityLogger,
            new QueryContextMap(store),
            queue,
            Settings(new XpSearchOptions { MaxEventsPerQuery = 2 }),
            NullLogger<ActivitySearchEventSink>.Instance);

        for (int i = 0; i < 5; i++)
        {
            await sink.HandleAsync(
                new EventRequest { Type = EventType.Click, QueryId = "q-6", ResultId = "doc-1", Position = 1 },
                CancellationToken.None);
        }

        Assert.That(queue.Items, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// The lookup reaches the database, so its failure must not throw out of a route that answers 202.
    /// The event itself is dropped: an id that cannot be resolved is an unknown id (SC-1).
    /// </summary>
    [Test]
    public async Task EventSink_WhenTheQueryLogLookupThrows_DropsTheEventWithoutThrowing()
    {
        var activityLogger = Substitute.For<ISearchActivityLogger>();
        var queue = new RecordingQueryLogQueue();
        var contexts = Substitute.For<IQueryContextMap>();
        contexts.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<QueryContext?>>(_ => throw new InvalidOperationException("the database is down"));

        var sink = new ActivitySearchEventSink(activityLogger, contexts, queue, Settings(), NullLogger<ActivitySearchEventSink>.Instance);

        await sink.HandleAsync(
            new EventRequest { Type = EventType.Click, QueryId = "q-3", ResultId = "doc-1", Position = 1 },
            CancellationToken.None);

        Assert.That(activityLogger.ReceivedCalls(), Is.Empty);
        Assert.That(queue.Items, Is.Empty);
    }

    /// <summary>
    /// The cross-instance half of the first-load handoff (WF-1): the journal's in-memory check only
    /// covers the instance that rendered, so the store refuses the duplicate row itself.
    /// </summary>
    [Test]
    public async Task QueryLog_RefusesASecondRowForAQueryIdItAlreadyHolds()
    {
        var store = new InMemoryQueryLogStore();
        var row = new QueryLogEntry("q-4", TestCorpus.IndexName, "mugs", 7, DateTime.UtcNow, "Store", "en", 3);

        await store.AppendAsync(row, CancellationToken.None);
        await store.AppendAsync(row with { ProcessingTimeMs = 9 }, CancellationToken.None);

        Assert.That(store.Rows, Has.Count.EqualTo(1));
    }
}
