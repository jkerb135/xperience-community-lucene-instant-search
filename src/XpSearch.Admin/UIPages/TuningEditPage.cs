using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Core.Indexing;

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The shared plumbing of every editing page in the Search tuning application: a model-based edit
/// page whose index drop-down is filled from the registered Lucene indexes.
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

    private readonly ILuceneIndexManager indexManager;
    private readonly IPageLinkGenerator pageLinkGenerator;
    private TModel? model;

    /// <summary>Initializes a new instance of the <see cref="TuningEditPage{TModel}"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components from the model's annotations.</param>
    /// <param name="formDataBinder">Binds the submitted values back onto the model.</param>
    /// <param name="indexManager">The integration's index registry, used to fill index drop-downs.</param>
    /// <param name="pageLinkGenerator">Generates the URL a create page redirects to.</param>
    protected TuningEditPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneIndexManager indexManager,
        IPageLinkGenerator pageLinkGenerator)
        : base(formItemCollectionProvider, formDataBinder)
    {
        ArgumentNullException.ThrowIfNull(indexManager);
        ArgumentNullException.ThrowIfNull(pageLinkGenerator);

        this.indexManager = indexManager;
        this.pageLinkGenerator = pageLinkGenerator;
    }

    /// <inheritdoc />
    protected override TModel Model => model ??= CreateModel();

    /// <summary>
    /// Gets the page to redirect to after a successful save, or <see langword="null"/> to stay put.
    /// Create pages return their listing so a second submit cannot insert a second row.
    /// </summary>
    protected virtual Type? RedirectTo => null;

    /// <summary>Gets the code names of every registered index, for an index drop-down.</summary>
    /// <returns>The index code names, ordered.</returns>
    protected IReadOnlyList<string> IndexNames() =>
    [
        .. indexManager.GetAllIndices()
            .Select(index => index.IndexName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Builds the initial model: an empty one for a create page, the stored row for an edit page.</summary>
    /// <returns>The model.</returns>
    protected abstract TModel CreateModel();

    /// <summary>Writes the submitted model to the database.</summary>
    /// <param name="submitted">The submitted model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message to show the user.</returns>
    protected abstract Task<string> PersistAsync(TModel submitted, CancellationToken cancellationToken);

    /// <inheritdoc />
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();

        // Post-configuring component properties from data the attribute notation cannot express is
        // the documented use of GetFormItems on model-based edit pages.
        string options = string.Join(
            "\r\n",
            IndexNames().Select(name => $"{name};{name}"));

        foreach (var dropDown in items.OfType<DropDownComponent>())
        {
            if (string.Equals(dropDown.Name, IndexPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                dropDown.Properties.Options = options;
            }
        }

        return items;
    }

    /// <inheritdoc />
    protected override async Task<ICommandResponse> ProcessFormData(TModel model, ICollection<IFormItem> formItems)
    {
        ArgumentNullException.ThrowIfNull(formItems);

        string message = await PersistAsync(model, CancellationToken.None).ConfigureAwait(false);

        if (RedirectTo is { } target)
        {
            return NavigateTo(pageLinkGenerator.GetPath(target));
        }

        return ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationSuccess)
        {
            Items = await formItems.OnlyVisible().GetClientProperties()
        })
        .AddSuccessMessage(message);
    }
}
