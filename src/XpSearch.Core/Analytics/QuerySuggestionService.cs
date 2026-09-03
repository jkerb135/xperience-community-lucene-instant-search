using System.Collections.Concurrent;

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
/// autocomplete fires on every keystroke and yesterday's popularity does not change between them.
/// </remarks>
public sealed class QuerySuggestionService : IQuerySuggestionSource
{
    private readonly IQueryLogStore store;
    private readonly IOptionsMonitor<XpSearchIndexSettings> settings;
    private readonly Func<DateTime> clock;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="settings">The current per-index settings (AR-2).</param>
    public QuerySuggestionService(IQueryLogStore store, IOptionsMonitor<XpSearchIndexSettings> settings)
        : this(store, settings, () => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class with a clock.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="settings">The current per-index settings (AR-2).</param>
    /// <param name="clock">Supplies the current UTC time; tests use it to expire the cache.</param>
    public QuerySuggestionService(IQueryLogStore store, IOptionsMonitor<XpSearchIndexSettings> settings, Func<DateTime> clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        this.store = store;
        this.settings = settings;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SuggestAsync(string indexName, string prefix, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            return [];
        }

        var now = clock();
        var indexSettings = settings.Get(indexName);
        string key = $"{indexName}|{prefix}|{limit}";

        if (cache.TryGetValue(key, out var cached) && now - cached.Created <= indexSettings.CacheTtl)
        {
            return cached.Suggestions;
        }

        var rows = await store
            .ReadAsync(indexName, now.AddDays(-Math.Max(1, indexSettings.QuerySuggestionDays)), now, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> suggestions =
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

        cache[key] = new CacheEntry(suggestions, now);

        return suggestions;
    }

    private sealed record CacheEntry(IReadOnlyList<string> Suggestions, DateTime Created);
}
