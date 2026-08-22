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
/// Every page under it uses a built-in client template - no custom React (spec §8.1). The query
/// tester (§8.4) and the analytics dashboard (§9.3) genuinely need bespoke UI and are not here yet.
/// </remarks>
public class SearchTuningApplication : ApplicationPage
{
    /// <summary>Unique identifier of the application, used when assigning permissions to roles.</summary>
    public const string IDENTIFIER = "XpSearch.SearchTuning";
}
