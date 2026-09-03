namespace XpSearch.Core.Analytics;

/// <summary>
/// Analytics settings, reachable through <c>XpSearchOptions.Analytics</c> (spec §9.2, §13.6).
/// </summary>
public sealed class XpSearchAnalyticsOptions
{
    /// <summary>
    /// Gets or sets how many days of <c>XpSearch.QueryLog</c> rows and answered popularity/synonym
    /// suggestions are kept. Older rows are deleted by <c>XpSearchQueryLogRetentionTask</c>. Defaults
    /// to 365 days; an administrator edits it on the Settings page of the Search ingestion application
    /// (AR-1), which is what the running application uses.
    /// </summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Gets or sets how many rows the retention task deletes per batch. Defaults to 1000.</summary>
    public int RetentionBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how far back query suggestions count query volume, in days. Defaults to 30.
    /// </summary>
    public int QuerySuggestionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many days of clicks the popularity signal is computed from (RK-1). Defaults to
    /// 30: popularity outside the window decays by being left out of the next run.
    /// </summary>
    public int PopularityLookbackDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many documents per index the popularity signal keeps, strongest first.
    /// Defaults to 100, which bounds both the stored rows and the boosted query.
    /// </summary>
    public int PopularityDocumentLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets how many of the window's most frequent queries are examined for a suggested boost
    /// rule. Defaults to 10.
    /// </summary>
    public int PopularitySuggestionQueries { get; set; } = 10;

    /// <summary>
    /// Gets or sets how long after a search with no click a following click still counts as the same
    /// visitor reformulating (SY-1). Defaults to 60 seconds.
    /// </summary>
    public int SynonymWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how often a reformulation pair has to happen in the window before it is suggested
    /// as a synonym (SY-1). Defaults to 3, which is what keeps the time-adjacency noise out.
    /// </summary>
    public int SynonymMinimumOccurrences { get; set; } = 3;
}
