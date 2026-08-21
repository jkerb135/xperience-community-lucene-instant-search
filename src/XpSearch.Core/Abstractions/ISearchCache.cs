using XpSearch.Core.Contract;

namespace XpSearch.Core.Abstractions;

/// <summary>
/// Short-lived cache of identical query responses (spec §4.7).
/// </summary>
public interface ISearchCache
{
    /// <summary>Returns the cached response for a key, or produces and caches one.</summary>
    /// <param name="indexName">Code name of the index; the eviction unit.</param>
    /// <param name="key">Hash of the normalized request.</param>
    /// <param name="factory">Runs the search when the key is not cached.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    Task<SearchResponse> GetOrAddAsync(
        string indexName,
        string key,
        Func<CancellationToken, Task<SearchResponse>> factory,
        CancellationToken cancellationToken);

    /// <summary>Drops every cached response for an index.</summary>
    /// <param name="indexName">Code name of the index whose content changed.</param>
    void Evict(string indexName);
}
