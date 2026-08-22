using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
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

namespace XpSearch.Admin.UIPages;

/// <summary>The stopword list of one index: an index and a block of words, one per line.</summary>
public class StopwordModel
{
    /// <summary>Gets or sets the code name of the index the list belongs to.</summary>
    [RequiredValidationRule]
    [DropDownComponent(Label = "Index", Order = 1)]
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

/// <summary>Lists the stopword lists, one per index (spec §8.1).</summary>
public class StopwordListing : ListingPage
{
    /// <inheritdoc />
    protected override string ObjectType => XpSearchStopwordListInfo.OBJECT_TYPE;

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchStopwordListInfo.StopwordListIndexName), "Index", searchable: true, sortable: true)
            .AddColumn(nameof(XpSearchStopwordListInfo.StopwordListWords), "Words to ignore");

        PageConfiguration.HeaderActions.AddLink<StopwordCreate>("New stopword list");
        PageConfiguration.AddEditRowAction<StopwordEdit>();
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");

        return base.ConfigurePage();
    }
}

/// <summary>Carries the edited list's identifier in the URL.</summary>
public class StopwordEditSection : EditSectionPage<XpSearchStopwordListInfo>
{
}

/// <summary>Edits the stopwords of one index.</summary>
public class StopwordEdit : TuningEditPage<StopwordModel>
{
    private readonly IInfoProvider<XpSearchStopwordListInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="StopwordEdit"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    public StopwordEdit(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneIndexManager indexManager,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, indexManager, pageLinkGenerator) =>
        this.provider = provider;

    /// <summary>Gets or sets the identifier of the edited list, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder))]
    public int ObjectId { get; set; }

    /// <inheritdoc />
    protected override StopwordModel CreateModel() =>
        provider.Get(ObjectId) is { } row ? StopwordModel.From(row) : new StopwordModel();

    /// <inheritdoc />
    protected override Task<string> PersistAsync(StopwordModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        provider.Set(submitted.ApplyTo(provider.Get(ObjectId) ?? new XpSearchStopwordListInfo()));

        return Task.FromResult($"Stopwords for '{submitted.IndexName}' saved.");
    }
}

/// <summary>Creates the stopword list of an index.</summary>
public class StopwordCreate : TuningEditPage<StopwordModel>
{
    private readonly IInfoProvider<XpSearchStopwordListInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="StopwordCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="provider">Provider of stopword list objects.</param>
    public StopwordCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneIndexManager indexManager,
        IPageLinkGenerator pageLinkGenerator,
        IInfoProvider<XpSearchStopwordListInfo> provider)
        : base(formItemCollectionProvider, formDataBinder, indexManager, pageLinkGenerator) =>
        this.provider = provider;

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(StopwordListing);

    /// <inheritdoc />
    protected override StopwordModel CreateModel() =>
        new() { IndexName = IndexNames().FirstOrDefault() ?? string.Empty };

    /// <inheritdoc />
    protected override Task<string> PersistAsync(StopwordModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var row = submitted.ApplyTo(new XpSearchStopwordListInfo());
        row.StopwordListGuid = Guid.NewGuid();

        provider.Set(row);

        return Task.FromResult($"Stopwords for '{submitted.IndexName}' created.");
    }
}
