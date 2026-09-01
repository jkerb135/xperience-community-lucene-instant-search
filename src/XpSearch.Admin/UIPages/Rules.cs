using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.RuleBuilder;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "rules",
    uiPageType: typeof(RuleListing),
    name: "Rules",
    templateName: TemplateNames.LISTING,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(RuleListing),
    slug: "create",
    uiPageType: typeof(RuleCreate),
    name: "New rule",
    templateName: RuleBuilderPage.TemplateName,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(RuleListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(RuleEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(RuleEditSection),
    slug: "edit",
    uiPageType: typeof(RuleEdit),
    name: "Rule",
    templateName: RuleBuilderPage.TemplateName,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>Lists the relevance rules (spec §8.1).</summary>
public class RuleListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IContactGroupCatalog contactGroups;

    /// <summary>Initializes a new instance of the <see cref="RuleListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="contactGroups">Resolves a stored contact group code name to what the marketer named it.</param>
    public RuleListing(ILuceneConfigurationStorageService storageService, IContactGroupCatalog contactGroups)
    {
        this.storageService = storageService;
        this.contactGroups = contactGroups;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchRuleInfo.OBJECT_TYPE;

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string indexName = IndexScope.Resolve(storageService, IndexIdentifier);

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchRuleInfo.RuleName), "Rule", searchable: true)

            // The conditions column reads the same summary the builder's rows show, minus the contact
            // group, which has a column of its own.
            .AddColumn(
                nameof(XpSearchRuleInfo.RuleConditions),
                "Conditions",
                formatter: (value, _) => RuleSummary.Describe(RuleJson.ReadConditions(value as string)))
            .AddColumn(nameof(XpSearchRuleInfo.RulePriority), "Priority", sortable: true)
            .AddColumn(nameof(XpSearchRuleInfo.RuleEnabled), "Enabled");

        // The contact group lives inside the conditions JSON now, so it is not a column of its own in
        // the database. LoadedExternally is how a listing shows a value it derives rather than selects;
        // the formatter reads the row it is handed. Two ColumnConfigurations naming the same database
        // column would collide in the generated query.
        PageConfiguration.ColumnConfigurations.Insert(
            2,
            new ColumnConfiguration
            {
                Name = "ContactGroup",
                Caption = "Contact group",
                Visible = true,
                LoadedExternally = true,
                Formatter = (_, row) => contactGroups.Label(RuleJson.ReadConditions(row?.GetValue(nameof(XpSearchRuleInfo.RuleConditions)) as string).ContactGroup),
            });

        PageConfiguration.HeaderActions.AddLink<RuleCreate>("New rule", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<RuleEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query.WhereEquals(nameof(XpSearchRuleInfo.RuleIndexName), indexName));

        return base.ConfigurePage();
    }
}

/// <summary>Carries the edited rule's identifier in the URL (spec §8.1).</summary>
public class RuleEditSection : EditSectionPage<XpSearchRuleInfo>
{
}

/// <summary>Edits one relevance rule in the if/then builder.</summary>
[UIEvaluatePermission(SystemPermissions.UPDATE)]
public class RuleEdit : RuleBuilderPage
{
    /// <summary>Initializes a new instance of the <see cref="RuleEdit"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="picker">Reads the index behind the item and attribute pickers.</param>
    public RuleEdit(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator,
        IRulePicker picker)
        : base(storageService, provider, contactGroups, pageLinkGenerator, picker)
    {
    }

    /// <summary>Gets or sets the identifier of the edited rule, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(RuleEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedRuleId => ObjectId;
}

/// <summary>Creates a relevance rule in the if/then builder.</summary>
[UIEvaluatePermission(SystemPermissions.CREATE)]
public class RuleCreate : RuleBuilderPage
{
    /// <summary>Initializes a new instance of the <see cref="RuleCreate"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="picker">Reads the index behind the item and attribute pickers.</param>
    public RuleCreate(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator,
        IRulePicker picker)
        : base(storageService, provider, contactGroups, pageLinkGenerator, picker)
    {
    }
}
