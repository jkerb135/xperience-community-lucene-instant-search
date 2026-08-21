using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.FacetListIdentifier,
    viewComponentType: typeof(FacetListWidgetViewComponent),
    name: "Search - Facet list",
    propertiesType: typeof(FacetListWidgetProperties),
    Description = "Displays filter checkboxes for one attribute of the search index.",
    IconClass = "icon-filter",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the facet list widget (spec §7.3, §7.4).</summary>
public sealed class FacetListWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the index attribute the facet filters on. The drop-down is filled from the
    /// selected index's schema, so only facetable fields can be chosen.
    /// </summary>
    [DropDownComponent(
        Label = "Attribute",
        Placeholder = "Select an attribute",
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchWidgetConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the heading shown above the values. Empty falls back to the attribute name.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets how several selected values combine.</summary>
    [DropDownComponent(
        Label = "Operator",
        Options = "or;Match any of the selected values\r\nand;Match all of the selected values",
        Order = OrderFirstWidgetProperty + 20)]
    public string Operator { get; set; } = "or";

    /// <summary>Gets or sets how many values are listed before "show more".</summary>
    [NumberInputComponent(Label = "Values shown", Order = OrderFirstWidgetProperty + 30)]
    public int Limit { get; set; } = 10;

    /// <summary>Gets or sets whether a "show more" button reveals the remaining values.</summary>
    [CheckBoxComponent(Label = "Show a \"show more\" button", Order = OrderFirstWidgetProperty + 40)]
    public bool ShowMore { get; set; }
}

/// <summary>Renders the <c>facetList</c> mount.</summary>
public sealed class FacetListWidgetViewComponent : XpSearchMountWidgetViewComponent<FacetListWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="FacetListWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public FacetListWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "facetList";

    /// <inheritdoc />
    protected override string? ConfigurationHint(FacetListWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties?.Attribute) ? WidgetResources.Hint_SelectAttribute : null;
}
