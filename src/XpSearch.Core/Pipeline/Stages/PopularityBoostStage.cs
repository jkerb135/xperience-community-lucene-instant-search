using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Popularity;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Boosts the documents an index's visitors actually click, after the admin-configured rules have had
/// their say (RK-1). Registered always, applied only for an index that opted in - an index that has
/// not, or one with no click evidence yet, gets an empty signal and this stage does nothing.
/// </summary>
/// <remarks>
/// The boost is bounded by <see cref="PopularitySignal.MaxFactor"/> and applied the same way
/// <see cref="BoostRulesStage"/> applies a rule's boost: a SHOULD clause on the document's id next to
/// the query everything else built. It applies identically to both variants of an experiment - the
/// opt-in is a property of the index, not of a tuning variant (ADR-0025).
/// </remarks>
public sealed class PopularityBoostStage : ISearchStage
{
    private readonly IPopularitySignalStore store;

    /// <summary>Initializes a new instance of the <see cref="PopularityBoostStage"/> class.</summary>
    /// <param name="store">Where the popularity signal is read from.</param>
    public PopularityBoostStage(IPopularitySignalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.PopularityBoost;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string indexName = context.Request.Index ?? string.Empty;

        if (string.IsNullOrWhiteSpace(indexName))
        {
            return;
        }

        var signal = await store.GetSignalAsync(indexName, cancellationToken).ConfigureAwait(false);
        var boosts = signal.Boosts();

        if (boosts.Count == 0)
        {
            return;
        }

        var boosted = new BooleanQuery { { context.BaseQuery, Occur.MUST } };

        foreach ((string documentId, double factor) in boosts)
        {
            // Lucene.NET 4.8 has no BoostQuery wrapper; the factor is a property of the query itself.
            var target = new TermQuery(new Term(BaseDocumentProperties.ID, documentId)) { Boost = (float)factor };

            boosted.Add(target, Occur.SHOULD);
        }

        context.BaseQuery = boosted;
        context.ScoreCheckpoints.Add(new ScoreCheckpoint("Popularity boost", boosted));

        if (context.Request.Explain ?? false)
        {
            context.QueryExplanations.Add(
                $"Popularity boost from {boosts.Count} document(s), up to {PopularitySignal.MaxFactor:0.0}x (signal {signal.Version}).");
        }
    }
}

