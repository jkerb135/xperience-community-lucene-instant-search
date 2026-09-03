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
    private static string RunTagHelper(string pathBase, bool defaultTheme, string theme = XpSearchAssets.DefaultThemeName)
    {
        var httpContext = new DefaultHttpContext { Request = { PathBase = new PathString(pathBase) } };
        var viewContext = new ViewContext
        {
            HttpContext = httpContext,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new ActionDescriptor(),
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        var helper = new XpSearchAssetsTagHelper { ViewContext = viewContext, DefaultTheme = defaultTheme, Theme = theme };
        var output = new TagHelperOutput(
            "xps-search-assets",
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "test"), output);

        using var writer = new StringWriter();
        output.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

        return writer.ToString();
    }

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
}
