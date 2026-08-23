namespace XpSearch.Core.Analytics;

/// <summary>
/// The default <see cref="ISearchAnalyticsService"/>: reads the rows of the range once and aggregates
/// them in memory.
/// </summary>
/// <remarks>
/// One read per report keeps every figure consistent with every other, at the cost of holding the
/// range's rows while it runs - fine for the day-to-month ranges a dashboard asks for, not for a
/// multi-year range on a busy site (see KNOWN-LIMITATIONS).
/// </remarks>
public sealed class SearchAnalyticsService : ISearchAnalyticsService
{
    private readonly IQueryLogStore store;

    /// <summary>Initializes a new instance of the <see cref="SearchAnalyticsService"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    public SearchAnalyticsService(IQueryLogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    /// <inheritdoc />
    public async Task<SearchAnalyticsReport> GetReportAsync(SearchAnalyticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Math.Max(1, query.Limit);
        var rows = await store.ReadAsync(query.IndexName, query.FromUtc, query.ToUtc, cancellationToken).ConfigureAwait(false);
        var byQuery = rows
            .Where(row => !string.IsNullOrEmpty(row.QueryText))
            .GroupBy(row => row.QueryText, StringComparer.Ordinal)
            .ToList();

        var clicked = rows.Where(row => row.ClickedPosition is > 0).ToList();

        return new SearchAnalyticsReport(
            TopQueries: [.. byQuery
                .Select(group => new QueryVolume(group.Key, group.Count(), Percentile95([.. group.Select(row => row.ProcessingTimeMs)])))
                .OrderByDescending(entry => entry.Volume)
                .ThenBy(entry => entry.Query, StringComparer.Ordinal)
                .Take(limit)],
            ZeroResultQueries: [.. byQuery
                .Select(group => group.Where(row => row.ResultCount == 0).ToList())
                .Where(group => group.Count > 0)
                .Select(group => new ZeroResultQuery(group[0].QueryText, group.Count, group.Max(row => row.Timestamp)))
                .OrderByDescending(entry => entry.Volume)
                .ThenBy(entry => entry.Query, StringComparer.Ordinal)
                .Take(limit)],
            ClickThrough: [.. byQuery
                .Select(ToClickThrough)
                .OrderByDescending(entry => entry.Volume)
                .ThenBy(entry => entry.Query, StringComparer.Ordinal)
                .Take(limit)],
            AverageClickedPosition: clicked.Count == 0 ? null : clicked.Average(row => (double)row.ClickedPosition!.Value),
            VolumeOverTime: VolumeOverTime(rows, query),
            SlowestQueries: [.. byQuery
                .Select(group => new SlowQuery(group.Key, group.Count(), Percentile95([.. group.Select(row => row.ProcessingTimeMs)])))
                .OrderByDescending(entry => entry.P95ProcessingTimeMs)
                .ThenBy(entry => entry.Query, StringComparer.Ordinal)
                .Take(limit)],
            TotalSearches: rows.Count,
            ZeroResultSearches: rows.Count(row => row.ResultCount == 0),
            Clicks: clicked.Count);
    }

    private static QueryClickThrough ToClickThrough(IGrouping<string, QueryLogEntry> group)
    {
        int volume = group.Count();
        var positions = group.Where(row => row.ClickedPosition is > 0).Select(row => (double)row.ClickedPosition!.Value).ToList();

        return new QueryClickThrough(
            group.Key,
            volume,
            positions.Count,
            volume == 0 ? 0 : positions.Count / (double)volume,
            positions.Count == 0 ? null : positions.Average());
    }

    /// <summary>
    /// Daily buckets over the whole requested range, so a chart shows the days nobody searched as
    /// zero rather than skipping them.
    /// </summary>
    private static IReadOnlyList<SearchVolumePoint> VolumeOverTime(IReadOnlyList<QueryLogEntry> rows, SearchAnalyticsQuery query)
    {
        var counts = rows
            .GroupBy(row => DateOnly.FromDateTime(row.Timestamp))
            .ToDictionary(group => group.Key, group => (Volume: group.Count(), Zero: group.Count(row => row.ResultCount == 0)));

        var day = DateOnly.FromDateTime(query.FromUtc);
        var last = DateOnly.FromDateTime(query.ToUtc);
        var points = new List<SearchVolumePoint>();

        while (day <= last)
        {
            counts.TryGetValue(day, out var count);
            points.Add(new SearchVolumePoint(day, count.Volume, count.Zero));
            day = day.AddDays(1);
        }

        return points;
    }

    /// <summary>
    /// The 95th percentile by nearest rank: the smallest value at or above 95% of the samples. With
    /// few samples it is simply the slowest one, which is the honest answer for a rare query.
    /// </summary>
    private static int Percentile95(int[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);

        int rank = (int)Math.Ceiling(0.95 * values.Length);

        return values[Math.Clamp(rank - 1, 0, values.Length - 1)];
    }
}
