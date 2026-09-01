using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Forms;
using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Core;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "weights",
    uiPageType: typeof(FieldWeightListing),
    name: "Field weights",
    templateName: TemplateNames.LISTING,
    order: 500)]

[assembly: UIPage(
    parentType: typeof(FieldWeightListing),
    slug: "create",
    uiPageType: typeof(FieldWeightCreate),
    name: "New field weight",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(FieldWeightListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(FieldWeightEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(FieldWeightEditSection),
    slug: "edit",
    uiPageType: typeof(FieldWeightEdit),
    name: "Field weight",
    templateName: TemplateNames.EDIT,
    order: 100)]

// The same pages again, inside an experiment: same classes, same templates, variant B's rows (XP-1).
[assembly: UIPage(
    parentType: typeof(ExperimentSection),
    slug: "weights",
    uiPageType: typeof(VariantFieldWeightListing),
    name: "Field weights",
    templateName: TemplateNames.LISTING,
    order: 500)]

[assembly: UIPage(
    parentType: typeof(VariantFieldWeightListing),
    slug: "create",
    uiPageType: typeof(VariantFieldWeightCreate),
    name: "New field weight",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(VariantFieldWeightListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(VariantFieldWeightEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(VariantFieldWeightEditSection),
    slug: "edit",
    uiPageType: typeof(VariantFieldWeightEdit),
    name: "Field weight",
    templateName: TemplateNames.EDIT,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>The form behind one per-field score multiplier (spec §8.2).</summary>
public class FieldWeightModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the weight applies to. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 1)]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the schema field the weight applies to.</summary>
    [RequiredValidationRule]
    [DropDownComponent(
        Label = "Field",
        Order = 2,
        Placeholder = "Select a field",
        Tooltip = "The attribute name as it appears in search results, for example Title.")]
    [FormComponentConfiguration(XpSearchConstants.WeightFieldConfiguratorIdentifier, nameof(IndexName))]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Gets or sets the multiplier.</summary>
    [DecimalNumberInputComponent(Label = "Weight", Order = 3, Tooltip = "1 is normal. 3 makes a match in this field count about three times as much.")]
    public decimal Weight { get; set; } = 1m;

    /// <summary>Copies the model onto a stored row.</summary>
    /// <param name="row">The row to fill.</param>
    /// <returns>The same row.</returns>
    public XpSearchFieldWeightInfo ApplyTo(XpSearchFieldWeightInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.WeightIndexName = IndexName;
        row.WeightFieldName = FieldName;
        row.WeightValue = Weight;

        return row;
    }

    /// <summary>Reads a stored row back into a model.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The model.</returns>
    public static FieldWeightModel From(XpSearchFieldWeightInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new FieldWeightModel
        {
            IndexName = row.WeightIndexName,
            FieldName = row.WeightFieldName,
            Weight = row.WeightValue
        };
    }
}

/// <summary>
/// Lists the field weights of one index and one tuning variant (spec §8.1, XP-1). The live listing and
/// an experiment's variant-B listing differ only in the variant they read and where their actions point.
/// </summary>
public abstract class FieldWeightListingBase : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="FieldWeightListingBase"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of field weight objects, to check what a delete would remove.</param>
    protected FieldWeightListingBase(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchFieldWeightInfo> provider)
    {
        this.storageService = storageService;
        Provider = provider;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchFieldWeightInfo.OBJECT_TYPE;

    /// <summary>Gets the provider of field weight objects.</summary>
    protected IInfoProvider<XpSearchFieldWeightInfo> Provider { get; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <summary>Gets the variant whose rows the listing shows. Live listings show the rows with no experiment.</summary>
    protected virtual TuningVariant Variant => TuningVariant.Live;

    /// <summary>Adds the header, row and table actions, which point at this variant's own editors.</summary>
    protected abstract void ConfigureActions();

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string index = IndexName;
        var variant = Variant;

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchFieldWeightInfo.WeightFieldName), "Field", searchable: true)
            .AddColumn(nameof(XpSearchFieldWeightInfo.WeightValue), "Weight");

        ConfigureActions();

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchFieldWeightInfo.WeightIndexName), index)
                .Where(VariantScope.Condition(nameof(XpSearchFieldWeightInfo.WeightExperimentID), variant)));

        return base.ConfigurePage();
    }

    /// <summary>
    /// Refuses a delete the listing's own filters would never have offered. The command carries only a
    /// row id, so neither the index filter nor the variant filter reaches it (ADR-0017, XP-1).
    /// </summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    protected Task<ICommandResponse<RowActionResult>> DeleteScoped(int id)
    {
        var row = Provider.Get(id);

        if (!IndexScope.Matches(row?.WeightIndexName, IndexName))
        {
            return Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
        }

        return (row?.WeightExperimentID ?? 0) == Variant.ExperimentId
            ? base.Delete(id)
            : Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(ExperimentScope.CrossVariantDeleteRefusal));
    }
}

/// <summary>Lists the live field weights of one index (spec §8.1).</summary>
public class FieldWeightListing : FieldWeightListingBase
{
    private readonly IInfoProvider<XpSearchPopularityIndexInfo> popularity;

    /// <summary>Initializes a new instance of the <see cref="FieldWeightListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of field weight objects, to check what a delete would remove.</param>
    /// <param name="popularity">Provider of the index's popularity settings, behind the opt-in toggle (RK-1).</param>
    public FieldWeightListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchFieldWeightInfo> provider,
        IInfoProvider<XpSearchPopularityIndexInfo> popularity)
        : base(storageService, provider) =>
        this.popularity = popularity;

    /// <summary>Deletes one live weight.</summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public override Task<ICommandResponse<RowActionResult>> Delete(int id) => DeleteScoped(id);

    /// <summary>
    /// Turns the popularity boost of this index on or off (RK-1). It is an index-wide setting, not a
    /// tuning row: an experiment tests tuning, and both of its variants see the same boost (ADR-0025).
    /// </summary>
    /// <returns>The row action result, which reloads the listing.</returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public Task<ICommandResponse<RowActionResult>> TogglePopularityBoost()
    {
        if (string.IsNullOrEmpty(IndexName))
        {
            return Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
        }

        var row = Settings() ?? new XpSearchPopularityIndexInfo
        {
            PopularityIndexGuid = Guid.NewGuid(),
            PopularityIndexName = IndexName
        };

        row.PopularityIndexEnabled = !row.PopularityIndexEnabled;
        popularity.Set(row);

        return Task.FromResult(ResponseFrom(new RowActionResult(true)).AddSuccessMessage(
            row.PopularityIndexEnabled
                ? "Popular results are now boosted for this index."
                : "Popularity no longer affects this index's ranking."));
    }

    /// <inheritdoc />
    protected override void ConfigureActions()
    {
        bool enabled = Settings()?.PopularityIndexEnabled ?? false;

        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = enabled ? "Boost by popularity: on" : "Boost by popularity: off",
                Content = enabled
                    ? "Results this index's visitors click most are boosted by up to 2x, from the signal the popularity task computes."
                    : "Ranking uses text relevance and your rules only. Turn the boost on to also favour the results visitors click.",
                ContentAsHtml = false,
            }
        ];

        PageConfiguration.HeaderActions.AddLink<FieldWeightCreate>("New field weight", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.HeaderActions.AddCommand(
            enabled ? "Stop boosting by popularity" : "Boost by popularity",
            nameof(TogglePopularityBoost));
        PageConfiguration.AddEditRowAction<FieldWeightEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
    }

    private XpSearchPopularityIndexInfo? Settings() =>
        popularity.Get()
            .WhereEquals(nameof(XpSearchPopularityIndexInfo.PopularityIndexName), IndexName)
            .TopN(1)
            .FirstOrDefault();
}

/// <summary>Lists the field weights of an experiment's variant B (XP-1).</summary>
public class VariantFieldWeightListing : FieldWeightListingBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantFieldWeightListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of field weight objects, to check what a delete would remove.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantFieldWeightListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchFieldWeightInfo> provider,
        IExperimentCatalog experiments)
        : base(storageService, provider) =>
        this.experiments = experiments;

    /// <summary>Gets or sets the identifier of the experiment the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override TuningVariant Variant => ExperimentScope.Variant(ExperimentIdentifier);

    /// <summary>
    /// Deletes one variant-B weight. Declared here rather than inherited: a page command has to be a
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

        PageConfiguration.HeaderActions.AddLink<VariantFieldWeightCreate>(
            "New field weight",
            parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.AddEditRowAction<VariantFieldWeightEdit>(parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(DeleteRow), "Delete");
    }
}

/// <summary>Carries the edited live weight's identifier in the URL.</summary>
public class FieldWeightEditSection : EditSectionPage<XpSearchFieldWeightInfo>
{
}

/// <summary>Carries the edited variant-B weight's identifier in the URL (XP-1).</summary>
public class VariantFieldWeightEditSection : EditSectionPage<XpSearchFieldWeightInfo>
{
}

/// <summary>
/// Edits or creates one field weight, in the live tuning or in an experiment's variant B (XP-1). The
/// variant is the only difference between the four pages built on this.
/// </summary>
public abstract class FieldWeightEditPageBase : IndexScopedEditPage<FieldWeightModel>
{
    /// <summary>Initializes a new instance of the <see cref="FieldWeightEditPageBase"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    protected FieldWeightEditPageBase(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        Provider = provider;

    /// <summary>Gets the provider of field weight objects.</summary>
    protected IInfoProvider<XpSearchFieldWeightInfo> Provider { get; }

    /// <summary>Gets the identifier of the edited row, or zero when the page creates one.</summary>
    protected virtual int EditedId => 0;

    /// <summary>Gets the experiment the written row belongs to, or <see langword="null"/> for the live tuning.</summary>
    protected virtual int? ExperimentId => null;

    /// <summary>Gets a value indicating whether the page may still write. A started experiment's B is frozen.</summary>
    protected virtual bool CanWrite => true;

    /// <summary>Gets the stored row being edited, or <see langword="null"/> when the page creates one.</summary>
    protected XpSearchFieldWeightInfo? EditedRow => EditedId > 0 ? Provider.Get(EditedId) : null;

    /// <inheritdoc />
    protected override string? EditedIndexName => EditedId > 0 ? EditedRow?.WeightIndexName : IndexName;

    /// <inheritdoc />
    protected override FieldWeightModel CreateModel() =>
        EditedRow is { } row ? FieldWeightModel.From(row) : new FieldWeightModel();

    /// <inheritdoc />
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();

        if (EditedId == 0)
        {
            return items;
        }

        // The stored field may no longer be in the index's schema; the configurator keeps it selectable
        // so an edit does not silently move the weight to another field.
        foreach (var dropDown in items.OfType<DropDownComponent>()
            .Where(component => string.Equals(component.Name, nameof(FieldWeightModel.FieldName), StringComparison.OrdinalIgnoreCase)))
        {
            dropDown.Properties.Options = WeightFieldConfigurator.WithStoredValue(dropDown.Properties.Options, Model.FieldName);
        }

        return items;
    }

    /// <inheritdoc />
    protected override Task<ICommandResponse> ProcessFormData(FieldWeightModel model, ICollection<IFormItem> formItems)
    {
        if (!CanWrite)
        {
            return Refuse(ExperimentScope.FrozenRefusal);
        }

        return EditedId > 0 && (EditedRow?.WeightExperimentID ?? 0) != (ExperimentId ?? 0)
            ? Refuse(ExperimentScope.CrossVariantRefusal)
            : base.ProcessFormData(model, formItems);
    }

    /// <inheritdoc />
    protected override Task<string> PersistAsync(FieldWeightModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = EditedRow;
        bool creating = row is null;

        row ??= new XpSearchFieldWeightInfo { WeightGuid = Guid.NewGuid() };

        submitted.ApplyTo(row);
        row.WeightExperimentID = ExperimentId;

        Provider.Set(row);

        return Task.FromResult($"Weight for '{submitted.FieldName}' {(creating ? "created" : "saved")}.");
    }

    /// <summary>Answers a submit that must not be written.</summary>
    /// <param name="message">Why it was not written.</param>
    /// <returns>The validation failure.</returns>
    protected Task<ICommandResponse> Refuse(string message) =>
        Task.FromResult<ICommandResponse>(
            ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationFailure)).AddErrorMessage(message));
}

/// <summary>Edits one live field weight.</summary>
public class FieldWeightEdit : FieldWeightEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="FieldWeightEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    public FieldWeightEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <summary>Gets or sets the identifier of the edited weight, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(FieldWeightEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates a live field weight.</summary>
public class FieldWeightCreate : FieldWeightEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="FieldWeightCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    public FieldWeightCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(FieldWeightListing);
}

/// <summary>
/// The variant-B field weight pages (XP-1): the experiment in the URL, the banner, and the refusal to
/// write once the experiment has started.
/// </summary>
public abstract class VariantFieldWeightPage : FieldWeightEditPageBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantFieldWeightPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    protected VariantFieldWeightPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider) =>
        this.experiments = experiments;

    /// <summary>Gets or sets the identifier of the experiment, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override int? ExperimentId => ExperimentIdentifier;

    /// <inheritdoc />
    protected override bool CanWrite => ExperimentScope.IsDraft(Experiment);

    /// <inheritdoc />
    protected override PageParameterValues? RedirectParameters => ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier);

    /// <summary>Gets the experiment in the URL, or <see langword="null"/> when it is not this index's.</summary>
    protected ExperimentSummary? Experiment => ExperimentScope.Resolve(experiments, ExperimentIdentifier, IndexName);

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        PageConfiguration.Callouts = [ExperimentScope.Banner(Experiment)];

        return base.ConfigurePage();
    }
}

/// <summary>Edits one field weight of an experiment's variant B (XP-1).</summary>
public class VariantFieldWeightEdit : VariantFieldWeightPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantFieldWeightEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantFieldWeightEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <summary>Gets or sets the identifier of the edited weight, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(VariantFieldWeightEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates a field weight in an experiment's variant B (XP-1).</summary>
public class VariantFieldWeightCreate : VariantFieldWeightPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantFieldWeightCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of field weight objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantFieldWeightCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchFieldWeightInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(VariantFieldWeightListing);
}

