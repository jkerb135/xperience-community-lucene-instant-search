using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;

using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
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
        var decorated = new CacheEvictingLuceneClient(lucene, cache);

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
        var decorated = new CacheEvictingLuceneClient(lucene, cache);

        await decorated.UpsertRecords([new Document()], TestCorpus.IndexName, CancellationToken.None);
        await decorated.DeleteRecords(["guid"], TestCorpus.IndexName);

        Assert.That(cache.Evictions, Is.EqualTo(2));
        await lucene.Received(1).UpsertRecords(Arg.Any<IEnumerable<Document>>(), TestCorpus.IndexName, Arg.Any<CancellationToken>());
        await lucene.Received(1).DeleteRecords(Arg.Any<IEnumerable<string>>(), TestCorpus.IndexName);
    }

    [Test]
    public async Task ReadOnlyCalls_AreForwardedWithoutEvicting()
    {
        var cache = new MemorySearchCache();
        var lucene = Substitute.For<ILuceneClient>();
        lucene.GetStatistics(Arg.Any<CancellationToken>()).Returns(new List<LuceneIndexStatisticsModel>());
        var decorated = new CacheEvictingLuceneClient(lucene, cache);

        await decorated.GetStatistics(CancellationToken.None);

        Assert.That(cache.Evictions, Is.Zero);
    }

    private static (ISearchPipeline Pipeline, CountingPipeline Inner, MemorySearchCache Cache) Build(XpSearchOptions? options = null)
    {
        var effective = options ?? new XpSearchOptions();
        var inner = new CountingPipeline();
        var cache = new MemorySearchCache();

        return (new CachedSearchPipeline(inner, cache, Microsoft.Extensions.Options.Options.Create(effective)), inner, cache);
    }

    private sealed class CountingPipeline : ISearchPipeline
    {
        internal int Calls { get; private set; }

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new SearchResponse
            {
                Hits = [],
                QueryId = request.QueryId ?? Guid.NewGuid().ToString()
            });
        }
    }

    private sealed class MemorySearchCache : ISearchCache
    {
        private readonly Dictionary<string, Dictionary<string, SearchResponse>> entries = new(StringComparer.OrdinalIgnoreCase);

        internal int Evictions { get; private set; }

        public async Task<SearchResponse> GetOrAddAsync(
            string indexName,
            string key,
            Func<CancellationToken, Task<SearchResponse>> factory,
            CancellationToken cancellationToken)
        {
            if (!entries.TryGetValue(indexName, out var forIndex))
            {
                forIndex = new Dictionary<string, SearchResponse>(StringComparer.Ordinal);
                entries[indexName] = forIndex;
            }

            if (forIndex.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var response = await factory(cancellationToken);
            forIndex[key] = response;

            return response;
        }

        public void Evict(string indexName)
        {
            Evictions++;
            entries.Remove(indexName);
        }
    }
}
