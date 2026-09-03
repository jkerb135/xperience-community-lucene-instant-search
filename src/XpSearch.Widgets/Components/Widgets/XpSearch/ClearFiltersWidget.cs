using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ClearFiltersIdentifier,
    viewComponentType: typeof(ClearFiltersWidgetViewComponent),
    name: "Search - Clear filters",
    propertiesType: typeof(ClearFiltersWidgetProperties),
    Description = "A button that removes every refinement the visitor has applied. Disabled while there is nothing to clear.",
    IconClass = "icon-times-circle",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the clear filters widget (spec §7.3).</summary>
public sealed class ClearFiltersWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>Gets or sets the button text. Empty keeps "Clear all".</summary>
    [TextInputComponent(
        Label = "Button text",
        Tooltip = "The wording of the button.",
        ExplanationText = "Empty keeps \"Clear all\". The button removes every refinement of this search instance - facets, categories and ranges alike - and is disabled while there is nothing to clear.",
        Order = OrderFirstWidgetProperty)]
    public string Label { get; set; } = string.Empty;
}

/// <summary>Renders the <c>clearFilters</c> mount.</summary>
public sealed class ClearFiltersWidgetViewComponent : XpSearchMountWidgetViewComponent<ClearFiltersWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="ClearFiltersWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ClearFiltersWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "clearFilters";

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(ClearFiltersWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // The live widget starts disabled - nothing is refined on a fresh page - so the preview
        // mirrors that state rather than a button that looks ready to press.
        return EditorPreview.El("div", "xps-clear-filters xps-clear-filters--disabled")
            .Add(EditorPreview.Button(
                "xps-button xps-button--link xps-clear-filters__button",
                string.IsNullOrWhiteSpace(properties.Label) ? WidgetResources.Preview_ClearAll : properties.Label));
    }
}
