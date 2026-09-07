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
using XpSearch.Widgets.Rendering;
using XpSearch.Widgets.TagHelpers;

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
        // The widget services a host registers, with the project's indexes stubbed.
        services.AddSingleton<XpSearch.Widgets.Options.IXpSearchIndexCatalog>(new FakeIndexCatalog("site-content"));
        services.AddXpSearchWidgets();

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
            // Parity: the client must ask for the page size the pipeline actually applied - the
            // index's maximum may have clamped what the widget asked for - not the widget's own.
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
            // The widget's own page size still goes over: only what the server rendered is missing.
            Assert.That(instance.GetProperty("initialState").GetProperty("pageSize").GetInt32(), Is.EqualTo(20));
        });
    }

    /// <summary>
    /// FC-1: what the visitor's own refinements are called crosses to the client as
    /// <c>data-xps-labels</c> on the mount, so the chips and the selected-but-empty rows of a
    /// filtered cold load never paint a stored code.
    /// </summary>
    [Test]
    public async Task The_mount_of_a_filtered_URL_carries_the_labels_of_the_selected_values()
    {
        var response = TwoResults();
        response.Facets = new Dictionary<string, FacetValue[]>(StringComparer.Ordinal)
        {
            ["ProductFieldTags"] =
            [
                new FacetValue { Value = "ColdBrew", Label = "Cold brew", Count = 4 },
                new FacetValue { Value = "HotTips", Label = "Hot tips", Count = 0 }
            ]
        };

        var component = ResultsWidget(new FakePipeline(response), queryString: "?q=coffee&ProductFieldTags=HotTips");

        var model = await component
            .BuildModelAsync(new ResultsWidgetProperties { Index = "site-content" }, CancellationToken.None)
            .ConfigureAwait(false);

        string markup = Rendered.Html(model.Mount!);
        var labels = Rendered.Json(markup, "data-xps-labels");

        Expect.Multiple(() =>
        {
            Assert.That(labels.GetProperty("ProductFieldTags").GetProperty("HotTips").GetString(), Is.EqualTo("Hot tips"));
            Assert.That(
                labels.GetProperty("ProductFieldTags").TryGetProperty("ColdBrew", out _),
                Is.False,
                "only what the visitor selected; the client learns the rest from the response");
        });
    }

    [Test]
    public async Task An_unfiltered_mount_carries_no_labels_attribute()
    {
        var component = ResultsWidget(new FakePipeline(TwoResults()));

        var model = await component
            .BuildModelAsync(new ResultsWidgetProperties { Index = "site-content" }, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(Rendered.Html(model.Mount!), Does.Not.Contain("data-xps-labels"));
    }

    /// <summary>
    /// RZ-1: the <c>&lt;xps-results&gt;</c> tag element renders the same first paint, labels included,
    /// as the Page Builder widget - the parity check for the one widget that has server content.
    /// </summary>
    [Test]
    public void The_results_tag_element_renders_the_same_first_paint_as_the_widget()
    {
        var response = TwoResults();
        response.Facets = new Dictionary<string, FacetValue[]>(StringComparer.Ordinal)
        {
            ["ProductFieldTags"] = [new FacetValue { Value = "HotTips", Label = "Hot tips", Count = 0 }]
        };

        const string queryString = "?q=coffee&ProductFieldTags=HotTips";
        var properties = new ResultsWidgetProperties { Index = "site-content", ResultsPerPage = 5 };
        var component = ResultsWidget(new FakePipeline(response), queryString: queryString);

        string widget = Rendered.Html(component.BuildModel(properties).Mount!);
        string tag = TagHelperTests.Tag(
            new ResultsTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content"), ServerResults(new FakePipeline(response)))
            {
                Options = component.ToOptions(properties),
                ViewContext = ViewContext(queryString)
            },
            "xps-results");

        Expect.Multiple(() =>
        {
            Assert.That(tag, Does.Contain("data-xps-server-rendered"));
            Assert.That(tag, Does.Contain("data-xps-labels"));
            Assert.That(tag, Is.EqualTo(widget));
        });
    }

    /// <summary>RZ-1 §1.5: <c>Html.XpSearchAsync</c> emits what the tag element emits.</summary>
    [Test]
    public async Task The_html_helper_renders_a_widget_from_its_options_record()
    {
        var html = provider.GetRequiredService<IHtmlHelper>();
        ((IViewContextAware)html).Contextualize(ViewContext("?q=espresso"));

        var options = new FacetListOptions { Index = "site-content", Attribute = "tags", Label = "Tags" };
        string rendered = Rendered.Html(await html.XpSearchAsync(options).ConfigureAwait(false));

        string tag = TagHelperTests.Tag(
            new FacetListTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { Options = options },
            "xps-facet-list");

        Assert.That(rendered, Is.EqualTo(tag));
    }

    /// <summary>
    /// SK-1 §2.3/§2.4: the widgets that follow the results share the search it already ran - through
    /// the per-request store, so document order decides. Here the results come first.
    /// </summary>
    [Test]
    public async Task The_widgets_after_the_results_render_from_the_search_it_already_ran()
    {
        var response = TwoResults();
        response.Total = 46;
        response.PageSize = 10;
        response.Facets = new Dictionary<string, FacetValue[]>(StringComparer.Ordinal)
        {
            ["ProductFieldTags"] = [new FacetValue { Value = "HotTips", Label = "Hot tips", Count = 3 }]
        };

        const string queryString = "?q=espresso&ProductFieldTags=HotTips";
        var viewContext = ViewContext(queryString);

        await Widgets
            .Results(new XpSearchMountRenderer(), new FakeEditorContext(XpSearchEditorMode.Live), new FakeIndexCatalog("site-content"), ServerResults(new FakePipeline(response)))
            .WithViewContext(viewContext)
            .BuildModelAsync(new ResultsWidgetProperties { Index = "site-content", ResultsPerPage = 10 }, CancellationToken.None)
            .ConfigureAwait(false);

        string pagination = TagHelperTests.Tag(
            new PaginationTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-pagination");
        string stats = TagHelperTests.Tag(
            new ResultStatsTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-result-stats");
        string chips = TagHelperTests.Tag(
            new ActiveFiltersTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-active-filters");

        Expect.Multiple(() =>
        {
            Assert.That(pagination, Does.Contain("<nav data-xps-server-rendered class=\"xps xps-pagination\" aria-label=\"Search results pages\">"));
            Assert.That(pagination, Does.Contain("<a class=\"xps-pagination__link\" rel=\"next\" href=\"?q=espresso&amp;ProductFieldTags=HotTips&amp;page=2\" data-xps-page=\"2\">"));
            Assert.That(pagination, Does.Contain("aria-current=\"page\""));
            // Five pages of ten, so nothing is elided and the ends are enabled.
            Assert.That(pagination, Does.Not.Contain("xps-pagination__item--ellipsis"));
            Assert.That(pagination, Does.Not.Contain("--skeleton"));

            Assert.That(stats, Does.Contain(
                "<span class=\"xps-result-stats__text\"><strong class=\"xps-result-stats__total\">46</strong>"
                + " results for &ldquo;espresso&rdquo;</span>"));

            Assert.That(chips, Does.Contain("<span class=\"xps-chip__value\">Hot tips</span>"));
            // Taking the only filter off leaves the query and drops the page.
            Assert.That(chips, Does.Contain("<a class=\"xps-chip__remove\" href=\"?q=espresso\" aria-label=\"Remove filter Hot tips\">"));
        });
    }

    /// <summary>The other order: nothing has run yet, so the same widgets paint their skeletons.</summary>
    [Test]
    public void A_widget_rendered_before_the_results_paints_its_skeleton()
    {
        var viewContext = ViewContext("?q=espresso");

        string pagination = TagHelperTests.Tag(
            new PaginationTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-pagination");
        string stats = TagHelperTests.Tag(
            new ResultStatsTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-result-stats");
        string chips = TagHelperTests.Tag(
            new ActiveFiltersTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")) { ViewContext = viewContext },
            "xps-active-filters");

        Expect.Multiple(() =>
        {
            Assert.That(pagination, Does.Contain("xps-pagination--skeleton").And.Not.Contain("<a "));
            Assert.That(stats, Does.Contain("xps-result-stats--skeleton"));
            Assert.That(chips, Does.Contain("xps-active-filters--skeleton"));
        });
    }

    /// <summary>
    /// SK-1 §2.3: the search box is the real GET form, ids per the client's <c>widgetId</c> rule, so
    /// submitting it without JavaScript reloads the page with <c>?q=</c> - and the client's first
    /// render replaces it with the same markup.
    /// </summary>
    [Test]
    public void The_search_box_renders_the_form_the_client_renders()
    {
        string markup = TagHelperTests.Tag(
            new SearchBoxTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content"))
            {
                InstanceId = "search-1",
                ViewContext = ViewContext("?q=espresso")
            },
            "xps-search-box");

        Assert.That(
            markup[(markup.IndexOf('>', StringComparison.Ordinal) + 1)..markup.LastIndexOf("</div>", StringComparison.Ordinal)],
            Is.EqualTo(SearchBoxForm));
    }

    /// <summary>
    /// What <c>searchBox</c>'s first render produces for the same state (<c>widgets/searchBox.ts</c>,
    /// <c>themes/fixtures/search-box.html</c>), with the three deliberate differences of a server
    /// form: the handover marker, <c>method="get"</c>, and the value the visitor arrived with (which
    /// the client assigns to the input rather than to the attribute).
    /// </summary>
    private const string SearchBoxForm =
        "<form data-xps-server-rendered class=\"xps xps-search-box\" role=\"search\" method=\"get\" novalidate>"
        + "<label class=\"xps-search-box__label xps-sr-only\" for=\"xps-search-1-search-box-input\">Search this site</label>"
        + "<div class=\"xps-search-box__field\">"
        + "<svg class=\"xps-search-box__icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\""
        + " stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\""
        + " focusable=\"false\"><circle cx=\"11\" cy=\"11\" r=\"7\"></circle><path d=\"m20 20-3.6-3.6\"></path></svg>"
        + "<input class=\"xps-search-box__input\" id=\"xps-search-1-search-box-input\" type=\"search\" name=\"q\""
        + " value=\"espresso\" placeholder=\"Search&#x2026;\" autocomplete=\"off\" autocapitalize=\"off\""
        + " autocorrect=\"off\" spellcheck=\"false\">"
        + "<span class=\"xps-search-box__loading xps-skeleton\" aria-hidden=\"true\"></span>"
        + "<button class=\"xps-button xps-search-box__reset\" type=\"reset\" aria-label=\"Clear the search query\">"
        + "<span aria-hidden=\"true\">&times;</span></button></div></form>";

    private ServerRenderedResults ServerResults(ISearchPipeline pipeline) =>
        new(
            pipeline,
            provider.GetRequiredService<ICompositeViewEngine>(),
            new FakeTemplateRegistry(),
            new CapturingLogger());

    private ResultsWidgetViewComponent ResultsWidget(
        ISearchPipeline pipeline,
        IXpSearchEditorContext? editor = null,
        string queryString = "?q=espresso") =>
        Widgets.Results(
            new XpSearchMountRenderer(),
            editor ?? new FakeEditorContext(XpSearchEditorMode.Live),
            new FakeIndexCatalog("site-content"),
            new ServerRenderedResults(
                pipeline,
                provider.GetRequiredService<ICompositeViewEngine>(),
                new FakeTemplateRegistry(),
                new CapturingLogger()))
            .WithViewContext(ViewContext(queryString));

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
