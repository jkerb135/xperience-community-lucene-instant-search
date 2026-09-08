using CMS.Helpers;

using Microsoft.Extensions.Options;

using XpSearch.Core.Options;

namespace XpSearch.Core.Analytics;

/// <summary>
/// Supplies the query suggestions of spec §4.3 and §13.6: the popular queries an index has already
/// answered, prefix-matched.
/// </summary>
public interface IQuerySuggestionSource
{
    /// <summary>Returns the most searched queries of an index that start with a prefix.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="prefix">The normalized, lowercased prefix to match.</param>
    /// <param name="limit">The largest number of suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The suggestions, most searched first, deduplicated.</returns>
    Task<IReadOnlyList<string>> SuggestAsync(string indexName, string prefix, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IQuerySuggestionSource"/>: counts the query log of the index's last
/// <c>QuerySuggestionDays</c> days, keeping only queries that found something.
/// </summary>
/// <remarks>
/// Results are cached per index, prefix and limit for the index's own <c>CacheTtl</c>, because
/// autocomplete fires on every keystroke and yesterday's popularity does not change between them. The
/// cache is Xperience's own <see cref="IProgressiveCache"/> with a dependency on the query log's dummy
/// key, so a newly logged search drops the entries across every instance of a web farm rather than
/// leaving each one stale until its own TTL runs out
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies,
/// WF-1).
/// </remarks>
public sealed class QuerySuggestionService : IQuerySuggestionSource
{
    private readonly IQueryLogStore store;
    private readonly IOptionsMonitor<XpSearchIndexSettings> settings;
    private readonly IProgressiveCache cache;
    private readonly Func<DateTime> clock;
    private readonly Func<string, CMSCacheDependency> dependency;

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="settings">The current per-index settings (AR-2).</param>
    /// <param name="cache">Xperience's progressive cache.</param>
    public QuerySuggestionService(IQueryLogStore store, IOptionsMonitor<XpSearchIndexSettings> settings, IProgressiveCache cache)
        : this(store, settings, cache, () => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class with a clock.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="settings">The current per-index settings (AR-2).</param>
    /// <param name="cache">Xperience's progressive cache.</param>
    /// <param name="clock">Supplies the current UTC time; tests use it to move the read window.</param>
    /// <param name="dependency">
    /// Builds the cache dependency from a dummy key. Defaults to <c>CacheHelper.GetCacheDependency</c>,
    /// which needs a running Xperience application, so tests substitute it.
    /// </param>
    public QuerySuggestionService(
        IQueryLogStore store,
        IOptionsMonitor<XpSearchIndexSettings> settings,
        IProgressiveCache cache,
        Func<DateTime> clock,
        Func<string, CMSCacheDependency>? dependency = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(clock);

        this.store = store;
        this.settings = settings;
        this.cache = cache;
        this.clock = clock;
        this.dependency = dependency ?? (key => CacheHelper.GetCacheDependency(key));
    }

    /// <summary>The dummy cache key every suggestion entry depends on: the query log object type.</summary>
    /// <returns>The key.</returns>
    public static string DependencyKey() => $"{XpSearchQueryLogInfo.OBJECT_TYPE}|all";

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> SuggestAsync(string indexName, string prefix, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var indexSettings = settings.Get(indexName);

        return cache.LoadAsync(
            (cacheSettings, token) =>
            {
                cacheSettings.CacheDependency = dependency(DependencyKey());

                return SuggestUncachedAsync(indexName, prefix, limit, indexSettings, token);
            },
            new CacheSettings(indexSettings.CacheTtl.TotalMinutes, "xpsearch", "query-suggestions", indexName, prefix, limit),
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> SuggestUncachedAsync(
        string indexName,
        string prefix,
        int limit,
        XpSearchIndexSettings indexSettings,
        CancellationToken cancellationToken)
    {
        var now = clock();

        var rows = await store
            .ReadAsync(indexName, now.AddDays(-Math.Max(1, indexSettings.QuerySuggestionDays)), now, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => row.ResultCount > 0
                    && row.QueryText.Length > 0
                    && row.QueryText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .GroupBy(row => row.QueryText, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(group => group.Key)
        ];
    }
}
