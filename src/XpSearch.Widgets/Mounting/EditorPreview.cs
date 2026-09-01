using System.Text;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// The element builders the static Page Builder previews are assembled from (spec §7.5). Every text
/// goes through <see cref="TagBuilder"/>, which HTML-encodes it, so an editor's property value can
/// never become markup.
/// </summary>
internal static class EditorPreview
{
    /// <summary>Builds an element with an optional class and an optional encoded text body.</summary>
    internal static TagBuilder El(string tagName, string? cssClass = null, string? text = null)
    {
        var tag = new TagBuilder(tagName);

        if (!string.IsNullOrEmpty(cssClass))
        {
            tag.Attributes["class"] = cssClass;
        }

        if (text is not null)
        {
            tag.InnerHtml.Append(text);
        }

        return tag;
    }

    /// <summary>Builds a disabled, self-closing input.</summary>
    internal static TagBuilder Input(string cssClass, string type)
    {
        var tag = new TagBuilder("input") { TagRenderMode = TagRenderMode.SelfClosing };
        tag.Attributes["class"] = cssClass;
        tag.Attributes["type"] = type;

        return tag.Disabled();
    }

    /// <summary>Builds a disabled button carrying a decorative glyph or a label.</summary>
    internal static TagBuilder Button(string cssClass, string text, bool glyph = false)
    {
        var tag = El("button", cssClass);
        tag.Attributes["type"] = "button";
        tag.Disabled();

        return glyph ? tag.Add(El("span", text: text).Decorative()) : tag.Add(El("span", text: text));
    }

    /// <summary>Builds the decorative chevron the live disclosure buttons draw.</summary>
    internal static TagBuilder Chevron(string cssClass) =>
        El("svg", cssClass)
            .Attr("viewBox", "0 0 24 24")
            .Attr("fill", "none")
            .Attr("stroke", "currentColor")
            .Attr("stroke-width", "1.5")
            .Decorative()
            .Add(El("path").Attr("d", "m6 9 6 6 6-6"));

    /// <summary>Builds a placeholder bar standing in for text the live widget loads.</summary>
    internal static TagBuilder Skeleton(string modifier) =>
        El("span", $"xps-skeleton xps-skeleton--{modifier}").Decorative();

    /// <summary>Builds the subtle line that names configuration the mirrored markup cannot show.</summary>
    internal static TagBuilder Note(string text) => El("p", "xps-editor-preview__note", text);

    /// <summary>Appends children to an element.</summary>
    internal static TagBuilder Add(this TagBuilder parent, params IHtmlContent[] children)
    {
        foreach (var child in children)
        {
            parent.InnerHtml.AppendHtml(child);
        }

        return parent;
    }

    /// <summary>Sets an attribute.</summary>
    internal static TagBuilder Attr(this TagBuilder tag, string name, string value)
    {
        tag.Attributes[name] = value;

        return tag;
    }

    /// <summary>Marks an element as decoration for assistive technology.</summary>
    internal static TagBuilder Decorative(this TagBuilder tag) => tag.Attr("aria-hidden", "true");

    /// <summary>Marks a form control as inoperable - a preview never runs a search.</summary>
    internal static TagBuilder Disabled(this TagBuilder tag) => tag.Attr("disabled", "disabled");

    /// <summary>
    /// Turns a <c>data-xps-widget</c> value into the CSS-modifier form used by
    /// <c>xps-editor-preview--…</c>: <c>facetList</c> becomes <c>facet-list</c>,
    /// <c>myCompany.dropdownFacet</c> becomes <c>my-company-dropdown-facet</c>.
    /// </summary>
    internal static string Kebab(string value)
    {
        var text = new StringBuilder(value.Length + 4);

        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (char.IsAsciiLetterUpper(character) && text.Length > 0 && text[^1] != '-')
                {
                    text.Append('-');
                }

                text.Append(char.ToLowerInvariant(character));
            }
            else if (text.Length > 0 && text[^1] != '-')
            {
                text.Append('-');
            }
        }

        return text.ToString().Trim('-');
    }
}
