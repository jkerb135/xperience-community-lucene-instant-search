using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

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
        Tooltip = "Which pagination control this widget renders.",
        ExplanationText = "\"Load more\" appends the next page instead of replacing it. Place either this or numbered pages, never both. The step is the page size of the search - the Search - Results widget's 'Results per page (0 = index setting)', or the index's 'Default page size' - and the index's 'Maximum result window' is how deep paging may go.",
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

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(PaginationWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (string.Equals(GetWidgetType(properties), "loadMore", StringComparison.Ordinal))
        {
            return EditorPreview.El("div", "xps-load-more")
                .Add(EditorPreview.Button("xps-button xps-load-more__load-more", WidgetResources.Preview_LoadMore));
        }

        var list = EditorPreview.El("ul", "xps-pagination__list")
            .Add(Item("xps-pagination__item--previous xps-pagination__item--disabled", "‹"));

        for (int page = 1; page <= 3; page++)
        {
            list.Add(Item(
                page == 1 ? "xps-pagination__item--page xps-pagination__item--current" : "xps-pagination__item--page",
                page.ToString(CultureInfo.CurrentUICulture)));
        }

        return EditorPreview.El("nav", "xps-pagination").Add(list.Add(Item("xps-pagination__item--next", "›")));
    }

    // A span, not an anchor: nothing in a preview is navigable.
    private static IHtmlContent Item(string modifiers, string text) =>
        EditorPreview.El("li", $"xps-pagination__item {modifiers}")
            .Add(EditorPreview.El("span", "xps-pagination__link", text).Attr("aria-disabled", "true"));
}
