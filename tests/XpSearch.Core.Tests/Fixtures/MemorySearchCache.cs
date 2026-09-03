using XpSearch.Core.Abstractions;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>
/// The response cache in a dictionary. <see cref="ProgressiveSearchCache"/> needs a running Xperience
/// application, so the contract it must satisfy is exercised through this instead.
/// </summary>
internal sealed class MemorySearchCache : ISearchCache
{
    private readonly Dictionary<string, Dictionary<string, SearchResponse>> entries = new(StringComparer.OrdinalIgnoreCase);

    internal int Evictions { get; private set; }

    /// <summary>Every index name <see cref="Evict"/> was called with, in order.</summary>
    internal List<string> Evicted { get; } = [];

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
        Evicted.Add(indexName);
        entries.Remove(indexName);
    }
}
