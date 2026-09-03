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
/// The default <see cref="IQuerySuggestionSource"/>: counts the query log of the last
/// <c>XpSearchOptions.Analytics.QuerySuggestionDays</c> days, keeping only queries that found
/// something.
/// </summary>
/// <remarks>
/// Results are cached per index, prefix and limit for <c>XpSearchOptions.CacheTtl</c>, because
/// autocomplete fires on every keystroke and yesterday's popularity does not change between them.
/// </remarks>
public sealed class QuerySuggestionService : IQuerySuggestionSource
{
    private readonly IQueryLogStore store;
    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly Func<DateTime> clock;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="options">The current search options.</param>
    public QuerySuggestionService(IQueryLogStore store, IOptionsMonitor<XpSearchOptions> options)
        : this(store, options, () => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QuerySuggestionService"/> class with a clock.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="options">The current search options.</param>
    /// <param name="clock">Supplies the current UTC time; tests use it to expire the cache.</param>
    public QuerySuggestionService(IQueryLogStore store, IOptionsMonitor<XpSearchOptions> options, Func<DateTime> clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.store = store;
        this.options = options;
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
        string key = $"{indexName}|{prefix}|{limit}";

        if (cache.TryGetValue(key, out var cached) && now - cached.Created <= options.CurrentValue.CacheTtl)
        {
            return cached.Suggestions;
        }

        var rows = await store
            .ReadAsync(indexName, now.AddDays(-Math.Max(1, options.CurrentValue.Analytics.QuerySuggestionDays)), now, cancellationToken)
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
