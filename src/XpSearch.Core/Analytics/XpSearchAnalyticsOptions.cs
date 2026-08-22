namespace XpSearch.Core.Analytics;

/// <summary>
/// Analytics settings, reachable through <c>XpSearchOptions.Analytics</c> (spec §9.2, §13.6).
/// </summary>
public sealed class XpSearchAnalyticsOptions
{
    /// <summary>
    /// Gets or sets how many days of <c>XpSearch.QueryLog</c> rows are kept. Rows older than this are
    /// deleted by <c>XpSearchQueryLogRetentionTask</c>. Defaults to 180 days (spec §9.2).
    /// </summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>Gets or sets how many rows the retention task deletes per batch. Defaults to 1000.</summary>
    public int RetentionBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how far back query suggestions count query volume, in days. Defaults to 30.
    /// </summary>
    public int QuerySuggestionDays { get; set; } = 30;
}
