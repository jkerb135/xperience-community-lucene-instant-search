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
    FederatedHits,

    /// <summary>
    /// Suggest previously logged popular queries. Requires the Phase 6 analytics store; until then
    /// the endpoint returns an empty list and logs a warning (spec §13.6).
    /// </summary>
    QuerySuggestions
}

/// <summary>
/// Per-index settings. Reachable through <see cref="XpSearchOptions.Indexes"/>; an index with no
/// entry uses the defaults on this type.
/// </summary>
public sealed class XpSearchIndexOptions
{
    /// <summary>Gets or sets how <c>/suggest</c> answers for this index. Defaults to <see cref="SuggestMode.FederatedHits"/>.</summary>
    public SuggestMode SuggestMode { get; set; } = SuggestMode.FederatedHits;

    /// <summary>
    /// Gets or sets the field federated-hits suggestions prefix-match and display.
    /// Defaults to <c>Title</c>, the field <c>XpSearchIndexingStrategy</c> writes page titles to.
    /// </summary>
    public string SuggestField { get; set; } = "Title";
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

    /// <summary>Gets or sets the page size used when a request omits <c>hitsPerPage</c>. Defaults to 20.</summary>
    public int DefaultHitsPerPage { get; set; } = 20;

    /// <summary>
    /// Gets or sets the server-side page size ceiling. Larger requested values are clamped to it and
    /// the clamped value is reported back in <c>hitsPerPage</c>. Defaults to 100; the contract's own
    /// ceiling of 1000 is rejected above, not clamped.
    /// </summary>
    public int MaxHitsPerPage { get; set; } = 100;

    /// <summary>Gets or sets the maximum number of values returned per facet dimension. Defaults to 100.</summary>
    public int MaxFacetValues { get; set; } = 100;

    /// <summary>
    /// Gets or sets how deep paging may go: <c>(page + 1) * hitsPerPage</c> must not exceed it, because
    /// Lucene collects every document up to that rank. A deeper request is a 400. Defaults to 10000.
    /// </summary>
    public int MaxResultWindow { get; set; } = 10_000;

    /// <summary>Gets or sets the number of suggestions returned when a request omits <c>maxItems</c>. Defaults to 5.</summary>
    public int DefaultMaxSuggestions { get; set; } = 5;

    /// <summary>Gets or sets the ceiling on <c>maxItems</c> for <c>/suggest</c>. Defaults to 20.</summary>
    public int MaxSuggestions { get; set; } = 20;

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
