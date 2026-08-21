using Microsoft.Extensions.DependencyInjection.Extensions;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Templates;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the services the Xperience Search Page Builder widgets need.
/// </summary>
public static class XpSearchWidgetsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mount renderer, the editor-mode seam, the index catalog and the result template
    /// registry. Call it next to <c>AddXpSearch()</c>; the widgets themselves register through their
    /// <c>RegisterWidget</c> assembly attributes.
    /// </summary>
    /// <param name="services">The service collection. <c>AddXpSearch</c> must also be called on it.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearchWidgets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddSingleton<IXpSearchMountRenderer, XpSearchMountRenderer>();
        services.TryAddSingleton<IXpSearchEditorContext, KenticoEditorContext>();
        services.TryAddSingleton<IXpSearchIndexCatalog, LuceneIndexCatalog>();
        services.TryAddSingleton<ISearchResultTemplateRegistry, SearchResultTemplateRegistry>();

        return services;
    }
}
