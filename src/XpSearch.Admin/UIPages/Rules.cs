using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

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

// The same pages again, inside an experiment: same classes, same templates, variant B's rows (XP-1).
[assembly: UIPage(
    parentType: typeof(ExperimentSection),
    slug: "rules",
    uiPageType: typeof(VariantRuleListing),
    name: "Rules",
    templateName: TemplateNames.LISTING,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(VariantRuleListing),
    slug: "create",
    uiPageType: typeof(VariantRuleCreate),
    name: "New rule",
    templateName: RuleBuilderPage.TemplateName,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(VariantRuleListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(VariantRuleEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(VariantRuleEditSection),
    slug: "edit",
    uiPageType: typeof(VariantRuleEdit),
    name: "Rule",
    templateName: RuleBuilderPage.TemplateName,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// Lists the relevance rules of one index and one tuning variant (spec §8.1, XP-1). The live listing
/// and an experiment's variant-B listing differ only in the variant they read and where their actions
/// point.
/// </summary>
public abstract class RuleListingBase : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IContactGroupCatalog contactGroups;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="RuleListingBase"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="contactGroups">Resolves a stored contact group code name to what the marketer named it.</param>
    /// <param name="provider">Provider of rule objects, to check what a delete would remove.</param>
    protected RuleListingBase(
        ILuceneConfigurationStorageService storageService,
        IContactGroupCatalog contactGroups,
        IInfoProvider<XpSearchRuleInfo> provider)
    {
        this.storageService = storageService;
        this.contactGroups = contactGroups;
        Provider = provider;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchRuleInfo.OBJECT_TYPE;

    /// <summary>Gets the provider of rule objects.</summary>
    protected IInfoProvider<XpSearchRuleInfo> Provider { get; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <summary>Gets the variant whose rows the listing shows. Live listings show the rows with no experiment.</summary>
    protected virtual TuningVariant Variant => TuningVariant.Live;

    /// <summary>Adds the header, row and table actions, which point at this variant's own editors.</summary>
    protected abstract void ConfigureActions();

    /// <summary>
    /// Refuses a delete the listing's own filters would never have offered. The command carries only a
    /// row id, so neither the index filter nor the variant filter reaches it (ADR-0017, XP-1).
    /// </summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    protected Task<ICommandResponse<RowActionResult>> DeleteScoped(int id)
    {
        var row = Provider.Get(id);

        if (!IndexScope.Matches(row?.RuleIndexName, IndexName))
        {
            return Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
        }

        return (row?.RuleExperimentID ?? 0) == Variant.ExperimentId
            ? base.Delete(id)
            : Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(ExperimentScope.CrossVariantDeleteRefusal));
    }

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string indexName = IndexName;
        var variant = Variant;

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

        ConfigureActions();

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchRuleInfo.RuleIndexName), indexName)
                .Where(VariantScope.Condition(nameof(XpSearchRuleInfo.RuleExperimentID), variant)));

        return base.ConfigurePage();
    }
}

/// <summary>Lists the live relevance rules of one index (spec §8.1).</summary>
public class RuleListing : RuleListingBase
{
    private readonly IInfoProvider<XpSearchPopularitySuggestionInfo> suggestions;
    private readonly IPageLinkGenerator pageLinkGenerator;

    /// <summary>Initializes a new instance of the <see cref="RuleListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="contactGroups">Resolves a stored contact group code name to what the marketer named it.</param>
    /// <param name="provider">Provider of rule objects, to check what a delete would remove.</param>
    /// <param name="suggestions">Provider of suggested rules, counted for the banner that points at them (RK-1).</param>
    /// <param name="pageLinkGenerator">Generates the banner button's link to the suggestions listing.</param>
    public RuleListing(
        ILuceneConfigurationStorageService storageService,
        IContactGroupCatalog contactGroups,
        IInfoProvider<XpSearchRuleInfo> provider,
        IInfoProvider<XpSearchPopularitySuggestionInfo> suggestions,
        IPageLinkGenerator pageLinkGenerator)
        : base(storageService, contactGroups, provider)
    {
        this.suggestions = suggestions;
        this.pageLinkGenerator = pageLinkGenerator;
    }

    /// <summary>Deletes one live rule.</summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public override Task<ICommandResponse<RowActionResult>> Delete(int id) => DeleteScoped(id);

    /// <inheritdoc />
    protected override void ConfigureActions()
    {
        int pending = PendingSuggestions();

        if (pending > 0)
        {
            PageConfiguration.Callouts =
            [
                new CalloutConfiguration
                {
                    Headline = pending == 1 ? "1 suggested rule is waiting" : $"{pending} suggested rules are waiting",
                    Content = "The popularity task found queries where one result clearly wins the clicks. "
                        + "Approve or dismiss the suggestions; nothing is applied until you do.",
                    ContentAsHtml = false,
                    ActionButton = new CalloutRedirectButtonConfiguration
                    {
                        Text = "Suggestions",
                        RedirectUrl = pageLinkGenerator.GetPath<PopularitySuggestionListing>(IndexScope.Route(IndexIdentifier)),
                    },
                }
            ];
        }

        PageConfiguration.HeaderActions.AddLink<RuleCreate>("New rule", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<RuleEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
    }

    private int PendingSuggestions() =>
        suggestions.Get()
            .WhereEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionIndexName), IndexName)
            .WhereEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionState), (int)PopularitySuggestionState.Pending)
            .Count;
}

/// <summary>Lists the relevance rules of an experiment's variant B (XP-1).</summary>
public class VariantRuleListing : RuleListingBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantRuleListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="contactGroups">Resolves a stored contact group code name to what the marketer named it.</param>
    /// <param name="provider">Provider of rule objects, to check what a delete would remove.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantRuleListing(
        ILuceneConfigurationStorageService storageService,
        IContactGroupCatalog contactGroups,
        IInfoProvider<XpSearchRuleInfo> provider,
        IExperimentCatalog experiments)
        : base(storageService, contactGroups, provider) =>
        this.experiments = experiments;

    /// <summary>Gets or sets the identifier of the experiment the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override TuningVariant Variant => ExperimentScope.Variant(ExperimentIdentifier);

    /// <summary>
    /// Deletes one variant-B rule. Declared here rather than inherited: a page command has to be a
    /// plain method on the final page class (see docs/internal/agent-primer.md).
    /// </summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public Task<ICommandResponse<RowActionResult>> DeleteRow(int id) => DeleteScoped(id);

    /// <inheritdoc />
    protected override void ConfigureActions()
    {
        var experiment = ExperimentScope.Resolve(experiments, ExperimentIdentifier, IndexName);

        PageConfiguration.Callouts = [ExperimentScope.Banner(experiment)];

        // A started experiment's variant B is what half the visitors are being served: it is read-only.
        if (!ExperimentScope.IsDraft(experiment))
        {
            return;
        }

        PageConfiguration.HeaderActions.AddLink<VariantRuleCreate>(
            "New rule",
            parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.AddEditRowAction<VariantRuleEdit>(parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(DeleteRow), "Delete");
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

/// <summary>Carries the edited variant-B rule's identifier in the URL (XP-1).</summary>
public class VariantRuleEditSection : EditSectionPage<XpSearchRuleInfo>
{
}

/// <summary>
/// The variant-B rule builder pages (XP-1): the same builder, over the rules of one experiment's draft.
/// </summary>
public abstract class VariantRuleBuilderPage : RuleBuilderPage
{
    private readonly IExperimentCatalog experiments;
    private readonly ILuceneConfigurationStorageService storageService;

    /// <summary>Initializes a new instance of the <see cref="VariantRuleBuilderPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="picker">Reads the index behind the item and attribute pickers.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    protected VariantRuleBuilderPage(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator,
        IRulePicker picker,
        IExperimentCatalog experiments)
        : base(storageService, provider, contactGroups, pageLinkGenerator, picker)
    {
        this.storageService = storageService;
        this.experiments = experiments;
    }

    /// <summary>Gets or sets the identifier of the experiment, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override TuningVariant Variant => ExperimentScope.Variant(ExperimentIdentifier);

    /// <inheritdoc />
    protected override bool CanWrite => ExperimentScope.IsDraft(Experiment);

    /// <inheritdoc />
    protected override string VariantBanner => ExperimentScope.Banner(Experiment).Headline;

    /// <inheritdoc />
    protected override string VariantBannerContent => ExperimentScope.BannerContent(Experiment);

    private ExperimentSummary? Experiment =>
        ExperimentScope.Resolve(experiments, ExperimentIdentifier, IndexScope.Resolve(storageService, IndexIdentifier));

    /// <inheritdoc />
    protected override string ListingPath() =>
        PageLinks.GetPath<VariantRuleListing>(ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
}

/// <summary>Edits one rule of an experiment's variant B (XP-1).</summary>
[UIEvaluatePermission(SystemPermissions.UPDATE)]
public class VariantRuleEdit : VariantRuleBuilderPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantRuleEdit"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="picker">Reads the index behind the item and attribute pickers.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantRuleEdit(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator,
        IRulePicker picker,
        IExperimentCatalog experiments)
        : base(storageService, provider, contactGroups, pageLinkGenerator, picker, experiments)
    {
    }

    /// <summary>Gets or sets the identifier of the edited rule, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(VariantRuleEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedRuleId => ObjectId;
}

/// <summary>Creates a rule in an experiment's variant B (XP-1).</summary>
[UIEvaluatePermission(SystemPermissions.CREATE)]
public class VariantRuleCreate : VariantRuleBuilderPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantRuleCreate"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="picker">Reads the index behind the item and attribute pickers.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantRuleCreate(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator,
        IRulePicker picker,
        IExperimentCatalog experiments)
        : base(storageService, provider, contactGroups, pageLinkGenerator, picker, experiments)
    {
    }
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
