using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// The first tuning stage (spec §8.3). Loads the index's rules, synonyms, stopwords and field
/// weights once for the request, selects the rules that apply - the one place a rule's schedule,
/// query pattern and contact group scope are all checked - and expands the query into slots of
/// interchangeable terms.
/// </summary>
/// <remarks>
/// Loading happens here rather than in each of the four tuning stages so one search costs one read
/// of the tuning source - which, behind <c>XpSearch.Admin</c>, is one cache lookup (spec §8.5).
/// </remarks>
public sealed class SynonymExpansionStage : ISearchStage
{
    private readonly IRelevanceTuningSource source;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="SynonymExpansionStage"/> class.</summary>
    /// <param name="source">Where relevance tuning is read from.</param>
    /// <param name="time">Clock used to evaluate rule schedules; substitutable in tests.</param>
    public SynonymExpansionStage(IRelevanceTuningSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);

        this.source = source;
        this.time = time;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.SynonymExpansion;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string index = context.Request.Index;

        var rules = await source.GetRulesAsync(index, cancellationToken).ConfigureAwait(false);
        var synonyms = await source.GetSynonymsAsync(index, cancellationToken).ConfigureAwait(false);
        var stopwords = await source.GetStopwordsAsync(index, cancellationToken).ConfigureAwait(false);
        var weights = await source.GetFieldWeightsAsync(index, cancellationToken).ConfigureAwait(false);

        context.Tuning = new TuningSet(
            RuleSelection.Active(rules, context.QueryText, time.GetUtcNow().UtcDateTime, context.ContactGroups),
            synonyms,
            stopwords,
            weights
                .GroupBy(weight => weight.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Weight, StringComparer.OrdinalIgnoreCase));

        context.QuerySlots = SynonymExpansion.Expand(context.QueryText, synonyms);

        if (context.Request.Explain ?? false)
        {
            foreach (string alternative in context.QuerySlots.SelectMany(slot => slot.Skip(1)))
            {
                context.QueryExplanations.Add(SynonymExpansion.Explain(alternative));
            }
        }
    }
}
