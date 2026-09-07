using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

using static XpSearch.Widgets.Mounting.EditorPreview;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// What a widget renders inside its mount before anything has answered (SK-1 §2.2): the shape it
/// will have, drawn in <c>xps-skeleton</c> bars. Normative markup:
/// <c>themes/fixtures/skeleton.html</c>, one block per widget, pinned by
/// <c>SkeletonParityTests</c>.
/// </summary>
/// <remarks>
/// The root is what the client's <c>createRoot</c> adopts, so its tag name must be the one that
/// widget asks for, and <c>data-xps-server-rendered</c> must come first, before the class the
/// client overwrites. The whole block is decoration: <c>aria-hidden</c>, no text, nothing focusable.
/// </remarks>
internal static class MountSkeleton
{
    /// <summary>The skeleton of one widget.</summary>
    /// <param name="widgetType">The <c>data-xps-widget</c> value, e.g. <c>facetList</c>.</param>
    /// <returns>The markup that goes inside the mount element.</returns>
    internal static IHtmlContent For(string widgetType) =>
        widgetType switch
        {
            "facetList" => Block("div", "xps-facet-list", FacetList()),
            "categoryTree" => Block("nav", "xps-category-tree", CategoryTree()),
            "rangeFilter" => Block("div", "xps-range-filter", RangeFilter()),
            "toggleFilter" => Block("div", "xps-toggle-filter", [Row("xps-toggle-filter__label")]),
            "resultStats" => Block("div", "xps-result-stats", [Line("xps-result-stats__text")]),
            "sortSelect" => Block("div", "xps-sort-select", SortSelect(), "xps-select"),
            "filterSort" => Block("div", "xps-filter-sort", [Control("xps-filter-sort__trigger")]),
            "pagination" => Block("nav", "xps-pagination", Pagination()),
            "activeFilters" => Block("div", "xps-active-filters", ActiveFilters()),
            "clearFilters" => Block("div", "xps-clear-filters", [Line("xps-clear-filters__button")]),
            "loadMore" => Block("div", "xps-load-more", [Control("xps-load-more__load-more")]),
            "suggestions" => Block("div", "xps-suggestions", Suggestions()),

            // A widget nobody here has drawn - a third party's, or `results`, whose skeleton the
            // client paints itself: the empty root, so the handover still happens.
            _ => Block("div", $"xps-{Kebab(widgetType)}"),
        };

    /// <summary>
    /// The wrapper, emitted as text rather than through <see cref="TagBuilder"/>: the fixture's
    /// attribute order is normative and <c>TagBuilder</c> sorts attributes by name.
    /// </summary>
    private static IHtmlContent Block(string tagName, string block, IHtmlContent[]? children = null, string? also = null)
    {
        string classes = also is null ? $"xps {block}" : $"xps {block} {also}";
        var content = new HtmlContentBuilder()
            .AppendHtml($"<{tagName} data-xps-server-rendered aria-hidden=\"true\" class=\"{classes} {block}--skeleton\">");

        foreach (var child in children ?? [])
        {
            content.AppendHtml(child);
        }

        return content.AppendHtml($"</{tagName}>");
    }

    private static TagBuilder Bar(string modifier) => El("span", $"xps-skeleton xps-skeleton--{modifier}");

    private static TagBuilder Heading(string cssClass) => El("h3", cssClass).Add(Bar("heading"));

    private static TagBuilder Line(string cssClass) => El("span", cssClass).Add(Bar("line"));

    private static TagBuilder Control(string cssClass) => El("span", cssClass).Add(Bar("control"));

    /// <summary>A facet row: the box, its name, its count.</summary>
    private static TagBuilder Row(string cssClass) =>
        El("span", cssClass).Add(Bar("box"), Bar("text"), Bar("count"));

    private static IHtmlContent[] FacetList()
    {
        var list = El("ul", "xps-facet-list__list");

        for (int row = 0; row < 4; row++)
        {
            list.Add(El("li", "xps-facet-list__item").Add(Row("xps-facet-list__label")));
        }

        return
        [
            Heading("xps-facet-list__title"),
            El("div", "xps-facet-list__body").Add(list)
        ];
    }

    private static IHtmlContent[] CategoryTree()
    {
        var children = El("ul", "xps-category-tree__list xps-category-tree__list--lvl1");

        for (int row = 0; row < 3; row++)
        {
            children.Add(El("li", "xps-category-tree__item").Add(TreeLink()));
        }

        var root = El("ul", "xps-category-tree__list xps-category-tree__list--lvl0")
            .Add(El("li", "xps-category-tree__item xps-category-tree__item--parent").Add(TreeLink(), children));

        return
        [
            Heading("xps-category-tree__title"),
            El("div", "xps-category-tree__body").Add(root)
        ];
    }

    private static TagBuilder TreeLink() =>
        El("span", "xps-category-tree__link").Add(Bar("text"), Bar("count"));

    private static IHtmlContent[] RangeFilter() =>
    [
        Heading("xps-range-filter__title"),
        El("div", "xps-range-filter__track").Add(Bar("track")),
        El("div", "xps-range-filter__inputs").Add(
            Control("xps-range-filter__input"),
            Control("xps-range-filter__separator"),
            Control("xps-range-filter__input"))
    ];

    private static IHtmlContent[] SortSelect() =>
    [
        Line("xps-select__label"),
        Control("xps-select__field")
    ];

    private static IHtmlContent[] Pagination()
    {
        var list = El("ul", "xps-pagination__list");

        for (int pill = 0; pill < 5; pill++)
        {
            list.Add(El("li", "xps-pagination__item").Add(El("span", "xps-pagination__link").Add(Bar("box"))));
        }

        return [list];
    }

    private static IHtmlContent[] ActiveFilters() =>
    [
        El("ul", "xps-active-filters__list").Add(
            El("li", "xps-active-filters__item").Add(
                El("span", "xps-chip").Add(Control("xps-chip__label"))))
    ];

    private static IHtmlContent[] Suggestions() =>
    [
        El("div", "xps-suggestions__field").Add(Bar("control"))
    ];
}
