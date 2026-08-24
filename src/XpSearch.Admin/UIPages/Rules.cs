using CMS.ContactManagement;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
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
    templateName: TemplateNames.EDIT,
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
    templateName: TemplateNames.EDIT,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>The form a marketer fills in to describe a relevance rule (spec §8.2).</summary>
public class RuleModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the rule applies to. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 1, Tooltip = "The search index this rule applies to.")]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name; it is what an explained result shows.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Rule name", Order = 2, Tooltip = "Shown in the ranking explanation, so name it after what it does.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the rule is live.</summary>
    [CheckBoxComponent(Label = "Enabled", Order = 3)]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the contact group the rule is scoped to. Empty applies the rule to everyone.
    /// </summary>
    /// <remarks>
    /// The object selector stores code names rather than GUIDs, which is what the documentation
    /// recommends and what the query pipeline compares against
    /// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/reference-admin-ui-form-components#object-selector).
    /// </remarks>
    [ObjectSelectorComponent(
        ContactGroupInfo.OBJECT_TYPE,
        MaximumItems = 1,
        Label = "Contact group",
        Order = 4,
        Tooltip = "Apply this rule only to visitors in this contact group. Empty applies it to everyone.")]
    public IEnumerable<ObjectRelatedItem> ContactGroup { get; set; } = [];

    /// <summary>Gets or sets how the pattern is matched, as the numeric value of <see cref="FlatCondition"/>.</summary>
    [DropDownComponent(
        Label = "When the visitor's search",
        Order = 5,
        Options = "0;Contains the words below\r\n1;Is exactly the words below\r\n2;Starts with the words below\r\n3;Is anything at all")]
    public string Condition { get; set; } = "0";

    /// <summary>Gets or sets the query pattern.</summary>
    [TextInputComponent(Label = "Words to look for", Order = 6)]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Gets or sets what the rule does, as the numeric value of <see cref="FlatConsequence"/>.</summary>
    [DropDownComponent(
        Label = "Then",
        Order = 7,
        Options = "0;Pin a result to a position\r\n1;Bury a result\r\n2;Boost a result\r\n3;Filter the results\r\n4;Redirect the visitor")]
    public string Consequence { get; set; } = "0";

    /// <summary>Gets or sets the result id to pin, bury or boost.</summary>
    [TextInputComponent(Label = "Result id", Order = 8, Tooltip = "The id from the search response, for pin, bury and boost.")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-based position a pinned result is moved to.</summary>
    [NumberInputComponent(Label = "Pin to position", Order = 9, Tooltip = "1 is the top of the first page.")]
    public int TargetPosition { get; set; } = 1;

    /// <summary>Gets or sets the boost multiplier.</summary>
    [DecimalNumberInputComponent(Label = "Boost multiplier", Order = 10, Tooltip = "Above 1 lifts the result, below 1 lowers it.")]
    public decimal BoostValue { get; set; } = 1m;

    /// <summary>Gets or sets the filter expression, as comma-separated <c>field:value</c> pairs.</summary>
    [TextInputComponent(Label = "Filter", Order = 11, Tooltip = "Comma-separated attribute:value pairs, for example Category:coffee, Tags:brewing. Use the attribute names that appear in search results, such as contentType.")]
    public string FilterExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the redirect destination.</summary>
    [TextInputComponent(Label = "Redirect URL", Order = 12, Tooltip = "Where a Redirect rule sends the visitor. Returned as the response's redirect member; the shipped search box follows it only for a submitted query.")]
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the first moment the rule applies, in UTC.</summary>
    [DateTimeInputComponent(Label = "Runs from", Order = 13)]
    public DateTime? ValidFrom { get; set; }

    /// <summary>Gets or sets the last moment the rule applies, in UTC.</summary>
    [DateTimeInputComponent(Label = "Runs until", Order = 14)]
    public DateTime? ValidTo { get; set; }

    /// <summary>Gets or sets the conflict resolution order; lower wins.</summary>
    [NumberInputComponent(Label = "Priority", Order = 15, Tooltip = "When two rules disagree, the lower number wins.")]
    public int Priority { get; set; } = 100;

    /// <summary>Copies the model onto a stored row.</summary>
    /// <param name="row">The row to fill.</param>
    /// <returns>The same row.</returns>
    public XpSearchRuleInfo ApplyTo(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.RuleIndexName = IndexName;
        row.RuleName = Name;
        row.RuleEnabled = Enabled;
        row.RuleContactGroup = ContactGroup?.FirstOrDefault()?.ObjectCodeName ?? string.Empty;
        row.RuleConditionType = ParseOption(Condition);
        row.RulePattern = Pattern;
        row.RuleConsequenceType = ParseOption(Consequence);
        row.RuleTargetObjectID = TargetId;
        row.RuleTargetPosition = TargetPosition;
        row.RuleBoostValue = BoostValue;
        row.RuleFilterExpression = FilterExpression;
        row.RuleRedirectUrl = RedirectUrl;
        row.RuleValidFrom = ValidFrom;
        row.RuleValidTo = ValidTo;
        row.RulePriority = Priority;

        return row;
    }

    /// <summary>Reads a stored row back into a model.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The model.</returns>
    public static RuleModel From(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new RuleModel
        {
            IndexName = row.RuleIndexName,
            Name = row.RuleName,
            Enabled = row.RuleEnabled,
            ContactGroup = string.IsNullOrWhiteSpace(row.RuleContactGroup)
                ? []
                : [new ObjectRelatedItem { ObjectCodeName = row.RuleContactGroup }],
            Condition = row.RuleConditionType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Pattern = row.RulePattern,
            Consequence = row.RuleConsequenceType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TargetId = row.RuleTargetObjectID,
            TargetPosition = row.RuleTargetPosition,
            BoostValue = row.RuleBoostValue,
            FilterExpression = row.RuleFilterExpression,
            RedirectUrl = row.RuleRedirectUrl,
            ValidFrom = row.RuleValidFrom,
            ValidTo = row.RuleValidTo,
            Priority = row.RulePriority
        };
    }

    /// <summary>Parses a drop-down option back into the numeric enum value it stands for.</summary>
    /// <param name="value">The selected option.</param>
    /// <returns>The numeric value, or zero when nothing was selected.</returns>
    public static int ParseOption(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
}

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
            .AddColumn(nameof(XpSearchRuleInfo.RulePattern), "Words to look for")
            .AddColumn(
                nameof(XpSearchRuleInfo.RuleContactGroup),
                "Contact group",
                formatter: (value, _) => contactGroups.Label(value as string))
            .AddColumn(nameof(XpSearchRuleInfo.RulePriority), "Priority", sortable: true)
            .AddColumn(nameof(XpSearchRuleInfo.RuleEnabled), "Enabled");

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

/// <summary>Edits one relevance rule.</summary>
public class RuleEdit : IndexScopedEditPage<RuleModel>
{
    private readonly IInfoProvider<XpSearchRuleInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="RuleEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of rule objects.</param>
    public RuleEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchRuleInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <summary>Gets or sets the identifier of the edited rule, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(RuleEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override string? EditedIndexName => provider.Get(ObjectId)?.RuleIndexName;

    /// <inheritdoc />
    protected override RuleModel CreateModel() =>
        provider.Get(ObjectId) is { } row ? RuleModel.From(row) : new RuleModel();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(RuleModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = provider.Get(ObjectId) ?? new XpSearchRuleInfo();

        provider.Set(submitted.ApplyTo(row));

        return Task.FromResult($"Rule '{submitted.Name}' saved.");
    }
}

/// <summary>Creates a relevance rule.</summary>
public class RuleCreate : IndexScopedEditPage<RuleModel>
{
    private readonly IInfoProvider<XpSearchRuleInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="RuleCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of rule objects.</param>
    public RuleCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchRuleInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(RuleListing);

    /// <inheritdoc />
    protected override RuleModel CreateModel() => new();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(RuleModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = submitted.ApplyTo(new XpSearchRuleInfo());
        row.RuleGuid = Guid.NewGuid();

        provider.Set(row);

        return Task.FromResult($"Rule '{submitted.Name}' created.");
    }
}
