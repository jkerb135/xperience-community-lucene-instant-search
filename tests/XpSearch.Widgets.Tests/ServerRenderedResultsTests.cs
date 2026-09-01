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
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Rendering;
using XpSearch.Widgets.Templates;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The server-rendered first paint of the results widget (spec §5.8): the widget's own search, run
/// through the same pipeline as the public endpoint, rendered into the mount element.
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
    public async Task It_renders_one_card_per_result_inside_a_server_rendered_block()
    {
        string html = (await RenderAsync(new FakePipeline(TwoResults())))!;

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<div data-xps-server-rendered class=\"xps xps-results\"><ol class=\"xps-results__list\">"));
            Assert.That(html, Does.Contain("<li class=\"xps-results__item\">"));
            Assert.That(html.Split("<article class=\"xps-result\">"), Has.Length.EqualTo(3));
            Assert.That(html, Does.Contain("href=\"/blog/espresso\""));
            // The highlighted form wins and keeps the shell's class, exactly like the JS template.
            Assert.That(html, Does.Contain("Choosing an <mark class=\"xps-highlight\">espresso</mark> machine"));
            Assert.That(html, Does.Contain("<li class=\"xps-result__meta-item\">Article</li>"));
            Assert.That(html, Does.Contain("<img class=\"xps-result__image\" src=\"/img/1.png\""));
            // Second result: no image, no highlight, no content type - those blocks are omitted.
            Assert.That(html, Does.Contain("Descaling &lt;b&gt;your&lt;/b&gt; machine"));
            Assert.That(html, Does.Not.Contain("<b>your</b>"));
        });
    }

    [Test]
    public async Task The_editors_page_size_fields_and_the_visitors_URL_state_reach_the_pipeline()
    {
        var pipeline = new FakePipeline(TwoResults());

        await RenderAsync(
            pipeline,
            options => options with { ResultsPerPage = 12, Fields = ["title", "url"] },
            queryString: "?q=espresso&page=3&tags=coffee");

        var request = pipeline.Requests.Single();
        Expect.Multiple(() =>
        {
            Assert.That(request.Index, Is.EqualTo("site-content"));
            Assert.That(request.PageSize, Is.EqualTo(12));
            Assert.That(request.Fields, Is.EqualTo(new[] { "title", "url" }));
            Assert.That(request.Query, Is.EqualTo("espresso"));
            Assert.That(request.Page, Is.EqualTo(3));
            Assert.That(request.Filters!.Facets!.Single().Values, Is.EqualTo(new[] { "coffee" }));
        });
    }

    [Test]
    public async Task The_attribute_overrides_choose_what_a_card_shows()
    {
        string html = (await RenderAsync(
            new FakePipeline(TwoResults()),
            options => options with
            {
                TitleAttribute = "heading",
                UrlAttribute = "permalink",
                SnippetAttributes = ["teaser"]
            }))!;

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain("href=\"/permalink/1\""));
            Assert.That(html, Does.Contain(">Heading one</a>"));
            Assert.That(html, Does.Contain("<p class=\"xps-result__snippet\">A teaser.</p>"));
            Assert.That(html, Does.Not.Contain("A dual-boiler"));
        });
    }

    [Test]
    public async Task An_empty_result_set_renders_the_empty_state()
    {
        string html = (await RenderAsync(new FakePipeline(new SearchResponse { Results = [] })))!;

        Assert.That(html, Is.EqualTo(
            "<div data-xps-server-rendered class=\"xps xps-results xps-results--empty\">"
            + "<div class=\"xps-results__empty\"><p>No results.</p></div></div>"));
    }

    [Test]
    public async Task A_failing_search_leaves_an_empty_mount_and_a_logged_warning()
    {
        var log = new CapturingLogger();

        string? content = await RenderAsync(new FakePipeline(new InvalidOperationException("index is gone")), logger: log);

        Expect.Multiple(() =>
        {
            Assert.That(content, Is.Null, "a broken search must not break the page");
            Assert.That(log.Warnings.Single(), Does.Contain("could not be rendered on the server"));
        });
    }

    [Test]
    public async Task An_unregistered_template_falls_back_to_the_default_card_with_a_warning()
    {
        var log = new CapturingLogger();

        string? html = await RenderAsync(
            new FakePipeline(TwoResults()),
            options => options with { TemplateIdentifier = "MyCompany.Missing" },
            log);

        Expect.Multiple(() =>
        {
            Assert.That(log.Warnings.Single(), Does.Contain("MyCompany.Missing"));
            Assert.That(html, Does.Contain("<article class=\"xps-result\">"));
        });
    }

    [Test]
    public async Task A_registered_template_whose_view_is_missing_falls_back_to_the_default_card()
    {
        var log = new CapturingLogger();
        var registry = new FakeTemplateRegistry(
            new SearchResultTemplate("MyCompany.ProductCard", "Product card", "~/Views/Nowhere/_Card.cshtml", []));

        string? html = await RenderAsync(
            new FakePipeline(TwoResults()),
            options => options with { TemplateIdentifier = "MyCompany.ProductCard" },
            log,
            registry);

        Expect.Multiple(() =>
        {
            Assert.That(log.Warnings.Single(), Does.Contain("_Card.cshtml"));
            Assert.That(html, Does.Contain("<article class=\"xps-result\">"));
        });
    }

    [Test]
    public async Task The_results_widget_renders_the_block_inside_its_mount_element_and_never_in_the_Page_Builder()
    {
        var editor = new FakeEditorContext(XpSearchEditorMode.Live);
        var component = new ResultsWidgetViewComponent(
            new XpSearchMountRenderer(),
            editor,
            new FakeIndexCatalog("site-content"),
            new ServerRenderedResults(
                new FakePipeline(TwoResults()),
                provider.GetRequiredService<ICompositeViewEngine>(),
                new FakeTemplateRegistry(),
                new CapturingLogger()))
        {
            ViewComponentContext = new ViewComponentContext { ViewContext = ViewContext("?q=espresso") }
        };

        var properties = new ResultsWidgetProperties { Index = "site-content" };
        var live = await component.BuildModelAsync(properties, CancellationToken.None).ConfigureAwait(false);

        string markup = Rendered.Html(live.Mount!);
        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("<div class=\"xps-mount\""));
            Assert.That(markup, Does.Contain("<div data-xps-server-rendered"));
            Assert.That(markup, Does.Contain("<article class=\"xps-result\">"));
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

    private static SearchResponse TwoResults() => new()
    {
        Total = 2,
        Page = 1,
        PageSize = 10,
        TotalPages = 1,
        Results =
        [
            new Result
            {
                Id = "doc-1",
                Attributes = Attributes(
                    """
                    {
                        "title": "Choosing an espresso machine",
                        "heading": "Heading one",
                        "url": "/blog/espresso",
                        "permalink": "/permalink/1",
                        "summary": "A dual-boiler machine holds temperature.",
                        "teaser": "A teaser.",
                        "contentType": "Article",
                        "image": "/img/1.png"
                    }
                    """),
                Highlights = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["title"] = "Choosing an <mark>espresso</mark> machine"
                }
            },
            new Result
            {
                Id = "doc-2",
                Attributes = Attributes("""{ "title": "Descaling <b>your</b> machine", "url": "/support/descaling" }""")
            }
        ]
    };

    private static Dictionary<string, JsonElement> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private async Task<string?> RenderAsync(
        ISearchPipeline pipeline,
        Func<ServerResultsOptions, ServerResultsOptions>? configure = null,
        CapturingLogger? logger = null,
        ISearchResultTemplateRegistry? registry = null,
        string queryString = "")
    {
        var viewContext = ViewContext(queryString);
        var renderer = new ServerRenderedResults(
            pipeline,
            provider.GetRequiredService<ICompositeViewEngine>(),
            registry ?? new FakeTemplateRegistry(),
            logger ?? new CapturingLogger());

        var blank = new ServerResultsOptions("site-content", 0, [], null, null, null, []);
        var options = configure is null ? blank : configure(blank);
        var content = await renderer.RenderAsync(viewContext, options, CancellationToken.None).ConfigureAwait(false);

        return content is null ? null : Rendered.Html(content);
    }

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

        public List<SearchRequest> Requests { get; } = [];

        public Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return failure is null ? Task.FromResult(response!) : Task.FromException<SearchResponse>(failure);
        }
    }

    private sealed class FakeTemplateRegistry : ISearchResultTemplateRegistry
    {
        private readonly SearchResultTemplate[] templates;

        public FakeTemplateRegistry(params SearchResultTemplate[] templates) => this.templates = templates;

        public IReadOnlyList<SearchResultTemplate> GetTemplates() => templates;

        public SearchResultTemplate? Find(string identifier) =>
            templates.FirstOrDefault(template => string.Equals(template.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingLogger : ILogger<ServerRenderedResults>
    {
        public List<string> Warnings { get; } = [];

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
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
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
