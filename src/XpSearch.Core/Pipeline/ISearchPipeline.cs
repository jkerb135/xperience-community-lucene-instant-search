using XpSearch.Core.Contract;

namespace XpSearch.Core.Pipeline;

/// <summary>
/// The order slots of the query pipeline (spec §4.4). Stages run ascending; consumers insert their
/// own with <c>services.AddXpSearchStage&lt;T&gt;(order)</c>.
/// </summary>
/// <remarks>
/// The slots reserved for later phases are declared but not filled, so Phase 6 can drop stages in
/// without renumbering anything that already ships.
/// </remarks>
public static class SearchStageOrder
{
    /// <summary>Validate and normalize the request: trim, lowercase, length cap, filter validation.</summary>
    public const int Normalize = 100;

    /// <summary>Resolve the contact groups of the visitor, so rules can be scoped to one (ADR-0021).</summary>
    public const int ResolveContactGroups = 150;

    /// <summary>Resolve the running experiment and the visitor's variant, before any tuning is read (XP-1).</summary>
    public const int ResolveExperiment = 160;

    /// <summary>Select the rules that fire and apply their query rewrites (ADR-0022).</summary>
    public const int QueryRewrite = 175;

    /// <summary>Load the index's synonyms, stopwords and field weights, and expand the query.</summary>
    public const int SynonymExpansion = 200;

    /// <summary>Drop the index's configured stopwords from the query.</summary>
    public const int StopwordRemoval = 300;

    /// <summary>Build the Lucene query from the normalized free text.</summary>
    public const int BuildQuery = 400;

    /// <summary>Apply facet refinements as drill-downs or boolean clauses.</summary>
    public const int FacetFilters = 500;

    /// <summary>Apply numeric refinements as range queries.</summary>
    public const int NumericFilters = 600;

    /// <summary>Apply the admin-configured boost and filter rules to the query.</summary>
    public const int BoostRules = 700;

    /// <summary>Boost the documents visitors click, for an index that opted in (RK-1).</summary>
    public const int PopularityBoost = 750;

    /// <summary>Execute the search and collect documents and facet counts.</summary>
    public const int Execute = 800;

    /// <summary>Explain every score checkpoint against the page, so each result carries its per-stage scores (QT-2).</summary>
    public const int ScoreBreakdown = 850;

    /// <summary>Reorder the executed results according to the pin and bury rules.</summary>
    public const int PinnedAndBuried = 900;

    /// <summary>Project the raw facet counts onto the requested dimensions.</summary>
    public const int CollectFacets = 950;

    /// <summary>Generate highlighted snippets.</summary>
    public const int Highlight = 1000;

    /// <summary>Project the result onto the response DTO.</summary>
    public const int Project = 1100;

    /// <summary>
    /// Retired. Search activity logging was a stage here until it moved to
    /// <c>ISearchRequestJournal</c>, called by the caching decorator so a cached search is journaled
    /// too. The slot stays free for a consumer's own terminal stage.
    /// </summary>
    public const int LogActivity = 1200;
}

/// <summary>
/// One step of the query pipeline. Stages are resolved from DI and executed in ascending
/// <see cref="Order"/>; a stage mutates the <see cref="SearchContext"/> in place.
/// </summary>
public interface ISearchStage
{
    /// <summary>Gets the position of this stage in the pipeline. See <see cref="SearchStageOrder"/>.</summary>
    int Order { get; }

    /// <summary>Runs the stage.</summary>
    /// <param name="context">State of the request being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the stage is done.</returns>
    Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a search request through the ordered stages and returns the response.
/// </summary>
public interface ISearchPipeline
{
    /// <summary>Executes one search.</summary>
    /// <param name="request">The request to run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with <c>tookMs</c> and <c>queryId</c> already set.</returns>
    /// <exception cref="Abstractions.IndexNotFoundException">The requested index is not registered.</exception>
    /// <exception cref="Abstractions.SearchValidationException">The request is not valid.</exception>
    Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken);
}
