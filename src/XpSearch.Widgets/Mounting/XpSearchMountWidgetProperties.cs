using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// The two properties every Xperience Search widget needs: which index the search runs against and
/// which search instance on the page the widget belongs to (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Widgets on one page are grouped by <see cref="InstanceId"/>; every group becomes one search
/// instance, which is what lets editors drop widgets in any section in any order.
/// </para>
/// <para>
/// <see cref="Index"/> uses <see cref="OrderIndex"/> so a facet attribute drop-down (which depends on
/// it through a form component configurator) can be ordered after it, as
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/editing-components/configure-editing-component-state
/// requires.
/// </para>
/// </remarks>
public abstract class XpSearchMountWidgetProperties : IWidgetProperties
{
    /// <summary>Form order of <see cref="Index"/>. A dependent property must order after it.</summary>
    public const int OrderIndex = 10;

    /// <summary>Form order of <see cref="InstanceId"/>.</summary>
    public const int OrderInstanceId = 20;

    /// <summary>Form order the first widget-specific property should use.</summary>
    public const int OrderFirstWidgetProperty = 30;

    /// <summary>
    /// Gets or sets the code name of the index to search. Leave empty when the project has exactly
    /// one index - the widget then uses it.
    /// </summary>
    [DropDownComponent(
        Label = "Search index",
        ExplanationText = "The index this search queries. All widgets of one search instance must select the same index.",
        DataProviderType = typeof(XpSearchIndexOptionsProvider),
        Order = OrderIndex)]
    public string Index { get; set; } = string.Empty;
    // The name of this property is a contract: XpSearchConstants.IndexPropertyName is what the facet
    // attribute configurator in XpSearch.Admin reads through IFormFieldValueProvider.

    /// <summary>
    /// Gets or sets the identifier that couples this widget to the other widgets of the same search.
    /// Defaults to <c>default</c>, so an editor placing one search on a page never has to think about it.
    /// </summary>
    [TextInputComponent(
        Label = "Instance ID",
        ExplanationText = "Widgets that share an instance ID form one search. Change it only to run two independent searches on one page.",
        Order = OrderInstanceId)]
    public string InstanceId { get; set; } = XpSearchWidgetConstants.DefaultInstanceId;
}
