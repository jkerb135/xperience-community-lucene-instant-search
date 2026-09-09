using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets.Mounting;

namespace XpSearch.Widgets.Options;

/// <summary>
/// Fills the "Search index" drop-down of every widget with the registered indexes.
/// </summary>
/// <remarks>
/// Data provider pattern per
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/reference-admin-ui-form-components
/// (<c>DropDownComponent.DataProviderType</c>). A <c>DropDownOptionItem</c> is a value and a text and
/// nothing else - it carries no group and no separator - so the indexes that cover the page's own
/// website channel are simply listed first, and the rest keep their name with a suffix (LC-1).
/// </remarks>
public sealed class XpSearchIndexOptionsProvider : IDropDownOptionsProvider
{
    private const string OtherChannelSuffix = " (other channel)";

    private readonly IXpSearchIndexCatalog catalog;
    private readonly IXpSearchPageContext? pageContext;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIndexOptionsProvider"/> class.</summary>
    /// <param name="catalog">The index catalog.</param>
    /// <param name="pageContext">
    /// The channel the page being edited belongs to (LC-1). <see langword="null"/>, or a request with
    /// no channel, lists every index in catalog order, as before.
    /// </param>
    public XpSearchIndexOptionsProvider(IXpSearchIndexCatalog catalog, IXpSearchPageContext? pageContext = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.pageContext = pageContext;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var names = catalog.GetIndexNames();

        if (pageContext is null || pageContext.GetChannel() is null)
        {
            return names.Select(name => new DropDownOptionItem { Value = name, Text = name });
        }

        var covering = new List<DropDownOptionItem>();
        var others = new List<DropDownOptionItem>();

        foreach (string name in names)
        {
            bool covers = await pageContext.CoversCurrentChannelAsync(name, CancellationToken.None).ConfigureAwait(false);

            (covers ? covering : others).Add(new DropDownOptionItem
            {
                Value = name,
                Text = covers ? name : name + OtherChannelSuffix
            });
        }

        return [.. covering, .. others];
    }
}
