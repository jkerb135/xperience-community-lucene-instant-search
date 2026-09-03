using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace XpSearch.Widgets.Rendering;

/// <summary>
/// The <c>&lt;link&gt;</c> and <c>&lt;script&gt;</c> tags that load the Xperience Search client.
/// </summary>
/// <remarks>
/// The files are Razor Class Library static web assets of <c>XperienceCommunity.Search.Widgets</c>, so
/// a host only needs <c>app.UseStaticFiles()</c>. A project that prefers Kentico's Page Builder
/// bundling can copy them into <c>~/wwwroot/PageBuilder/Public/Widgets/XpSearch/</c> instead and emit
/// its own tags; see
/// https://docs.kentico.com/documentation/developers-and-admins/development/builders/bundle-static-assets-of-builder-components.
/// </remarks>
public static class XpSearchAssets
{
    /// <summary>Path of the structural stylesheet, relative to the application root.</summary>
    public const string ShellStylesheetPath = "/_content/XperienceCommunity.Search.Widgets/xpsearch/shell.css";

    /// <summary>Path of the opt-in visual theme, relative to the application root.</summary>
    public const string DefaultThemeStylesheetPath = "/_content/XperienceCommunity.Search.Widgets/xpsearch/default.css";

    /// <summary>Name of the palette loaded when none is named. It is <c>kentico-violet</c> under its original file name.</summary>
    public const string DefaultThemeName = "default";

    /// <summary>The palettes the package ships, and the stylesheet each one loads.</summary>
    /// <remarks>
    /// <c>default.css</c> and <c>kentico-violet.css</c> are the same bytes, built from the same entry
    /// point; the older name is kept so a host that hard-coded it keeps working.
    /// </remarks>
    private static readonly Dictionary<string, string> themes = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultThemeName] = DefaultThemeStylesheetPath,
        ["kentico-violet"] = "/_content/XperienceCommunity.Search.Widgets/xpsearch/kentico-violet.css",
        ["kentico-orange"] = "/_content/XperienceCommunity.Search.Widgets/xpsearch/kentico-orange.css"
    };

    /// <summary>Path of the UMD bundle, relative to the application root.</summary>
    public const string ScriptPath = "/_content/XperienceCommunity.Search.Widgets/xpsearch/xpsearch.umd.js";

    /// <summary>The names <see cref="Render(PathString, bool, string)"/> accepts, in the order the guide lists them.</summary>
    public static IEnumerable<string> ThemeNames => themes.Keys;

    /// <summary>Gets the stylesheet path of a shipped palette.</summary>
    /// <param name="theme">The palette name, one of <see cref="ThemeNames"/>.</param>
    /// <returns>The path, relative to the application root.</returns>
    /// <exception cref="ArgumentException">The name is not one of the shipped palettes.</exception>
    public static string ThemeStylesheetPath(string theme) =>
        themes.TryGetValue(theme ?? string.Empty, out string? path)
            ? path
            // Never build the path from the name: it comes from a view attribute, and a shipped
            // stylesheet is the only thing this helper may point a <link> at.
            : throw new ArgumentException($"'{theme}' is not a shipped theme. Use one of: {string.Join(", ", themes.Keys)}.", nameof(theme));

    /// <summary>Builds the asset tags.</summary>
    /// <param name="pathBase">The application's path base, so the tags work under a virtual directory.</param>
    /// <param name="defaultTheme">Whether a visual theme is loaded on top of the structural stylesheet.</param>
    /// <param name="theme">Which palette to load, one of <see cref="ThemeNames"/>. Ignored when <paramref name="defaultTheme"/> is false.</param>
    /// <returns>The tags, in load order.</returns>
    public static IHtmlContent Render(PathString pathBase, bool defaultTheme = true, string theme = DefaultThemeName)
    {
        var content = new HtmlContentBuilder();
        content.AppendHtml(Stylesheet(pathBase, ShellStylesheetPath));

        if (defaultTheme)
        {
            content.AppendHtml(Stylesheet(pathBase, ThemeStylesheetPath(theme)));
        }

        // `defer` runs the bundle before DOMContentLoaded, which is when it calls mountAll().
        var script = new TagBuilder("script") { TagRenderMode = TagRenderMode.Normal };
        script.Attributes["src"] = pathBase.Add(ScriptPath).Value;
        script.Attributes["defer"] = "defer";
        content.AppendHtml(script);

        return content;
    }

    private static TagBuilder Stylesheet(PathString pathBase, string path)
    {
        var link = new TagBuilder("link") { TagRenderMode = TagRenderMode.SelfClosing };
        link.Attributes["rel"] = "stylesheet";
        link.Attributes["href"] = pathBase.Add(path).Value;

        return link;
    }
}
