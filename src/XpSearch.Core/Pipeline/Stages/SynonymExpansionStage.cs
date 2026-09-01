using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Loads the index's synonyms, stopwords and field weights once for the request and expands the
/// query - as <see cref="QueryRewriteStage"/> left it - into slots of interchangeable terms
/// (spec §8.3).
/// </summary>
/// <remarks>
/// Loading happens here rather than in each of the later tuning stages so one search costs one read
/// of the tuning source - which, behind <c>XpSearch.Admin</c>, is one cache lookup (spec §8.5). The
/// rules are not loaded here: which of them fire is decided by <see cref="QueryRewriteStage"/>,
/// before a rewrite can change the query they were matched against.
/// </remarks>
public sealed class SynonymExpansionStage : ISearchStage
{
    private readonly IRelevanceTuningSource source;

    /// <summary>Initializes a new instance of the <see cref="SynonymExpansionStage"/> class.</summary>
    /// <param name="source">Where relevance tuning is read from.</param>
    public SynonymExpansionStage(IRelevanceTuningSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        this.source = source;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.SynonymExpansion;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string index = context.Request.Index;
        var variant = context.Experiment.Tuning;

        var synonyms = await source.GetSynonymsAsync(index, cancellationToken, variant).ConfigureAwait(false);
        var stopwords = await source.GetStopwordsAsync(index, cancellationToken, variant).ConfigureAwait(false);
        var weights = await source.GetFieldWeightsAsync(index, cancellationToken, variant).ConfigureAwait(false);

        context.Tuning = context.Tuning with
        {
            Synonyms = synonyms,
            Stopwords = stopwords,
            FieldWeights = weights
                .GroupBy(weight => weight.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Weight, StringComparer.OrdinalIgnoreCase)
        };

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
