using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XpSearch.Admin.UIPages;

[assembly: UIApplication(
    identifier: SearchTuningApplication.IDENTIFIER,
    type: typeof(SearchTuningApplication),
    slug: "xpsearch-tuning",
    name: "Search ingestion",
    category: BaseApplicationCategories.DEVELOPMENT,
    icon: Icons.Cogwheel,
    templateName: TemplateNames.SECTION_LAYOUT)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The Search ingestion application (spec §10.8): the ingestion API keys and the ingestion log.
/// </summary>
/// <remarks>
/// Registered per
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-application-pages.
/// Everything that belongs to one search index lives under the Lucene integration's index instead -
/// see <see cref="IndexTuningSection"/> and docs/adr/0017-index-scoped-admin.md. The identifier and
/// slug are unchanged, so existing role grants on this application keep working for the two pages
/// that stayed.
/// The permissions are the three its pages evaluate: the API key listing evaluates VIEW and its
/// delete action DELETE, the create page evaluates CREATE, and the ingestion log listing evaluates
/// VIEW
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-permission-checks).
/// </remarks>
[UIPermission(SystemPermissions.VIEW)]
[UIPermission(SystemPermissions.CREATE)]
[UIPermission(SystemPermissions.DELETE)]
public class SearchTuningApplication : ApplicationPage
{
    /// <summary>Unique identifier of the application, used when assigning permissions to roles.</summary>
    public const string IDENTIFIER = "XpSearch.SearchTuning";
}
