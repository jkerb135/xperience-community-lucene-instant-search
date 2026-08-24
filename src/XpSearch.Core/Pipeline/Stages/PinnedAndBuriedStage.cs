using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Reorders the executed results according to the pin and bury rules (spec §8.3).
/// </summary>
/// <remarks>
/// The documented rules, in the order a support ticket will ask about them:
/// <list type="bullet">
/// <item>Consequences are applied in precedence order (priority, then rule id, then the order the
/// rule lists them) and the first one to name a document wins; later ones naming the same document
/// are ignored.</item>
/// <item>Bury removes the document from the page that came back and decrements the total. Taking a
/// document out of the result set altogether is <c>Hide</c>, which <see cref="BoostRulesStage"/>
/// applies before the search runs.</item>
/// <item>Pin moves the document to its one-based position. Only the page that contains that position
/// is touched, so pinning to 3 does nothing on page 2.</item>
/// <item>A pinned document that the query did not match is loaded by id and injected only if it also
/// matches every active filter - the language, facet and numeric refinements in play.</item>
/// </list>
/// </remarks>
public sealed class PinnedAndBuriedStage : ISearchStage
{
    private readonly ILuceneIndexAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="PinnedAndBuriedStage"/> class.</summary>
    /// <param name="accessor">The Lucene seam, used to load a pinned document that is not in the result set.</param>
    public PinnedAndBuriedStage(ILuceneIndexAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        this.accessor = accessor;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.PinnedAndBuried;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var placements = context.Tuning.Rules
            .SelectMany(rule => rule.Consequences.Select(consequence => (Rule: rule, Consequence: consequence)))
            .Where(placement => placement.Consequence is RuleConsequence.Pin or RuleConsequence.Bury)
            .Where(placement => !string.IsNullOrWhiteSpace(TargetOf(placement.Consequence)))
            .ToList();

        if (placements.Count == 0)
        {
            return Task.CompletedTask;
        }

        var documents = context.Documents.ToList();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        int offset = (context.Page - 1) * context.PageSize;

        foreach (var placement in placements.Where(placement => handled.Add(TargetOf(placement.Consequence))))
        {
            if (placement.Consequence is RuleConsequence.Bury bury)
            {
                Bury(context, documents, bury.TargetId);
                continue;
            }

            Pin(context, documents, placement.Rule, (RuleConsequence.Pin)placement.Consequence, offset);
        }

        context.Documents = documents;
        return Task.CompletedTask;
    }

    private static void Bury(SearchContext context, List<ScoredDocument> documents, string targetId)
    {
        int index = IndexOf(documents, targetId);

        if (index < 0)
        {
            return;
        }

        documents.RemoveAt(index);
        context.Total = Math.Max(0, context.Total - 1);
    }

    /// <summary>The document a pin or a bury names.</summary>
    private static string TargetOf(RuleConsequence consequence) =>
        consequence switch
        {
            RuleConsequence.Pin pin => pin.TargetId ?? string.Empty,
            RuleConsequence.Bury bury => bury.TargetId ?? string.Empty,
            _ => string.Empty
        };

    private void Pin(SearchContext context, List<ScoredDocument> documents, TuningRule rule, RuleConsequence.Pin pin, int offset)
    {
        int slot = pin.Position - 1 - offset;

        if (slot < 0 || slot >= context.PageSize)
        {
            return;
        }

        int index = IndexOf(documents, pin.TargetId);

        if (index >= 0)
        {
            var moved = documents[index];
            documents.RemoveAt(index);
            documents.Insert(Math.Min(slot, documents.Count), moved);
        }
        else
        {
            if (Load(context, pin.TargetId) is not { } injected)
            {
                return;
            }

            documents.Insert(Math.Min(slot, documents.Count), injected);
            context.Total++;

            if (documents.Count > context.PageSize)
            {
                documents.RemoveAt(documents.Count - 1);
            }
        }

        if (context.Request.Explain ?? false)
        {
            if (!context.DocumentExplanations.TryGetValue(pin.TargetId, out var explanations))
            {
                explanations = [];
                context.DocumentExplanations[pin.TargetId] = explanations;
            }

            explanations.Add(RuleSelection.Explain(rule));
        }
    }

    /// <summary>Loads a document by result id, but only if it also matches every active filter.</summary>
    private ScoredDocument? Load(SearchContext context, string targetId)
    {
        var query = new BooleanQuery
        {
            { new TermQuery(new Term(BaseDocumentProperties.ID, targetId)), Occur.MUST }
        };

        foreach (var clause in context.ActiveFilters.Clauses)
        {
            query.Add(clause);
        }

        return accessor.UseSearcher(context.Request.Index, searcher =>
        {
            var hits = searcher.Search(query, 1);

            return hits.ScoreDocs.Length == 0
                ? null
                : new ScoredDocument(searcher.Doc(hits.ScoreDocs[0].Doc), hits.ScoreDocs[0].Score);
        });
    }

    private static int IndexOf(List<ScoredDocument> documents, string targetId) =>
        documents.FindIndex(document =>
            string.Equals(ProjectResponseStage.ResolveResultId(document.Document), targetId, StringComparison.Ordinal));
}
