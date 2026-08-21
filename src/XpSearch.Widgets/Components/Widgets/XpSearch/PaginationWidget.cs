using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.PaginationIdentifier,
    viewComponentType: typeof(PaginationWidgetViewComponent),
    name: "Search - Pagination",
    propertiesType: typeof(PaginationWidgetProperties),
    Description = "Moves between the pages of a search result list.",
    IconClass = "icon-chevron-right",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the pagination widget (spec §7.3).</summary>
public sealed class PaginationWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>The <see cref="Style"/> value that renders numbered page links.</summary>
    public const string StyleNumbered = "numbered";

    /// <summary>The <see cref="Style"/> value that renders a "load more" button.</summary>
    public const string StyleLoadMore = "loadMore";

    /// <summary>Gets or sets which pagination control is rendered.</summary>
    [DropDownComponent(
        Label = "Style",
        Options = $"{StyleNumbered};Numbered pages\r\n{StyleLoadMore};Load more button",
        ExplanationText = "\"Load more\" needs the loadMore JavaScript widget, which ships in a later release.",
        Order = OrderFirstWidgetProperty)]
    public string Style { get; set; } = StyleNumbered;
}

/// <summary>Renders the <c>pagination</c> (or <c>loadMore</c>) mount.</summary>
public sealed class PaginationWidgetViewComponent : XpSearchMountWidgetViewComponent<PaginationWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="PaginationWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public PaginationWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "pagination";

    /// <inheritdoc />
    /// <remarks>The style picks the JavaScript widget rather than becoming an option of one.</remarks>
    protected override string GetWidgetType(PaginationWidgetProperties properties) =>
        string.Equals(properties?.Style, PaginationWidgetProperties.StyleLoadMore, StringComparison.OrdinalIgnoreCase)
            ? "loadMore"
            : "pagination";

    /// <inheritdoc />
    protected override void BuildConfig(PaginationWidgetProperties properties, IDictionary<string, object?> config)
    {
    }
}
