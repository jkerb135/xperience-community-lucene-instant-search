using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;

namespace XpSearch.Admin.UIPages.RuleBuilder;

/// <summary>
/// The if/then rule builder (ADR-0022, design canvas 5a-5f): the rule-level settings strip, the
/// <c>If</c> column of condition summary rows with a side panel behind each, and the <c>Then</c>
/// column of consequence cards.
/// </summary>
/// <remarks>
/// A custom client template, because the model is a list of conditions and an ordered list of ten
/// kinds of consequence - a shape no built-in template can express
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
/// It replaces the flat one-condition/one-consequence EDIT form of ADR-0014, at the same URLs.
/// </remarks>
public abstract class RuleBuilderPage : Page<RuleBuilderClientProperties>
{
    /// <summary>Name the registrations use for the client template.</summary>
    public const string TemplateName = "@xperience-community/xperience-search/RuleBuilder";

    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IInfoProvider<XpSearchRuleInfo> provider;
    private readonly IContactGroupCatalog contactGroups;
    private readonly IPageLinkGenerator pageLinkGenerator;

    /// <summary>Initializes a new instance of the <see cref="RuleBuilderPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of rule objects.</param>
    /// <param name="contactGroups">Supplies the contact groups the Context toggle offers.</param>
    /// <param name="pageLinkGenerator">Generates the URL Cancel, Save and Delete navigate back to.</param>
    protected RuleBuilderPage(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchRuleInfo> provider,
        IContactGroupCatalog contactGroups,
        IPageLinkGenerator pageLinkGenerator)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(contactGroups);
        ArgumentNullException.ThrowIfNull(pageLinkGenerator);

        this.storageService = storageService;
        this.provider = provider;
        this.contactGroups = contactGroups;
        this.pageLinkGenerator = pageLinkGenerator;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets the identifier of the edited rule, or zero on a create page.</summary>
    protected virtual int EditedRuleId => 0;

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

    /// <summary>Gets the stored row being edited, or <see langword="null"/> on a create page.</summary>
    protected XpSearchRuleInfo? EditedRow => EditedRuleId > 0 ? provider.Get(EditedRuleId) : null;

    /// <inheritdoc />
    public override async Task<RuleBuilderClientProperties> ConfigureTemplateProperties(RuleBuilderClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var index = IndexScope.ResolveModel(storageService, IndexIdentifier);

        properties.IndexName = index?.IndexName ?? string.Empty;
        properties.Languages = [.. index?.LanguageNames ?? []];
        properties.ContactGroups = await contactGroups.GetAllAsync(CancellationToken.None).ConfigureAwait(false);

        var row = EditedRow;

        properties.IsNew = row is null;
        properties.Migrated = row?.RuleMigrated ?? false;

        if (row is null)
        {
            properties.Rule = SeedRule();
        }
        else if (!IndexScope.Matches(row.RuleIndexName, properties.IndexName))
        {
            properties.Error = "This rule belongs to a different search index.";
        }
        else
        {
            properties.Rule = RuleDto.From(InfoRelevanceTuningSource.Read(row));
        }

        return properties;
    }

    /// <summary>Reads the rule again, which is what the builder does after a failed save or a reload.</summary>
    /// <returns>The rule.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<ICommandResponse<RuleDto>> Load()
    {
        var row = EditedRow;

        return Task.FromResult(ResponseFrom(row is null ? SeedRule() : RuleDto.From(InfoRelevanceTuningSource.Read(row))));
    }

    /// <summary>Leaves the builder without saving.</summary>
    /// <returns>A navigation back to the listing.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> Cancel() => Task.FromResult(NavigateTo(ListingPath()));

    /// <summary>Validates the submitted rule and stores it.</summary>
    /// <param name="submitted">The rule as the builder has it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A navigation back to the listing when a new rule was created, otherwise the result - the
    /// errors that refused the save, or the rule as it was stored.
    /// </returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public Task<ICommandResponse> Save(RuleDto submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        string indexName = IndexName;

        if (string.IsNullOrEmpty(indexName))
        {
            return Refuse(RuleSaveResult.Failed("This index is not registered."));
        }

        var row = EditedRow;

        // A rule reached through another index's URL is refused rather than silently re-homed.
        if (row is not null && !IndexScope.Matches(row.RuleIndexName, indexName))
        {
            return Refuse(RuleSaveResult.Failed("This rule belongs to a different search index and was not saved."));
        }

        (var conditions, var consequences) = submitted.ToModel();

        var errors = RuleValidation.Validate(submitted.Name, conditions, consequences);

        if (errors.Count > 0)
        {
            return Refuse(RuleSaveResult.Refused(errors));
        }

        bool creating = row is null;

        row ??= new XpSearchRuleInfo { RuleGuid = Guid.NewGuid() };

        row.RuleIndexName = indexName;
        row.RuleName = submitted.Name.Trim();
        row.RuleEnabled = submitted.Enabled;
        row.RulePriority = submitted.Priority;
        row.RuleValidFrom = RuleDto.Moment(submitted.ValidFrom);
        row.RuleValidTo = RuleDto.Moment(submitted.ValidTo);
        row.RuleConditions = RuleJson.Write(conditions);
        row.RuleConsequences = RuleJson.Write(consequences);

        // The "converted from the previous format" note has now been seen and acted on.
        row.RuleMigrated = false;

        provider.Set(row);

        if (creating)
        {
            return Task.FromResult<ICommandResponse>(NavigateTo(ListingPath()));
        }

        return Task.FromResult<ICommandResponse>(
            ResponseFrom(new RuleSaveResult { Rule = RuleDto.From(InfoRelevanceTuningSource.Read(row)) })
                .AddSuccessMessage($"Rule '{row.RuleName}' saved."));
    }

    /// <summary>Deletes the edited rule.</summary>
    /// <returns>A navigation back to the listing.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public Task<INavigateResponse> Delete()
    {
        if (EditedRow is { } row && IndexScope.Matches(row.RuleIndexName, IndexName))
        {
            provider.Delete(row);
        }

        return Task.FromResult(NavigateTo(ListingPath()));
    }

    /// <summary>Builds the rule a create page starts from. The seeded page overrides it.</summary>
    /// <returns>The rule.</returns>
    protected virtual RuleDto SeedRule() => new();

    private Task<ICommandResponse> Refuse(RuleSaveResult result) =>
        Task.FromResult<ICommandResponse>(ResponseFrom(result));

    private string ListingPath() => pageLinkGenerator.GetPath<RuleListing>(IndexScope.Route(IndexIdentifier));
}
