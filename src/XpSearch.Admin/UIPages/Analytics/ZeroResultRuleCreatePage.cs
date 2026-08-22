using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;

[assembly: UIPage(
    parentType: typeof(AnalyticsDashboardPage),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(ZeroResultRuleCreatePage),
    name: "New rule",
    templateName: TemplateNames.EDIT,
    order: 100,
    ParameterDefaultValue = ZeroResultRuleCreatePage.EmptySeed)]

namespace XpSearch.Admin.UIPages.Analytics;

/// <summary>
/// The rule create page a zero-result row deep-links to (spec §9.3), pre-filled with the query that
/// found nothing. Identical to <see cref="RuleCreate"/> in every other respect.
/// </summary>
/// <remarks>
/// It is a page of its own rather than a parameter on <see cref="RuleCreate"/> because a UI page can
/// only be handed a value through a parameterized URL slug, and the "New rule" page under the rules
/// listing has a static one
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages#parameterized-url-slugs).
/// Hidden from the navigation menu; it is only ever reached from the dashboard.
/// </remarks>
[UINavigation(false)]
[UIEvaluatePermission(SystemPermissions.CREATE)]
public class ZeroResultRuleCreatePage : RuleCreate
{
    /// <summary>The seed of an empty rule: the value the URL carries when nothing was pre-filled.</summary>
    public const string EmptySeed = "Cg";

    /// <summary>Initializes a new instance of the <see cref="ZeroResultRuleCreatePage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of rule objects.</param>
    public ZeroResultRuleCreatePage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneIndexManager indexManager,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchRuleInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, indexManager, pageLinkGenerator, provider)
    {
    }

    /// <summary>Gets or sets the encoded index and query, taken from the URL. See <see cref="RuleSeed"/>.</summary>
    [PageParameter(typeof(StringPageModelBinder))]
    public string Seed { get; set; } = EmptySeed;

    /// <inheritdoc />
    protected override RuleModel CreateModel()
    {
        var model = base.CreateModel();
        (string indexName, string query) = RuleSeed.Decode(Seed);

        if (!string.IsNullOrEmpty(indexName))
        {
            model.IndexName = indexName;
        }

        model.Pattern = query;
        model.Name = string.IsNullOrEmpty(query) ? model.Name : $"Rule for '{query}'";

        return model;
    }
}
