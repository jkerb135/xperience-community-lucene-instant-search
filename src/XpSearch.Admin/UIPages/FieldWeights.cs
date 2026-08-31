using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Forms;
using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;
using XpSearch.Core;

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

/// <summary>Lists the field weights, per index (spec §8.1).</summary>
public class FieldWeightListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;

    /// <summary>Initializes a new instance of the <see cref="FieldWeightListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    public FieldWeightListing(ILuceneConfigurationStorageService storageService) => this.storageService = storageService;

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchFieldWeightInfo.OBJECT_TYPE;

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string indexName = IndexScope.Resolve(storageService, IndexIdentifier);

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchFieldWeightInfo.WeightFieldName), "Field", searchable: true)
            .AddColumn(nameof(XpSearchFieldWeightInfo.WeightValue), "Weight");

        PageConfiguration.HeaderActions.AddLink<FieldWeightCreate>("New field weight", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<FieldWeightEdit>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");
        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query.WhereEquals(nameof(XpSearchFieldWeightInfo.WeightIndexName), indexName));

        return base.ConfigurePage();
    }
}

/// <summary>Carries the edited weight's identifier in the URL.</summary>
public class FieldWeightEditSection : EditSectionPage<XpSearchFieldWeightInfo>
{
}

/// <summary>Edits one field weight.</summary>
public class FieldWeightEdit : IndexScopedEditPage<FieldWeightModel>
{
    private readonly IInfoProvider<XpSearchFieldWeightInfo> provider;

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
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <summary>Gets or sets the identifier of the edited weight, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(FieldWeightEditSection))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override string? EditedIndexName => provider.Get(ObjectId)?.WeightIndexName;

    /// <inheritdoc />
    protected override FieldWeightModel CreateModel() =>
        provider.Get(ObjectId) is { } row ? FieldWeightModel.From(row) : new FieldWeightModel();

    /// <inheritdoc />
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();

        foreach (var dropDown in items.OfType<DropDownComponent>()
            .Where(component => string.Equals(component.Name, nameof(FieldWeightModel.FieldName), StringComparison.OrdinalIgnoreCase)))
        {
            dropDown.Properties.Options = WeightFieldConfigurator.WithStoredValue(dropDown.Properties.Options, Model.FieldName);
        }

        return items;
    }

    /// <inheritdoc />
    protected override Task<string> PersistAsync(FieldWeightModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        provider.Set(submitted.ApplyTo(provider.Get(ObjectId) ?? new XpSearchFieldWeightInfo()));

        return Task.FromResult($"Weight for '{submitted.FieldName}' saved.");
    }
}

/// <summary>Creates a field weight.</summary>
public class FieldWeightCreate : IndexScopedEditPage<FieldWeightModel>
{
    private readonly IInfoProvider<XpSearchFieldWeightInfo> provider;

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
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.provider = provider;

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(FieldWeightListing);

    /// <inheritdoc />
    protected override FieldWeightModel CreateModel() => new();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(FieldWeightModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = submitted.ApplyTo(new XpSearchFieldWeightInfo());
        row.WeightGuid = Guid.NewGuid();

        provider.Set(row);

        return Task.FromResult($"Weight for '{submitted.FieldName}' created.");
    }
}
