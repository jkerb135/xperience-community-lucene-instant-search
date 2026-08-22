using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

namespace XpSearch.Admin.UIPages;

/// <summary>A form model whose row belongs to exactly one search index.</summary>
public interface IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the row belongs to.</summary>
    string IndexName { get; set; }
}

/// <summary>
/// The shared plumbing of every model-based editing page in this package: one cached model, one
/// persist step and an optional redirect after a successful submit.
/// </summary>
/// <typeparam name="TModel">The form model.</typeparam>
/// <remarks>
/// Model-based pages
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-pages-with-forms)
/// are the route a code-only package can take: an <c>InfoEditPage</c> renders a UI form, and UI forms
/// are authored in the Modules application, not in code. See ADR-0014.
/// </remarks>
public abstract class TuningEditPage<TModel> : ModelEditPage<TModel>
    where TModel : class, new()
{
    /// <summary>Name of the model property that carries the index code name.</summary>
    protected const string IndexPropertyName = "IndexName";

    private readonly IPageLinkGenerator pageLinkGenerator;
    private TModel? model;

    /// <summary>Initializes a new instance of the <see cref="TuningEditPage{TModel}"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components from the model's annotations.</param>
    /// <param name="formDataBinder">Binds the submitted values back onto the model.</param>
    /// <param name="pageLinkGenerator">Generates the URL a create page redirects to.</param>
    protected TuningEditPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        IPageLinkGenerator pageLinkGenerator)
        : base(formItemCollectionProvider, formDataBinder)
    {
        ArgumentNullException.ThrowIfNull(pageLinkGenerator);

        this.pageLinkGenerator = pageLinkGenerator;
    }

    /// <inheritdoc />
    protected override TModel Model => model ??= Prepare(CreateModel());

    /// <summary>
    /// Gets the page to redirect to after a successful save, or <see langword="null"/> to stay put.
    /// Create pages return their listing so a second submit cannot insert a second row.
    /// </summary>
    protected virtual Type? RedirectTo => null;

    /// <summary>Gets the URL parameter values <see cref="RedirectTo"/> needs, or <see langword="null"/> when it has none.</summary>
    protected virtual PageParameterValues? RedirectParameters => null;

    /// <summary>Builds the initial model: an empty one for a create page, the stored row for an edit page.</summary>
    /// <returns>The model.</returns>
    protected abstract TModel CreateModel();

    /// <summary>Last chance to adjust a freshly built model before the form renders it.</summary>
    /// <param name="created">The model <see cref="CreateModel"/> produced.</param>
    /// <returns>The model to render.</returns>
    protected virtual TModel Prepare(TModel created) => created;

    /// <summary>Writes the submitted model to the database.</summary>
    /// <param name="submitted">The submitted model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message to show the user.</returns>
    protected abstract Task<string> PersistAsync(TModel submitted, CancellationToken cancellationToken);

    /// <inheritdoc />
    protected override async Task<ICommandResponse> ProcessFormData(TModel model, ICollection<IFormItem> formItems)
    {
        ArgumentNullException.ThrowIfNull(formItems);

        string message = await PersistAsync(model, CancellationToken.None).ConfigureAwait(false);

        if (RedirectTo is { } target)
        {
            return NavigateTo(pageLinkGenerator.GetPath(target, RedirectParameters));
        }

        return ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationSuccess)
        {
            Items = await formItems.OnlyVisible().GetClientProperties()
        })
        .AddSuccessMessage(message);
    }
}

/// <summary>
/// An editing page inside <see cref="IndexTuningSection"/>: the index is not a choice, it is the one
/// the URL points at. See docs/adr/0017-index-scoped-admin.md.
/// </summary>
/// <typeparam name="TModel">The form model.</typeparam>
public abstract class IndexScopedEditPage<TModel> : TuningEditPage<TModel>
    where TModel : class, IIndexScopedModel, new()
{
    private readonly ILuceneConfigurationStorageService storageService;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="IndexScopedEditPage{TModel}"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components from the model's annotations.</param>
    /// <param name="formDataBinder">Binds the submitted values back onto the model.</param>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="pageLinkGenerator">Generates the URL a create page redirects to.</param>
    protected IndexScopedEditPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator)
        : base(formItemCollectionProvider, formDataBinder, pageLinkGenerator)
    {
        ArgumentNullException.ThrowIfNull(storageService);

        this.storageService = storageService;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    protected override PageParameterValues? RedirectParameters => IndexScope.Route(IndexIdentifier);

    /// <summary>
    /// Gets the index of the row being edited, or <see langword="null"/> when the page creates a new
    /// row. A row whose index differs from the URL's is refused on submit.
    /// </summary>
    protected virtual string? EditedIndexName => IndexName;

    /// <inheritdoc />
    protected override TModel Prepare(TModel created)
    {
        ArgumentNullException.ThrowIfNull(created);

        created.IndexName = IndexName;

        return created;
    }

    /// <inheritdoc />
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();

        // The index comes from the URL, so it is shown rather than chosen. Post-configuring component
        // properties the attribute notation cannot express is the documented use of GetFormItems.
        foreach (var text in items.OfType<TextInputComponent>()
            .Where(component => string.Equals(component.Name, IndexPropertyName, StringComparison.OrdinalIgnoreCase)))
        {
            text.Properties.EditMode = FormEditMode.ReadOnly;
        }

        return items;
    }

    /// <inheritdoc />
    protected override Task<ICommandResponse> ProcessFormData(TModel model, ICollection<IFormItem> formItems)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!IndexScope.Matches(EditedIndexName, IndexName))
        {
            return Task.FromResult<ICommandResponse>(
                ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationFailure))
                    .AddErrorMessage("This record belongs to a different search index and was not saved."));
        }

        // The index field is read-only, so never trust what came back from the client for it.
        model.IndexName = IndexName;

        return base.ProcessFormData(model, formItems);
    }
}
