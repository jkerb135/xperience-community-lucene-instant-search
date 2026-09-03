using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;

using Microsoft.Extensions.Options;

using XpSearch.Admin.UIPages;
using XpSearch.Core.Options;

using IFormItemCollectionProvider = Kentico.Xperience.Admin.Base.Forms.Internal.IFormItemCollectionProvider;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "settings",
    uiPageType: typeof(GlobalSettingsPage),
    name: "Settings",
    templateName: TemplateNames.EDIT,
    order: 900)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The global search settings (AR-1): every value that is not per index. The host's
/// <c>AddXpSearch(o =&gt; ...)</c> lambda seeds them once; from then on this form owns them.
/// </summary>
public class GlobalSettingsModel
{
    /// <summary>Gets or sets how long an identical query is served from cache, in seconds. 0 turns caching off.</summary>
    [MinimumIntegerValueValidationRule(0)]
    [NumberInputComponent(Label = "Response cache lifetime (seconds)", Order = 1, Tooltip = "How long an identical query is served from cache. 0 turns response caching off.")]
    public int CacheTtlSeconds { get; set; }

    /// <summary>Gets or sets the maximum accepted length of the query text.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(1000)]
    [NumberInputComponent(Label = "Maximum query length", Order = 2, Tooltip = "Longer query text is truncated.")]
    public int MaxQueryLength { get; set; }

    /// <summary>Gets or sets the page size used when a request omits one.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(1000)]
    [NumberInputComponent(Label = "Default page size", Order = 3, Tooltip = "Used when a request does not ask for a page size.")]
    public int DefaultPageSize { get; set; }

    /// <summary>Gets or sets the server-side page size ceiling.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(1000)]
    [NumberInputComponent(Label = "Maximum page size", Order = 4, Tooltip = "Larger requested page sizes are clamped to this.")]
    public int MaxPageSize { get; set; }

    /// <summary>Gets or sets the maximum number of values returned per facet dimension.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Maximum values per facet", Order = 5)]
    public int MaxFacetValues { get; set; }

    /// <summary>Gets or sets how deep paging may go.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Maximum result window", Order = 6, Tooltip = "Page multiplied by page size may not exceed this; a deeper request is refused.")]
    public int MaxResultWindow { get; set; }

    /// <summary>Gets or sets the number of suggestions returned when a request omits a limit.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(100)]
    [NumberInputComponent(Label = "Default suggestion count", Order = 7)]
    public int DefaultSuggestLimit { get; set; }

    /// <summary>Gets or sets the ceiling on the suggestion limit.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(100)]
    [NumberInputComponent(Label = "Maximum suggestion count", Order = 8)]
    public int MaxSuggestLimit { get; set; }

    /// <summary>Gets or sets how many days of search analytics are kept.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Remove search analytics older than X days",
        Order = 9,
        Tooltip = "Query log rows and answered popularity/synonym suggestions older than this are deleted by the 'XpSearch.QueryLogRetention' scheduled task. Suggestions still waiting for an answer are never deleted.")]
    public int RetentionDays { get; set; }

    /// <summary>Gets or sets how many rows the retention task deletes per batch.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Retention batch size", Order = 10)]
    public int RetentionBatchSize { get; set; }

    /// <summary>Gets or sets how far back query suggestions count query volume, in days.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Query suggestion window (days)", Order = 11)]
    public int QuerySuggestionDays { get; set; }

    /// <summary>Gets or sets how many days of clicks the popularity signal is computed from.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Popularity lookback (days)", Order = 12)]
    public int PopularityLookbackDays { get; set; }

    /// <summary>Gets or sets how many documents per index the popularity signal keeps.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Popularity documents per index", Order = 13)]
    public int PopularityDocumentLimit { get; set; }

    /// <summary>Gets or sets how many frequent queries are examined for a suggested boost rule.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Popularity suggestion queries", Order = 14)]
    public int PopularitySuggestionQueries { get; set; }

    /// <summary>Gets or sets the reformulation window synonym mining uses, in seconds.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Synonym reformulation window (seconds)", Order = 15)]
    public int SynonymWindowSeconds { get; set; }

    /// <summary>Gets or sets how often a reformulation has to happen before it is suggested.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(Label = "Synonym minimum occurrences", Order = 16)]
    public int SynonymMinimumOccurrences { get; set; }

    /// <summary>Reads the current values off the options.</summary>
    /// <param name="options">The options in effect, stored values included.</param>
    /// <returns>The model the form renders.</returns>
    public static GlobalSettingsModel From(XpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new GlobalSettingsModel
        {
            CacheTtlSeconds = (int)Math.Max(0, options.CacheTtl.TotalSeconds),
            MaxQueryLength = options.MaxQueryLength,
            DefaultPageSize = options.DefaultPageSize,
            MaxPageSize = options.MaxPageSize,
            MaxFacetValues = options.MaxFacetValues,
            MaxResultWindow = options.MaxResultWindow,
            DefaultSuggestLimit = options.DefaultSuggestLimit,
            MaxSuggestLimit = options.MaxSuggestLimit,
            RetentionDays = options.Analytics.RetentionDays,
            RetentionBatchSize = options.Analytics.RetentionBatchSize,
            QuerySuggestionDays = options.Analytics.QuerySuggestionDays,
            PopularityLookbackDays = options.Analytics.PopularityLookbackDays,
            PopularityDocumentLimit = options.Analytics.PopularityDocumentLimit,
            PopularitySuggestionQueries = options.Analytics.PopularitySuggestionQueries,
            SynonymWindowSeconds = options.Analytics.SynonymWindowSeconds,
            SynonymMinimumOccurrences = options.Analytics.SynonymMinimumOccurrences
        };
    }

    /// <summary>Turns the submitted form into the values the stored row is written from.</summary>
    /// <returns>The submitted values.</returns>
    public SearchSettingsValues ToValues() =>
        new()
        {
            CacheTtlSeconds = Math.Max(0, CacheTtlSeconds),
            MaxQueryLength = MaxQueryLength,
            DefaultPageSize = DefaultPageSize,
            MaxPageSize = MaxPageSize,
            MaxFacetValues = MaxFacetValues,
            MaxResultWindow = MaxResultWindow,
            DefaultSuggestLimit = DefaultSuggestLimit,
            MaxSuggestLimit = MaxSuggestLimit,
            RetentionDays = RetentionDays,
            RetentionBatchSize = RetentionBatchSize,
            QuerySuggestionDays = QuerySuggestionDays,
            PopularityLookbackDays = PopularityLookbackDays,
            PopularityDocumentLimit = PopularityDocumentLimit,
            PopularitySuggestionQueries = PopularitySuggestionQueries,
            SynonymWindowSeconds = SynonymWindowSeconds,
            SynonymMinimumOccurrences = SynonymMinimumOccurrences
        };
}

/// <summary>
/// Edits the single global settings row (AR-1). Saving it invalidates the options overlay, so the new
/// values are in effect on the next search without an application restart.
/// </summary>
public class GlobalSettingsPage : TuningEditPage<GlobalSettingsModel>
{
    private readonly IInfoProvider<XpSearchSettingsInfo> rows;
    private readonly IOptionsMonitor<XpSearchOptions> options;

    /// <summary>Initializes a new instance of the <see cref="GlobalSettingsPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="rows">Provider of the stored settings row.</param>
    /// <param name="options">The options in effect, which is what the form shows.</param>
    public GlobalSettingsPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSettingsInfo> rows,
        IOptionsMonitor<XpSearchOptions> options)
        : base(formItemCollectionProvider, formDataBinder, pageLinkGenerator)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);

        this.rows = rows;
        this.options = options;
    }

    /// <inheritdoc />
    protected override GlobalSettingsModel CreateModel() => GlobalSettingsModel.From(options.CurrentValue);

    /// <inheritdoc />
    protected override Task<string> PersistAsync(GlobalSettingsModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = StoredSearchSettings.NewRow(submitted.ToValues());
        var stored = rows.Get().TopN(1).FirstOrDefault();

        if (stored is not null)
        {
            // Keep the identity of the seeded row, so this is an update rather than a second row.
            row.SettingsID = stored.SettingsID;
            row.SettingsGuid = stored.SettingsGuid;
        }

        rows.Set(row);

        return Task.FromResult("The search settings were saved.");
    }
}
