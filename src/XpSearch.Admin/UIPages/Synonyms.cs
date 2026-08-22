using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;

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

    /// <summary>Copies the model onto a stored row.</summary>
    /// <param name="row">The row to fill.</param>
    /// <returns>The same row.</returns>
    public XpSearchSynonymInfo ApplyTo(XpSearchSynonymInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.SynonymIndexName = IndexName;
        row.SynonymType = RuleModel.ParseOption(Direction);
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

/// <summary>Lists the synonym groups (spec §8.1).</summary>
public class SynonymListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;

    /// <summary>Initializes a new instance of the <see cref="SynonymListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    public SynonymListing(ILuceneConfigurationStorageService storageService) => this.storageService = storageService;

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexEditPage))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchSynonymInfo.OBJECT_TYPE;

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string indexName = IndexScope.Resolve(storageService, IndexIdentifier);

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymInput), "Words", searchable: true)
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymOutput), "Replacements")
            .AddColumn(nameof(XpSearchSynonymInfo.SynonymEnabled), "Enabled");

        PageConfiguration.HeaderActions.AddLink<SynonymCreate>("New synonym", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<SynonymEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query.WhereEquals(nameof(XpSearchSynonymInfo.SynonymIndexName), indexName));

        return base.ConfigurePage();
    }
}

/// <summary>Carries the edited synonym's identifier in the URL.</summary>
public class SynonymEditSection : EditSectionPage<XpSearchSynonymInfo>
{
}

/// <summary>Edits one synonym group.</summary>
public class SynonymEdit : IndexScopedEditPage<SynonymModel>
{
    private readonly IInfoProvider<XpSearchSynonymInfo> provider;

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
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <summary>Gets or sets the identifier of the edited group, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(SynonymEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override string? EditedIndexName => provider.Get(ObjectId)?.SynonymIndexName;

    /// <inheritdoc />
    protected override SynonymModel CreateModel() =>
        provider.Get(ObjectId) is { } row ? SynonymModel.From(row) : new SynonymModel();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(SynonymModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        provider.Set(submitted.ApplyTo(provider.Get(ObjectId) ?? new XpSearchSynonymInfo()));

        return Task.FromResult("Synonym saved.");
    }
}

/// <summary>Creates a synonym group.</summary>
public class SynonymCreate : IndexScopedEditPage<SynonymModel>
{
    private readonly IInfoProvider<XpSearchSynonymInfo> provider;

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
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(SynonymListing);

    /// <inheritdoc />
    protected override SynonymModel CreateModel() => new();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(SynonymModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = submitted.ApplyTo(new XpSearchSynonymInfo());
        row.SynonymGuid = Guid.NewGuid();

        provider.Set(row);

        return Task.FromResult("Synonym created.");
    }
}
