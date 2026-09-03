using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;

using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.Extensions.Options;

using XpSearch.Admin.UIPages;
using XpSearch.Core.Options;

using IFormItemCollectionProvider = Kentico.Xperience.Admin.Base.Forms.Internal.IFormItemCollectionProvider;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "search-settings",
    uiPageType: typeof(SearchSettingsPage),
    name: "Search settings",
    templateName: TemplateNames.EDIT,
    order: 110)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The search settings of one index (AR-2): every value the host's <c>AddXpSearch(o =&gt; ...)</c>
/// lambda sets as a default for all indexes, overridable per index here.
/// </summary>
public class SearchSettingsModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the settings belong to. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 0)]
    public string IndexName { get; set; } = string.Empty;

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

    /// <summary>Gets or sets how many days of this index's search analytics are kept.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Remove search analytics older than X days",
        Order = 9,
        Tooltip = "This index's query log rows and answered popularity/synonym suggestions older than this are deleted by the 'XpSearch.QueryLogRetention' scheduled task. Suggestions still waiting for an answer are never deleted.")]
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

    /// <summary>Gets or sets how many of this index's documents the popularity signal keeps.</summary>
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

    /// <summary>Reads the settings in effect for the index.</summary>
    /// <param name="settings">The index's settings, its stored row included.</param>
    /// <returns>The model the form renders.</returns>
    public static SearchSettingsModel From(XpSearchIndexSettings settings)
    {
        var values = SearchSettingsValues.From(settings);

        return new SearchSettingsModel
        {
            CacheTtlSeconds = values.CacheTtlSeconds,
            MaxQueryLength = values.MaxQueryLength,
            DefaultPageSize = values.DefaultPageSize,
            MaxPageSize = values.MaxPageSize,
            MaxFacetValues = values.MaxFacetValues,
            MaxResultWindow = values.MaxResultWindow,
            DefaultSuggestLimit = values.DefaultSuggestLimit,
            MaxSuggestLimit = values.MaxSuggestLimit,
            RetentionDays = values.RetentionDays,
            RetentionBatchSize = values.RetentionBatchSize,
            QuerySuggestionDays = values.QuerySuggestionDays,
            PopularityLookbackDays = values.PopularityLookbackDays,
            PopularityDocumentLimit = values.PopularityDocumentLimit,
            PopularitySuggestionQueries = values.PopularitySuggestionQueries,
            SynonymWindowSeconds = values.SynonymWindowSeconds,
            SynonymMinimumOccurrences = values.SynonymMinimumOccurrences
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
/// Edits one index's settings row (AR-2). The form shows the settings in effect - the code-configured
/// defaults until someone saves - and a save is live on the next search without a restart.
/// </summary>
public class SearchSettingsPage : IndexScopedEditPage<SearchSettingsModel>
{
    private readonly IInfoProvider<XpSearchSettingsInfo> rows;
    private readonly IOptionsMonitor<XpSearchIndexSettings> settings;

    /// <summary>Initializes a new instance of the <see cref="SearchSettingsPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="rows">Provider of the stored settings rows.</param>
    /// <param name="settings">The settings in effect, which is what the form shows.</param>
    public SearchSettingsPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSettingsInfo> rows,
        IOptionsMonitor<XpSearchIndexSettings> settings)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(settings);

        this.rows = rows;
        this.settings = settings;
    }

    /// <inheritdoc />
    protected override SearchSettingsModel CreateModel() => SearchSettingsModel.From(settings.Get(IndexName));

    /// <inheritdoc />
    protected override Task<string> PersistAsync(SearchSettingsModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = StoredSearchSettings.NewRow(submitted.ToValues(), submitted.IndexName);
        var stored = rows.Get()
            .WhereEquals(nameof(XpSearchSettingsInfo.SettingsIndexName), submitted.IndexName)
            .TopN(1)
            .FirstOrDefault();

        if (stored is not null)
        {
            // Keep the identity of the existing row, so this is an update rather than a second row.
            row.SettingsID = stored.SettingsID;
            row.SettingsGuid = stored.SettingsGuid;
        }

        rows.Set(row);

        return Task.FromResult($"The search settings of '{submitted.IndexName}' were saved.");
    }
}
