using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Turns the score checkpoints the scoring stages left into the per-stage scores of every document
/// on the page (QT-2), so the query tester can show how a score was built.
/// </summary>
/// <remarks>
/// Every boost is folded into <see cref="SearchContext.BaseQuery"/> before the search runs, so the
/// contribution of one stage cannot be read off the result. Each scoring stage instead pushes a
/// <see cref="ScoreCheckpoint"/> - the query as it stood after it - and this stage asks Lucene to
/// explain each checkpoint against each document: <c>Explain(query, docId).Value</c> is exactly the
/// score that query gives that document. A page of results times a handful of checkpoints is a few
/// hundred explains, and it only runs for <c>explain=true</c>, which the tester sets and visitors
/// never do.
/// </remarks>
public sealed class ScoreBreakdownStage : ISearchStage
{
    private readonly ILuceneIndexAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="ScoreBreakdownStage"/> class.</summary>
    /// <param name="accessor">The Lucene seam, used to explain a checkpoint against a document.</param>
    public ScoreBreakdownStage(ILuceneIndexAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        this.accessor = accessor;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.ScoreBreakdown;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!(context.Request.Explain ?? false) || context.Documents.Count == 0 || context.ScoreCheckpoints.Count == 0)
        {
            return Task.CompletedTask;
        }

        accessor.UseSearcher(context.Request.Index, searcher =>
        {
            foreach (var document in context.Documents)
            {
                string id = ProjectResponseStage.ResolveResultId(document.Document);

                context.ScoreSteps[id] = ScoreBreakdown.StepsFor(searcher, context, id, document.DocId);
            }

            return true;
        });

        return Task.CompletedTask;
    }
}
