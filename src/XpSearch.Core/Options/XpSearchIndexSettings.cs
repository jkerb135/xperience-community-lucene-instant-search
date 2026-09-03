namespace XpSearch.Core.Options;

/// <summary>
/// The search settings of one index (AR-2), read through
/// <c>IOptionsMonitor&lt;XpSearchIndexSettings&gt;.Get(indexCodeName)</c>: the values the host's
/// <c>AddXpSearch(o =&gt; ...)</c> lambda configured, with that index's stored row over them.
/// </summary>
/// <remarks>
/// The unnamed instance (<c>Get(Options.DefaultName)</c>) carries the code defaults alone, which is
/// what an index nobody registered is pruned with.
/// </remarks>
public sealed class XpSearchIndexSettings
{
    // The shipped defaults live on XpSearchOptions; reading them from a default instance keeps the two
    // types from drifting apart.
    private static readonly XpSearchOptions CodeDefaults = new();

    /// <summary>Gets or sets how long an identical query is served from cache. Zero turns caching off.</summary>
    public TimeSpan CacheTtl { get; set; } = CodeDefaults.CacheTtl;

    /// <summary>Gets or sets the maximum accepted length of the query text.</summary>
    public int MaxQueryLength { get; set; } = CodeDefaults.MaxQueryLength;

    /// <summary>Gets or sets the server-side page size ceiling.</summary>
    public int MaxPageSize { get; set; } = CodeDefaults.MaxPageSize;

    /// <summary>Gets or sets the maximum number of values returned per facet dimension.</summary>
    public int MaxFacetValues { get; set; } = CodeDefaults.MaxFacetValues;

    /// <summary>Gets or sets how deep paging may go.</summary>
    public int MaxResultWindow { get; set; } = CodeDefaults.MaxResultWindow;

    /// <summary>Gets or sets the ceiling on the suggestion limit.</summary>
    public int MaxSuggestLimit { get; set; } = CodeDefaults.MaxSuggestLimit;

    /// <summary>Gets or sets how many days of search analytics are kept for this index.</summary>
    public int RetentionDays { get; set; } = CodeDefaults.Analytics.RetentionDays;

    /// <summary>Gets or sets how many rows the retention task deletes per batch.</summary>
    public int RetentionBatchSize { get; set; } = CodeDefaults.Analytics.RetentionBatchSize;

    /// <summary>Gets or sets how far back query suggestions count query volume, in days.</summary>
    public int QuerySuggestionDays { get; set; } = CodeDefaults.Analytics.QuerySuggestionDays;

    /// <summary>Gets or sets how many days of clicks the popularity signal is computed from.</summary>
    public int PopularityLookbackDays { get; set; } = CodeDefaults.Analytics.PopularityLookbackDays;

    /// <summary>Gets or sets how many documents the popularity signal keeps for this index.</summary>
    public int PopularityDocumentLimit { get; set; } = CodeDefaults.Analytics.PopularityDocumentLimit;

    /// <summary>Gets or sets how many frequent queries are examined for a suggested boost rule.</summary>
    public int PopularitySuggestionQueries { get; set; } = CodeDefaults.Analytics.PopularitySuggestionQueries;

    /// <summary>Gets or sets the reformulation window synonym mining uses, in seconds.</summary>
    public int SynonymWindowSeconds { get; set; } = CodeDefaults.Analytics.SynonymWindowSeconds;

    /// <summary>Gets or sets how often a reformulation has to happen before it is suggested.</summary>
    public int SynonymMinimumOccurrences { get; set; } = CodeDefaults.Analytics.SynonymMinimumOccurrences;

    /// <summary>Builds the settings an index with no stored row has.</summary>
    /// <param name="options">The code-configured options.</param>
    /// <returns>The settings.</returns>
    public static XpSearchIndexSettings FromOptions(XpSearchOptions options)
    {
        var settings = new XpSearchIndexSettings();
        settings.SetDefaultsFrom(options);

        return settings;
    }

    /// <summary>Copies the root values of <paramref name="options"/> onto these settings.</summary>
    /// <param name="options">The code-configured options, which are the defaults of every index.</param>
    public void SetDefaultsFrom(XpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CacheTtl = options.CacheTtl;
        MaxQueryLength = options.MaxQueryLength;
        MaxPageSize = options.MaxPageSize;
        MaxFacetValues = options.MaxFacetValues;
        MaxResultWindow = options.MaxResultWindow;
        MaxSuggestLimit = options.MaxSuggestLimit;
        RetentionDays = options.Analytics.RetentionDays;
        RetentionBatchSize = options.Analytics.RetentionBatchSize;
        QuerySuggestionDays = options.Analytics.QuerySuggestionDays;
        PopularityLookbackDays = options.Analytics.PopularityLookbackDays;
        PopularityDocumentLimit = options.Analytics.PopularityDocumentLimit;
        PopularitySuggestionQueries = options.Analytics.PopularitySuggestionQueries;
        SynonymWindowSeconds = options.Analytics.SynonymWindowSeconds;
        SynonymMinimumOccurrences = options.Analytics.SynonymMinimumOccurrences;
    }
}
