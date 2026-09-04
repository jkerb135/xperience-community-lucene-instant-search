using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Rendering;
using XpSearch.Core.Tests.Fixtures;

using NUnit.Framework;

namespace XpSearch.Core.Tests;

/// <summary>
/// The server-rendered first paint (spec §5.8) as a Core capability: a host that only references
/// XpSearch.Core - no Page Builder widgets, no result partial - runs the visitor's first search and
/// gets the built-in card markup back. The widget path is covered by XpSearch.Widgets.Tests.
/// </summary>
[TestFixture]
internal sealed class ServerRenderedResultsTests
{
    /// <summary>The built-in card of the second result: no image, no highlight, no content type.</summary>
    private const string PlainCard = "<article class=\"xps-result\"><div class=\"xps-result__body\">"
        + "<h3 class=\"xps-result__title\"><a class=\"xps-result__link\" href=\"/support/descaling\">"
        + "Descaling &lt;b&gt;your&lt;/b&gt; machine</a></h3></div></article>";

    private ServiceProvider provider = null!;

    /// <summary>What the last <see cref="RenderAsync"/> returned, for the assertions the markup does not carry.</summary>
    private ServerResultsRender? lastRender;

    [OneTimeSetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var listener = new System.Diagnostics.DiagnosticListener("XpSearch.Core.Tests");
        services.AddSingleton(listener);
        services.AddSingleton<System.Diagnostics.DiagnosticSource>(listener);
        services.AddSingleton<IWebHostEnvironment>(new StubEnvironment());
        services.AddSingleton<IHostEnvironment>(new StubEnvironment());
        // No application part: this is the plain-JS host, which has MVC but none of our views.
        services.AddControllersWithViews();

        provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown() => provider.Dispose();

    [Test]
    public void AddXpSearch_registers_the_rendering_services_so_a_host_without_the_widgets_can_inject_them()
    {
        var services = new ServiceCollection().AddXpSearch();

        var registry = services.Single(descriptor => descriptor.ServiceType == typeof(ISearchResultTemplateRegistry));
        var renderer = services.Single(descriptor => descriptor.ServiceType == typeof(ServerRenderedResults));

        Expect.Multiple(() =>
        {
            Assert.That(registry.ImplementationType, Is.EqualTo(typeof(SearchResultTemplateRegistry)));
            Assert.That(registry.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(renderer.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
        });
    }

    [Test]
    public async Task It_renders_one_card_per_result_inside_a_server_rendered_block()
    {
        string html = (await RenderAsync(new FakePipeline(TwoResults())))!;

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<div data-xps-server-rendered class=\"xps xps-results\"><ol class=\"xps-results__list\">"));
            Assert.That(html, Does.Contain("<li class=\"xps-results__item\">"));
            Assert.That(html.Split("<article class=\"xps-result\">"), Has.Length.EqualTo(3));
            // The built-in card is the markup of themes/fixtures/results.html, emitted without a view.
            Assert.That(html, Does.Contain(PlainCard));
            Assert.That(html, Does.Contain(
                "<div class=\"xps-result__media\"><img class=\"xps-result__image\" src=\"/img/1.png\" alt=\"\" width=\"96\" height=\"96\"></div>"));
            // The highlighted form wins and keeps the shell's class, exactly like the JS template.
            Assert.That(html, Does.Contain("Choosing an <mark class=\"xps-highlight\">espresso</mark> machine"));
            Assert.That(html, Does.Contain("<ul class=\"xps-result__meta\"><li class=\"xps-result__meta-item xps-result__type\">Article</li></ul>"));
            Assert.That(html, Does.Contain("<p class=\"xps-result__path\">Home / Blog / Coffee</p>"));
            Assert.That(html, Does.Contain("<p class=\"xps-result__snippet\">A dual-boiler machine holds temperature.</p>"));
            Assert.That(html, Does.EndWith("</ol></div>"));
        });
    }

    /// <summary>
    /// The media slot's fallback: a result with a file type but no image gets the document glyph,
    /// byte-identical to the client's and the widgets' copy (the widgets client's card-parity test).
    /// </summary>
    [Test]
    public async Task A_result_with_a_file_type_and_no_image_gets_the_document_glyph()
    {
        var response = new SearchResponse
        {
            Results =
            [
                new Result
                {
                    Id = "doc-1",
                    Attributes = Attributes("""{ "title": "Warranty terms", "url": "/legal/warranty.pdf", "fileType": "pdf" }""")
                },
                new Result
                {
                    Id = "doc-2",
                    Attributes = Attributes("""{ "title": "No media", "url": "/none" }""")
                }
            ]
        };

        string html = (await RenderAsync(new FakePipeline(response)))!;

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain(
                "<div class=\"xps-result__media\"><svg class=\"xps-result__icon\" viewBox=\"0 0 24 24\" fill=\"none\""
                + " stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\""
                + " aria-hidden=\"true\" focusable=\"false\"><path d=\"M14 2H7a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h10a2 2 0"
                + " 0 0 2-2V7z\"></path><path d=\"M14 2v5h5\"></path></svg></div>"));
            // Only one card has a media slot: no file type and no image means no slot at all.
            Assert.That(html.Split("xps-result__media"), Has.Length.EqualTo(2));
        });
    }

    /// <summary>
    /// The sample of <c>docs/guides/server-rendering.md</c>: the same call a host's Razor page makes,
    /// and the two values it hands to <c>createSearch</c>.
    /// </summary>
    [Test]
    public async Task The_guides_plain_host_sample_renders_and_hands_over_its_query_id_and_page_size()
    {
        var renderer = new ServerRenderedResults(
            new FakePipeline(TwoResults()),
            provider.GetRequiredService<ICompositeViewEngine>(),
            new FakeTemplateRegistry(),
            new CapturingLogger());

        var render = await renderer.RenderAsync(
            ViewContext("?q=espresso"),
            new ServerResultsOptions(
                Index: "site-content",
                ResultsPerPage: 10,
                Fields: ["title", "url", "summary", "image", "contentType"],
                TemplateIdentifier: null,
                TitleAttribute: null,
                UrlAttribute: null,
                SnippetAttributes: []),
            CancellationToken.None).ConfigureAwait(false);

        Expect.Multiple(() =>
        {
            Assert.That(render!.QueryId, Is.EqualTo("server-query-1"));
            Assert.That(render.PageSize, Is.EqualTo(10));
            Assert.That(Html(render.Content), Does.Contain(PlainCard));
        });
    }

    [Test]
    public async Task The_callers_page_size_fields_and_the_visitors_URL_state_reach_the_pipeline()
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
    public async Task A_query_param_that_is_not_an_attribute_of_the_index_is_not_a_filter()
    {
        var pipeline = new FakePipeline(TwoResults());
        var schema = new IndexSchema(
            "site-content",
            [new SchemaField("tags", SearchFieldKind.Taxonomy, false, true, false, true)]);

        // `uh` is Kentico's preview parameter: adopting it made the query endpoint answer 400.
        await RenderAsync(pipeline, queryString: "?q=espresso&uh=abc123&tags=coffee", schemas: new FakeSchemas(schema));

        var filters = pipeline.Requests.Single().Filters!;
        Assert.That(filters.Facets!.Single().Attribute, Is.EqualTo("tags"));
    }

    [Test]
    public async Task An_unreadable_schema_still_renders_and_logs_a_warning()
    {
        var log = new CapturingLogger();
        var pipeline = new FakePipeline(TwoResults());

        string? html = await RenderAsync(
            pipeline,
            logger: log,
            queryString: "?tags=coffee",
            schemas: new FakeSchemas(new InvalidOperationException("no such index")));

        Expect.Multiple(() =>
        {
            Assert.That(log.Warnings.Single(), Does.Contain("schema of index"));
            Assert.That(pipeline.Requests.Single().Filters!.Facets!.Single().Attribute, Is.EqualTo("tags"));
            Assert.That(html, Does.Contain("<article class=\"xps-result\">"));
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

        // The client's default empty state, glyph included (widgets client `card-parity.test.ts`).
        Assert.That(html, Is.EqualTo(
            "<div data-xps-server-rendered class=\"xps xps-results xps-results--empty\">"
            + "<div class=\"xps-results__empty\">"
            + "<svg class=\"xps-results__empty-icon\" viewBox=\"0 0 24 24\" fill=\"none\""
            + " stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\""
            + " aria-hidden=\"true\" focusable=\"false\"><circle cx=\"11\" cy=\"11\" r=\"7\"></circle>"
            + "<path d=\"M8 11h6\"></path><path d=\"m20 20-4.35-4.35\"></path></svg>"
            + "<p class=\"xps-results__empty-title\">No results.</p></div></div>"));
    }

    [Test]
    public async Task The_empty_state_names_the_query_and_encodes_it()
    {
        string html = (await RenderAsync(
            new FakePipeline(new SearchResponse { Results = [] }),
            queryString: "?q=%3Cscript%3E"))!;

        Expect.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<p class=\"xps-results__empty-title\">No results for &ldquo;&lt;script&gt;&rdquo;</p>"));
            Assert.That(html, Does.Not.Contain("<script>"));
        });
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
    public async Task An_unregistered_template_falls_back_to_the_built_in_card_with_a_warning()
    {
        var log = new CapturingLogger();

        string? html = await RenderAsync(
            new FakePipeline(TwoResults()),
            options => options with { TemplateIdentifier = "MyCompany.Missing" },
            log);

        Expect.Multiple(() =>
        {
            Assert.That(log.Warnings.Single(), Does.Contain("MyCompany.Missing"));
            Assert.That(html, Does.Contain(PlainCard));
        });
    }

    [Test]
    public async Task A_registered_template_whose_view_is_missing_falls_back_to_the_built_in_card()
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
            Assert.That(html, Does.Contain(PlainCard));
        });
    }

    [Test]
    public async Task A_default_view_path_that_cannot_be_resolved_falls_back_to_the_built_in_card()
    {
        var log = new CapturingLogger();

        string? html = await RenderAsync(
            new FakePipeline(TwoResults()),
            options => options with { DefaultViewPath = "~/Components/Widgets/XpSearch/_Result.cshtml" },
            log);

        Expect.Multiple(() =>
        {
            Assert.That(log.Warnings.Single(), Does.Contain("_Result.cshtml"));
            Assert.That(html, Does.Contain(PlainCard));
        });
    }

    /// <summary>
    /// FC-1: the first paint of a filtered URL asks for counts on the attributes the visitor
    /// filtered by - which FC-1 makes always carry those values - and hands the client what they are
    /// called, so no chip and no fallback row paints a stored code before the first response lands.
    /// </summary>
    [Test]
    public async Task The_first_paint_of_a_filtered_URL_hands_over_the_labels_of_the_selected_values()
    {
        var response = TwoResults();
        response.Facets = new Dictionary<string, FacetValue[]>(StringComparer.Ordinal)
        {
            ["ProductFieldTags"] =
            [
                new FacetValue { Value = "ColdBrew", Label = "Cold brew", Count = 2 },
                new FacetValue { Value = "HotTips", Label = "Hot tips", Count = 0 }
            ],
            ["CoffeeTastes"] =
            [
                new FacetValue { Value = "Sweet/Acidy", Label = "Acidy", Count = 0, Path = ["Sweet"] }
            ]
        };

        var pipeline = new FakePipeline(response);
        var schema = new IndexSchema(
            "site-content",
            [
                new SchemaField("ProductFieldTags", SearchFieldKind.Taxonomy, false, true, false, true),
                new SchemaField("CoffeeTastes", SearchFieldKind.Taxonomy, false, true, false, true),
                new SchemaField("ProductFieldPrice", SearchFieldKind.Number, false, false, true, true)
            ]);

        var renderer = new ServerRenderedResults(
            pipeline,
            provider.GetRequiredService<ICompositeViewEngine>(),
            new FakeTemplateRegistry(),
            new CapturingLogger(),
            new FakeSchemas(schema));

        var render = await renderer.RenderAsync(
            ViewContext("?q=coffee&ProductFieldTags=HotTips&CoffeeTastes=Sweet%2FAcidy&ProductFieldPrice_lte=200"),
            new ServerResultsOptions("site-content", 10, [], null, null, null, []),
            CancellationToken.None).ConfigureAwait(false);

        Expect.Multiple(() =>
        {
            Assert.That(
                pipeline.Requests.Single().Facets,
                Is.EqualTo(new[] { "ProductFieldTags", "CoffeeTastes" }),
                "counts are asked for on the filtered attributes only; a numeric refinement is not a facet");
            Assert.That(render!.Labels.Keys, Is.EquivalentTo(new[] { "ProductFieldTags", "CoffeeTastes" }));
            Assert.That(render.Labels["ProductFieldTags"], Is.EqualTo(new Dictionary<string, string> { ["HotTips"] = "Hot tips" }),
                "only the selected values: the client needs to name what the visitor arrived with, not the taxonomy");
            Assert.That(render.Labels["CoffeeTastes"], Is.EqualTo(new Dictionary<string, string> { ["Sweet/Acidy"] = "Acidy" }));
        });
    }

    [Test]
    public async Task An_unfiltered_first_paint_asks_for_no_facets_and_hands_over_no_labels()
    {
        var pipeline = new FakePipeline(TwoResults());

        await RenderAsync(pipeline, queryString: "?q=espresso");

        Expect.Multiple(() =>
        {
            Assert.That(pipeline.Requests.Single().Facets, Is.Null);
            Assert.That(lastRender!.Labels, Is.Empty);
        });
    }

    private static SearchResponse TwoResults() => new()
    {
        Total = 2,
        Page = 1,
        PageSize = 10,
        QueryId = "server-query-1",
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
                        "path": "Home / Blog / Coffee",
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

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);

        return writer.ToString();
    }

    private async Task<string?> RenderAsync(
        ISearchPipeline pipeline,
        Func<ServerResultsOptions, ServerResultsOptions>? configure = null,
        CapturingLogger? logger = null,
        ISearchResultTemplateRegistry? registry = null,
        string queryString = "",
        IIndexSchemaProvider? schemas = null)
    {
        var renderer = new ServerRenderedResults(
            pipeline,
            provider.GetRequiredService<ICompositeViewEngine>(),
            registry ?? new FakeTemplateRegistry(),
            logger ?? new CapturingLogger(),
            schemas);

        var blank = new ServerResultsOptions("site-content", 0, [], null, null, null, []);
        var options = configure is null ? blank : configure(blank);
        var render = await renderer.RenderAsync(ViewContext(queryString), options, CancellationToken.None).ConfigureAwait(false);
        lastRender = render;

        return render is null ? null : Html(render.Content);
    }

    private ViewContext ViewContext(string queryString)
    {
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.QueryString = new QueryString(queryString);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        return new ViewContext(
            actionContext,
            new StubView(),
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            new TempDataDictionary(httpContext, provider.GetRequiredService<ITempDataProvider>()),
            TextWriter.Null,
            new HtmlHelperOptions());
    }

    /// <summary>The view the host is rendering from; nothing here renders it.</summary>
    private sealed class StubView : IView
    {
        public string Path => "/Pages/Search.cshtml";

        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
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

    /// <summary>A schema provider that answers with a fixed schema, or throws.</summary>
    private sealed class FakeSchemas : IIndexSchemaProvider
    {
        private readonly IndexSchema? schema;
        private readonly Exception? failure;

        public FakeSchemas(IndexSchema schema) => this.schema = schema;

        public FakeSchemas(Exception failure) => this.failure = failure;

        public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) =>
            failure is null ? Task.FromResult(schema!) : Task.FromException<IndexSchema>(failure);
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
