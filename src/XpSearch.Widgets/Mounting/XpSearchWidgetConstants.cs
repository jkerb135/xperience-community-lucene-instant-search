namespace XpSearch.Widgets;

/// <summary>
/// Identifiers and defaults shared by the Page Builder widgets (spec §7).
/// </summary>
public static class XpSearchWidgetConstants
{
    /// <summary>The <c>data-xps-instance</c> value a widget uses when the editor leaves Instance ID empty.</summary>
    public const string DefaultInstanceId = "default";

    /// <summary>The CSS class the JavaScript bootstrap scans for.</summary>
    public const string MountCssClass = "xps-mount";

    /// <summary>Path of the shared mount view every widget renders.</summary>
    public const string MountViewPath = "~/Components/Widgets/XpSearch/_Mount.cshtml";

    /// <summary>
    /// Identifier of the form component configurator that fills a facet attribute drop-down from the
    /// selected index's schema (spec §7.4). Pair it with a <c>DropDownComponent</c> property:
    /// <c>[FormComponentConfiguration(XpSearchWidgetConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]</c>.
    /// The configurator itself lives in <c>XpSearch.Admin</c>; the string identifier keeps live-site
    /// code free of a dependency on <c>Kentico.Xperience.Admin</c>.
    /// </summary>
    public const string FacetAttributeConfiguratorIdentifier = "xpsearch.facetAttribute";

    /// <summary>Widget identifier of the search box widget.</summary>
    public const string SearchBoxIdentifier = "XpSearch.SearchBox";

    /// <summary>Widget identifier of the results widget.</summary>
    public const string ResultsIdentifier = "XpSearch.Results";

    /// <summary>Widget identifier of the facet list widget.</summary>
    public const string FacetListIdentifier = "XpSearch.FacetList";

    /// <summary>Widget identifier of the pagination widget.</summary>
    public const string PaginationIdentifier = "XpSearch.Pagination";

    /// <summary>Widget identifier of the result stats widget.</summary>
    public const string ResultStatsIdentifier = "XpSearch.ResultStats";

    /// <summary>Widget identifier of the sort selector widget.</summary>
    public const string SortSelectIdentifier = "XpSearch.SortSelect";

    /// <summary>Widget identifier of the suggestions widget.</summary>
    public const string SuggestionsIdentifier = "XpSearch.Suggestions";
}
