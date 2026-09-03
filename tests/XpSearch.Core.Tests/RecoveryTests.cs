using CMS.Websites.Routing;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Popularity;
using XpSearch.Core.Recovery;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// No-results recovery (SG-1): the verified did-you-mean correction, the opt-in popular searches, and
/// the honesty rules around both - nothing on a probe, and a verification search that never becomes a
/// row in the query log.
/// </summary>
[TestFixture]
internal sealed class RecoveryTests
{
    private TestHarness harness = null!;
    private FakeQuerySuggestionSource queries = null!;

    [SetUp]
    public void Build()
    {
        harness = new TestHarness();
        queries = new FakeQuerySuggestionSource();
    }

    [TearDown]
    public void Drop() => harness.Dispose();

    [Test]
    public async Task AMisspelledQueryThatFoundNothing_IsOfferedTheCorrectionTheIndexKnows()
    {
        var response = await Recovery().ExecuteAsync(TestHarness.Request("esspresso"), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Total, Is.Zero, "the misspelling itself finds nothing without typo tolerance");
            Assert.That(response.DidYouMean, Is.EqualTo("espresso"));
            Assert.That(response.PopularSearches, Is.Null, "popular searches stay off until the host opts in");
        });
    }

    [Test]
    public async Task ACorrectionIsOnlyOfferedOnceASearchHasConfirmedItFindsSomething()
    {
        var counting = new CountingPipeline(harness.Pipeline);
        var response = await Recovery(inner: counting).ExecuteAsync(
            TestHarness.Request("esspresso"),
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.DidYouMean, Is.EqualTo("espresso"));
            Assert.That(counting.Calls, Is.EqualTo(2), "the search itself, plus at most one verification");
            Assert.That(counting.Requests[1].Probe, Is.True, "the verification is a probe");
            Assert.That(counting.Requests[1].Query, Is.EqualTo("espresso"));
        });
    }

    /// <summary>A word the index cannot spell anything near is left alone rather than guessed at.</summary>
    [Test]
    public async Task AQueryWithNoNearbyTerm_IsOfferedNoCorrection()
    {
        var response = await Recovery().ExecuteAsync(
            TestHarness.Request("zzzzqwertyuiop"),
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Total, Is.Zero);
            Assert.That(response.DidYouMean, Is.Null);
        });
    }

    [Test]
    public async Task ASearchThatFoundSomething_IsNeverEnriched()
    {
        var options = Options(popular: 3);
        queries.Suggestions.AddRange(["latte", "grinder"]);

        var response = await Recovery(options).ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Total, Is.GreaterThan(0));
            Assert.That(response.DidYouMean, Is.Null);
            Assert.That(response.PopularSearches, Is.Null);
        });
    }

    [Test]
    public async Task WithDidYouMeanOff_TheDeadEndStaysADeadEnd()
    {
        var options = Options();
        options.Indexes[TestCorpus.IndexName].DidYouMean = false;

        var response = await Recovery(options).ExecuteAsync(TestHarness.Request("esspresso"), CancellationToken.None);

        Assert.That(response.DidYouMean, Is.Null);
    }

    [Test]
    public async Task AProbeThatFoundNothing_IsNeverEnriched()
    {
        var options = Options(popular: 3);
        queries.Suggestions.AddRange(["latte", "grinder"]);
        var probe = TestHarness.Request("esspresso");
        probe.Probe = true;

        var response = await Recovery(options).ExecuteAsync(probe, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.DidYouMean, Is.Null);
            Assert.That(response.PopularSearches, Is.Null);
        });
    }

    [Test]
    public async Task WithTheOptionOn_TheDeadEndOffersTheMostSearchedQueries()
    {
        var options = Options(popular: 2);
        queries.Suggestions.AddRange(["latte", "grinder", "filters"]);

        var response = await Recovery(options).ExecuteAsync(
            TestHarness.Request("zzzzqwertyuiop"),
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.PopularSearches, Is.EqualTo(new[] { "latte", "grinder" }).AsCollection);
            Assert.That(queries.Prefixes, Is.EqualTo(new[] { string.Empty }).AsCollection,
                "the empty prefix is what makes the popular queries the same computation autocomplete already caches");
        });
    }

    [Test]
    public async Task WithTheOptionOff_NoPopularSearchIsAskedForAtAll()
    {
        var response = await Recovery().ExecuteAsync(
            TestHarness.Request("zzzzqwertyuiop"),
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.PopularSearches, Is.Null);
            Assert.That(queries.Prefixes, Is.Empty);
        });
    }

    /// <summary>
    /// Analytics honesty: the extra search the correction is verified with is a probe, so the one
    /// journal call site skips it. One dead end must leave exactly one query log row - the visitor's.
    /// </summary>
    [Test]
    public async Task TheVerificationSearch_NeverBecomesAQueryLogRow()
    {
        var queue = new RecordingQueryLogQueue();
        var channel = Substitute.For<IWebsiteChannelContext>();
        channel.WebsiteChannelName.Returns("Store");
        var activities = Substitute.For<ISearchActivityLogger>();

        var pipeline = new CachedSearchPipeline(
            Recovery(),
            new MemorySearchCache(),
            new PerIndexSettings(new XpSearchOptions()),
            TestIndexRegistry.Of(TestCorpus.IndexName),
            new StubContactGroupResolver(),
            new StubExperimentResolver(),
            new SearchRequestJournal(activities, new QueryContextMap(), queue, channel, NullLogger<SearchRequestJournal>.Instance),
            new FakePopularitySignalStore(),
            new FixedTypoToleranceSource(false));

        var response = await pipeline.ExecuteAsync(TestHarness.Request("esspresso"), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.DidYouMean, Is.EqualTo("espresso"));
            Assert.That(queue.Items.Select(item => item.Entry!.QueryText), Is.EqualTo(new[] { "esspresso" }).AsCollection);
        });

        activities.Received(1).LogSearch("esspresso", 0);
        activities.DidNotReceive().LogSearch("espresso", Arg.Any<int>());
    }

    private XpSearchOptions Options(int popular = 0)
    {
        var options = new XpSearchOptions();
        options.Indexes[TestCorpus.IndexName].PopularSearchesOnNoResults = popular;

        return options;
    }

    private RecoverySearchPipeline Recovery(XpSearchOptions? options = null, ISearchPipeline? inner = null) =>
        new(
            inner ?? harness.Pipeline,
            new StaticOptionsMonitor<XpSearchOptions>(options ?? new XpSearchOptions()),
            queries,
            harness.Index,
            new StaticSchemaProvider(TestCorpus.Schema));

    /// <summary>Records every request the recovery layer sent through, verification included.</summary>
    private sealed class CountingPipeline(ISearchPipeline inner) : ISearchPipeline
    {
        internal List<SearchRequest> Requests { get; } = [];

        internal int Calls => Requests.Count;

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
