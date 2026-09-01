using Microsoft.Extensions.DependencyInjection.Extensions;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the services the Xperience Search Page Builder widgets need.
/// </summary>
public static class XpSearchWidgetsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mount renderer, the editor-mode seam and the index catalog. Call it next to
    /// <c>AddXpSearch()</c>, which registers the rendering services the widgets use (the result
    /// template registry and the server-rendered first paint); the widgets themselves register
    /// through their <c>RegisterWidget</c> assembly attributes.
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

        return services;
    }
}
