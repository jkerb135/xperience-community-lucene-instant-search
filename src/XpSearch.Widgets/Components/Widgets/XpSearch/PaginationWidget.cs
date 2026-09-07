using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.TagHelpers;

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
    public const string StyleNumbered = PaginationOptions.StyleNumbered;

    /// <summary>The <see cref="Style"/> value that renders a "load more" button.</summary>
    public const string StyleLoadMore = PaginationOptions.StyleLoadMore;

    /// <summary>Gets or sets which pagination control is rendered.</summary>
    [DropDownComponent(
        Label = "Style",
        Options = $"{StyleNumbered};Numbered pages\r\n{StyleLoadMore};Load more button",
        Tooltip = "Which pagination control this widget renders.",
        ExplanationText = "\"Load more\" appends the next page instead of replacing it. Place either this or numbered pages, never both. The step is the page size of the search - the Search - Results widget's 'Results per page (0 = index setting)', or the index's 'Default page size' - and the index's 'Maximum result window' is how deep paging may go.",
        Order = OrderFirstWidgetProperty)]
    public string Style { get; set; } = StyleNumbered;

    /// <summary>Gets or sets how many page links are shown either side of the current one.</summary>
    [RequiredValidationRule]
    [MinimumIntegerValueValidationRule(0)]
    [NumberInputComponent(
        Label = "Pages either side of the current one",
        Tooltip = "How many numbered page links surround the current page.",
        ExplanationText = "3 shows up to seven numbers around the current page; 0 shows the current page alone. The first and last pages are always reachable through the ellipsis, whatever this is.",
        Order = OrderFirstWidgetProperty + 10)]
    [VisibleIfEqualTo(nameof(Style), StyleNumbered)]
    public int Padding { get; set; } = 3;

    /// <summary>Gets or sets whether the "first page" control is offered.</summary>
    [CheckBoxComponent(
        Label = "Show the \"first page\" control",
        Tooltip = "Offers the « control that jumps to page one.",
        ExplanationText = "The « control at the start of the row. Clear it on a short list, where page one is a number away anyway.",
        Order = OrderFirstWidgetProperty + 20)]
    [VisibleIfEqualTo(nameof(Style), StyleNumbered)]
    public bool ShowFirst { get; set; } = true;

    /// <summary>Gets or sets whether the "last page" control is offered.</summary>
    [CheckBoxComponent(
        Label = "Show the \"last page\" control",
        Tooltip = "Offers the » control that jumps to the final page.",
        ExplanationText = "The » control at the end of the row. Clear it where jumping to the deepest page is not worth offering, such as a very large result set.",
        Order = OrderFirstWidgetProperty + 30)]
    [VisibleIfEqualTo(nameof(Style), StyleNumbered)]
    public bool ShowLast { get; set; } = true;
}

/// <summary>Renders the <c>pagination</c> (or <c>loadMore</c>) mount.</summary>
public sealed class PaginationWidgetViewComponent : XpSearchMountWidgetViewComponent<PaginationWidgetProperties, PaginationOptions>
{
    /// <summary>Initializes a new instance of the <see cref="PaginationWidgetViewComponent"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper.</param>
    /// <param name="editorContext">The current editing mode.</param>
    public PaginationWidgetViewComponent(
        XpSearchMountTagHelper<PaginationOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    /// <inheritdoc />
    public override PaginationOptions ToOptions(PaginationWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new PaginationOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            Style = properties.Style,
            Padding = properties.Padding,
            ShowFirst = properties.ShowFirst,
            ShowLast = properties.ShowLast
        };
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(PaginationWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (string.Equals(TagHelper.GetWidgetType(ToOptions(properties)), "loadMore", StringComparison.Ordinal))
        {
            return EditorPreview.El("div", "xps-load-more")
                .Add(EditorPreview.Button("xps-button xps-load-more__load-more", WidgetResources.Preview_LoadMore));
        }

        var list = EditorPreview.El("ul", "xps-pagination__list");

        if (properties.ShowFirst)
        {
            list.Add(Item("xps-pagination__item--first xps-pagination__item--disabled", "«"));
        }

        list.Add(Item("xps-pagination__item--previous xps-pagination__item--disabled", "‹"));

        for (int page = 1; page <= 3; page++)
        {
            list.Add(Item(
                page == 1 ? "xps-pagination__item--page xps-pagination__item--current" : "xps-pagination__item--page",
                page.ToString(CultureInfo.CurrentUICulture)));
        }

        list.Add(Item("xps-pagination__item--next", "›"));

        if (properties.ShowLast)
        {
            list.Add(Item("xps-pagination__item--last", "»"));
        }

        return EditorPreview.El("nav", "xps-pagination").Add(list);
    }

    // A span, not an anchor: nothing in a preview is navigable.
    private static IHtmlContent Item(string modifiers, string text) =>
        EditorPreview.El("li", $"xps-pagination__item {modifiers}")
            .Add(EditorPreview.El("span", "xps-pagination__link", text).Attr("aria-disabled", "true"));
}
