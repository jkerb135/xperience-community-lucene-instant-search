using System.Globalization;
using System.Resources;

namespace XpSearch.Widgets.Resources;

/// <summary>
/// The editor-facing strings of the widgets, kept in <c>WidgetResources.resx</c> so they can be
/// localized, as
/// https://docs.kentico.com/documentation/developers-and-admins/development/builders/distribute-builder-components
/// asks of distributed builder components.
/// </summary>
internal static class WidgetResources
{
    private static readonly ResourceManager Manager =
        new("XpSearch.Widgets.Resources.WidgetResources", typeof(WidgetResources).Assembly);

    internal static string Unconfigured_Edit => Get(nameof(Unconfigured_Edit));

    internal static string Unconfigured_ReadOnly => Get(nameof(Unconfigured_ReadOnly));

    internal static string Unconfigured_Preview => Get(nameof(Unconfigured_Preview));

    internal static string Unconfigured_Title => Get(nameof(Unconfigured_Title));

    internal static string Hint_SelectIndex => Get(nameof(Hint_SelectIndex));

    internal static string Hint_SelectAttribute => Get(nameof(Hint_SelectAttribute));

    internal static string Hint_RangeBounds => Get(nameof(Hint_RangeBounds));

    internal static string Hint_FilterSortFacets => Get(nameof(Hint_FilterSortFacets));

    internal static string Hint_SortOptions => Get(nameof(Hint_SortOptions));

    internal static string Preview_Badge => Get(nameof(Preview_Badge));

    internal static string Preview_Note_Generic => Get(nameof(Preview_Note_Generic));

    internal static string Preview_Note_Attribute => Get(nameof(Preview_Note_Attribute));

    internal static string Preview_Note_Results => Get(nameof(Preview_Note_Results));

    internal static string Preview_Note_Suggestions => Get(nameof(Preview_Note_Suggestions));

    internal static string Preview_Unset => Get(nameof(Preview_Unset));

    internal static string Preview_SearchPlaceholder => Get(nameof(Preview_SearchPlaceholder));

    internal static string Preview_ShowMore => Get(nameof(Preview_ShowMore));

    internal static string Preview_LoadMore => Get(nameof(Preview_LoadMore));

    internal static string Preview_From => Get(nameof(Preview_From));

    internal static string Preview_To => Get(nameof(Preview_To));

    private static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
