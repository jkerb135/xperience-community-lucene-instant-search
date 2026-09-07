using Microsoft.AspNetCore.Razor.TagHelpers;

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

    /// <summary>Gets how many page links are shown either side of the current one. Null keeps the JavaScript default, 3.</summary>
    public int? Padding { get; init; }

    /// <summary>Gets whether the "first page" control is offered. Null keeps the JavaScript default, <see langword="true"/>.</summary>
    public bool? ShowFirst { get; init; }

    /// <summary>Gets whether the "last page" control is offered. Null keeps the JavaScript default, <see langword="true"/>.</summary>
    public bool? ShowLast { get; init; }
}

/// <summary><c>&lt;xps-pagination /&gt;</c> - mounts the <c>pagination</c> or <c>loadMore</c> widget.</summary>
[HtmlTargetElement("xps-pagination")]
public sealed class PaginationTagHelper : XpSearchMountTagHelper<PaginationOptions>
{
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

    /// <summary>Gets or sets <see cref="PaginationOptions.Padding"/>.</summary>
    [HtmlAttributeName("padding")]
    public int? Padding { get; set; }

    /// <summary>Gets or sets <see cref="PaginationOptions.ShowFirst"/>.</summary>
    [HtmlAttributeName("show-first")]
    public bool? ShowFirst { get; set; }

    /// <summary>Gets or sets <see cref="PaginationOptions.ShowLast"/>.</summary>
    [HtmlAttributeName("show-last")]
    public bool? ShowLast { get; set; }

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

        return options with
        {
            Style = Style ?? options.Style,
            Padding = Padding ?? options.Padding,
            ShowFirst = ShowFirst ?? options.ShowFirst,
            ShowLast = ShowLast ?? options.ShowLast
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The style is the widget type, not an option of one, so it never reaches the config. The rest is
    /// reflected: an unset option leaves the JavaScript default, an explicit <c>false</c> departs from it.
    /// </remarks>
    protected override void BuildConfig(PaginationOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(config);

        ReflectConfig(options, config);
        config.Remove("style");
    }
}
