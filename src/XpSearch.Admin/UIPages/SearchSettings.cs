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
    [TextInputComponent(
        Label = "Index",
        Order = 0,
        Tooltip = "The index these settings belong to.",
        ExplanationText = "Settings are per index: every other index keeps its own values, and an index nobody saved answers with the values the application's AddXpSearch(o => ...) lambda sets.")]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets how long an identical query is served from cache, in seconds. 0 turns caching off.</summary>
    [MinimumIntegerValueValidationRule(0)]
    [NumberInputComponent(
        Label = "Response cache lifetime (seconds)",
        Order = 1,
        Tooltip = "How long an identical query is served from cache. 0 turns response caching off.",
        ExplanationText = "Applies to every search of this index - all search widgets and every API caller. A save applies to the next request; the index's cached responses are dropped with it.")]
    public int CacheTtlSeconds { get; set; }

    /// <summary>Gets or sets the maximum accepted length of the query text.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(1000)]
    [NumberInputComponent(
        Label = "Maximum query length",
        Order = 2,
        Tooltip = "Longer query text is truncated.",
        ExplanationText = "What a visitor types into the Search - Search box beyond this many characters is cut off before the search runs; the request is not refused. Applies to every API caller too. Suggestion requests are not affected.")]
    public int MaxQueryLength { get; set; }

    /// <summary>Gets or sets the server-side page size ceiling.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(1000)]
    [NumberInputComponent(
        Label = "Maximum page size",
        Order = 4,
        Tooltip = "Larger requested page sizes are clamped to this.",
        ExplanationText = "Widgets own their sizes and the index owns the caps: this clamps the Search - Results widget's 'Results per page' and every API caller, and the clamped value is what the response reports back.")]
    public int MaxPageSize { get; set; }

    /// <summary>Gets or sets the maximum number of values returned per facet dimension.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Maximum values per facet",
        Order = 5,
        Tooltip = "How many values one facet dimension returns in a response.",
        ExplanationText = "The ceiling on what the Search - Facet list and Search - Category tree widgets have to show: their own 'Values shown' / 'Nodes per level' can only display values the response carried.")]
    public int MaxFacetValues { get; set; }

    /// <summary>Gets or sets how deep paging may go.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Maximum result window",
        Order = 6,
        Tooltip = "Page multiplied by page size may not exceed this; a deeper request is refused.",
        ExplanationText = "How deep the Search - Pagination widget can go: with a page size of 20, a window of 10000 is 500 pages, and a link past that returns a validation error instead of results.")]
    public int MaxResultWindow { get; set; }

    /// <summary>Gets or sets the ceiling on the suggestion limit.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [MaximumIntegerValueValidationRule(100)]
    [NumberInputComponent(
        Label = "Maximum suggestion count",
        Order = 8,
        Tooltip = "A larger requested suggestion count is clamped to this.",
        ExplanationText = "Caps the Search - Suggestions widget's 'Maximum items' and the Search - Search box widget's 'Maximum suggestions', however high an editor sets them.")]
    public int MaxSuggestLimit { get; set; }

    /// <summary>Gets or sets how many days of this index's search analytics are kept.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Remove search analytics older than X days",
        Order = 9,
        Tooltip = "How many days of this index's search analytics are kept.",
        ExplanationText = "The 'XpSearch.QueryLogRetention' scheduled task deletes this index's query log rows and its answered popularity and synonym suggestions once they are older than this; suggestions still waiting for an answer are never deleted. Sets how far back the Analytics page can report.")]
    public int RetentionDays { get; set; }

    /// <summary>Gets or sets how many rows the retention task deletes per batch.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Retention batch size",
        Order = 10,
        Tooltip = "How many rows the retention task deletes at a time.",
        ExplanationText = "Only affects the 'XpSearch.QueryLogRetention' task's load on the database, never what is kept. Lower it if the deletion blocks other work; raise it to finish a large backlog sooner.")]
    public int RetentionBatchSize { get; set; }

    /// <summary>Gets or sets how far back query suggestions count query volume, in days.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Query suggestion window (days)",
        Order = 11,
        Tooltip = "How far back popular queries are counted from the query log.",
        ExplanationText = "Feeds the Search - Suggestions widget on an index configured (in code) to suggest popular queries or both. A short window follows what visitors search for now; a long one is steadier but slower to change.")]
    public int QuerySuggestionDays { get; set; }

    /// <summary>Gets or sets how many days of clicks the popularity signal is computed from.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Popularity lookback (days)",
        Order = 12,
        Tooltip = "How many days of result clicks the popularity signal is computed from.",
        ExplanationText = "Used by the 'XpSearch.PopularitySignal' scheduled task. The signal ranks results higher on an index that has popularity boosting turned on, and it is what the index's Suggestions listing draws on. Older clicks stop counting, so a short window reacts faster to what is popular now.")]
    public int PopularityLookbackDays { get; set; }

    /// <summary>Gets or sets how many of this index's documents the popularity signal keeps.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Popularity documents per index",
        Order = 13,
        Tooltip = "How many of the most-clicked documents the popularity signal keeps.",
        ExplanationText = "The 'XpSearch.PopularitySignal' task stores this many documents for this index; only those can be boosted when popularity boosting is on. Everything below the cut simply ranks as it did before.")]
    public int PopularityDocumentLimit { get; set; }

    /// <summary>Gets or sets how many frequent queries are examined for a suggested boost rule.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Popularity suggestion queries",
        Order = 14,
        Tooltip = "How many of this index's most frequent queries are examined for a suggested boost rule.",
        ExplanationText = "Decides how many rows the 'XpSearch.PopularitySignal' task can put on this index's Suggestions listing for review. Raise it for more candidates to approve, lower it for a shorter list.")]
    public int PopularitySuggestionQueries { get; set; }

    /// <summary>Gets or sets the reformulation window synonym mining uses, in seconds.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Synonym reformulation window (seconds)",
        Order = 15,
        Tooltip = "How long after a search with no click a following, successful search still counts as the same visitor rephrasing.",
        ExplanationText = "Used by the 'XpSearch.PopularitySignal' task when it mines candidate pairs for the Synonym suggestions listing. A wider window finds more pairs and more coincidences; a narrow one finds fewer, cleaner ones.")]
    public int SynonymWindowSeconds { get; set; }

    /// <summary>Gets or sets how often a reformulation has to happen before it is suggested.</summary>
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Synonym minimum occurrences",
        Order = 16,
        Tooltip = "How often the same rephrasing has to happen before it is suggested.",
        ExplanationText = "The noise filter on the Synonym suggestions listing: a pair seen fewer times than this is never offered for review. Raise it on a busy site, lower it on a quiet one where nothing ever reaches the listing.")]
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
            MaxPageSize = values.MaxPageSize,
            MaxFacetValues = values.MaxFacetValues,
            MaxResultWindow = values.MaxResultWindow,
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
            MaxPageSize = MaxPageSize,
            MaxFacetValues = MaxFacetValues,
            MaxResultWindow = MaxResultWindow,
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
