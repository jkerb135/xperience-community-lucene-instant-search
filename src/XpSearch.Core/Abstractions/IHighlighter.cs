using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Abstractions;

/// <summary>
/// Produces the HTML-safe snippets that land in <c>Hit._highlights</c>.
/// </summary>
public interface IHighlighter
{
    /// <summary>Highlights one document.</summary>
    /// <param name="context">The executed search; supplies the query and the index's analyzer.</param>
    /// <param name="document">The document to highlight.</param>
    /// <param name="options">The request's highlight options, or <see langword="null"/> for the defaults.</param>
    /// <returns>
    /// Snippets keyed by field name, or <see langword="null"/> when nothing was highlighted. Values are
    /// HTML-encoded content with the configured tags inserted, so they are safe to render as HTML.
    /// </returns>
    Dictionary<string, string>? Highlight(SearchContext context, ScoredDocument document, HighlightOptions? options);
}
