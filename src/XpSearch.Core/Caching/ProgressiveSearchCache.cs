using CMS.Helpers;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;

namespace XpSearch.Core.Caching;

/// <summary>
/// The response cache, on Xperience's own <see cref="IProgressiveCache"/>.
/// </summary>
/// <remarks>
/// Platform caching rather than ASP.NET output caching: it is the documented Xperience API
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/data-caching),
/// it collapses parallel identical misses into one search, and it works on SaaS. Eviction goes
/// through a dummy dependency key per index - every entry depends on
/// <c>xpsearch|index|&lt;name&gt;</c>, and touching that key drops them all
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
/// </remarks>
public sealed class ProgressiveSearchCache : ISearchCache
{
    private const string KeyPrefix = "xpsearch";

    private readonly IProgressiveCache cache;
    private readonly XpSearchOptions options;

    /// <summary>Initializes a new instance of the <see cref="ProgressiveSearchCache"/> class.</summary>
    /// <param name="cache">Xperience's progressive cache.</param>
    /// <param name="options">The configured search options.</param>
    public ProgressiveSearchCache(IProgressiveCache cache, IOptions<XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        this.cache = cache;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public Task<SearchResponse> GetOrAddAsync(
        string indexName,
        string key,
        Func<CancellationToken, Task<SearchResponse>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        return cache.LoadAsync(
            async (settings, token) =>
            {
                settings.CacheDependency = CacheHelper.GetCacheDependency(DependencyKey(indexName));
                return await factory(token).ConfigureAwait(false);
            },
            new CacheSettings(options.CacheTtl.TotalMinutes, KeyPrefix, indexName, key),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Evict(string indexName)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);

        CacheHelper.TouchKey(DependencyKey(indexName));
    }

    private static string DependencyKey(string indexName) => $"{KeyPrefix}|index|{indexName}";
}
