using Microsoft.Extensions.DependencyInjection.Extensions;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the services the Xperience Search widgets need.
/// </summary>
public static class XpSearchWidgetsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mount renderer, the editor-mode seam, the index catalog and the tag helper of
    /// every shipped widget. Call it next to <c>AddXpSearch()</c>, which registers the rendering
    /// services the widgets use (the result template registry and the server-rendered first paint);
    /// the Page Builder widgets themselves register through their <c>RegisterWidget</c> assembly
    /// attributes.
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

        return services
            .AddXpSearchWidget<SearchBoxTagHelper, SearchBoxOptions>()
            .AddXpSearchWidget<ResultsTagHelper, ResultsOptions>()
            .AddXpSearchWidget<FacetListTagHelper, FacetListOptions>()
            .AddXpSearchWidget<CategoryTreeTagHelper, CategoryTreeOptions>()
            .AddXpSearchWidget<RangeFilterTagHelper, RangeFilterOptions>()
            .AddXpSearchWidget<SortSelectTagHelper, SortSelectOptions>()
            .AddXpSearchWidget<PaginationTagHelper, PaginationOptions>()
            .AddXpSearchWidget<ResultStatsTagHelper, ResultStatsOptions>()
            .AddXpSearchWidget<ActiveFiltersTagHelper, ActiveFiltersOptions>()
            .AddXpSearchWidget<ClearFiltersTagHelper, ClearFiltersOptions>()
            .AddXpSearchWidget<ToggleFilterTagHelper, ToggleFilterOptions>()
            .AddXpSearchWidget<LoadMoreTagHelper, LoadMoreOptions>()
            .AddXpSearchWidget<FilterSortTagHelper, FilterSortOptions>()
            .AddXpSearchWidget<SuggestionsTagHelper, SuggestionsOptions>()
            .AddXpSearchWidget<XpSearchWidgetTagHelper, WidgetOptions>();
    }

    /// <summary>
    /// Registers one widget's tag helper, so the Page Builder layer and <c>Html.XpSearchAsync</c> can
    /// resolve it by its options type. A third-party widget calls this for its own pair.
    /// </summary>
    /// <typeparam name="TTagHelper">The tag helper class.</typeparam>
    /// <typeparam name="TOptions">Its options record.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearchWidget<TTagHelper, TOptions>(this IServiceCollection services)
        where TTagHelper : XpSearchMountTagHelper<TOptions>
        where TOptions : XpSearchMountOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        // Transient: a tag helper carries the state of one render (the resolved index, the first paint).
        services.TryAddTransient<TTagHelper>();
        services.TryAddTransient<XpSearchMountTagHelper<TOptions>>(provider => provider.GetRequiredService<TTagHelper>());

        return services;
    }
}
