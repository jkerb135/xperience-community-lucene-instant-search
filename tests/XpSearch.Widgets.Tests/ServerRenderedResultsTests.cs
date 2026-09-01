using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Rendering;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The Results widget's use of the Core first paint (spec §5.8): the search runs inside the mount
/// element, never in the Page Builder, it renders through this library's <c>_Result.cshtml</c>
/// partial, and what the server did is handed to the client. The renderer itself is covered by
/// <c>XpSearch.Core.Tests.ServerRenderedResultsTests</c>.
/// </summary>
[TestFixture]
internal sealed class ServerRenderedResultsTests
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
        services.AddSingleton<IWebHostEnvironment>(new StubEnvironment());
        services.AddSingleton<IHostEnvironment>(new StubEnvironment());
        services.AddControllersWithViews().AddApplicationPart(typeof(XpSearchMountRenderer).Assembly);

        provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown() => provider.Dispose();

    [Test]
    public void AddXpSearchWidgets_next_to_AddXpSearch_registers_the_rendering_services_once()
    {
        var services = new ServiceCollection().AddXpSearch().AddXpSearchWidgets();

        Expect.Multiple(() =>
        {
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(ServerRenderedResults)), Is.EqualTo(1));
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(ISearchResultTemplateRegistry)), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task The_results_widget_renders_the_block_inside_its_mount_element_and_never_in_the_Page_Builder()
    {
        var editor = new FakeEditorContext(XpSearchEditorMode.Live);
        var component = ResultsWidget(new FakePipeline(TwoResults()), editor);

        var properties = new ResultsWidgetProperties { Index = "site-content" };
        var live = await component.BuildModelAsync(properties, CancellationToken.None).ConfigureAwait(false);

        string markup = Rendered.Html(live.Mount!);
        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("<div class=\"xps-mount\""));
            Assert.That(markup, Does.Contain("<div data-xps-server-rendered"));
            Assert.That(markup, Does.Contain("<article class=\"xps-result\">"));
            Assert.That(markup, Does.Contain("href=\"/blog/espresso\""));
            // The partial's own §3 additions: path line, type label, and the file-type glyph the
            // media slot falls back to. Byte-identical to the client's card (card-parity.test.ts).
            Assert.That(markup, Does.Contain("<p class=\"xps-result__path\">Home / Blog / Coffee</p>"));
            Assert.That(markup, Does.Contain("<li class=\"xps-result__meta-item xps-result__type\">Article</li>"));
            Assert.That(markup, Does.Contain(
                "<svg class=\"xps-result__icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\""
                + " stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\""
                + " focusable=\"false\"><path d=\"M14 2H7a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7z\"></path>"
                + "<path d=\"M14 2v5h5\"></path></svg>"));
            // The card came from this library's Razor partial, not from the C# fallback Core emits
            // for a host without it: the partial's markup is laid out over several lines.
            Assert.That(markup, Does.Not.Contain("<article class=\"xps-result\"><div"));
            Assert.That(markup, Does.EndWith("</div>"));
        });

        editor.Mode = XpSearchEditorMode.Edit;
        var edited = await component.BuildModelAsync(properties, CancellationToken.None).ConfigureAwait(false);

        Expect.Multiple(() =>
        {
            // The Page Builder gets the static preview; no search runs there.
            Assert.That(edited.Mount, Is.Null);
            Assert.That(Rendered.Html(edited.Preview!), Does.Not.Contain("data-xps-server-rendered"));
        });
    }

    [Test]
    public async Task The_first_paint_hands_its_query_id_and_the_page_size_it_used_to_the_client()
    {
        var component = ResultsWidget(new FakePipeline(TwoResults()));

        var model = await component
            .BuildModelAsync(new ResultsWidgetProperties { Index = "site-content" }, CancellationToken.None)
            .ConfigureAwait(false);

        var instance = Rendered.Json(Rendered.Html(model.Mount!), "data-xps-instance-config");
        Expect.Multiple(() =>
        {
            Assert.That(instance.GetProperty("initialQueryId").GetString(), Is.EqualTo("server-query-1"));
            // Parity: the widget left the page size unset, so the client must ask for the page size
            // the pipeline actually applied rather than fall back to its own default.
            Assert.That(instance.GetProperty("initialState").GetProperty("pageSize").GetInt32(), Is.EqualTo(10));
        });
    }

    [Test]
    public async Task A_search_that_did_not_render_hands_nothing_over()
    {
        var component = ResultsWidget(new FakePipeline(new InvalidOperationException("index is gone")));

        var model = await component
            .BuildModelAsync(new ResultsWidgetProperties { Index = "site-content" }, CancellationToken.None)
            .ConfigureAwait(false);

        var instance = Rendered.Json(Rendered.Html(model.Mount!), "data-xps-instance-config");
        Expect.Multiple(() =>
        {
            Assert.That(instance.TryGetProperty("initialQueryId", out _), Is.False);
            Assert.That(instance.TryGetProperty("initialState", out _), Is.False);
        });
    }

    private ResultsWidgetViewComponent ResultsWidget(ISearchPipeline pipeline, IXpSearchEditorContext? editor = null) =>
        new(
            new XpSearchMountRenderer(),
            editor ?? new FakeEditorContext(XpSearchEditorMode.Live),
            new FakeIndexCatalog("site-content"),
            new ServerRenderedResults(
                pipeline,
                provider.GetRequiredService<ICompositeViewEngine>(),
                new FakeTemplateRegistry(),
                new CapturingLogger()))
        {
            ViewComponentContext = new ViewComponentContext { ViewContext = ViewContext("?q=espresso") }
        };

    private static SearchResponse TwoResults() => new()
    {
        Total = 1,
        Page = 1,
        PageSize = 10,
        QueryId = "server-query-1",
        TotalPages = 1,
        Results =
        [
            new Result
            {
                Id = "doc-1",
                Attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    """
                    {
                        "title": "Choosing an espresso machine",
                        "url": "/blog/espresso",
                        "path": "Home / Blog / Coffee",
                        "contentType": "Article",
                        "fileType": "pdf"
                    }
                    """)!
            }
        ]
    };

    private ViewContext ViewContext(string queryString)
    {
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.QueryString = new QueryString(queryString);

        var view = provider
            .GetRequiredService<IRazorViewEngine>()
            .GetView(executingFilePath: null, XpSearchWidgetConstants.MountViewPath, isMainPage: false);
        Assert.That(view.Success, Is.True, "the mount view was not found");

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        return new ViewContext(
            actionContext,
            view.View!,
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            new TempDataDictionary(httpContext, provider.GetRequiredService<ITempDataProvider>()),
            TextWriter.Null,
            new HtmlHelperOptions());
    }

    /// <summary>A pipeline that answers with a fixed response, or throws.</summary>
    private sealed class FakePipeline : ISearchPipeline
    {
        private readonly SearchResponse? response;
        private readonly Exception? failure;

        public FakePipeline(SearchResponse response) => this.response = response;

        public FakePipeline(Exception failure) => this.failure = failure;

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken) =>
            failure is null ? Task.FromResult(response!) : Task.FromException<SearchResponse>(failure);
    }

    private sealed class FakeTemplateRegistry : ISearchResultTemplateRegistry
    {
        public IReadOnlyList<SearchResultTemplate> GetTemplates() => [];

        public SearchResultTemplate? Find(string identifier) => null;
    }

    private sealed class CapturingLogger : ILogger<ServerRenderedResults>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ApplicationName { get; set; } = typeof(ServerRenderedResultsTests).Assembly.GetName().Name!;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
