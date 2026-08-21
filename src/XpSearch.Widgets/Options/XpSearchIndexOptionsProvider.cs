using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XpSearch.Widgets.Options;

/// <summary>
/// Fills the "Search index" drop-down of every widget with the registered indexes.
/// </summary>
/// <remarks>
/// Data provider pattern per
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/reference-admin-ui-form-components
/// (<c>DropDownComponent.DataProviderType</c>).
/// </remarks>
public sealed class XpSearchIndexOptionsProvider : IDropDownOptionsProvider
{
    private readonly IXpSearchIndexCatalog catalog;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIndexOptionsProvider"/> class.</summary>
    /// <param name="catalog">The index catalog.</param>
    public XpSearchIndexOptionsProvider(IXpSearchIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    /// <inheritdoc />
    public Task<IEnumerable<DropDownOptionItem>> GetOptionItems() =>
        Task.FromResult(catalog.GetIndexNames().Select(name => new DropDownOptionItem { Value = name, Text = name }));
}
