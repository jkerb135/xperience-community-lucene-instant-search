using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Renders <c>_Mount.cshtml</c> through a minimal MVC host, which proves the view is compiled into
/// the Razor Class Library and is reachable at the path the base view component returns.
/// </summary>
[TestFixture]
internal sealed class MountViewRenderingTests
{
    private ServiceProvider provider = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var listener = new System.Diagnostics.DiagnosticListener("XpSearch.Widgets.Tests");
        services.AddSingleton(listener);
        services.AddSingleton<System.Diagnostics.DiagnosticSource>(listener);
        services.AddSingleton<IWebHostEnvironment>(new StubWebHostEnvironment());
        services.AddSingleton<IHostEnvironment>(new StubWebHostEnvironment());
        services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(XpSearchMountRenderer).Assembly);

        provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown() => provider.Dispose();

    private async Task<string> RenderAsync(XpSearchMountViewModel model)
    {
        var engine = provider.GetRequiredService<IRazorViewEngine>();
        var result = engine.GetView(executingFilePath: null, XpSearchWidgetConstants.MountViewPath, isMainPage: false);
        Assert.That(result.Success, Is.True, $"the view was not found: {string.Join(", ", result.SearchedLocations)}");

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        using var writer = new StringWriter();

        var viewContext = new ViewContext(
            actionContext,
            result.View!,
            new ViewDataDictionary<XpSearchMountViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            },
            new TempDataDictionary(httpContext, provider.GetRequiredService<ITempDataProvider>()),
            writer,
            new HtmlHelperOptions());

        await result.View!.RenderAsync(viewContext);

        return writer.ToString().Trim();
    }

    [Test]
    public async Task A_configured_widget_renders_its_mount_element()
    {
        var component = new SearchBoxWidgetViewComponent(
            new XpSearchMountRenderer(),
            new FakeEditorContext(XpSearchEditorMode.Live),
            new FakeIndexCatalog("site-content"));

        string html = await RenderAsync(component.BuildModel(new SearchBoxWidgetProperties { Placeholder = "Search" }));

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<div class=\"xps-mount\""));
            Assert.That(Rendered.Attribute(html, "data-xps-widget"), Is.EqualTo("searchBox"));
            Assert.That(Rendered.Json(html, "data-xps-config").GetProperty("placeholder").GetString(), Is.EqualTo("Search"));
        });
    }

    [Test]
    public async Task A_configured_widget_renders_its_preview_inside_the_Page_Builder()
    {
        var component = new SearchBoxWidgetViewComponent(
            new XpSearchMountRenderer(),
            new FakeEditorContext(XpSearchEditorMode.Edit),
            new FakeIndexCatalog("site-content"));

        string html = await RenderAsync(component.BuildModel(new SearchBoxWidgetProperties { Placeholder = "Search" }));

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<div class=\"xps xps-editor-preview xps-editor-preview--search-box\""));
            Assert.That(html, Does.Contain("placeholder=\"Search\""));
            Assert.That(html, Does.Not.Contain("xps-mount"));
        });
    }

    [Test]
    public async Task An_unconfigured_widget_renders_the_instruction_block_for_an_editor()
    {
        var component = new SearchBoxWidgetViewComponent(
            new XpSearchMountRenderer(),
            new FakeEditorContext(XpSearchEditorMode.Edit),
            new FakeIndexCatalog("a", "b"));

        string html = await RenderAsync(component.BuildModel(new SearchBoxWidgetProperties()));

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain("xps-widget-message"));
            Assert.That(html, Does.Contain("Select a search index"));
            Assert.That(html, Does.Not.Contain("xps-mount"));
        });
    }

    [Test]
    public async Task An_unconfigured_widget_renders_nothing_for_a_visitor()
    {
        var component = new SearchBoxWidgetViewComponent(
            new XpSearchMountRenderer(),
            new FakeEditorContext(XpSearchEditorMode.Live),
            new FakeIndexCatalog("a", "b"));

        string html = await RenderAsync(component.BuildModel(new SearchBoxWidgetProperties()));

        Assert.That(html, Is.Empty);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ApplicationName { get; set; } = typeof(MountViewRenderingTests).Assembly.GetName().Name!;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
