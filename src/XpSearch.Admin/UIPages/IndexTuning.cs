using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Forms;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;

using IFormItemCollectionProvider = Kentico.Xperience.Admin.Base.Forms.Internal.IFormItemCollectionProvider;

[assembly: UIPage(
    parentType: typeof(IndexEditPage),
    slug: "tuning",
    uiPageType: typeof(IndexTuningSection),
    name: "Tuning",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "settings",
    uiPageType: typeof(IndexSettingsPage),
    name: "Settings",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: PageExtender(typeof(IndexListingTuningExtender))]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The per-index tuning section: everything that belongs to one search index, reached by clicking
/// the index in <c>Lucene Search</c> → <c>indexes</c>. See docs/adr/0017-index-scoped-admin.md.
/// </summary>
/// <remarks>
/// Hung under the Lucene integration's <see cref="IndexEditPage"/>, whose parameterized URL slug
/// already carries the index identifier, so this page only contributes the static <c>tuning</c>
/// segment and its children form the left navigation
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
/// <see cref="SecondaryMenuSectionPage"/> already opts itself out of the parent's navigation.
/// </remarks>
public class IndexTuningSection : SecondaryMenuSectionPage
{
}

/// <summary>
/// Resolution and validation of the index a tuning page is scoped to.
/// </summary>
public static class IndexScope
{
    /// <summary>Turns the index identifier carried by the URL into the index code name the tuning tables key on.</summary>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="indexIdentifier">The identifier from the URL.</param>
    /// <returns>The index code name, or an empty string when no such index is registered.</returns>
    public static string Resolve(ILuceneIndexManager indexManager, int indexIdentifier) =>
        indexManager is null ? string.Empty : indexManager.GetIndex(indexIdentifier)?.IndexName ?? string.Empty;

    /// <summary>Builds the URL parameter values every link to a page inside the section needs.</summary>
    /// <param name="indexIdentifier">The identifier from the URL.</param>
    /// <returns>The parameter values, keyed by the page that contributes the parameterized slug.</returns>
    public static PageParameterValues Route(int indexIdentifier) =>
        new() { { typeof(IndexEditPage), indexIdentifier } };

    /// <summary>
    /// Tells whether a stored row belongs to the index in the URL. A row reached through another
    /// index's URL must be rejected rather than silently re-homed.
    /// </summary>
    /// <param name="storedIndexName">The index code name on the stored row.</param>
    /// <param name="routeIndexName">The index code name the URL resolves to.</param>
    /// <returns><see langword="true"/> when the row may be edited through this URL.</returns>
    public static bool Matches(string? storedIndexName, string? routeIndexName) =>
        !string.IsNullOrEmpty(routeIndexName)
        && string.Equals(storedIndexName ?? string.Empty, routeIndexName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Makes a click on a row of the Lucene integration's index listing open this package's tuning
/// section instead of the integration's bare edit form.
/// </summary>
/// <remarks>
/// Extenders run after the extended page's own <c>ConfigurePage</c>, and
/// <c>ListingConfiguration.RowAction</c> is a single writable value, so this overwrites the
/// integration's <c>AddEditRowAction&lt;IndexEditPage&gt;()</c>
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-extenders).
/// The integration's form itself stays reachable, as <see cref="IndexSettingsPage"/> inside the section.
/// </remarks>
public class IndexListingTuningExtender : PageExtender<IndexListingPage>
{
    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        Page.PageConfiguration.AddEditRowAction<IndexTuningSection>();

        return base.ConfigurePage();
    }
}

/// <summary>
/// The Lucene integration's index configuration form, reachable from the tuning sidebar.
/// </summary>
/// <remarks>
/// A copy of <see cref="IndexEditPage"/>'s model and submit logic, because the integration exposes
/// <see cref="BaseIndexEditPage"/> but not its concrete page for re-parenting. See the
/// docs/internal/KNOWN-LIMITATIONS.md entry.
/// </remarks>
[UIEvaluatePermission(SystemPermissions.UPDATE)]
public class IndexSettingsPage : BaseIndexEditPage
{
    private LuceneConfigurationModel? model;

    /// <summary>Initializes a new instance of the <see cref="IndexSettingsPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads and writes the stored index configuration.</param>
    /// <param name="indexManager">The integration's index registry.</param>
    public IndexSettingsPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        ILuceneIndexManager indexManager)
        : base(formItemCollectionProvider, formDataBinder, storageService, indexManager)
    {
    }

    /// <summary>Gets or sets the identifier of the index, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexEditPage))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override LuceneConfigurationModel Model =>
        model ??= new LuceneConfigurationModel(
            StorageService.GetIndexDataOrNullAsync(IndexIdentifier).GetAwaiter().GetResult() ?? new());

    /// <inheritdoc />
    protected override async Task<ICommandResponse> ProcessFormData(LuceneConfigurationModel model, ICollection<IFormItem> formItems)
    {
        var result = await ValidateAndProcess(model).ConfigureAwait(false);

        var response = ResponseFrom(new FormSubmissionResult(
            result == IndexModificationResult.Success
                ? FormSubmissionStatus.ValidationSuccess
                : FormSubmissionStatus.ValidationFailure));

        return result == IndexModificationResult.Success
            ? response.AddSuccessMessage("Index saved.")
            : response.AddErrorMessage("Could not save the index.");
    }
}
