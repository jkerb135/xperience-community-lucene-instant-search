using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Core.Rendering;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>results</c> widget - the result list.</summary>
public sealed record ResultsOptions : XpSearchMountOptions
{
    /// <summary>What a widget that asked for no page size is given (AR-3).</summary>
    public const int DefaultResultsPerPage = 20;

    /// <summary>Gets how many results a page shows. An instance-wide option: pagination steps in it.</summary>
    public int ResultsPerPage { get; init; } = DefaultResultsPerPage;

    /// <summary>Gets the identifier of a registered result template (spec §5.8). Empty renders the shipped card.</summary>
    public string? Template { get; init; }

    /// <summary>Gets the document fields to retrieve. Empty retrieves the index defaults. An instance-wide option.</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>Gets the attribute the default card reads the title from. Empty keeps <c>title</c>.</summary>
    public string? TitleAttribute { get; init; }

    /// <summary>Gets the attribute the default card links to. Empty keeps <c>url</c>.</summary>
    public string? UrlAttribute { get; init; }

    /// <summary>Gets the attributes tried, in order, for the snippet. Empty keeps summary, content, excerpt.</summary>
    public IReadOnlyList<string> SnippetAttributes { get; init; } = [];
}

/// <summary><c>&lt;xps-results /&gt;</c> - mounts the <c>results</c> widget, first paint included.</summary>
/// <remarks>Not sealed: SK-1 replaces the first paint with a skeleton by deriving from it (RZ-1 §8).</remarks>
[HtmlTargetElement("xps-results")]
public class ResultsTagHelper : XpSearchMountTagHelper<ResultsOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ResultsTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="serverResults">
    /// Renders the first page of results server-side (spec §5.8). Optional: without it - a host that
    /// registered the widgets but not <c>AddXpSearch()</c> - the mount is left empty for the client.
    /// </param>
    public ResultsTagHelper(
        IXpSearchMountRenderer renderer,
        IXpSearchIndexCatalog indexCatalog,
        ServerRenderedResults? serverResults = null)
        : base(renderer, indexCatalog) => ServerResults = serverResults;

    /// <summary>
    /// Gets the server-side renderer of the first page of results, or <see langword="null"/> when the
    /// host did not register it. A subclass that replaces <see cref="BuildContentAsync"/> - the
    /// skeleton first paint of SK-1 - needs it to decide what it may render.
    /// </summary>
    protected ServerRenderedResults? ServerResults { get; }

    /// <summary>
    /// Gets what the server actually painted in this render, or <see langword="null"/> when it painted
    /// nothing: the instance config hands the client the page size and the query id the server used.
    /// A subclass may read it after <see cref="BuildContentAsync"/> has run, for the widgets that
    /// share this first paint (pagination, result stats, active filters).
    /// </summary>
    protected ServerResultsRender? FirstPaint { get; private set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.ResultsPerPage"/>.</summary>
    [HtmlAttributeName("results-per-page")]
    public int? ResultsPerPage { get; set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.Template"/>.</summary>
    [HtmlAttributeName("template")]
    public string? Template { get; set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.Fields"/>.</summary>
    [HtmlAttributeName("fields")]
    public IEnumerable<string>? Fields { get; set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.TitleAttribute"/>.</summary>
    [HtmlAttributeName("title-attribute")]
    public string? TitleAttribute { get; set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.UrlAttribute"/>.</summary>
    [HtmlAttributeName("url-attribute")]
    public string? UrlAttribute { get; set; }

    /// <summary>Gets or sets <see cref="ResultsOptions.SnippetAttributes"/>.</summary>
    [HtmlAttributeName("snippet-attributes")]
    public IEnumerable<string>? SnippetAttributes { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "results";

    /// <inheritdoc />
    protected override ResultsOptions Merge(ResultsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            ResultsPerPage = ResultsPerPage ?? options.ResultsPerPage,
            Template = Template ?? options.Template,
            Fields = Fields?.ToList() ?? options.Fields,
            TitleAttribute = TitleAttribute ?? options.TitleAttribute,
            UrlAttribute = UrlAttribute ?? options.UrlAttribute,
            SnippetAttributes = SnippetAttributes?.ToList() ?? options.SnippetAttributes
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(ResultsOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(options.Template))
        {
            config["template"] = options.Template.Trim();
        }

        // Which attribute a card shows is a display option of this list, not of the search.
        if (!string.IsNullOrWhiteSpace(options.TitleAttribute))
        {
            config["titleAttribute"] = options.TitleAttribute.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.UrlAttribute))
        {
            config["urlAttribute"] = options.UrlAttribute.Trim();
        }

        if (options.SnippetAttributes.Count > 0)
        {
            config["snippetAttributes"] = options.SnippetAttributes;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Page size and retrieved fields are properties of the search, not of the list that displays it,
    /// so they belong in the instance options the bootstrap passes to <c>createSearch()</c>.
    /// </remarks>
    protected override void BuildInstanceConfig(ResultsOptions options, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instanceConfig);

        // The page size the server actually applied, not the one the widget asked for: the index's
        // maximum may have clamped it, and the hydration query must ask for the same page the visitor
        // is already looking at.
        instanceConfig["initialState"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["pageSize"] = FirstPaint?.PageSize ?? PageSize(options)
        };

        // Only when the server really answered a search: the client then reuses the id instead of
        // journaling the same page load twice.
        if (!string.IsNullOrWhiteSpace(FirstPaint?.QueryId))
        {
            instanceConfig["initialQueryId"] = FirstPaint.QueryId;
        }

        if (options.Fields.Count > 0)
        {
            instanceConfig["fields"] = options.Fields;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The first paint of a shared result URL is rendered server-side, so results are there before
    /// the bundle runs and a visitor without JavaScript still sees them (spec §5.8).
    /// </remarks>
    protected override async Task<IHtmlContent?> BuildContentAsync(
        ResultsOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        FirstPaint = null;

        if (ServerResults is null || ViewContext?.HttpContext is null)
        {
            return null;
        }

        FirstPaint = await ServerResults.RenderAsync(
            ViewContext,
            new ServerResultsOptions(
                CurrentIndex,
                PageSize(options),
                options.Fields,
                options.Template,
                options.TitleAttribute,
                options.UrlAttribute,
                options.SnippetAttributes,
                XpSearchWidgetConstants.DefaultResultViewPath),
            cancellationToken).ConfigureAwait(false);

        // What the visitor's own refinements are called, so the client can name them before its first
        // response arrives (FC-1).
        MountLabels = FirstPaint?.Labels;

        return FirstPaint?.Content;
    }

    // A page size of 0 is a validation error on the wire, so it never reaches the client.
    private static int PageSize(ResultsOptions options) =>
        options.ResultsPerPage > 0 ? options.ResultsPerPage : ResultsOptions.DefaultResultsPerPage;
}
