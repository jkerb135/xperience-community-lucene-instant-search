using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Caching;

/// <summary>
/// Decorates <see cref="ILuceneClient"/> and drops this library's cached responses for an index
/// whenever the integration rebuilds, updates or deletes it (spec §4.7).
/// </summary>
/// <remarks>
/// Spec §4.7 says "hook the Lucene integration's rebuild event". There is no such event:
/// <c>Kentico.Xperience.Lucene</c> 15.0.5 exposes no rebuild notification and its own
/// <c>LuceneSearchCacheInvalidator</c> is <see langword="internal"/>. Decorating the write-side
/// service is Kentico's documented substitute
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/decorate-system-services):
/// the container resolves the previous <see cref="ILuceneClient"/> into this constructor, every call
/// is forwarded, and eviction happens after the underlying write succeeds. Writes made by another
/// process against shared storage are not observed - the TTL bounds that staleness.
/// </remarks>
public sealed class CacheEvictingLuceneClient : ILuceneClient
{
    private readonly ILuceneClient inner;
    private readonly ISearchCache cache;

    /// <summary>Initializes a new instance of the <see cref="CacheEvictingLuceneClient"/> class.</summary>
    /// <param name="inner">The previously registered client, resolved by the container.</param>
    /// <param name="cache">The response cache to evict.</param>
    public CacheEvictingLuceneClient(ILuceneClient inner, ISearchCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);

        this.inner = inner;
        this.cache = cache;
    }

    /// <inheritdoc />
    public async Task Rebuild(string indexName, CancellationToken? cancellationToken)
    {
        await inner.Rebuild(indexName, cancellationToken).ConfigureAwait(false);
        cache.Evict(indexName);
    }

    /// <inheritdoc />
    public async Task<int> UpsertRecords(IEnumerable<Document> documents, string indexName, CancellationToken cancellationToken)
    {
        int result = await inner.UpsertRecords(documents, indexName, cancellationToken).ConfigureAwait(false);
        cache.Evict(indexName);

        return result;
    }

    /// <inheritdoc />
    public async Task<int> DeleteRecords(IEnumerable<string> itemGuids, string indexName)
    {
        int result = await inner.DeleteRecords(itemGuids, indexName).ConfigureAwait(false);
        cache.Evict(indexName);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIndex(LuceneIndex luceneIndex)
    {
        ArgumentNullException.ThrowIfNull(luceneIndex);

        bool result = await inner.DeleteIndex(luceneIndex).ConfigureAwait(false);
        cache.Evict(luceneIndex.IndexName);

        return result;
    }

    /// <inheritdoc />
    public Task<ICollection<LuceneIndexStatisticsModel>> GetStatistics(CancellationToken cancellationToken) =>
        inner.GetStatistics(cancellationToken);
}
