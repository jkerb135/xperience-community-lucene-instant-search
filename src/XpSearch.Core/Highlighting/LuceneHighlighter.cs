using System.Net;

using Lucene.Net.Search.Highlight;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Highlighting;

/// <summary>
/// Highlighting over <c>Lucene.Net.Search.Highlight</c> with a <see cref="QueryScorer"/> and a
/// <see cref="SimpleFragmenter"/> (spec §4.6).
/// </summary>
/// <remarks>
/// The stored value is HTML-encoded <em>before</em> the highlighter runs and never after, so the only
/// unencoded markup in the result is the configured pre and post tag. Encoding first is what makes
/// the output safe to render: a document containing <c>&lt;script&gt;</c> comes back as text, and no
/// re-encoding pass can then destroy the tags the highlighter inserted.
/// </remarks>
public sealed class LuceneHighlighter : IHighlighter
{
    private const string DefaultPreTag = "<mark>";
    private const string DefaultPostTag = "</mark>";
    private const int DefaultSnippetLength = 200;

    /// <inheritdoc />
    public Dictionary<string, string>? Highlight(SearchContext context, ScoredDocument document, HighlightOptions? options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);

        string[] fields = options?.Fields ?? [];

        if (fields.Length == 0)
        {
            return null;
        }

        string preTag = options?.PreTag ?? DefaultPreTag;
        string postTag = options?.PostTag ?? DefaultPostTag;
        int snippetLength = (int)Math.Max(1, options?.SnippetLength ?? DefaultSnippetLength);

        var highlighter = new Highlighter(new SimpleHTMLFormatter(preTag, postTag), new QueryScorer(context.BaseQuery))
        {
            TextFragmenter = new SimpleFragmenter(snippetLength)
        };

        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string field in fields)
        {
            var schemaField = context.Schema.Find(field);

            if (schemaField is null)
            {
                continue;
            }

            string? stored = document.Document.Get(schemaField.Name);

            if (string.IsNullOrEmpty(stored))
            {
                continue;
            }

            // Encode first, then highlight the encoded text. The analyzer sees the same text the
            // caller will render, so the tag offsets are correct without a second encoding pass.
            string encoded = WebUtility.HtmlEncode(stored);
            string searchField = LuceneFieldNames.SearchFieldName(schemaField);
            string? fragment = highlighter.GetBestFragment(context.Analyzer, searchField, encoded);

            if (!string.IsNullOrEmpty(fragment))
            {
                snippets[schemaField.Name] = fragment;
            }
        }

        return snippets.Count == 0 ? null : snippets;
    }
}
