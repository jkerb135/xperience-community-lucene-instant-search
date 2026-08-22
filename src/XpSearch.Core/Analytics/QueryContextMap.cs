using System.Collections.Concurrent;

namespace XpSearch.Core.Analytics;

/// <summary>What a search with a given <c>queryId</c> was.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="IndexName">Code name of the index that was searched.</param>
public sealed record QueryContext(string Query, string IndexName);

/// <summary>
/// Remembers what each <c>queryId</c> searched for, so a later click or conversion event can be
/// attributed to the query that produced it (spec §9.1).
/// </summary>
public interface IQueryContextMap
{
    /// <summary>Records the query behind a <c>queryId</c>.</summary>
    /// <param name="queryId">Correlation id sent back to the caller in the response.</param>
    /// <param name="context">What was searched.</param>
    void Set(string queryId, QueryContext context);

    /// <summary>Looks a <c>queryId</c> up.</summary>
    /// <param name="queryId">Correlation id received on an event.</param>
    /// <returns>What was searched, or <see langword="null"/> when the id is unknown or has expired.</returns>
    QueryContext? Get(string queryId);
}

/// <summary>
/// The default <see cref="IQueryContextMap"/>: an in-memory map bounded by both age and size.
/// </summary>
/// <remarks>
/// Entries live for <see cref="Retention"/> (30 minutes) and the map holds at most
/// <see cref="Capacity"/> (10 000) of them; when it is full the oldest entries are dropped. The map is
/// per application instance, so on a load-balanced site an event that lands on another instance
/// resolves no query text - the event is still recorded, only without the query (ADR-0015).
/// </remarks>
public sealed class QueryContextMap : IQueryContextMap
{
    /// <summary>How long an entry is kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    /// <summary>The largest number of entries kept at once.</summary>
    public const int Capacity = 10_000;

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Func<DateTime> clock;

    /// <summary>Initializes a new instance of the <see cref="QueryContextMap"/> class.</summary>
    public QueryContextMap()
        : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QueryContextMap"/> class with a clock.</summary>
    /// <param name="clock">Supplies the current UTC time; tests use it to age entries.</param>
    public QueryContextMap(Func<DateTime> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.clock = clock;
    }

    /// <inheritdoc />
    public void Set(string queryId, QueryContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(context);

        var now = clock();

        if (entries.Count >= Capacity)
        {
            Trim(now);
        }

        entries[queryId] = new Entry(context, now);
    }

    /// <inheritdoc />
    public QueryContext? Get(string queryId)
    {
        if (string.IsNullOrWhiteSpace(queryId) || !entries.TryGetValue(queryId, out var entry))
        {
            return null;
        }

        if (clock() - entry.Added > Retention)
        {
            entries.TryRemove(queryId, out _);

            return null;
        }

        return entry.Context;
    }

    private void Trim(DateTime now)
    {
        foreach (var expired in entries.Where(entry => now - entry.Value.Added > Retention))
        {
            entries.TryRemove(expired.Key, out _);
        }

        // Age alone may not free anything on a busy instance, so the oldest tenth goes as well.
        foreach (var oldest in entries.OrderBy(entry => entry.Value.Added).Take(Math.Max(1, Capacity / 10)))
        {
            if (entries.Count < Capacity)
            {
                break;
            }

            entries.TryRemove(oldest.Key, out _);
        }
    }

    private sealed record Entry(QueryContext Context, DateTime Added);
}
