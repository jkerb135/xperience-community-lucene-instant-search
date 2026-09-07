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
}
