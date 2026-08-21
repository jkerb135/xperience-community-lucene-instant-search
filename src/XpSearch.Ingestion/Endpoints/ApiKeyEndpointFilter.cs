using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Security;

namespace XpSearch.Ingestion.Endpoints;

/// <summary>
/// Authenticates the <c>Authorization: Bearer</c> API key of an ingestion request and checks that the
/// key is enabled, unexpired and scoped to this index and this operation (spec §10.4).
/// </summary>
/// <remarks>
/// An endpoint filter rather than an authentication scheme: the ingestion routes are the only ones
/// that use keys, and adding a scheme would change how the host's own authentication is configured
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/secure-custom-endpoints).
/// A refusal says which of the two problems it was - 401 for an unusable key, 403 for a key that is
/// simply not allowed here - and never which keys exist.
/// </remarks>
public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    /// <summary>Key under which the authenticated key's prefix is put on <see cref="HttpContext.Items"/>.</summary>
    public const string KeyPrefixItem = "XpSearch.Ingestion.KeyPrefix";

    private readonly string operation;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyEndpointFilter"/> class.</summary>
    /// <param name="operation">The operation the endpoint performs: <c>write</c>, <c>delete</c>, <c>rebuild</c> or <c>read</c>.</param>
    public ApiKeyEndpointFilter(string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);

        this.operation = operation;
    }

    /// <summary>Reads the bearer token out of an <c>Authorization</c> header.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The token, or <see langword="null"/> when the header is missing or not a bearer header.</returns>
    public static string? BearerToken(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? header = context.Request.Headers.Authorization;

        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        string index = http.Request.RouteValues.TryGetValue("index", out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
        var keys = http.RequestServices.GetRequiredService<IApiKeyService>();

        var (key, failure) = await keys
            .AuthenticateAsync(BearerToken(http), index, operation, http.RequestAborted)
            .ConfigureAwait(false);

        switch (failure)
        {
            case ApiKeyFailure.None:
                http.Items[KeyPrefixItem] = key!.Prefix;

                return await next(context).ConfigureAwait(false);

            case ApiKeyFailure.OutOfScope:
                return Results.Problem(
                    title: "The API key is not allowed to perform this operation.",
                    detail: $"The key is not scoped to '{operation}' on index '{index}'.",
                    statusCode: StatusCodes.Status403Forbidden);

            default:
                return Results.Problem(
                    title: "The API key is not valid.",
                    detail: failure switch
                    {
                        ApiKeyFailure.Disabled => "The key has been disabled.",
                        ApiKeyFailure.Expired => "The key has expired.",
                        _ => "Send a valid key in the Authorization header as 'Bearer <key>'.",
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
        }
    }
}

/// <summary>
/// Names the API key behind the current request for the ingestion log, falling back to
/// <c>in-process</c> when there is no request - a scheduled task calling <see cref="IXpSearchIndexer"/>
/// directly, for instance.
/// </summary>
public sealed class HttpIngestionCaller : IIngestionCaller
{
    /// <summary>Value logged for a caller that did not come in over HTTP.</summary>
    public const string InProcess = "in-process";

    private readonly IHttpContextAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="HttpIngestionCaller"/> class.</summary>
    /// <param name="accessor">Access to the current request, if any.</param>
    public HttpIngestionCaller(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        this.accessor = accessor;
    }

    /// <inheritdoc />
    public string KeyPrefix =>
        accessor.HttpContext?.Items.TryGetValue(ApiKeyEndpointFilter.KeyPrefixItem, out object? prefix) == true
            ? prefix as string ?? InProcess
            : InProcess;
}
