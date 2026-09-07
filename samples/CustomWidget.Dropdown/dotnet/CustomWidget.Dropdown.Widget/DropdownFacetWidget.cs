using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Razor.TagHelpers;

using MyCompany.Search.Widgets;

using XpSearch.Core;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

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
/// What the widget needs to render, whichever way it is placed. Kentico-free: this is what a Razor
/// view, a unit test and the Page Builder widget all speak.
/// </summary>
public sealed record DropdownFacetOptions : XpSearchMountOptions
{
    /// <summary>Gets the facet attribute to filter on.</summary>
    public string? Attribute { get; init; }

    /// <summary>Gets the visible label of the drop-down.</summary>
    public string Label { get; init; } = "Filter";

    /// <summary>Gets the text of the option that applies no filter.</summary>
    public string AllLabel { get; init; } = "All";
}

/// <summary>
/// <c>&lt;my-dropdown-facet attribute="brand" /&gt;</c> - the Razor surface, and the single
/// implementation of the mount: the Page Builder widget below scaffolds on it.
/// </summary>
[HtmlTargetElement("my-dropdown-facet")]
public sealed class DropdownFacetTagHelper : XpSearchMountTagHelper<DropdownFacetOptions>
{
    /// <summary>Initializes a new instance of the <see cref="DropdownFacetTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">Supplies the sole index when none was named.</param>
    public DropdownFacetTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="DropdownFacetOptions.Attribute"/>.</summary>
    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    /// <summary>Gets or sets <see cref="DropdownFacetOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="DropdownFacetOptions.AllLabel"/>.</summary>
    [HtmlAttributeName("all-label")]
    public string? AllLabel { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "myCompany.dropdownFacet";

    /// <inheritdoc />
    public override string? Validate(DropdownFacetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (string.IsNullOrWhiteSpace(options.Attribute) ? "Select the attribute to filter on." : null);
    }

    /// <inheritdoc />
    protected override DropdownFacetOptions Merge(DropdownFacetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Attribute = Attribute ?? options.Attribute,
            Label = Label ?? options.Label,
            AllLabel = AllLabel ?? options.AllLabel
        };
    }
}

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

/// <summary>Places the <c>myCompany.dropdownFacet</c> mount in the Page Builder.</summary>
public sealed class DropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties, DropdownFacetOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DropdownFacetWidgetViewComponent"/> class.
    /// </summary>
    /// <param name="tagHelper">The widget's tag helper - where the mount is actually built.</param>
    /// <param name="editorContext">The current editing mode.</param>
    public DropdownFacetWidgetViewComponent(
        XpSearchMountTagHelper<DropdownFacetOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    /// <inheritdoc />
    public override DropdownFacetOptions ToOptions(DropdownFacetWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new DropdownFacetOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            Attribute = properties.Attribute,
            Label = properties.Label,
            AllLabel = properties.AllLabel
        };
    }
}
