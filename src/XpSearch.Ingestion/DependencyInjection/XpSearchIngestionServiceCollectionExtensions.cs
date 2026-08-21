using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Endpoints;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Persistence;
using XpSearch.Ingestion.Queue;
using XpSearch.Ingestion.Schema;
using XpSearch.Ingestion.Security;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the ingestion API (spec §10).
/// </summary>
public static class XpSearchIngestionServiceCollectionExtensions
{
    /// <summary>Registers the ingestion API with its defaults.</summary>
    /// <param name="services">The service collection. <c>AddXpSearch</c> must already have been called on it.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddXpSearchIngestion(this IServiceCollection services) =>
        services.AddXpSearchIngestion(_ => { });

    /// <summary>Registers the ingestion API.</summary>
    /// <param name="services">The service collection. <c>AddXpSearch</c> must already have been called on it.</param>
    /// <param name="configure">Configures the ingestion options.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Call order matters: <c>AddKenticoLucene</c>, then <c>AddXpSearch</c>, then this. The rebuild
    /// replay decorates whatever <c>ILuceneClient</c> is registered at this point, so it wraps the
    /// core package's cache-evicting decorator rather than being wrapped by it.
    /// Map the endpoints with <c>app.MapXpSearchIngestion()</c> and, for the per-key rate limit to
    /// take effect, call <c>app.UseRateLimiter()</c>.
    /// </remarks>
    public static IServiceCollection AddXpSearchIngestion(this IServiceCollection services, Action<XpSearchIngestionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<XpSearchIngestionModuleInstaller>();
        services.TryAddSingleton<IExternalDocumentStore, InfoExternalDocumentStore>();
        services.TryAddSingleton<IApiKeyStore, InfoApiKeyStore>();
        services.TryAddSingleton<IIngestionLog, InfoIngestionLog>();
        services.TryAddSingleton<IApiKeyService, ApiKeyService>();
        services.TryAddSingleton<IIndexStrategySource, LuceneIndexStrategySource>();
        services.TryAddSingleton<IIngestionSchemaProvider, IngestionSchemaProvider>();
        services.TryAddSingleton<IFieldTypeGuard, FieldTypeGuard>();
        services.TryAddSingleton<IRebuildCompletionWaiter, LuceneQuiescenceWaiter>();
        services.TryAddSingleton<IIngestionWorkProcessor, ExternalDocumentWriter>();
        services.TryAddSingleton<IIngestionQueue, ThreadQueueIngestionQueue>();
        services.TryAddSingleton<IIngestionCaller, HttpIngestionCaller>();
        services.TryAddSingleton<IXpSearchIndexer, XpSearchIndexer>();

        services.DecorateLuceneClient<ExternalDocumentReplayLuceneClient>((provider, inner) =>
            new ExternalDocumentReplayLuceneClient(
                inner,
                provider.GetRequiredService<IIngestionQueue>(),
                provider.GetRequiredService<Logging.ILogger<ExternalDocumentReplayLuceneClient>>()));

        services.AddRateLimiter(limiter => limiter.AddPolicy(IngestionContractConstants.RateLimitPolicy, PartitionByKey(services)));

        return services;
    }

    /// <summary>
    /// Rate-limits per API key (spec §10.4) with a fixed window, partitioned by the key prefix so one
    /// integration's bulk import cannot starve another's
    /// (https://learn.microsoft.com/aspnet/core/performance/rate-limit). A request with no key is
    /// partitioned by remote address, which bounds the cost of rejecting it.
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PartitionByKey(IServiceCollection services) =>
        context =>
        {
            var options = context.RequestServices.GetRequiredService<IOptions<XpSearchIngestionOptions>>().Value;
            string? key = ApiKeyEndpointFilter.BearerToken(context);
            string partition = key is { Length: >= ApiKeyService.PrefixLength }
                ? key[..ApiKeyService.PrefixLength]
                : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.RateLimitPermitsPerWindow,
                Window = options.RateLimitWindow,
                QueueLimit = 0,
            });
        };
}

/// <summary>
/// Maps the ingestion endpoints into the request pipeline.
/// </summary>
public static class XpSearchIngestionApplicationBuilderExtensions
{
    /// <summary>Maps the ingestion endpoints. Call it after <c>app.UseKentico()</c>.</summary>
    /// <param name="app">The application builder, which must also be an <see cref="IEndpointRouteBuilder"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The builder cannot map endpoints.</exception>
    public static IApplicationBuilder UseXpSearchIngestion(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app is not IEndpointRouteBuilder endpoints)
        {
            throw new InvalidOperationException(
                "UseXpSearchIngestion requires an application builder that can map endpoints, such as WebApplication. Call endpoints.MapXpSearchIngestion() instead.");
        }

        endpoints.MapXpSearchIngestion();

        return app;
    }
}
