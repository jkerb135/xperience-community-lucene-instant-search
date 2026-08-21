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
