using System.Diagnostics;

using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;
using XpSearch.Core.Personalization;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline;

/// <summary>One document that matched, as it came back from Lucene.</summary>
/// <param name="Document">The stored fields of the document.</param>
/// <param name="Score">The raw Lucene score, before any Phase 5 boost.</param>
public sealed record ScoredDocument(Document Document, float Score);

/// <summary>
/// The mutable state a search request carries through <see cref="ISearchPipeline"/>. Every stage
/// reads what earlier stages produced and writes what later stages consume.
/// </summary>
public sealed class SearchContext
{
    /// <summary>Initializes a new instance of the <see cref="SearchContext"/> class.</summary>
    /// <param name="request">The deserialized request, as received.</param>
    /// <param name="schema">Schema of the index being searched.</param>
    /// <param name="analyzer">The index's own analyzer, used for both parsing and highlighting.</param>
    /// <param name="facetsConfig">The index's facet configuration, or <see langword="null"/> when it has no taxonomy.</param>
    /// <param name="cancellationToken">Cancellation token of the HTTP request.</param>
    public SearchContext(
        SearchRequest request,
        IndexSchema schema,
        Analyzer analyzer,
        FacetsConfig? facetsConfig,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Request = request;
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        FacetsConfig = facetsConfig;
        CancellationToken = cancellationToken;
        QueryText = request.Query ?? string.Empty;
        IndexName = request.Index ?? string.Empty;
    }

    /// <summary>Gets the <see cref="Stopwatch"/> timestamp taken when the search started.</summary>
    public long StartedTimestamp { get; } = Stopwatch.GetTimestamp();

    /// <summary>Gets how long the search has been running. The response's <c>tookMs</c> comes from this.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(StartedTimestamp);

    /// <summary>Gets the request as received. Normalized values live on this context, not on it.</summary>
    public SearchRequest Request { get; }

    /// <summary>Gets the schema of the index being searched.</summary>
    public IndexSchema Schema { get; }

    /// <summary>
    /// Gets or sets the registered code name of the index being searched, which is what per-index
    /// settings are keyed by (AR-2). <see cref="SearchPipeline"/> resolves it; it falls back to the
    /// name the request asked for.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>Gets the analyzer of the index being searched.</summary>
    public Analyzer Analyzer { get; }

    /// <summary>Gets the facet configuration of the index, or <see langword="null"/> when it has no taxonomy sidecar.</summary>
    public FacetsConfig? FacetsConfig { get; }

    /// <summary>Gets the cancellation token of the HTTP request.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets or sets the normalized free-text query: trimmed, lowercased and length-capped.</summary>
    public string QueryText { get; set; }

    /// <summary>Gets or sets the one-based page number, after validation.</summary>
    public int Page { get; set; }

    /// <summary>Gets or sets the page size, after validation and server-side clamping.</summary>
    public int PageSize { get; set; }

    /// <summary>Gets or sets the facet dimensions counts were requested for.</summary>
    public IReadOnlyList<string> RequestedFacets { get; set; } = [];

    /// <summary>
    /// Gets or sets the validated facet refinements, one entry per attribute and all ANDed. The
    /// attribute of each entry is resolved to the schema's own casing.
    /// </summary>
    public IReadOnlyList<FacetFilter> FacetFilters { get; set; } = [];

    /// <summary>Gets or sets the validated numeric refinements, all ANDed.</summary>
    public IReadOnlyList<NumericFilter> NumericFilters { get; set; } = [];

    /// <summary>Gets or sets the field to sort on, or <see langword="null"/> for relevance ordering.</summary>
    public SchemaField? SortField { get; set; }

    /// <summary>Gets or sets a value indicating whether the sort is descending.</summary>
    public bool SortDescending { get; set; }

    /// <summary>Gets or sets the fields to project into each result's attributes; empty means every retrievable field.</summary>
    public IReadOnlyList<string> Fields { get; set; } = [];

    /// <summary>
    /// Gets or sets the query everything else wraps: free text, language filter and any refinement
    /// that could not be expressed as a drill-down.
    /// </summary>
    public Query BaseQuery { get; set; } = new MatchAllDocsQuery();

    /// <summary>
    /// Gets or sets <see cref="BaseQuery"/> rewritten against the index reader, set once per request by
    /// <c>ExecuteSearchStage</c> while the searcher lease is still open. Highlighting scores against
    /// this: a rewritten query holds the concrete matched terms, so a multi-term query (typo
    /// tolerance) is expanded once per request instead of once per document per field.
    /// <see langword="null"/> when no highlighting was requested.
    /// </summary>
    public Query? HighlightQuery { get; set; }

    /// <summary>
    /// Gets the drill-down refinements, keyed by facet dimension. Executed through
    /// <see cref="DrillSideways"/> so counts for a drilled dimension stay "what if I picked another value".
    /// </summary>
    public IDictionary<string, IReadOnlyList<string>> DrillDown { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the refinements that must hold for a document to belong in the results: the language
    /// filter and every facet and numeric filter, whether or not it was expressed as a drill-down.
    /// A pinned document is only injected when it also matches these (spec §8.3).
    /// </summary>
    public BooleanQuery ActiveFilters { get; } = new();

    /// <summary>Gets or sets the relevance tuning configured for this index (spec §8.2).</summary>
    public TuningSet Tuning { get; set; } = TuningSet.Empty;

    /// <summary>
    /// Gets or sets the code names of the contact groups the visitor belongs to, resolved once per
    /// request by <c>ResolveContactGroupsStage</c> (ADR-0021). Empty when there is no contact, no
    /// consent to tracking, or no HTTP context - which leaves only the unscoped rules applying.
    /// </summary>
    public IReadOnlySet<string> ContactGroups { get; set; } = ContactGroupSets.None;

    /// <summary>
    /// Gets or sets the experiment that applies to this request and the variant the visitor was
    /// bucketed into, resolved by <c>ResolveExperimentStage</c> (XP-1). The tuning stages read
    /// <see cref="ExperimentAssignment.Tuning"/>; the default means "no experiment, live tuning".
    /// </summary>
    public ExperimentAssignment Experiment { get; set; } = ExperimentAssignment.None;

    /// <summary>
    /// Gets or sets the synonym-expanded query: one slot per query position, each holding the
    /// interchangeable terms for that position with the original first. Empty means "no expansion".
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> QuerySlots { get; set; } = [];

    /// <summary>
    /// Gets the <c>explain=true</c> entries that apply to every hit: field weights, synonym
    /// expansions and query-time rules.
    /// </summary>
    public IList<string> QueryExplanations { get; } = new List<string>();

    /// <summary>
    /// Gets the <c>explain=true</c> entries that apply to one hit, keyed by its result id.
    /// </summary>
    public IDictionary<string, List<string>> DocumentExplanations { get; } =
        new Dictionary<string, List<string>>(StringComparer.Ordinal);

    /// <summary>Gets or sets the total number of matching documents across all pages.</summary>
    public int Total { get; set; }

    /// <summary>Gets or sets the documents of the requested page, in ranked order.</summary>
    public IReadOnlyList<ScoredDocument> Documents { get; set; } = [];

    /// <summary>Gets or sets the raw facet counts produced by the search, before projection.</summary>
    public Lucene.Net.Facet.Facets? Facets { get; set; }

    /// <summary>
    /// Gets or sets the projected facet values: requested dimensions only, non-zero counts only,
    /// each list ordered by count descending then value ascending.
    /// <see langword="null"/> when the request asked for no facets.
    /// </summary>
    public Dictionary<string, FacetValue[]>? FacetValues { get; set; }

    /// <summary>
    /// Gets or sets the highlighted snippets, one entry per document in <see cref="Documents"/> and in
    /// the same order. An entry is <see langword="null"/> when nothing was highlighted for it.
    /// </summary>
    public IReadOnlyList<Dictionary<string, string>?> Highlights { get; set; } = [];

    /// <summary>
    /// Gets or sets the destination of the first matching redirect rule, in precedence order, or
    /// <see langword="null"/> when none matched (spec §8.2). The projection stage copies it onto
    /// the response; the results are returned alongside it and following it is the client's call.
    /// </summary>
    public SearchRedirect? Redirect { get; set; }

    /// <summary>Gets or sets the response under construction. The projection stage creates it.</summary>
    public SearchResponse? Response { get; set; }
}
