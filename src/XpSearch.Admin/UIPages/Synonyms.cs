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
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "synonyms",
    uiPageType: typeof(SynonymListing),
    name: "Synonyms",
    templateName: TemplateNames.LISTING,
    order: 300)]

[assembly: UIPage(
    parentType: typeof(SynonymListing),
    slug: "create",
    uiPageType: typeof(SynonymCreate),
    name: "New synonym",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(SynonymListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(SynonymEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(SynonymEditSection),
    slug: "edit",
    uiPageType: typeof(SynonymEdit),
    name: "Synonym",
    templateName: TemplateNames.EDIT,
    order: 100)]

// The same pages again, inside an experiment: same classes, same templates, variant B's rows (XP-1).
[assembly: UIPage(
    parentType: typeof(ExperimentSection),
    slug: "synonyms",
    uiPageType: typeof(VariantSynonymListing),
    name: "Synonyms",
    templateName: TemplateNames.LISTING,
    order: 300)]

[assembly: UIPage(
    parentType: typeof(VariantSynonymListing),
    slug: "create",
    uiPageType: typeof(VariantSynonymCreate),
    name: "New synonym",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(VariantSynonymListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(VariantSynonymEditSection),
    name: "Edit",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(VariantSynonymEditSection),
    slug: "edit",
    uiPageType: typeof(VariantSynonymEdit),
    name: "Synonym",
    templateName: TemplateNames.EDIT,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>The form behind one synonym group (spec §8.2).</summary>
public class SynonymModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the group applies to. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 1)]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the direction, as the numeric value of <see cref="Core.Tuning.SynonymDirection"/>.</summary>
    [DropDownComponent(
        Label = "Direction",
        Order = 2,
        Options = "0;Two-way - every word finds every other\r\n1;One-way - the words below find the replacements")]
    public string Direction { get; set; } = "0";

    /// <summary>Gets or sets the comma-separated input terms.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Words", Order = 3, Tooltip = "Comma-separated, for example: sofa, couch, settee.")]
    public string Input { get; set; } = string.Empty;

    /// <summary>Gets or sets the comma-separated output terms of a one-way group.</summary>
    [TextInputComponent(Label = "Replacements (one-way only)", Order = 4, Tooltip = "Comma-separated. Leave empty for a two-way group.")]
    public string Output { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the group is applied.</summary>
    [CheckBoxComponent(Label = "Enabled", Order = 5)]
    public bool Enabled { get; set; } = true;

    /// <summary>Parses a drop-down option back into the numeric enum value it stands for.</summary>
    /// <param name="value">The selected option.</param>
    /// <returns>The numeric value, or zero when nothing was selected.</returns>
    public static int ParseOption(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;

    /// <summary>Copies the model onto a stored row.</summary>
    /// <param name="row">The row to fill.</param>
    /// <returns>The same row.</returns>
    public XpSearchSynonymInfo ApplyTo(XpSearchSynonymInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.SynonymIndexName = IndexName;
        row.SynonymType = ParseOption(Direction);
        row.SynonymInput = Input;
        row.SynonymOutput = Output;
        row.SynonymEnabled = Enabled;

        return row;
    }

    /// <summary>Reads a stored row back into a model.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The model.</returns>
    public static SynonymModel From(XpSearchSynonymInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SynonymModel
        {
            IndexName = row.SynonymIndexName,
            Direction = row.SynonymType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Input = row.SynonymInput,
            Output = row.SynonymOutput,
            Enabled = row.SynonymEnabled
        };
    }
}

/// <summary>
/// Lists the synonym groups of one index and one tuning variant (spec §8.1, XP-1). The live listing and
/// an experiment's variant-B listing differ only in the variant they read and where their actions point.
/// </summary>
public abstract class SynonymListingBase : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="SynonymListingBase"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of synonym objects, to check what a delete would remove.</param>
    protected SynonymListingBase(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchSynonymInfo> provider)
    {
        this.storageService = storageService;
        Provider = provider;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchSynonymInfo.OBJECT_TYPE;

    /// <summary>Gets the provider of synonym objects.</summary>
    protected IInfoProvider<XpSearchSynonymInfo> Provider { get; }

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
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymInput), "Words", searchable: true)
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymOutput), "Replacements")
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymEnabled), "Enabled");

        ConfigureActions();

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchSynonymInfo.SynonymIndexName), index)
                .Where(VariantScope.Condition(nameof(XpSearchSynonymInfo.SynonymExperimentID), variant)));

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

        if (!IndexScope.Matches(row?.SynonymIndexName, IndexName))
        {
            return Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
        }

        return (row?.SynonymExperimentID ?? 0) == Variant.ExperimentId
            ? base.Delete(id)
            : Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(ExperimentScope.CrossVariantDeleteRefusal));
    }
}

/// <summary>Lists the live synonym groups of one index (spec §8.1).</summary>
public class SynonymListing : SynonymListingBase
{
    private readonly IInfoProvider<XpSearchSynonymSuggestionInfo> suggestions;

    /// <summary>Initializes a new instance of the <see cref="SynonymListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of synonym objects, to check what a delete would remove.</param>
    /// <param name="suggestions">Provider of mined synonym candidates, counted for the banner that points at them (SY-1).</param>
    public SynonymListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchSynonymInfo> provider,
        IInfoProvider<XpSearchSynonymSuggestionInfo> suggestions)
        : base(storageService, provider) =>
        this.suggestions = suggestions;

    /// <summary>Deletes one live synonym group.</summary>
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
                    Headline = pending == 1 ? "1 suggested synonym is waiting" : $"{pending} suggested synonyms are waiting",
                    Content = "The popularity task found searches that got no click, followed by a different search that did. "
                        + "Open Synonym suggestions in the menu to approve or dismiss them; nothing is applied until you do.",
                    ContentAsHtml = false,
                }
            ];
        }

        PageConfiguration.HeaderActions.AddLink<SynonymCreate>("New synonym", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.HeaderActions.AddLink<SynonymSuggestionListing>("Suggestions", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<SynonymEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
    }

    private int PendingSuggestions() =>
        suggestions.Get()
            .WhereEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionIndexName), IndexName)
            .WhereEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionState), (int)PopularitySuggestionState.Pending)
            .Count;
}

/// <summary>Lists the synonym groups of an experiment's variant B (XP-1).</summary>
public class VariantSynonymListing : SynonymListingBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantSynonymListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of synonym objects, to check what a delete would remove.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantSynonymListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchSynonymInfo> provider,
        IExperimentCatalog experiments)
        : base(storageService, provider) =>
        this.experiments = experiments;

    /// <summary>Gets or sets the identifier of the experiment the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <inheritdoc />
    protected override TuningVariant Variant => ExperimentScope.Variant(ExperimentIdentifier);

    /// <summary>
    /// Deletes one variant-B synonym group. Declared here rather than inherited: a page command has to
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

        // Once the experiment runs, its variant B is what half the visitors are being served: changing
        // it would rewrite the test under its own results, so the listing is read-only from then on.
        if (!ExperimentScope.IsDraft(experiment))
        {
            return;
        }

        PageConfiguration.HeaderActions.AddLink<VariantSynonymCreate>(
            "New synonym",
            parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.AddEditRowAction<VariantSynonymEdit>(parameters: ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(DeleteRow), "Delete");
    }
}

/// <summary>Carries the edited live synonym's identifier in the URL.</summary>
public class SynonymEditSection : EditSectionPage<XpSearchSynonymInfo>
{
}

/// <summary>Carries the edited variant-B synonym's identifier in the URL (XP-1).</summary>
public class VariantSynonymEditSection : EditSectionPage<XpSearchSynonymInfo>
{
}

/// <summary>
/// Edits or creates one synonym group, in the live tuning or in an experiment's variant B (XP-1). The
/// variant is the only difference between the four pages built on this.
/// </summary>
public abstract class SynonymEditPageBase : IndexScopedEditPage<SynonymModel>
{
    /// <summary>Initializes a new instance of the <see cref="SynonymEditPageBase"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    protected SynonymEditPageBase(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        Provider = provider;

    /// <summary>Gets the provider of synonym objects.</summary>
    protected IInfoProvider<XpSearchSynonymInfo> Provider { get; }

    /// <summary>Gets the identifier of the edited row, or zero when the page creates one.</summary>
    protected virtual int EditedId => 0;

    /// <summary>Gets the experiment the written row belongs to, or <see langword="null"/> for the live tuning.</summary>
    protected virtual int? ExperimentId => null;

    /// <summary>Gets a value indicating whether the page may still write. A started experiment's B is frozen.</summary>
    protected virtual bool CanWrite => true;

    /// <summary>Gets the stored row being edited, or <see langword="null"/> when the page creates one.</summary>
    protected XpSearchSynonymInfo? EditedRow => EditedId > 0 ? Provider.Get(EditedId) : null;

    /// <inheritdoc />
    protected override string? EditedIndexName => EditedId > 0 ? EditedRow?.SynonymIndexName : IndexName;

    /// <inheritdoc />
    protected override SynonymModel CreateModel() =>
        EditedRow is { } row ? SynonymModel.From(row) : new SynonymModel();

    /// <inheritdoc />
    protected override Task<ICommandResponse> ProcessFormData(SynonymModel model, ICollection<IFormItem> formItems)
    {
        if (!CanWrite)
        {
            return Refuse(ExperimentScope.FrozenRefusal);
        }

        // A row reached through the other variant's URL is refused rather than silently re-homed: saving
        // a draft row through the live editor would promote it into the live tuning.
        return EditedId > 0 && (EditedRow?.SynonymExperimentID ?? 0) != (ExperimentId ?? 0)
            ? Refuse(ExperimentScope.CrossVariantRefusal)
            : base.ProcessFormData(model, formItems);
    }

    /// <inheritdoc />
    protected override Task<string> PersistAsync(SynonymModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = EditedRow;
        bool creating = row is null;

        row ??= new XpSearchSynonymInfo { SynonymGuid = Guid.NewGuid() };

        submitted.ApplyTo(row);
        row.SynonymExperimentID = ExperimentId;

        Provider.Set(row);

        return Task.FromResult(creating ? "Synonym created." : "Synonym saved.");
    }

    /// <summary>Answers a submit that must not be written.</summary>
    /// <param name="message">Why it was not written.</param>
    /// <returns>The validation failure.</returns>
    protected Task<ICommandResponse> Refuse(string message) =>
        Task.FromResult<ICommandResponse>(
            ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationFailure)).AddErrorMessage(message));
}

/// <summary>Edits one live synonym group.</summary>
public class SynonymEdit : SynonymEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="SynonymEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    public SynonymEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <summary>Gets or sets the identifier of the edited group, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(SynonymEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates a live synonym group.</summary>
public class SynonymCreate : SynonymEditPageBase
{
    /// <summary>Initializes a new instance of the <see cref="SynonymCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    public SynonymCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(SynonymListing);
}

/// <summary>
/// The variant-B synonym pages (XP-1): the experiment in the URL, the banner, and the refusal to write
/// once the experiment has started.
/// </summary>
public abstract class VariantSynonymPage : SynonymEditPageBase
{
    private readonly IExperimentCatalog experiments;

    /// <summary>Initializes a new instance of the <see cref="VariantSynonymPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    protected VariantSynonymPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider,
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

/// <summary>Edits one synonym group of an experiment's variant B (XP-1).</summary>
public class VariantSynonymEdit : VariantSynonymPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantSynonymEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantSynonymEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <summary>Gets or sets the identifier of the edited group, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(VariantSynonymEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override int EditedId => ObjectId;
}

/// <summary>Creates a synonym group in an experiment's variant B (XP-1).</summary>
public class VariantSynonymCreate : VariantSynonymPage
{
    /// <summary>Initializes a new instance of the <see cref="VariantSynonymCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of synonym objects.</param>
    /// <param name="experiments">Reads the experiment, for the banner and the draft check.</param>
    public VariantSynonymCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchSynonymInfo> provider,
        IExperimentCatalog experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator, provider, experiments)
    {
    }

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(VariantSynonymListing);
}
