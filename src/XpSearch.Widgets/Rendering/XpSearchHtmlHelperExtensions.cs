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

    /// <summary>Emits only the stylesheet links, for a layout that loads them in the <c>&lt;head&gt;</c>.</summary>
    /// <param name="html">The HTML helper.</param>
    /// <param name="defaultTheme">Whether a visual theme is loaded on top of the structural stylesheet.</param>
    /// <param name="theme">Which shipped palette to load: <c>default</c> (= <c>kentico-violet</c>) or <c>kentico-orange</c>.</param>
    /// <returns>The links.</returns>
    public static IHtmlContent XpSearchStyles(this IHtmlHelper html, bool defaultTheme = true, string theme = Rendering.XpSearchAssets.DefaultThemeName)
    {
        ArgumentNullException.ThrowIfNull(html);

        return Rendering.XpSearchAssets.RenderStyles(html.ViewContext.HttpContext.Request.PathBase, defaultTheme, theme);
    }

    /// <summary>Emits only the script tag, for a layout that loads it at the end of the body.</summary>
    /// <param name="html">The HTML helper.</param>
    /// <returns>The script tag.</returns>
    public static IHtmlContent XpSearchScripts(this IHtmlHelper html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return Rendering.XpSearchAssets.RenderScripts(html.ViewContext.HttpContext.Request.PathBase);
    }
}
