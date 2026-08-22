using Microsoft.Extensions.DependencyInjection.Extensions;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Core.Tuning;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Search tuning application (spec §8, §10.8).
/// </summary>
public static class XpSearchAdminServiceCollectionExtensions
{
    /// <summary>Registers the relevance tuning storage and the database-backed tuning source.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Call order: <c>AddKenticoLucene</c>, <c>AddXpSearch</c>, <c>AddXpSearchIngestion</c>, then this.
    /// The tuning source is replaced rather than added, because <c>AddXpSearch</c> has already
    /// registered the empty one that lets Core run without this package (spec §2.2). The admin pages
    /// themselves need no registration - they are discovered from the <c>UIPage</c> assembly
    /// attributes
    /// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
    /// </remarks>
    public static IServiceCollection AddXpSearchAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<XpSearchTuningModuleInstaller>();
        services.Replace(ServiceDescriptor.Singleton<IRelevanceTuningSource, InfoRelevanceTuningSource>());

        return services;
    }
}
