using XpSearch.Core.Analytics;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Options;

/// <summary>
/// How the <c>/api/xpsearch/suggest</c> endpoint answers for a given index (spec §4.3).
/// </summary>
public enum SuggestMode
{
    /// <summary>
    /// Prefix-match the index's suggest field and return the top matching documents, for a dropdown
    /// that shows actual results. This is the default and the only mode implemented in Phase 1.
    /// </summary>
    Documents,

    /// <summary>
    /// Suggest previously logged popular queries. Requires the Phase 6 analytics store; until then
    /// the endpoint returns an empty list and logs a warning (spec §13.6).
    /// </summary>
    QuerySuggestions,

    /// <summary>
    /// Both at once, queries first: one response carrying the popular queries that start with the
    /// prefix and the documents that match it, which the shipped panel renders as two groups.
    /// </summary>
    Mixed
}

/// <summary>
/// One sort order a request may ask for by name through <c>sort</c>.
/// </summary>
/// <param name="Field">The attribute to sort on. It must be marked sortable in the index schema.</param>
/// <param name="Descending">Whether to sort descending.</param>
public sealed record SortKey(string Field, bool Descending = false);

/// <summary>
/// Per-index settings. Reachable through <see cref="XpSearchOptions.Indexes"/>; an index with no
/// entry uses the defaults on this type.
/// </summary>
public sealed class XpSearchIndexOptions
{
    /// <summary>
    /// Gets the sort keys this index accepts in <c>sort</c>, keyed by the name a request sends -
    /// <c>o.Indexes["MyIndex"].SortKeys["newest"] = new SortKey("PublishedAt", Descending: true)</c>.
    /// </summary>
    /// <remarks>
    /// A request may always name a sortable attribute directly with the <c>_asc</c> and <c>_desc</c>
    /// suffixes; a configured key is how an index publishes a stable, presentable name for one.
    /// </remarks>
    public IDictionary<string, SortKey> SortKeys { get; } = new Dictionary<string, SortKey>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets how <c>/suggest</c> answers for this index. Defaults to <see cref="SuggestMode.Documents"/>.</summary>
    public SuggestMode SuggestMode { get; set; } = SuggestMode.Documents;

    private string suggestField = IndexSchemaProvider.TitleAttribute;

    /// <summary>
    /// Gets or sets the attribute document suggestions prefix-match and display.
    /// Defaults to <c>title</c>, the attribute every document carries its display name under.
    /// </summary>
    /// <remarks>
    /// On most Kentico sites the display name is the web page item name, which is a slug with a
    /// generated suffix - set this to a human-readable field of your own.
    /// </remarks>
    public string SuggestField
    {
        get => suggestField;

        set
        {
            suggestField = value;
            SuggestFieldConfigured = true;
        }
    }

    /// <summary>Gets a value indicating whether <see cref="SuggestField"/> was set rather than left at its default.</summary>
    internal bool SuggestFieldConfigured { get; private set; }

    /// <summary>
    /// Gets or sets whether a search of this index that found nothing offers a corrected spelling in
    /// <c>didYouMean</c>. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The correction is spelled against the index's own terms and is only offered once the server
    /// has verified it actually returns results, so it costs one extra search per dead end.
    /// </remarks>
    public bool DidYouMean { get; set; } = true;

    /// <summary>
    /// Gets or sets how many of the index's most-searched queries a search that found nothing offers
    /// in <c>popularSearches</c>. Defaults to <c>0</c>, which turns the feature off.
    /// </summary>
    /// <remarks>
    /// Opt-in on purpose: the queries come from the query log, so turning it on shows anonymous
    /// visitors what other visitors searched for.
    /// </remarks>
    public int PopularSearchesOnNoResults { get; set; }
}

/// <summary>
/// Configuration of the search API, bound through <c>services.AddXpSearch(o =&gt; ...)</c>.
/// </summary>
public sealed class XpSearchOptions
{
    /// <summary>Gets or sets how long an identical query is served from cache. Defaults to 60 seconds (spec §4.7).</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the maximum accepted length of <c>query</c>; longer text is truncated. Defaults to 256.</summary>
    public int MaxQueryLength { get; set; } = 256;

    /// <summary>
    /// Gets or sets the page size used when a request omits <c>pageSize</c>. Defaults to 20. Code-only
    /// and not per index (AR-3): it is for API callers that send no size; widgets always send one.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets the server-side page size ceiling. Larger requested values are clamped to it and
    /// the clamped value is reported back in <c>pageSize</c>. Defaults to 100; the contract's own
    /// ceiling of 1000 is rejected above, not clamped.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum number of values returned per facet dimension. Defaults to 100.</summary>
    public int MaxFacetValues { get; set; } = 100;

    /// <summary>
    /// Gets or sets how deep paging may go: <c>page * pageSize</c> must not exceed it, because
    /// Lucene collects every document up to that rank. A deeper request is a 400. Defaults to 10000.
    /// </summary>
    public int MaxResultWindow { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the number of suggestions returned when a request omits <c>limit</c>. Defaults to 5.
    /// Code-only and not per index (AR-3): it is for API callers that send no size; widgets always send one.
    /// </summary>
    public int DefaultSuggestLimit { get; set; } = 5;

    /// <summary>Gets or sets the ceiling on <c>limit</c> for <c>/suggest</c>. Defaults to 20.</summary>
    public int MaxSuggestLimit { get; set; } = 20;

    /// <summary>Gets the analytics settings: query log retention and query suggestions (spec §9.2).</summary>
    public XpSearchAnalyticsOptions Analytics { get; } = new();

    /// <summary>Gets the per-index settings, keyed by index code name (case-insensitive).</summary>
    /// <remarks>The indexer creates a missing entry, so <c>o.Indexes["MyIndex"].SuggestMode = ...</c> is enough to configure an index.</remarks>
    public IndexOptionsCollection Indexes { get; } = [];
}

/// <summary>
/// Per-index settings keyed by index code name. Reading a missing key creates and stores a default
/// <see cref="XpSearchIndexOptions"/> so configuration lambdas do not need a null dance.
/// </summary>
public sealed class IndexOptionsCollection : Dictionary<string, XpSearchIndexOptions>
{
    /// <summary>Initializes a new instance of the <see cref="IndexOptionsCollection"/> class.</summary>
    public IndexOptionsCollection()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    /// <summary>Gets or sets the settings of an index, creating them on first read.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The settings for <paramref name="indexName"/>.</returns>
    public new XpSearchIndexOptions this[string indexName]
    {
        get
        {
            if (!TryGetValue(indexName, out var options))
            {
                options = new XpSearchIndexOptions();
                base[indexName] = options;
            }

            return options;
        }

        set => base[indexName] = value;
    }
}
