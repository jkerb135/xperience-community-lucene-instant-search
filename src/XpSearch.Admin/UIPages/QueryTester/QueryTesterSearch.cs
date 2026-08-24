using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.UIPages.QueryTester;

/// <summary>One execution of the query tester: the response plus the query-level explanations.</summary>
/// <param name="Response">The search response, produced with <c>explain=true</c>.</param>
/// <param name="QueryExplanations">
/// The explanation lines that apply to every hit - synonym expansions, stopword removals, field
/// weights and query-time rules - in application order.
/// </param>
public sealed record QueryTesterSideResult(SearchResponse Response, IReadOnlyList<string> QueryExplanations);

/// <summary>
/// Runs one query tester search, with the index's relevance tuning applied or with none of it
/// (spec §8.4, the "with rules / without rules" toggle).
/// </summary>
public interface IQueryTesterSearch
{
    /// <summary>Executes the request.</summary>
    /// <param name="request">The request. <c>explain</c> is set by the caller.</param>
    /// <param name="applyTuning">
    /// <see langword="true"/> to run the index's rules, synonyms, stopwords and field weights;
    /// <see langword="false"/> to run the query as Core alone would.
    /// </param>
    /// <param name="contactGroup">
    /// Code name of the contact group to simulate, so an admin can see a group-scoped rule fire
    /// without being a member. Empty runs as the admin's own contact would.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response and the query-level explanations.</returns>
    Task<QueryTesterSideResult> ExecuteAsync(SearchRequest request, bool applyTuning, string contactGroup, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IQueryTesterSearch"/>: assembles a <see cref="SearchPipeline"/> from the
/// registered stages for each side of the comparison.
/// </summary>
/// <remarks>
/// <para>
/// It does not call the registered <see cref="ISearchPipeline"/>, for two reasons. The registered one
/// is the caching decorator, and a tester that answers from a cache cannot show the effect of a rule
/// a marketer just saved. And the tuning a search uses is loaded into
/// <see cref="SearchContext.Tuning"/> by <see cref="SynonymExpansionStage"/> from the DI-registered
/// <see cref="IRelevanceTuningSource"/>, so "without rules" can only be expressed by swapping that
/// one stage for one built over <see cref="EmptyRelevanceTuningSource"/>. Every other stage is the
/// instance the live pipeline runs.
/// </para>
/// <para>
/// Building the pipeline by hand also keeps testing a query out of the analytics: the search activity
/// and the aggregate query log row are written by <see cref="ISearchRequestJournal"/> from the caching
/// decorator (spec §9.2), which the tester does not go through. When a contact group is being
/// simulated, <see cref="ResolveContactGroupsStage"/> is swapped for one that seeds that one group;
/// both sides get the same treatment, so the comparison stays honest.
/// </para>
/// </remarks>
public sealed class QueryTesterSearch : IQueryTesterSearch
{
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;
    private readonly ISearchStage[] stages;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="QueryTesterSearch"/> class.</summary>
    /// <param name="accessor">The Lucene seam.</param>
    /// <param name="schemaProvider">Supplies the schema of the index being searched.</param>
    /// <param name="stages">Every registered pipeline stage.</param>
    /// <param name="time">Clock used to evaluate rule schedules.</param>
    public QueryTesterSearch(
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        IEnumerable<ISearchStage> stages,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(time);

        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
        this.stages = [.. stages];
        this.time = time;
    }

    /// <inheritdoc />
    public async Task<QueryTesterSideResult> ExecuteAsync(
        SearchRequest request,
        bool applyTuning,
        string contactGroup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var capture = new CaptureExplanationsStage();
        bool simulate = !string.IsNullOrWhiteSpace(contactGroup);

        var sideStages = new List<ISearchStage>(
            stages.Where(stage => (applyTuning || stage is not SynonymExpansionStage)
                && !(simulate && stage is ResolveContactGroupsStage)))
        {
            capture
        };

        if (simulate)
        {
            sideStages.Add(new SimulateContactGroupStage(contactGroup.Trim()));
        }

        if (!applyTuning)
        {
            sideStages.Add(new SynonymExpansionStage(new EmptyRelevanceTuningSource(), time));
        }

        var response = await new SearchPipeline(accessor, schemaProvider, sideStages)
            .ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return new QueryTesterSideResult(response, capture.QueryExplanations);
    }

    /// <summary>
    /// Puts one contact group on the context instead of resolving the admin's own, so the tester can
    /// show what a member of that group would get (ADR-0021). It replaces
    /// <see cref="ResolveContactGroupsStage"/> and runs in its slot, before any tuning stage.
    /// </summary>
    private sealed class SimulateContactGroupStage : ISearchStage
    {
        private readonly IReadOnlySet<string> group;

        internal SimulateContactGroupStage(string codeName) => group = ContactGroupSets.Of([codeName]);

        public int Order => SearchStageOrder.ResolveContactGroups;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.ContactGroups = group;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A terminal stage that keeps the query-level explanations, which the response only carries
    /// merged into every hit's <c>ranking.boosts</c>.
    /// </summary>
    private sealed class CaptureExplanationsStage : ISearchStage
    {
        public IReadOnlyList<string> QueryExplanations { get; private set; } = [];

        public int Order => int.MaxValue;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            QueryExplanations = [.. context.QueryExplanations];

            return Task.CompletedTask;
        }
    }
}
