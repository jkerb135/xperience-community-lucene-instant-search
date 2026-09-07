using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Rendering;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The tag helper and HTML helper that load the client bundle and the two stylesheets from the
/// library's static web assets.
/// </summary>
[TestFixture]
internal sealed class AssetsTests
{
    private static ViewContext ViewContextFor(string pathBase)
    {
        var httpContext = new DefaultHttpContext { Request = { PathBase = new PathString(pathBase) } };

        return new ViewContext
        {
            HttpContext = httpContext,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new ActionDescriptor(),
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
    }

    private static string Run(TagHelper helper, string tagName)
    {
        var output = new TagHelperOutput(
            tagName,
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "test"), output);

        using var writer = new StringWriter();
        output.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

        return writer.ToString();
    }

    private static string RunTagHelper(string pathBase, bool defaultTheme, string theme = XpSearchAssets.DefaultThemeName) =>
        Run(
            new XpSearchAssetsTagHelper { ViewContext = ViewContextFor(pathBase), DefaultTheme = defaultTheme, Theme = theme },
            "xps-search-assets");

    [Test]
    public void The_tag_helper_emits_the_shell_theme_and_bundle_from_the_package_content_path()
    {
        string html = RunTagHelper(string.Empty, defaultTheme: true);

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("xps-search-assets"), "the placeholder tag must not survive");
            Assert.That(html, Does.Contain($"href=\"{XpSearchAssets.ShellStylesheetPath}\""));
            Assert.That(html, Does.Contain($"href=\"{XpSearchAssets.DefaultThemeStylesheetPath}\""));
            Assert.That(html, Does.Contain($"src=\"{XpSearchAssets.ScriptPath}\""));
            Assert.That(html, Does.Contain("defer"));
        });
    }

    [Test]
    public void The_default_theme_can_be_left_out()
    {
        string html = RunTagHelper(string.Empty, defaultTheme: false);

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain("shell.css"));
            Assert.That(html, Does.Not.Contain("default.css"));
        });
    }

    [TestCase("kentico-violet")]
    [TestCase("kentico-orange")]
    [TestCase("KENTICO-ORANGE")]
    public void A_named_palette_replaces_the_default_stylesheet(string theme)
    {
        string html = RunTagHelper(string.Empty, defaultTheme: true, theme);

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain($"href=\"{XpSearchAssets.ThemeStylesheetPath(theme)}\""));
            Assert.That(html, Does.Not.Contain("default.css"), "only one palette may be loaded");
            Assert.That(html, Does.Contain("shell.css"), "the structural stylesheet is always first");
        });
    }

    [TestCase("")]
    [TestCase("../../appsettings.json")]
    [TestCase("kentico-teal")]
    public void An_unknown_palette_is_refused_rather_than_turned_into_a_path(string theme) =>
        Assert.Throws<ArgumentException>(new Action(() => _ = XpSearchAssets.ThemeStylesheetPath(theme)));

    [Test]
    public void The_paths_honour_the_application_path_base()
    {
        string html = RunTagHelper("/site", defaultTheme: true);

        Assert.That(html, Does.Contain($"src=\"/site{XpSearchAssets.ScriptPath}\""));
    }

    [Test]
    public void The_html_helper_extension_emits_the_same_tags()
    {
        var httpContext = new DefaultHttpContext();
        string direct = Rendered.Html(XpSearchAssets.Render(httpContext.Request.PathBase));

        Assert.That(direct, Is.EqualTo(RunTagHelper(string.Empty, defaultTheme: true)));
    }

    [Test]
    public void The_split_tag_helpers_emit_the_styles_and_the_script_separately()
    {
        string styles = Run(new XpSearchStylesTagHelper { ViewContext = ViewContextFor("/site") }, "xps-search-styles");
        string scripts = Run(new XpSearchScriptsTagHelper { ViewContext = ViewContextFor("/site") }, "xps-search-scripts");

        Expect.Multiple(() =>
        {
            Assert.That(styles, Does.Contain($"href=\"/site{XpSearchAssets.ShellStylesheetPath}\""));
            Assert.That(styles, Does.Contain($"href=\"/site{XpSearchAssets.DefaultThemeStylesheetPath}\""));
            Assert.That(styles, Does.Not.Contain("<script"));
            Assert.That(scripts, Does.Contain($"src=\"/site{XpSearchAssets.ScriptPath}\""));
            Assert.That(scripts, Does.Not.Contain("<link"));
        });
    }

    [Test]
    public void The_styles_tag_helper_honours_the_theme_attributes()
    {
        string bare = Run(
            new XpSearchStylesTagHelper { ViewContext = ViewContextFor(string.Empty), DefaultTheme = false },
            "xps-search-styles");
        string orange = Run(
            new XpSearchStylesTagHelper { ViewContext = ViewContextFor(string.Empty), Theme = "kentico-orange" },
            "xps-search-styles");

        Expect.Multiple(() =>
        {
            Assert.That(bare, Does.Contain("shell.css").And.Not.Contain("default.css"));
            Assert.That(orange, Does.Contain("kentico-orange.css").And.Not.Contain("default.css"));
        });
    }

    [Test]
    public void The_shorthand_is_exactly_the_styles_followed_by_the_scripts()
    {
        string composed =
            Run(new XpSearchStylesTagHelper { ViewContext = ViewContextFor(string.Empty) }, "xps-search-styles")
            + Run(new XpSearchScriptsTagHelper { ViewContext = ViewContextFor(string.Empty) }, "xps-search-scripts");

        Assert.That(RunTagHelper(string.Empty, defaultTheme: true), Is.EqualTo(composed));
    }

    [Test]
    public void The_split_html_helper_extensions_emit_the_same_tags()
    {
        var pathBase = new PathString("/site");

        Expect.Multiple(() =>
        {
            Assert.That(
                Rendered.Html(XpSearchAssets.RenderStyles(pathBase)),
                Is.EqualTo(Run(new XpSearchStylesTagHelper { ViewContext = ViewContextFor("/site") }, "xps-search-styles")));
            Assert.That(
                Rendered.Html(XpSearchAssets.RenderScripts(pathBase)),
                Is.EqualTo(Run(new XpSearchScriptsTagHelper { ViewContext = ViewContextFor("/site") }, "xps-search-scripts")));
        });
    }
}
