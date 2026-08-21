using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Produces the highlighted snippets for every document on the page.
/// </summary>
public sealed class HighlightStage : ISearchStage
{
    private readonly IHighlighter highlighter;

    /// <summary>Initializes a new instance of the <see cref="HighlightStage"/> class.</summary>
    /// <param name="highlighter">The highlighter to use.</param>
    public HighlightStage(IHighlighter highlighter)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        this.highlighter = highlighter;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.Highlight;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Highlights =
        [
            .. context.Documents.Select(document => highlighter.Highlight(context, document, context.Request.Highlight))
        ];

        return Task.CompletedTask;
    }
}
