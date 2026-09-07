using Microsoft.Extensions.DependencyInjection;

namespace MyCompany.Search.Widgets;

/// <summary>Registers the dropdown facet widget in the host application.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DropdownFacetTagHelper"/> as the implementation of
    /// <see cref="DropdownFacetOptions"/>. Call it next to <c>AddXpSearchWidgets()</c>: without it the
    /// <c>&lt;my-dropdown-facet /&gt;</c> tag still renders, but the Page Builder widget cannot resolve
    /// its tag helper from the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDropdownFacetWidget(this IServiceCollection services) =>
        services.AddXpSearchWidget<DropdownFacetTagHelper, DropdownFacetOptions>();
}
