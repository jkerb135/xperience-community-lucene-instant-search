using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Core.Rendering;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>pagination</c> widget - moving between the pages of a result list.</summary>
public sealed record PaginationOptions : XpSearchMountOptions
{
    /// <summary>The <see cref="Style"/> value that renders numbered page links.</summary>
    public const string StyleNumbered = "numbered";

    /// <summary>The <see cref="Style"/> value that renders a "load more" button.</summary>
    public const string StyleLoadMore = "loadMore";

    /// <summary>Gets which pagination control is rendered: <see cref="StyleNumbered"/> or <see cref="StyleLoadMore"/>.</summary>
    public string Style { get; init; } = StyleNumbered;
}

/// <summary><c>&lt;xps-pagination /&gt;</c> - mounts the <c>pagination</c> or <c>loadMore</c> widget.</summary>
[HtmlTargetElement("xps-pagination")]
public sealed class PaginationTagHelper : XpSearchMountTagHelper<PaginationOptions>
{
    /// <summary>Pages offered either side of the current one; the client's own default.</summary>
    private const int Padding = 3;

    /// <summary>Initializes a new instance of the <see cref="PaginationTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public PaginationTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="PaginationOptions.Style"/>.</summary>
    [HtmlAttributeName("style")]
    public string? Style { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "pagination";

    /// <inheritdoc />
    /// <remarks>The style picks the JavaScript widget rather than becoming an option of one.</remarks>
    public override string GetWidgetType(PaginationOptions options) =>
        string.Equals(options?.Style, PaginationOptions.StyleLoadMore, StringComparison.OrdinalIgnoreCase)
            ? "loadMore"
            : "pagination";

    /// <inheritdoc />
    protected override PaginationOptions Merge(PaginationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with { Style = Style ?? options.Style };
    }

    /// <inheritdoc />
    /// <remarks>The widget takes no options of its own; the style is in the widget type.</remarks>
    protected override void BuildConfig(PaginationOptions options, IDictionary<string, object?> config)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// Real page links when the results widget of this instance has already painted (SK-1 §2.3/§2.4):
    /// the same window of pages, the same markup and the same <c>page</c> parameter the client's
    /// <c>urlFor</c> writes, so a crawler and a visitor without JavaScript can page through the
    /// results. A skeleton when no search has run before this widget.
    /// </remarks>
    protected override Task<IHtmlContent?> BuildContentAsync(
        PaginationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var httpContext = ViewContext?.HttpContext;
        var paint = ServerFirstPaint.Find(httpContext, CurrentInstanceId);

        if (paint is null || httpContext is null || paint.PageSize <= 0 || !string.Equals(GetWidgetType(options), "pagination", StringComparison.Ordinal))
        {
            return base.BuildContentAsync(options, cancellationToken);
        }

        long totalPages = (paint.Total + paint.PageSize - 1) / paint.PageSize;

        // One page is nothing to navigate: the client hides the whole control there, so the server
        // renders nothing rather than a row of dead ends.
        if (totalPages <= 1)
        {
            return Task.FromResult<IHtmlContent?>(null);
        }

        var query = httpContext.Request.Query;
        long current = Math.Clamp(paint.Page, 1, totalPages);
        // The client's window (behaviors/pagination.ts): padding 3 either side, kept full at the ends.
        long first = Math.Max(1, Math.Min(current - Padding, totalPages - (Padding * 2)));
        long last = Math.Min(totalPages, Math.Max(current + Padding, (Padding * 2) + 1));

        var nav = new HtmlContentBuilder()
            .AppendHtml("<nav data-xps-server-rendered class=\"xps xps-pagination\" aria-label=\"Search results pages\">")
            .AppendHtml("<ul class=\"xps-pagination__list\">");

        Control(nav, query, "first", "First page", "&laquo;", 1, current == 1);
        Control(nav, query, "previous", "Previous page", "&lsaquo;", Math.Max(1, current - 1), current == 1);

        if (first > 1)
        {
            Page(nav, query, 1, false);

            if (first > 2)
            {
                Ellipsis(nav);
            }
        }

        for (long page = first; page <= last; page++)
        {
            Page(nav, query, page, page == current);
        }

        if (last < totalPages)
        {
            if (last < totalPages - 1)
            {
                Ellipsis(nav);
            }

            Page(nav, query, totalPages, false);
        }

        Control(nav, query, "next", "Next page", "&rsaquo;", Math.Min(totalPages, current + 1), current >= totalPages);
        Control(nav, query, "last", "Last page", "&raquo;", totalPages, current >= totalPages);

        return Task.FromResult<IHtmlContent?>(nav.AppendHtml("</ul></nav>"));
    }

    private static void Control(
        IHtmlContentBuilder nav,
        IQueryCollection query,
        string kind,
        string name,
        string glyph,
        long page,
        bool disabled)
    {
        string body = $"<span aria-hidden=\"true\">{glyph}</span><span class=\"xps-sr-only\">{name}</span>";
        // `rel` on the two step controls only: prev/next describe the sequence a crawler follows.
        string rel = kind switch { "previous" => " rel=\"prev\"", "next" => " rel=\"next\"", _ => string.Empty };

        nav.AppendHtml($"<li class=\"xps-pagination__item xps-pagination__item--{kind}")
            .AppendHtml(disabled ? " xps-pagination__item--disabled\">" : "\">");

        if (disabled)
        {
            nav.AppendHtml($"<span class=\"xps-pagination__link\" aria-disabled=\"true\">{body}</span>");
        }
        else
        {
            nav.AppendHtml($"<a class=\"xps-pagination__link\"{rel} href=\"")
                .Append(SearchQueryState.WithPage(query, page))
                .AppendHtml($"\" data-xps-page=\"{page}\">{body}</a>");
        }

        nav.AppendHtml("</li>");
    }

    private static void Page(IHtmlContentBuilder nav, IQueryCollection query, long page, bool current)
    {
        nav.AppendHtml("<li class=\"xps-pagination__item xps-pagination__item--page")
            .AppendHtml(current ? " xps-pagination__item--current\">" : "\">")
            .AppendHtml("<a class=\"xps-pagination__link\" href=\"")
            .Append(SearchQueryState.WithPage(query, page))
            .AppendHtml($"\" data-xps-page=\"{page}\"")
            .AppendHtml(current ? " aria-current=\"page\">" : ">")
            .AppendHtml($"<span class=\"xps-sr-only\">Page </span>{page}</a></li>");
    }

    private static void Ellipsis(IHtmlContentBuilder nav) =>
        nav.AppendHtml(
            "<li class=\"xps-pagination__item xps-pagination__item--ellipsis\">"
            + "<span class=\"xps-pagination__ellipsis\" aria-hidden=\"true\">&hellip;</span></li>");
}
