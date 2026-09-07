using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>clearFilters</c> widget - the button that removes every refinement.</summary>
public sealed record ClearFiltersOptions : XpSearchMountOptions
{
    /// <summary>Gets the button text. Empty keeps "Clear all".</summary>
    public string? Label { get; init; }
}

/// <summary><c>&lt;xps-clear-filters /&gt;</c> - mounts the <c>clearFilters</c> widget.</summary>
[HtmlTargetElement("xps-clear-filters")]
public sealed class ClearFiltersTagHelper : XpSearchMountTagHelper<ClearFiltersOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ClearFiltersTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ClearFiltersTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ClearFiltersOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "clearFilters";

    /// <inheritdoc />
    protected override ClearFiltersOptions Merge(ClearFiltersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with { Label = Label ?? options.Label };
    }

    /// <inheritdoc />
    /// <remarks>
    /// A real "Clear all" link when the visitor arrived filtering by something (SK-1 §2.3): the same
    /// URL without the filters and without the page, so it works before the bundle runs. The client
    /// replaces it with its own button on the first response. A skeleton when nothing is filtered, or
    /// when no search has run before this widget - the names of what is filtered come from the search
    /// the results widget of this instance already ran (§2.4).
    /// </remarks>
    protected override Task<IHtmlContent?> BuildContentAsync(
        ClearFiltersOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var httpContext = ViewContext?.HttpContext;
        var paint = ServerFirstPaint.Find(httpContext, CurrentInstanceId);

        if (paint is null || httpContext is null || paint.Labels.Count == 0)
        {
            return base.BuildContentAsync(options, cancellationToken);
        }

        var content = new HtmlContentBuilder()
            .AppendHtml("<div data-xps-server-rendered class=\"xps xps-clear-filters\">")
            .AppendHtml("<a class=\"xps-button xps-button--link xps-clear-filters__button\" href=\"")
            .Append(Cleared(httpContext.Request.Query, paint.Labels.Keys))
            .AppendHtml("\">")
            .Append(string.IsNullOrWhiteSpace(options.Label) ? "Clear all" : options.Label)
            .AppendHtml("</a></div>");

        return Task.FromResult<IHtmlContent?>(content);
    }

    /// <summary>
    /// The current URL without the filters the search applied and without the page. Foreign
    /// parameters a page URL carries (<c>utm_*</c>, Kentico's <c>uh</c>) and the state the visitor did
    /// not filter with (<c>q</c>, <c>sort</c>) are kept, so clearing refines nothing else away.
    /// </summary>
    private static string Cleared(IQueryCollection query, IEnumerable<string> attributes)
    {
        var filters = new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase);

        var pairs = query
            .Where(parameter =>
                !string.Equals(parameter.Key, "page", StringComparison.Ordinal)
                && !filters.Contains(parameter.Key)
                && !(parameter.Key.EndsWith("_op", StringComparison.Ordinal) && filters.Contains(parameter.Key[..^3])))
            .SelectMany(parameter => parameter.Value.Select(value => KeyValuePair.Create<string, string?>(parameter.Key, value)))
            .ToList();

        string url = QueryString.Create(pairs).ToString();

        return url.Length > 0 ? url : "?";
    }
}
