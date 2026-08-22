using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Caching;
using XpSearch.Core.Endpoints;
using XpSearch.Core.Facets;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Search;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Xperience Search API.
/// </summary>
public static class XpSearchServiceCollectionExtensions
{
    /// <summary>Registers the search API with its defaults.</summary>
    /// <param name="services">The service collection. <c>AddKenticoLucene</c> must already have been called on it.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearch(this IServiceCollection services) =>
        services.AddXpSearch(_ => { }, _ => { });

    /// <summary>Registers the search API.</summary>
    /// <param name="services">The service collection. <c>AddKenticoLucene</c> must already have been called on it.</param>
    /// <param name="configure">Configures the API options.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearch(this IServiceCollection services, Action<XpSearchOptions> configure) =>
        services.AddXpSearch(configure, _ => { });

    /// <summary>Registers the search API and the per-field indexing overrides.</summary>
    /// <param name="services">The service collection. <c>AddKenticoLucene</c> must already have been called on it.</param>
    /// <param name="configure">Configures the API options.</param>
    /// <param name="configureIndexing">Configures how content type fields are auto-detected.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearch(
        this IServiceCollection services,
        Action<XpSearchOptions> configure,
        Action<XpSearchIndexingOptions> configureIndexing)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(configureIndexing);

        services.Configure(configure);

        var indexingOptions = new XpSearchIndexingOptions();
        configureIndexing(indexingOptions);
        services.TryAddSingleton(indexingOptions);

        services.TryAddSingleton<ILuceneIndexAccessor, LuceneIndexAccessor>();
        services.TryAddSingleton<IDataClassDefinitionSource, DataClassInfoDefinitionSource>();
        services.TryAddSingleton<IIndexContentTypeSource, LuceneIndexContentTypeSource>();
        services.TryAddSingleton<IContentTypeFieldSource, FormInfoContentTypeFieldSource>();
        services.TryAddSingleton<IIndexSchemaProvider, IndexSchemaProvider>();
        services.TryAddSingleton<IFacetProvider, TaxonomyFacetProvider>();
        services.TryAddSingleton<IHighlighter, LuceneHighlighter>();
        services.TryAddSingleton<ISearchCache, ProgressiveSearchCache>();
        services.TryAddSingleton<ISuggestService, DocumentSuggestService>();
        services.TryAddSingleton<ISearchEventSink, LoggingSearchEventSink>();
        services.TryAddSingleton<XpSearchIndexingStrategy>();

        services.AddXpSearchStage<NormalizeRequestStage>();
        services.AddXpSearchStage<BuildQueryStage>();
        services.AddXpSearchStage<FacetFilterStage>();
        services.AddXpSearchStage<NumericFilterStage>();
        services.AddXpSearchStage<ExecuteSearchStage>();
        services.AddXpSearchStage<CollectFacetsStage>();
        services.AddXpSearchStage<HighlightStage>();
        services.AddXpSearchStage<ProjectResponseStage>();

        services.TryAddSingleton<SearchPipeline>();
        services.TryAddSingleton<ISearchPipeline>(provider => new CachedSearchPipeline(
            provider.GetRequiredService<SearchPipeline>(),
            provider.GetRequiredService<ISearchCache>(),
            provider.GetRequiredService<Options.IOptions<XpSearchOptions>>()));

        services.DecorateLuceneClient<CacheEvictingLuceneClient>(
            (provider, inner) => new CacheEvictingLuceneClient(
                inner,
                provider.GetRequiredService<ISearchCache>(),
                provider.GetRequiredService<ILuceneIndexAccessor>()));

        return services;
    }

    /// <summary>Adds a custom pipeline stage. Its own <see cref="ISearchStage.Order"/> decides where it runs.</summary>
    /// <typeparam name="TStage">The stage type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearchStage<TStage>(this IServiceCollection services)
        where TStage : class, ISearchStage
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISearchStage, TStage>());

        return services;
    }

    /// <summary>Adds a custom pipeline stage at an explicit position, ignoring the type's own order.</summary>
    /// <typeparam name="TStage">The stage type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="order">Where the stage runs; see <see cref="SearchStageOrder"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearchStage<TStage>(this IServiceCollection services, int order)
        where TStage : class, ISearchStage
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISearchStage>(provider =>
            new OrderedStage(ActivatorUtilities.CreateInstance<TStage>(provider), order));

        return services;
    }

    /// <summary>
    /// Puts a decorator in front of whatever <see cref="ILuceneClient"/> is already registered, so this
    /// library can observe the integration's index writes
    /// (https://docs.kentico.com/documentation/developers-and-admins/customization/decorate-system-services).
    /// </summary>
    /// <typeparam name="TDecorator">The decorator type, which also identifies the decoration: applying the same one twice is a no-op.</typeparam>
    /// <param name="services">The service collection. <c>AddKenticoLucene</c> must already have been called on it.</param>
    /// <param name="decorate">Builds the decorator from the container and the previously registered client.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The previous descriptor is captured from the collection instead of resolved from the container,
    /// so the decoration behaves the same whichever container the host uses, and decorations stack in
    /// registration order.
    /// </remarks>
    public static IServiceCollection DecorateLuceneClient<TDecorator>(
        this IServiceCollection services,
        Func<IServiceProvider, ILuceneClient, TDecorator> decorate)
        where TDecorator : class, ILuceneClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorate);

        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ILuceneClient));

        if (existing is null || services.Any(descriptor => descriptor.ServiceType == typeof(DecorationMarker<TDecorator>)))
        {
            return services;
        }

        services.AddSingleton<DecorationMarker<TDecorator>>();
        services.Remove(existing);
        services.Add(new ServiceDescriptor(
            typeof(ILuceneClient),
            provider => decorate(provider, Instantiate(provider, existing)),
            existing.Lifetime));

        return services;
    }

    /// <summary>Records that a decorator has been applied, so a second call cannot double-wrap the client.</summary>
    /// <typeparam name="TDecorator">The decorator type.</typeparam>
    private sealed class DecorationMarker<TDecorator>
        where TDecorator : class, ILuceneClient
    {
    }

    private static ILuceneClient Instantiate(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ILuceneClient instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (ILuceneClient)descriptor.ImplementationFactory(provider);
        }

        return (ILuceneClient)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }

    private sealed class OrderedStage : ISearchStage
    {
        private readonly ISearchStage inner;

        internal OrderedStage(ISearchStage inner, int order)
        {
            this.inner = inner;
            Order = order;
        }

        public int Order { get; }

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(context, cancellationToken);
    }
}

/// <summary>
/// Maps the search endpoints into the request pipeline.
/// </summary>
public static class XpSearchApplicationBuilderExtensions
{
    /// <summary>Maps the search endpoints. Call it after <c>app.UseKentico()</c>.</summary>
    /// <param name="app">The application builder, which must also be an <see cref="IEndpointRouteBuilder"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The builder cannot map endpoints.</exception>
    public static IApplicationBuilder UseXpSearch(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app is not IEndpointRouteBuilder endpoints)
        {
            throw new InvalidOperationException(
                "UseXpSearch requires an application builder that can map endpoints, such as WebApplication. Call endpoints.MapXpSearch() instead.");
        }

        endpoints.MapXpSearch();

        return app;
    }
}
