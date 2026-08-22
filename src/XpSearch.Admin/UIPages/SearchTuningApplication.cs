using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XpSearch.Admin.UIPages;

[assembly: UIApplication(
    identifier: SearchTuningApplication.IDENTIFIER,
    type: typeof(SearchTuningApplication),
    slug: "xpsearch-tuning",
    name: "Search tuning",
    category: BaseApplicationCategories.DEVELOPMENT,
    icon: Icons.Cogwheel,
    templateName: TemplateNames.SECTION_LAYOUT)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The Search tuning application (spec §8.1, extended by §10.8): rules, synonyms, stopwords, field
/// weights, API keys, index status and the ingestion log.
/// </summary>
/// <remarks>
/// Registered per
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-application-pages.
/// Every page under it uses a built-in client template - no custom React (spec §8.1) - except the
/// query tester (§8.4) and the analytics dashboard (§9.3), which genuinely need bespoke UI and come
/// from this package's own client module. See docs/adr/0016-admin-client.md.
/// The permissions are the four the application's pages evaluate: the built-in listing, create and
/// edit templates evaluate VIEW, CREATE and UPDATE/DELETE on their own, and the two custom pages
/// evaluate VIEW (and CREATE for the "Create rule" deep link) on their page commands
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-permission-checks).
/// </remarks>
[UIPermission(SystemPermissions.VIEW)]
[UIPermission(SystemPermissions.CREATE)]
[UIPermission(SystemPermissions.UPDATE)]
[UIPermission(SystemPermissions.DELETE)]
public class SearchTuningApplication : ApplicationPage
{
    /// <summary>Unique identifier of the application, used when assigning permissions to roles.</summary>
    public const string IDENTIFIER = "XpSearch.SearchTuning";
}
