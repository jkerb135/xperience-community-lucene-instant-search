using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace XpSearch.Widgets.Rendering;

/// <summary>
/// <c>@Html.XpSearchAssets()</c>, for views that do not register the tag helper.
/// </summary>
public static class XpSearchHtmlHelperExtensions
{
    /// <summary>Emits the stylesheet and script tags of the Xperience Search client.</summary>
    /// <param name="html">The HTML helper.</param>
    /// <param name="defaultTheme">Whether a visual theme is loaded on top of the structural stylesheet.</param>
    /// <param name="theme">Which shipped palette to load: <c>default</c> (= <c>kentico-violet</c>) or <c>kentico-orange</c>.</param>
    /// <returns>The tags.</returns>
    public static IHtmlContent XpSearchAssets(this IHtmlHelper html, bool defaultTheme = true, string theme = Rendering.XpSearchAssets.DefaultThemeName)
    {
        ArgumentNullException.ThrowIfNull(html);

        return Rendering.XpSearchAssets.Render(html.ViewContext.HttpContext.Request.PathBase, defaultTheme, theme);
    }
}
