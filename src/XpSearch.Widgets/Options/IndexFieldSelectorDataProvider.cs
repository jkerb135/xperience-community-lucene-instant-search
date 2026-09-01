using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;

using XpSearch.Core.Abstractions;

namespace XpSearch.Widgets.Options;

/// <summary>
/// Fills the Results widget's field selectors from the real schema of the registered indexes, so an
/// editor picks a stored field instead of typing a field name (spec §7.4).
/// </summary>
/// <remarks>
/// A general selector's data provider is resolved from the service container and, unlike a form
/// component configurator, never sees the other values of the dialog - so the options are the union
/// of the retrievable fields of every registered index rather than only those of the index the
/// widget selected. With one index, the usual case, the two are the same list; see
/// <c>docs/internal/KNOWN-LIMITATIONS.md</c>.
/// </remarks>
public class IndexFieldSelectorDataProvider : IGeneralSelectorDataProvider
{
    private readonly IXpSearchIndexCatalog catalog;
    private readonly IIndexSchemaProvider? schemas;

    /// <summary>Initializes a new instance of the <see cref="IndexFieldSelectorDataProvider"/> class.</summary>
    /// <param name="catalog">The registered indexes.</param>
    /// <param name="services">
    /// Supplies <see cref="IIndexSchemaProvider"/>, which is resolved rather than injected because a
    /// host that registered the widgets but not <c>AddXpSearch()</c> has none: the selector is then
    /// empty instead of the dialog being broken.
    /// </param>
    public IndexFieldSelectorDataProvider(IXpSearchIndexCatalog catalog, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(services);

        this.catalog = catalog;
        schemas = services.GetService(typeof(IIndexSchemaProvider)) as IIndexSchemaProvider;
    }

    /// <inheritdoc />
    public async Task<PagedSelectListItems<string>> GetItemsAsync(string searchTerm, int pageIndex, CancellationToken cancellationToken)
    {
        var names = await FieldNamesAsync(cancellationToken).ConfigureAwait(false);

        return new PagedSelectListItems<string>
        {
            // An index has tens of fields, not thousands: the whole list fits one page.
            NextPageAvailable = false,
            Items = names
                .Where(name => string.IsNullOrEmpty(searchTerm)
                    || name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Select(Item)
                .ToList()
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Selected values are echoed back as they are, so a field an older widget stored - one typed by
    /// hand, or belonging to an index that has since changed - keeps its value instead of being
    /// dropped as invalid.
    /// </remarks>
    public Task<IEnumerable<ObjectSelectorListItem<string>>> GetSelectedItemsAsync(
        IEnumerable<string> selectedValues,
        CancellationToken cancellationToken) =>
        Task.FromResult(selectedValues?.Select(Item) ?? []);

    /// <summary>Which fields of a schema the selector offers. The default is the stored ones.</summary>
    /// <param name="field">The schema field.</param>
    /// <returns><see langword="true"/> when the field may be selected.</returns>
    protected virtual bool Include(SchemaField field) => field?.Retrievable == true;

    private static ObjectSelectorListItem<string> Item(string name) =>
        new() { Value = name, Text = name, IsValid = true };

    private async Task<IReadOnlyList<string>> FieldNamesAsync(CancellationToken cancellationToken)
    {
        if (schemas is null)
        {
            return [];
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string index in catalog.GetIndexNames())
        {
            IndexSchema schema;
            try
            {
                schema = await schemas.GetSchemaAsync(index, cancellationToken).ConfigureAwait(false);
            }
            catch (IndexNotFoundException)
            {
                continue;
            }

            names.AddRange(schema.Fields.Where(Include).Select(field => field.Name).Where(seen.Add));
        }

        return names;
    }
}
