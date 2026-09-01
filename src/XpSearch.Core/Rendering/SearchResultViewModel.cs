using System.Text.Json;

using Microsoft.AspNetCore.Html;

using XpSearch.Core.Contract;

namespace XpSearch.Core.Rendering;

/// <summary>
/// The model a result template partial receives: one search result, plus the conveniences the
/// default card needs (spec §5.8). Everything the server projected is on <see cref="Result"/>;
/// <see cref="Attribute"/> and <see cref="Highlight"/> read it without the caller handling
/// <see cref="JsonElement"/> or the highlight tags.
/// </summary>
public sealed class SearchResultViewModel
{
    /// <summary>Attributes the default card falls back through for its snippet.</summary>
    public static readonly IReadOnlyList<string> DefaultSnippetAttributes = ["summary", "content", "excerpt"];

    private readonly string titleAttribute;
    private readonly string urlAttribute;
    private readonly IReadOnlyList<string> snippetAttributes;

    /// <summary>Initializes a new instance of the <see cref="SearchResultViewModel"/> class.</summary>
    /// <param name="result">The result to render.</param>
    /// <param name="titleAttribute">Attribute the title comes from; <see langword="null"/> means <c>title</c>.</param>
    /// <param name="urlAttribute">Attribute the link comes from; <see langword="null"/> means <c>url</c>.</param>
    /// <param name="snippetAttributes">
    /// Attributes tried in order for the snippet; <see langword="null"/> or empty means
    /// <see cref="DefaultSnippetAttributes"/>.
    /// </param>
    public SearchResultViewModel(
        Result result,
        string? titleAttribute = null,
        string? urlAttribute = null,
        IReadOnlyList<string>? snippetAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        this.titleAttribute = string.IsNullOrWhiteSpace(titleAttribute) ? "title" : titleAttribute.Trim();
        this.urlAttribute = string.IsNullOrWhiteSpace(urlAttribute) ? "url" : urlAttribute.Trim();
        this.snippetAttributes = snippetAttributes is { Count: > 0 } ? snippetAttributes : DefaultSnippetAttributes;
    }

    /// <summary>Gets the result as the search API returned it.</summary>
    public Result Result { get; }

    /// <summary>Gets the title, highlighted when the response carried a highlight for it.</summary>
    public IHtmlContent Title => Highlight(titleAttribute);

    /// <summary>Gets the link target, or <c>#</c> when the result carries no such attribute.</summary>
    public string Url => Attribute(urlAttribute) is { Length: > 0 } url ? url : "#";

    /// <summary>Gets the image URL, or <see langword="null"/> when the result carries none.</summary>
    public string? Image => Attribute("image") is { Length: > 0 } image ? image : null;

    /// <summary>Gets the content type, or <see langword="null"/> when the result carries none.</summary>
    public string? ContentType => Attribute("contentType") is { Length: > 0 } type ? type : null;

    /// <summary>
    /// Gets the snippet: the first configured attribute that has a value, highlighted, or
    /// <see langword="null"/> when none of them does.
    /// </summary>
    public IHtmlContent? Snippet
    {
        get
        {
            string? field = snippetAttributes.FirstOrDefault(
                name => Result.Highlights?.GetValueOrDefault(name) is { Length: > 0 } || Attribute(name) is { Length: > 0 });

            return field is null ? null : Highlight(field);
        }
    }

    /// <summary>Reads one attribute as text.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or <see langword="null"/> when the result does not carry the attribute.</returns>
    public string? Attribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Result.Attributes is null || !Result.Attributes.TryGetValue(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    /// <summary>
    /// Reads one attribute as HTML: the response's highlighted form when there is one - already
    /// encoded server-side with <c>&lt;mark&gt;</c> around the matches (spec §4.6), given the shell's
    /// class the same way the JavaScript template does - otherwise the encoded plain value.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The markup.</returns>
    public IHtmlContent Highlight(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Result.Highlights?.GetValueOrDefault(name) is { Length: > 0 } marked
            ? new HtmlString(marked.Replace("<mark>", "<mark class=\"xps-highlight\">", StringComparison.Ordinal))
            : new HtmlString(System.Net.WebUtility.HtmlEncode(Attribute(name) ?? string.Empty));
    }
}
