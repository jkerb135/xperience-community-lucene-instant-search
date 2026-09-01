using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Rendering;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ResultsIdentifier,
    viewComponentType: typeof(ResultsWidgetViewComponent),
    name: "Search - Results",
    propertiesType: typeof(ResultsWidgetProperties),
    Description = "The result list of a search.",
    IconClass = "icon-list",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the results widget (spec §7.3).</summary>
public sealed class ResultsWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>Gets or sets how many results a page shows. Zero keeps the API's configured default.</summary>
    [NumberInputComponent(
        Label = "Results per page",
        ExplanationText = "Leave at 0 to use the page size configured for the index.",
        Order = OrderFirstWidgetProperty)]
    public int ResultsPerPage { get; set; }

    /// <summary>Gets or sets the identifier of a registered result template (spec §5.8).</summary>
    [DropDownComponent(
        Label = "Result template",
        Placeholder = "Default template",
        DataProviderType = typeof(ResultTemplateOptionsProvider),
        Order = OrderFirstWidgetProperty + 10)]
    public string ResultTemplate { get; set; } = string.Empty;

    /// <summary>Gets or sets the document fields to retrieve, one per line. Empty retrieves the index defaults.</summary>
    [TextAreaComponent(
        Label = "Fields to show",
        ExplanationText = "One index field name per line, for example title, url, summary, image. Leave empty for the defaults.",
        Order = OrderFirstWidgetProperty + 20)]
    public string Fields { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute the default template reads the title from. Empty keeps <c>title</c>.</summary>
    [TextInputComponent(
        Label = "Title attribute",
        ExplanationText = "Index field the card's heading comes from. Leave empty for title.",
        Order = OrderFirstWidgetProperty + 30)]
    public string TitleAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute the default template links to. Empty keeps <c>url</c>.</summary>
    [TextInputComponent(
        Label = "Link attribute",
        ExplanationText = "Index field the card links to. Leave empty for url.",
        Order = OrderFirstWidgetProperty + 40)]
    public string UrlAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attributes tried, in order, for the snippet, one per line.</summary>
    [TextAreaComponent(
        Label = "Snippet attributes",
        ExplanationText = "One index field name per line, tried in order; the first one with a value wins. Leave empty for summary, content, excerpt.",
        Order = OrderFirstWidgetProperty + 50)]
    public string SnippetAttributes { get; set; } = string.Empty;
}

/// <summary>Renders the <c>results</c> mount.</summary>
public sealed class ResultsWidgetViewComponent : XpSearchMountWidgetViewComponent<ResultsWidgetProperties>
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    private readonly ServerRenderedResults? serverResults;

    /// <summary>Initializes a new instance of the <see cref="ResultsWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="serverResults">
    /// Renders the first page of results server-side (spec §5.8). Optional: without it - a host that
    /// registered the widgets but not <c>AddXpSearch()</c> - the mount is left empty for the client.
    /// </param>
    public ResultsWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog,
        ServerRenderedResults? serverResults = null)
        : base(renderer, editorContext, indexCatalog) => this.serverResults = serverResults;

    /// <inheritdoc />
    protected override string WidgetType => "results";

    /// <inheritdoc />
    protected override void BuildConfig(ResultsWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(properties.ResultTemplate))
        {
            config["template"] = properties.ResultTemplate.Trim();
        }

        // Which attribute a card shows is a display option of this list, not of the search.
        if (!string.IsNullOrWhiteSpace(properties.TitleAttribute))
        {
            config["titleAttribute"] = properties.TitleAttribute.Trim();
        }

        if (!string.IsNullOrWhiteSpace(properties.UrlAttribute))
        {
            config["urlAttribute"] = properties.UrlAttribute.Trim();
        }

        var snippets = ParseLines(properties.SnippetAttributes);
        if (snippets.Count > 0)
        {
            config["snippetAttributes"] = snippets;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Page size and retrieved fields are properties of the search, not of the list that displays it,
    /// so they belong in the instance options the bootstrap passes to <c>createSearch()</c>.
    /// </remarks>
    protected override void BuildInstanceConfig(ResultsWidgetProperties properties, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(instanceConfig);

        if (properties.ResultsPerPage > 0)
        {
            instanceConfig["initialState"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pageSize"] = properties.ResultsPerPage
            };
        }

        var fields = ParseLines(properties.Fields);
        if (fields.Count > 0)
        {
            instanceConfig["fields"] = fields;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The first paint of a shared result URL is rendered server-side, so results are there before
    /// the bundle runs and a visitor without JavaScript still sees them (spec §5.8).
    /// </remarks>
    protected override Task<IHtmlContent?> BuildMountContentAsync(
        ResultsWidgetProperties properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var viewContext = ViewComponentContext.ViewContext;

        if (serverResults is null || viewContext?.HttpContext is null)
        {
            return Task.FromResult<IHtmlContent?>(null);
        }

        return serverResults.RenderAsync(
            viewContext,
            new ServerResultsOptions(
                CurrentIndex,
                properties.ResultsPerPage,
                ParseLines(properties.Fields),
                properties.ResultTemplate,
                properties.TitleAttribute,
                properties.UrlAttribute,
                ParseLines(properties.SnippetAttributes)),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(ResultsWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // Four cards is enough to read as a list; a page size of 50 must not fill the builder.
        int cards = Math.Clamp(properties.ResultsPerPage <= 0 ? 3 : properties.ResultsPerPage, 1, 4);
        var list = EditorPreview.El("ol", "xps-results__list");

        for (int card = 0; card < cards; card++)
        {
            list.Add(EditorPreview.El("li", "xps-results__item")
                .Add(EditorPreview.El("article", "xps-result xps-result--skeleton")
                    .Add(EditorPreview.El("div", "xps-result__body")
                        .Add(
                            EditorPreview.Skeleton("title"),
                            EditorPreview.Skeleton("text"),
                            EditorPreview.Skeleton("text")))));
        }

        var fields = ParseLines(properties.Fields);

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("div", "xps-results").Add(list))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Results,
                properties.ResultsPerPage > 0
                    ? properties.ResultsPerPage.ToString(CultureInfo.CurrentUICulture)
                    : WidgetResources.Preview_Unset,
                string.IsNullOrWhiteSpace(properties.ResultTemplate) ? WidgetResources.Preview_Unset : properties.ResultTemplate.Trim(),
                fields.Count > 0 ? string.Join(", ", fields) : WidgetResources.Preview_Unset)));
    }

    private static IReadOnlyList<string> ParseLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
