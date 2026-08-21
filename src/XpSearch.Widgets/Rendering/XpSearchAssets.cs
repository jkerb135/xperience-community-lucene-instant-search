using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace XpSearch.Widgets.Rendering;

/// <summary>
/// The <c>&lt;link&gt;</c> and <c>&lt;script&gt;</c> tags that load the Xperience Search client.
/// </summary>
/// <remarks>
/// The files are Razor Class Library static web assets of <c>YourCo.Xperience.Search.Widgets</c>, so
/// a host only needs <c>app.UseStaticFiles()</c>. A project that prefers Kentico's Page Builder
/// bundling can copy them into <c>~/wwwroot/PageBuilder/Public/Widgets/XpSearch/</c> instead and emit
/// its own tags; see
/// https://docs.kentico.com/documentation/developers-and-admins/development/builders/bundle-static-assets-of-builder-components.
/// </remarks>
public static class XpSearchAssets
{
    /// <summary>Path of the structural stylesheet, relative to the application root.</summary>
    public const string ShellStylesheetPath = "/_content/YourCo.Xperience.Search.Widgets/xpsearch/shell.css";

    /// <summary>Path of the opt-in visual theme, relative to the application root.</summary>
    public const string DefaultThemeStylesheetPath = "/_content/YourCo.Xperience.Search.Widgets/xpsearch/default.css";

    /// <summary>Path of the UMD bundle, relative to the application root.</summary>
    public const string ScriptPath = "/_content/YourCo.Xperience.Search.Widgets/xpsearch/xpsearch.umd.js";

    /// <summary>Builds the asset tags.</summary>
    /// <param name="pathBase">The application's path base, so the tags work under a virtual directory.</param>
    /// <param name="defaultTheme">Whether the opt-in visual theme is loaded on top of the structural stylesheet.</param>
    /// <returns>The tags, in load order.</returns>
    public static IHtmlContent Render(PathString pathBase, bool defaultTheme = true)
    {
        var content = new HtmlContentBuilder();
        content.AppendHtml(Stylesheet(pathBase, ShellStylesheetPath));

        if (defaultTheme)
        {
            content.AppendHtml(Stylesheet(pathBase, DefaultThemeStylesheetPath));
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
