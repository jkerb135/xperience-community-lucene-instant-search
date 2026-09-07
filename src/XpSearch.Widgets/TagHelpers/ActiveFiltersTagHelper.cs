using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>activeFilters</c> widget - the visitor's refinements as removable chips.</summary>
public sealed record ActiveFiltersOptions : XpSearchMountOptions
{
    /// <summary>Gets the heading screen readers announce for the chip list. It is never shown on screen.</summary>
    public string? Title { get; init; }

    /// <summary>Gets whether the chips stay on one scrolling row instead of wrapping.</summary>
    public bool Scroll { get; init; }
}

/// <summary><c>&lt;xps-active-filters /&gt;</c> - mounts the <c>activeFilters</c> widget.</summary>
[HtmlTargetElement("xps-active-filters")]
public sealed class ActiveFiltersTagHelper : XpSearchMountTagHelper<ActiveFiltersOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveFiltersTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ActiveFiltersTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Title"/>.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Scroll"/>.</summary>
    [HtmlAttributeName("scroll")]
    public bool? Scroll { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "activeFilters";

    /// <inheritdoc />
    protected override ActiveFiltersOptions Merge(ActiveFiltersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Title = Title ?? options.Title,
            Scroll = Scroll ?? options.Scroll
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Real chips for what the visitor arrived filtering by (SK-1 §2.3): each one a link to the same
    /// URL without that value, so a visitor without JavaScript can take a filter off. The names come
    /// from the search the results widget of this instance already ran (§2.4) - it is the only thing
    /// on the server that knows what a stored value is called (FC-1). A skeleton when nothing is
    /// filtered, or when no search has run before this widget.
    /// </remarks>
    protected override Task<IHtmlContent?> BuildContentAsync(
        ActiveFiltersOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var httpContext = ViewContext?.HttpContext;
        var paint = ServerFirstPaint.Find(httpContext, CurrentInstanceId);

        if (paint is null || httpContext is null || paint.Labels.Count == 0)
        {
            return base.BuildContentAsync(options, cancellationToken);
        }

        string id = $"xps-{CurrentInstanceId}-active-filters-title";
        var chips = new HtmlContentBuilder()
            .AppendHtml("<div data-xps-server-rendered class=\"xps xps-active-filters")
            .AppendHtml(options.Scroll ? " xps-active-filters--scroll\">" : "\">")
            .AppendHtml("<h3 class=\"xps-active-filters__title xps-sr-only\" id=\"").Append(id).AppendHtml("\">")
            .Append(string.IsNullOrWhiteSpace(options.Title) ? "Active filters" : options.Title)
            .AppendHtml("</h3><ul class=\"xps-active-filters__list\" aria-labelledby=\"").Append(id).AppendHtml("\">");

        foreach ((string attribute, var values) in paint.Labels)
        {
            foreach ((string value, string label) in values)
            {
                chips
                    .AppendHtml("<li class=\"xps-active-filters__item\"><span class=\"xps-chip\">")
                    .AppendHtml("<span class=\"xps-chip__label\"><span class=\"xps-chip__value\">")
                    .Append(label)
                    .AppendHtml("</span></span><a class=\"xps-chip__remove\" href=\"")
                    .Append(Without(httpContext.Request.Query, attribute, value))
                    .AppendHtml("\" aria-label=\"Remove filter ")
                    .Append(label)
                    .AppendHtml("\"><span aria-hidden=\"true\">&times;</span></a></span></li>");
            }
        }

        return Task.FromResult<IHtmlContent?>(chips.AppendHtml("</ul></div>"));
    }

    /// <summary>
    /// The current URL without one value of one filter, back on the first page - what taking a chip
    /// off means. Values are comma-joined and <c>encodeURIComponent</c>-escaped inside their
    /// parameter, the way the client writes them, so the tokens are compared decoded and the survivors
    /// re-joined as they were.
    /// </summary>
    private static string Without(IQueryCollection query, string attribute, string value)
    {
        var pairs = new List<KeyValuePair<string, string?>>();

        foreach ((string key, var raw) in query)
        {
            if (string.Equals(key, "page", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string? one in raw)
            {
                // The response names facets with the schema's casing, the URL with the visitor's.
                if (!string.Equals(key, attribute, StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add(new KeyValuePair<string, string?>(key, one));

                    continue;
                }

                string kept = string.Join(
                    ',',
                    (one ?? string.Empty)
                        .Split(',')
                        .Where(token => token.Length > 0 && !string.Equals(Uri.UnescapeDataString(token), value, StringComparison.Ordinal)));

                if (kept.Length > 0)
                {
                    pairs.Add(new KeyValuePair<string, string?>(key, kept));
                }
            }
        }

        string url = QueryString.Create(pairs).ToString();

        return url.Length > 0 ? url : "?";
    }
}
