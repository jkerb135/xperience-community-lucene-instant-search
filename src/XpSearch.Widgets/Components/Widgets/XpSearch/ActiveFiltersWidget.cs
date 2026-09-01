using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ActiveFiltersIdentifier,
    viewComponentType: typeof(ActiveFiltersWidgetViewComponent),
    name: "Search - Active filters",
    propertiesType: typeof(ActiveFiltersWidgetProperties),
    Description = "Shows the refinements the visitor has applied as removable chips.",
    IconClass = "icon-tags",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the active filters widget (spec §7.3).</summary>
public sealed class ActiveFiltersWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the heading screen readers announce for the chip list. Empty keeps
    /// "Active filters". It is never shown on screen.
    /// </summary>
    [TextInputComponent(
        Label = "Screen-reader heading",
        ExplanationText = "Announced before the chips. Not shown on screen.",
        Order = OrderFirstWidgetProperty)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the chips stay on one scrolling row instead of wrapping.</summary>
    [CheckBoxComponent(
        Label = "Keep the chips on one scrolling row",
        ExplanationText = "Off, the chips wrap onto as many rows as they need.",
        Order = OrderFirstWidgetProperty + 10)]
    public bool Scroll { get; set; }
}

/// <summary>Renders the <c>activeFilters</c> mount.</summary>
public sealed class ActiveFiltersWidgetViewComponent : XpSearchMountWidgetViewComponent<ActiveFiltersWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveFiltersWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ActiveFiltersWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "activeFilters";

    /// <inheritdoc />
    /// <remarks>Two chips of the shape the live widget renders; the values are only known at run time.</remarks>
    protected override IHtmlContent BuildEditorPreview(ActiveFiltersWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string cssClass = properties.Scroll
            ? "xps-active-filters xps-active-filters--scroll"
            : "xps-active-filters";

        return EditorPreview.El("div", cssClass)
            .Add(EditorPreview.El("ul", "xps-active-filters__list").Add(Chip(), Chip()));
    }

    /// <summary>One chip: an attribute name, a value bar and the remove button, all inert.</summary>
    private static TagBuilder Chip() =>
        EditorPreview.El("li", "xps-active-filters__item")
            .Add(EditorPreview.El("span", "xps-chip")
                .Add(
                    EditorPreview.El("span", "xps-chip__label")
                        .Add(
                            EditorPreview.El("span", "xps-chip__attribute").Add(EditorPreview.Skeleton("text")),
                            EditorPreview.Skeleton("text")),
                    EditorPreview.Button("xps-chip__remove", "×", glyph: true)));
}
