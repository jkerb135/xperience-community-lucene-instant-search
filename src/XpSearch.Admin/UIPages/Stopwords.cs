using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Core.Tuning;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "stopwords",
    uiPageType: typeof(StopwordListing),
    name: "Stopwords",
    templateName: TemplateNames.LISTING,
    order: 400)]

[assembly: UIPage(
    parentType: typeof(StopwordListing),
    slug: "create",
    uiPageType: typeof(StopwordCreate),
    name: "New stopword list",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(StopwordListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(StopwordEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(StopwordEditSection),
    slug: "edit",
    uiPageType: typeof(StopwordEdit),
    name: "Stopwords",
    templateName: TemplateNames.EDIT,
    order: 100)]

// The same pages again, inside an experiment: same classes, same templates, variant B's rows (XP-1).
[assembly: UIPage(
    parentType: typeof(ExperimentSection),
    slug: "stopwords",
    uiPageType: typeof(VariantStopwordListing),
    name: "Stopwords",
    templateName: TemplateNames.LISTING,
    order: 400)]

[assembly: UIPage(
    parentType: typeof(VariantStopwordListing),
    slug: "create",
    uiPageType: typeof(VariantStopwordCreate),
    name: "New stopword list",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(VariantStopwordListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(VariantStopwordEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(VariantStopwordEditSection),
    slug: "edit",
    uiPageType: typeof(VariantStopwordEdit),
    name: "Stopwords",
    templateName: TemplateNames.EDIT,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>The stopword list of one index: an index and a block of words, one per line.</summary>
public class StopwordModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the list belongs to. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 1)]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the stopwords, one per line.</summary>
    [TextAreaComponent(Label = "Words to ignore", Order = 2, Tooltip = "One word per line. These words are ignored when someone searches.")]
    public string Words { get; set; } = string.Empty;

    /// <summary>Copies the model onto a stored row.</summary>
    /// <param name="row">The row to fill.</param>
    /// <returns>The same row.</returns>
    public XpSearchStopwordListInfo ApplyTo(XpSearchStopwordListInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.StopwordListIndexName = IndexName;
        row.StopwordListWords = Words;

        return row;
    }

    /// <summary>Reads a stored row back into a model.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The model.</returns>
    public static StopwordModel From(XpSearchStopwordListInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new StopwordModel { IndexName = row.StopwordListIndexName, Words = row.StopwordListWords };
    }
}

/// <summary>
/// Lists the stopword lists of one index and one tuning variant (spec §8.1, XP-1). The live listing and
/// an experiment's variant-B listing differ only in the variant they read and where their actions point.
/// </summary>
public abstract class StopwordListingBase : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="StopwordListingBase"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of stopword list objects, to check what a delete would remove.</param>
    protected StopwordListingBase(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchStopwordListInfo> provider)
    {
        this.storageService = storageService;
        Provider = provider;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchStopwordListInfo.OBJECT_TYPE;

    /// <summary>Gets the provider of stopword list objects.</summary>
    protected IInfoProvider<XpSearchStopwordListInfo> Provider { get; }

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
            .AddColumn(nameof(XpSearchStopwordListInfo.StopwordListWords), "Words to ignore");

        ConfigureActions();

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchStopwordListInfo.StopwordListIndexName), index)
                .Where(VariantScope.Condition(nameof(XpSearchStopwordListInfo.StopwordListExperimentID), variant)));

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

        if (!IndexScope.Matches(row?.StopwordListIndexName, IndexName))
        {
            return Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
        }

        return (row?.StopwordListExperimentID ?? 0) == Variant.ExperimentId
            ? base.Delete(id)
            : Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(ExperimentScope.CrossVariantDeleteRefusal));
    }
}

/// <summary>Lists the live stopword lists of one index (spec §8.1).</summary>
public class StopwordListing : StopwordListingBase
{
    /// <summary>Initializes a new instance of the <see cref="StopwordListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of stopword list objects, to check what a delete would remove.</param>
    public StopwordListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(storageService, provider)
    {
    }

    /// <summary>Deletes one live stopword list.</summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public override Task<ICommandResponse<RowActionResult>> Delete(int id) => DeleteScoped(id);

    /// <inheritdoc />
    protected override void ConfigureActions()
    {
        PageConfiguration.HeaderActions.AddLink<StopwordCreate>("New stopword list", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<StopwordEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
    }
}

/// <summary>Lists the stopword lists of an experiment's variant B (XP-1).</summary>
public class VariantStopwordListing : StopwordListingBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantStopwordListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of stopword list objects, to check what a delete would remove.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantStopwordListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchStopwordListInfo> provider,
        IExperimentCatalog experiments)
        : base(storageService, provider) =>
        this.experiments = experiments;

    /// <summary>Gets or sets the identifier of the experiment the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override TuningVariant Variant => ExperimentScope.Variant(ExperimentIdentifier);

    /// <summary>
    /// Deletes one variant-B stopword list. Declared here rather than inherited: a page command has to
    /// be a plain method on the final page class (see docs/internal/agent-primer.md).
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

        PageConfiguration.HeaderActions.AddLink<VariantStopwordCreate>(
            "New stopword list",
            parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.AddEditRowAction<VariantStopwordEdit>(parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(DeleteRow), "Delete");
    }
}

/// <summary>Carries the edited live list's identifier in the URL.</summary>
public class StopwordEditSection : EditSectionPage<XpSearchStopwordListInfo>
{
}

/// <summary>Carries the edited variant-B list's identifier in the URL (XP-1).</summary>
public class VariantStopwordEditSection : EditSectionPage<XpSearchStopwordListInfo>
{
}

/// <summary>
/// Edits or creates a stopword list, in the live tuning or in an experiment's variant B (XP-1). The
/// variant is the only difference between the four pages built on this.
/// </summary>
public abstract class StopwordEditPageBase : IndexScopedEditPage<StopwordModel>
{
    /// <summary>Initializes a new instance of the <see cref="StopwordEditPageBase"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    protected StopwordEditPageBase(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        Provider = provider;

    /// <summary>Gets the provider of stopword list objects.</summary>
    protected IInfoProvider<XpSearchStopwordListInfo> Provider { get; }

    /// <summary>Gets the identifier of the edited row, or zero when the page creates one.</summary>
    protected virtual int EditedId => 0;

    /// <summary>Gets the experiment the written row belongs to, or <see langword="null"/> for the live tuning.</summary>
    protected virtual int? ExperimentId => null;

    /// <summary>Gets a value indicating whether the page may still write. A started experiment's B is frozen.</summary>
    protected virtual bool CanWrite => true;

    /// <summary>Gets the stored row being edited, or <see langword="null"/> when the page creates one.</summary>
    protected XpSearchStopwordListInfo? EditedRow => EditedId > 0 ? Provider.Get(EditedId) : null;

    /// <inheritdoc />
    protected override string? EditedIndexName => EditedId > 0 ? EditedRow?.StopwordListIndexName : IndexName;

    /// <inheritdoc />
    protected override StopwordModel CreateModel() =>
        EditedRow is { } row ? StopwordModel.From(row) : new StopwordModel();

    /// <inheritdoc />
    protected override Task<ICommandResponse> ProcessFormData(StopwordModel model, ICollection<IFormItem> formItems)
    {
        if (!CanWrite)
        {
            return Refuse(ExperimentScope.FrozenRefusal);
        }

        return EditedId > 0 && (EditedRow?.StopwordListExperimentID ?? 0) != (ExperimentId ?? 0)
            ? Refuse(ExperimentScope.CrossVariantRefusal)
            : base.ProcessFormData(model, formItems);
    }

    /// <inheritdoc />
    protected override Task<string> PersistAsync(StopwordModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = EditedRow;
        bool creating = row is null;

        row ??= new XpSearchStopwordListInfo { StopwordListGuid = Guid.NewGuid() };

        submitted.ApplyTo(row);
        row.StopwordListExperimentID = ExperimentId;

        Provider.Set(row);

        return Task.FromResult($"Stopwords for '{submitted.IndexName}' {(creating ? "created" : "saved")}.");
    }

    /// <summary>Answers a submit that must not be written.</summary>
    /// <param name="message">Why it was not written.</param>
    /// <returns>The validation failure.</returns>
    protected Task<ICommandResponse> Refuse(string message) =>
        Task.FromResult<ICommandResponse>(
            ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationFailure)).AddErrorMessage(message));
}

/// <summary>Edits the live stopwords of one index.</summary>
public class StopwordEdit : StopwordEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="StopwordEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    public StopwordEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <summary>Gets or sets the identifier of the edited list, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(StopwordEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates the live stopword list of an index.</summary>
public class StopwordCreate : StopwordEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="StopwordCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    public StopwordCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(StopwordListing);
}

/// <summary>
/// The variant-B stopword pages (XP-1): the experiment in the URL, the banner, and the refusal to write
/// once the experiment has started.
/// </summary>
public abstract class VariantStopwordPage : StopwordEditPageBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantStopwordPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    protected VariantStopwordPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider,
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

/// <summary>Edits the stopwords of an experiment's variant B (XP-1).</summary>
public class VariantStopwordEdit : VariantStopwordPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantStopwordEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantStopwordEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <summary>Gets or sets the identifier of the edited list, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(VariantStopwordEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates the stopword list of an experiment's variant B (XP-1).</summary>
public class VariantStopwordCreate : VariantStopwordPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantStopwordCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantStopwordCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(VariantStopwordListing);
}
