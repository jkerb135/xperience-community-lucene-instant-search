using System.Collections.Concurrent;

namespace XpSearch.Core.Analytics;

/// <summary>What a search with a given <c>queryId</c> was.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="IndexName">Code name of the index that was searched.</param>
/// <param name="MaxPosition">
/// The highest result position the search returned (<c>page * pageSize</c>), which bounds the
/// <c>position</c> an event may claim (SC-1). <c>0</c> when it is not known, which is the case for
/// every context resolved from the query log rather than set by the journal.
/// </param>
/// <param name="ResultCount">
/// How many documents the search matched, or zero when it is not known. Only the query log carries
/// it (WF-1), so it is the fallback bound when <paramref name="MaxPosition"/> is <c>0</c>.
/// </param>
public sealed record QueryContext(string Query, string IndexName, int MaxPosition = 0, int ResultCount = 0);

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

    /// <summary>Looks a <c>queryId</c> up in this instance's own memory.</summary>
    /// <param name="queryId">Correlation id received on an event.</param>
    /// <returns>What was searched, or <see langword="null"/> when the id is unknown or has expired.</returns>
    QueryContext? Get(string queryId);

    /// <summary>
    /// Looks a <c>queryId</c> up in this instance's memory and, on a miss, in the query log - which is
    /// what makes an event that lands on another instance of a web farm resolvable (WF-1).
    /// </summary>
    /// <param name="queryId">Correlation id received on an event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was searched, or <see langword="null"/> when the id is unknown or has expired.</returns>
    Task<QueryContext?> GetAsync(string queryId, CancellationToken cancellationToken);

    /// <summary>Counts one event against a <c>queryId</c>'s budget (SC-1).</summary>
    /// <param name="queryId">Correlation id received on an event.</param>
    /// <returns>
    /// How many events have been counted for this id, this one included; <c>0</c> when the id is
    /// unknown or has expired. The caller decides the budget, so a shared implementation only has to
    /// count.
    /// </returns>
    int CountEvent(string queryId);
}

/// <summary>
/// The default <see cref="IQueryContextMap"/>: an in-memory map bounded by both age and size.
/// </summary>
/// <remarks>
/// <para>
/// Two tiers. Entries live in memory for <see cref="Retention"/> (30 minutes) and the map holds at most
/// <see cref="Capacity"/> (10 000) of them; when it is full the oldest entries are dropped. That tier
/// is per application instance, so <see cref="GetAsync"/> falls back to the query log row the search
/// wrote - it is keyed by the same <c>queryId</c> and carries the query text, the index and the result
/// count - which is how an event that lands on another instance of a web farm still resolves its query
/// (WF-1, ADR-0015).
/// </para>
/// <para>
/// The fallback only sees rows the log queue has already drained (up to 10 s, see
/// <see cref="XpSearchQueryLogQueueWorker"/>), so a cross-instance click within that window still
/// resolves nothing: the event is recorded without the query.
/// </para>
/// <para>
/// <see cref="CountEvent"/> counts on the local entry only, so the per-query event budget (SC-1) is
/// enforced per instance: a caller spreading replays across a farm gets the budget once per node.
/// </para>
/// </remarks>
public sealed class QueryContextMap : IQueryContextMap
{
    /// <summary>How long an entry is kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    /// <summary>The largest number of entries kept at once.</summary>
    public const int Capacity = 10_000;

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly IQueryLogStore? store;
    private readonly Func<DateTime> clock;

    /// <summary>Initializes a new instance of the <see cref="QueryContextMap"/> class.</summary>
    /// <param name="store">
    /// The query log the second tier reads, or <see langword="null"/> for a memory-only map.
    /// </param>
    public QueryContextMap(IQueryLogStore? store = null)
        : this(store, () => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QueryContextMap"/> class with a clock.</summary>
    /// <param name="store">
    /// The query log the second tier reads, or <see langword="null"/> for a memory-only map.
    /// </param>
    /// <param name="clock">Supplies the current UTC time; tests use it to age entries.</param>
    public QueryContextMap(IQueryLogStore? store, Func<DateTime> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.store = store;
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

    /// <inheritdoc />
    public async Task<QueryContext?> GetAsync(string queryId, CancellationToken cancellationToken)
    {
        if (Get(queryId) is { } local)
        {
            return local;
        }

        if (store is null || string.IsNullOrWhiteSpace(queryId))
        {
            return null;
        }

        var row = await store.GetByQueryIdAsync(queryId, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // The log row knows no page window, so MaxPosition stays 0 and ResultCount carries the bound.
        var context = new QueryContext(row.QueryText, row.IndexName, 0, row.ResultCount);

        // Kept locally so CountEvent has an entry to count on: without it every event resolved from
        // the database would be outside any budget. The budget is therefore per instance (WF-1 entry
        // in KNOWN-LIMITATIONS), and the entry ages from now rather than from the search.
        Set(queryId, context);

        return context;
    }

    /// <inheritdoc />
    public int CountEvent(string queryId) =>
        Get(queryId) is null || !entries.TryGetValue(queryId, out var entry)
            ? 0
            : Interlocked.Increment(ref entry.Events);

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

    private sealed class Entry(QueryContext context, DateTime added)
    {
        // A field rather than a property: Interlocked.Increment needs a ref.
        internal int Events;

        internal QueryContext Context { get; } = context;

        internal DateTime Added { get; } = added;
    }
}
