namespace XpSearch.Core.Analytics;

/// <summary>What a report covers.</summary>
/// <param name="IndexName">Code name of the index, or an empty string for every index.</param>
/// <param name="FromUtc">Start of the range, inclusive.</param>
/// <param name="ToUtc">End of the range, inclusive.</param>
/// <param name="Limit">How many rows each top-N list holds. Defaults to 20.</param>
/// <param name="ExperimentId">
/// Identifier of an experiment to report on, or <see langword="null"/> for every search in the range
/// (XP-1). With it set, only the searches that experiment answered are counted.
/// </param>
/// <param name="Variant">
/// The variant to report on - <c>A</c> or <c>B</c> - when <paramref name="ExperimentId"/> is set.
/// Empty or <see langword="null"/> covers both variants.
/// </param>
public sealed record SearchAnalyticsQuery(
    string IndexName,
    DateTime FromUtc,
    DateTime ToUtc,
    int Limit = 20,
    int? ExperimentId = null,
    string? Variant = null);

/// <summary>How often a query was searched for.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="P95ProcessingTimeMs">The 95th percentile of its server-side processing time.</param>
public sealed record QueryVolume(string Query, int Volume, int P95ProcessingTimeMs);

/// <summary>A query that found nothing - the report a content team acts on (spec §9.3).</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="LastSeen">When it was last searched for, in UTC.</param>
public sealed record ZeroResultQuery(string Query, int Volume, DateTime LastSeen);

/// <summary>How often a query's results were clicked.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="Clicks">How many of those searches led to a click.</param>
/// <param name="ClickThroughRate">Clicks divided by volume, between zero and one.</param>
/// <param name="AverageClickedPosition">Mean clicked position, or <see langword="null"/> when nothing was clicked.</param>
public sealed record QueryClickThrough(string Query, int Volume, int Clicks, double ClickThroughRate, double? AverageClickedPosition);

/// <summary>Search volume on one day.</summary>
/// <param name="Day">The day, in UTC.</param>
/// <param name="Volume">How many searches ran that day.</param>
/// <param name="ZeroResultVolume">How many of those searches found nothing.</param>
public sealed record SearchVolumePoint(DateOnly Day, int Volume, int ZeroResultVolume);

/// <summary>How slow a query is.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="P95ProcessingTimeMs">The 95th percentile of its server-side processing time.</param>
public sealed record SlowQuery(string Query, int Volume, int P95ProcessingTimeMs);

/// <summary>Everything the analytics dashboard shows for one index and date range (spec §9.3).</summary>
/// <param name="TopQueries">The most searched queries.</param>
/// <param name="ZeroResultQueries">The most searched queries that found nothing.</param>
/// <param name="ClickThrough">Click-through rate per query, most searched first.</param>
/// <param name="AverageClickedPosition">Mean clicked position across the range, or <see langword="null"/>.</param>
/// <param name="VolumeOverTime">Searches per day, oldest first, with no gaps for empty days.</param>
/// <param name="SlowestQueries">The queries with the highest 95th percentile processing time.</param>
/// <param name="TotalSearches">How many searches the range holds.</param>
/// <param name="ZeroResultSearches">How many of those searches found nothing.</param>
/// <param name="Clicks">How many of those searches led to a click.</param>
public sealed record SearchAnalyticsReport(
    IReadOnlyList<QueryVolume> TopQueries,
    IReadOnlyList<ZeroResultQuery> ZeroResultQueries,
    IReadOnlyList<QueryClickThrough> ClickThrough,
    double? AverageClickedPosition,
    IReadOnlyList<SearchVolumePoint> VolumeOverTime,
    IReadOnlyList<SlowQuery> SlowestQueries,
    int TotalSearches,
    int ZeroResultSearches,
    int Clicks);

/// <summary>
/// Reads the aggregate query log into the reports of spec §9.3. The dashboard page only renders what
/// this returns.
/// </summary>
public interface ISearchAnalyticsService
{
    /// <summary>Produces every report for one index and date range.</summary>
    /// <param name="query">What to report on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<SearchAnalyticsReport> GetReportAsync(SearchAnalyticsQuery query, CancellationToken cancellationToken);
}
