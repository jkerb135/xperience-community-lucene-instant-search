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

    internal static string Hint_SortOptions => Get(nameof(Hint_SortOptions));

    private static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
