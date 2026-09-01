using System.Globalization;
using System.Reflection;

using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;

using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the response cache and the eviction that the <see cref="ILuceneClient"/> decorator triggers.
/// </summary>
/// <remarks>
/// <see cref="ProgressiveSearchCache"/> itself needs a running Xperience application (it goes through
/// <c>IProgressiveCache</c> and <c>CacheHelper</c>), so the contract it must satisfy is exercised
/// through an in-memory implementation of the same interface.
/// </remarks>
[TestFixture]
internal sealed class CachingTests
{
    [Test]
    public async Task IdenticalRequests_AreServedFromCache()
    {
        var (pipeline, inner, _) = Build();

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);

        Assert.That(inner.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task DifferentRequests_AreDifferentEntries()
    {
        var (pipeline, inner, _) = Build();

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        await pipeline.ExecuteAsync(TestHarness.Request("grinder"), CancellationToken.None);

        Assert.That(inner.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task QueryId_IsNotPartOfTheKeyAndIsReIssuedOnAHit()
    {
        var (pipeline, inner, _) = Build();
        var first = TestHarness.Request("espresso");
        first.QueryId = "first";
        var second = TestHarness.Request("espresso");
        second.QueryId = "second";

        var one = await pipeline.ExecuteAsync(first, CancellationToken.None);
        var two = await pipeline.ExecuteAsync(second, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(inner.Calls, Is.EqualTo(1), "queryId must not change the cache key");
            Assert.That(one.QueryId, Is.EqualTo("first"));
            Assert.That(two.QueryId, Is.EqualTo("second"), "each caller gets its own correlation id back");
        });
    }

    [Test]
    public async Task ACachedResponse_KeepsTheRedirectItWasCachedWith()
    {
        var pipeline = Cached(new SearchResponse
        {
            Results = [],
            Redirect = new SearchRedirect { Rule = "Espresso landing page", Url = "/promotions/espresso" }
        });

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        var hit = await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);

        Assert.That(hit.Redirect?.Url, Is.EqualTo("/promotions/espresso"));
    }

    /// <summary>
    /// Guards the copy the decorator makes to re-issue <c>queryId</c>: every other contract member has
    /// to survive it, including one added to <see cref="SearchResponse"/> after this test was written.
    /// </summary>
    [Test]
    public async Task ACachedResponse_CarriesEveryOtherContractMemberBack()
    {
        var properties = typeof(SearchResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var cachedInstance = new SearchResponse();
        foreach (var property in properties)
        {
            property.SetValue(cachedInstance, NonDefault(property.PropertyType));
        }

        var pipeline = Cached(cachedInstance);
        var request = TestHarness.Request("espresso");
        request.QueryId = "mine";

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        var hit = await pipeline.ExecuteAsync(request, CancellationToken.None);

        Expect.Multiple(() =>
        {
            foreach (var property in properties.Where(p => p.Name != nameof(SearchResponse.QueryId)))
            {
                Assert.That(property.GetValue(hit), Is.EqualTo(property.GetValue(cachedInstance)), property.Name);
            }

            Assert.That(hit.QueryId, Is.EqualTo("mine"));
            Assert.That(cachedInstance.QueryId, Is.EqualTo(NonDefault(typeof(string))), "the cached instance must not be mutated");
        });
    }

    private static object NonDefault(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;

        return actual switch
        {
            _ when actual == typeof(string) => "non-default",
            _ when actual.IsArray => Array.CreateInstance(actual.GetElementType()!, 0),
            _ when actual.IsValueType => Convert.ChangeType(7, actual, CultureInfo.InvariantCulture),
            _ => Activator.CreateInstance(actual)!
        };
    }

    [Test]
    public async Task ZeroTtl_DisablesCaching()
    {
        var (pipeline, inner, _) = Build(new XpSearchOptions { CacheTtl = TimeSpan.Zero });

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);

        Assert.That(inner.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task RebuildingTheIndex_EvictsTheCachedResponses()
    {
        var (pipeline, inner, cache) = Build();
        var lucene = Substitute.For<ILuceneClient>();
        lucene.Rebuild(Arg.Any<string>(), Arg.Any<CancellationToken?>()).Returns(Task.CompletedTask);
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        var decorated = new CacheEvictingLuceneClient(lucene, cache, accessor);

        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);
        await decorated.Rebuild(TestCorpus.IndexName, CancellationToken.None);
        await pipeline.ExecuteAsync(TestHarness.Request("espresso"), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(inner.Calls, Is.EqualTo(2), "the rebuild must have dropped the cached response");
            Assert.That(cache.Evictions, Is.EqualTo(1));
        });

        await lucene.Received(1).Rebuild(TestCorpus.IndexName, Arg.Any<CancellationToken?>());
    }

    [Test]
    public async Task EveryWritingCall_IsForwardedAndEvicts()
    {
        var cache = new MemorySearchCache();
        var lucene = Substitute.For<ILuceneClient>();
        lucene.UpsertRecords(Arg.Any<IEnumerable<Document>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1);
        lucene.DeleteRecords(Arg.Any<IEnumerable<string>>(), Arg.Any<string>()).Returns(1);
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        var decorated = new CacheEvictingLuceneClient(lucene, cache, accessor);

        await decorated.UpsertRecords([new Document()], TestCorpus.IndexName, CancellationToken.None);
        await decorated.DeleteRecords(["guid"], TestCorpus.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(cache.Evictions, Is.EqualTo(2));

            // The integration never invalidates its cached searcher after an in-place write, so a
            // pushed document would stay invisible until the process restarted.
            accessor.Received(2).Invalidate(TestCorpus.IndexName);
        });

        await lucene.Received(1).UpsertRecords(Arg.Any<IEnumerable<Document>>(), TestCorpus.IndexName, Arg.Any<CancellationToken>());
        await lucene.Received(1).DeleteRecords(Arg.Any<IEnumerable<string>>(), TestCorpus.IndexName);
    }

    [Test]
    public async Task ReadOnlyCalls_AreForwardedWithoutEvicting()
    {
        var cache = new MemorySearchCache();
        var lucene = Substitute.For<ILuceneClient>();
        lucene.GetStatistics(Arg.Any<CancellationToken>()).Returns(new List<LuceneIndexStatisticsModel>());
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        var decorated = new CacheEvictingLuceneClient(lucene, cache, accessor);

        await decorated.GetStatistics(CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(cache.Evictions, Is.Zero);
            accessor.DidNotReceiveWithAnyArgs().Invalidate(default!);
        });
    }

    /// <summary>
    /// <c>LuceneIndexAccessor.Invalidate</c> reaches the integration's cached searcher through the
    /// internal <c>LuceneSearchCacheInvalidator</c>, which the container registers as a singleton.
    /// Nothing else in the integration is public enough to do it, so an upgrade that renames or moves
    /// the type has to fail here rather than in a host, silently.
    /// </summary>
    [Test]
    public void TheIntegrationsSearchCacheInvalidator_IsStillReachable()
    {
        var invalidator = typeof(Kentico.Xperience.Lucene.Core.Search.ILuceneSearchService).Assembly
            .GetType("Kentico.Xperience.Lucene.Core.Search.LuceneSearchCacheInvalidator");

        Expect.Multiple(() =>
        {
            Assert.That(invalidator, Is.Not.Null);
            Assert.That(
                invalidator!.GetMethod("Invalidate", [typeof(Kentico.Xperience.Lucene.Core.Indexing.LuceneIndex)]),
                Is.Not.Null);
        });
    }

    private static (ISearchPipeline Pipeline, CountingPipeline Inner, MemorySearchCache Cache) Build(XpSearchOptions? options = null)
    {
        var effective = options ?? new XpSearchOptions();
        var inner = new CountingPipeline();
        var cache = new MemorySearchCache();

        return (
            new CachedSearchPipeline(
                inner,
                cache,
                Microsoft.Extensions.Options.Options.Create(effective),
                new StubContactGroupResolver(),
                new StubExperimentResolver(),
                Substitute.For<ISearchRequestJournal>()),
            inner,
            cache);
    }

    private static CachedSearchPipeline Cached(SearchResponse response) =>
        new(
            new FixedPipeline(response),
            new MemorySearchCache(),
            Microsoft.Extensions.Options.Options.Create(new XpSearchOptions()),
            new StubContactGroupResolver(),
            new StubExperimentResolver(),
            Substitute.For<ISearchRequestJournal>());

    private sealed class FixedPipeline(SearchResponse response) : ISearchPipeline
    {
        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class CountingPipeline : ISearchPipeline
    {
        internal int Calls { get; private set; }

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new SearchResponse
            {
                Results = [],
                QueryId = request.QueryId ?? Guid.NewGuid().ToString()
            });
        }
    }

}
