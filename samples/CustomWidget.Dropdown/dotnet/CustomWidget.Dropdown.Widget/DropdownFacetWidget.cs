using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using MyCompany.Search.Widgets;

using XpSearch.Core;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: "MyCompany.DropdownFacet",
    viewComponentType: typeof(DropdownFacetWidgetViewComponent),
    name: "Search - Dropdown filter",
    propertiesType: typeof(DropdownFacetWidgetProperties),
    Description = "Filters a search on one attribute, as a single-select drop-down.",
    IconClass = "icon-chevron-down",
    AllowCache = false)]

namespace MyCompany.Search.Widgets;

/// <summary>
/// Editor properties of the dropdown facet widget. <c>Index</c> (order 10) and <c>InstanceId</c>
/// (order 20) come from the base class.
/// </summary>
public sealed class DropdownFacetWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the facet attribute to filter on. Filled from the selected index's facetable
    /// fields and hidden until an index is chosen.
    /// </summary>
    [DropDownComponent(Label = "Attribute", Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the visible label of the drop-down.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = "Filter";

    /// <summary>Gets or sets the text of the option that applies no filter.</summary>
    [TextInputComponent(Label = "\"All\" option text", Order = OrderFirstWidgetProperty + 20)]
    public string AllLabel { get; set; } = "All";
}

/// <summary>Renders the <c>myCompany.dropdownFacet</c> mount.</summary>
public sealed class DropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DropdownFacetWidgetViewComponent"/> class.
    /// </summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">Supplies the sole index when the editor left the index empty.</param>
    public DropdownFacetWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "myCompany.dropdownFacet";

    /// <inheritdoc />
    protected override string? ConfigurationHint(DropdownFacetWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties.Attribute) ? "Select the attribute to filter on." : null;
}
