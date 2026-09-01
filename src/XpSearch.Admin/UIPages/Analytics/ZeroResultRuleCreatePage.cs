using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Admin.UIPages.RuleBuilder;

[assembly: UIPage(
    parentType: typeof(RuleListing),
    slug: "from-query",
    uiPageType: typeof(ZeroResultRuleSection),
    name: "New rule from a query",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 300)]

[assembly: UIPage(
    parentType: typeof(ZeroResultRuleSection),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(ZeroResultRuleCreatePage),
    name: "New rule",
    templateName: RuleBuilderPage.TemplateName,
    order: 100,
    ParameterDefaultValue = ZeroResultRuleCreatePage.EmptySeed)]

namespace XpSearch.Admin.UIPages.Analytics;

/// <summary>
/// The <c>from-query</c> URL segment that carries the seed of a rule created from a zero-result row.
/// Renders nothing of its own.
/// </summary>
/// <remarks>
/// It exists because <see cref="RuleListing"/>'s parameterized slug is already spoken for by
/// <see cref="RuleEditSection"/>, and because a SECTION_LAYOUT page with one child simply displays
/// that child - the same shape <see cref="IndexTuningRoot"/> uses.
/// </remarks>
[UINavigation(false)]
public class ZeroResultRuleSection : SecondaryMenuSectionPage
{
}

/// <summary>
/// The rule builder a zero-result row deep-links to (spec §9.3), seeded with a query condition for
/// the query that found nothing and no actions yet.
/// </summary>
/// <remarks>
/// <para>
/// It is a page of its own rather than a parameter on <see cref="RuleCreate"/> because a UI page can
/// only be handed a value through a parameterized URL slug, and the "New rule" page under the rules
/// listing has a static one
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages#parameterized-url-slugs).
/// </para>
/// <para>
/// It hangs under the rules listing, not under <see cref="AnalyticsDashboardPage"/> where it used to.
/// A page renders "within the nearest RoutingContentPlaceholder element of the client-side page
/// template assigned to its parent page"
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages),
/// and the dashboard's custom React template renders no such placeholder - so the seeded URL resolved
/// to this page and then painted the dashboard instead of it (HW-10 defect 3). The LISTING template
/// of the rules listing does render one, which is why the plain "New rule" page under it always
/// worked.
/// </para>
/// </remarks>
[UINavigation(false)]
[UIEvaluatePermission(SystemPermissions.CREATE)]
public class ZeroResultRuleCreatePage : RuleBuilderPage
{
    /// <summary>The seed of an empty rule: the value the URL carries when nothing was pre-filled.</summary>
    public const string EmptySeed = "Cg";

    /// <summary>Initializes a new instance of the <see cref="ZeroResultRuleCreatePage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    public ZeroResultRuleCreatePage(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator)
        : base(storageService, provider, contactGroups, pageLinkGenerator)
    {
    }

    /// <summary>Gets or sets the encoded index and query, taken from the URL. See <see cref="RuleSeed"/>.</summary>
    [PageParameter(typeof(StringPageModelBinder))]
    public string Seed { get; set; } = EmptySeed;

    /// <summary>Builds the rule the seeded page starts from.</summary>
    /// <param name="query">The query that found nothing.</param>
    /// <returns>A rule that fires on that query and does nothing yet.</returns>
    public static RuleDto SeedFor(string? query)
    {
        string text = (query ?? string.Empty).Trim();

        return new RuleDto
        {
            Name = text.Length == 0 ? string.Empty : $"Rule for '{text}'",
            Conditions = new RuleConditionsDto
            {
                QueryEnabled = text.Length > 0,
                QueryOperator = "contains",
                QueryPattern = text,
            },
        };
    }

    /// <inheritdoc />
    protected override RuleDto SeedRule()
    {
        // The index comes from the URL's index segment; only the query part of the seed is used.
        (_, string query) = RuleSeed.Decode(Seed);

        return SeedFor(query);
    }
}
