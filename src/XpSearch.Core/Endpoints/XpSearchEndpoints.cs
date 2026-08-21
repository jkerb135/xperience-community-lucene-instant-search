using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Endpoints;

/// <summary>
/// Maps the three endpoints of the search API onto an ASP.NET Core route builder.
/// </summary>
/// <remarks>
/// Minimal APIs rather than controllers: the Xperience host template maps no controllers of its own
/// and constrains its conventional route to an allow-list, so a controller in this package would
/// never be reached. Custom endpoints are plain ASP.NET Core
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/secure-custom-endpoints);
/// the query endpoint is public by design (spec §10.4).
/// </remarks>
public static class XpSearchEndpoints
{
    /// <summary>Maps <c>/api/xpsearch/query</c>, <c>/suggest</c> and <c>/events</c>.</summary>
    /// <param name="endpoints">The route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapXpSearch(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(ContractConstants.QueryRoute, Query).WithName("XpSearchQuery");
        endpoints.MapPost(ContractConstants.SuggestRoute, Suggest).WithName("XpSearchSuggest");
        endpoints.MapPost(ContractConstants.EventsRoute, Events).WithName("XpSearchEvents");

        return endpoints;
    }

    private static async Task<IResult> Query(SearchRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        if (request is null)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["body"] = ["A search request body is required."] });
        }

        try
        {
            var pipeline = context.RequestServices.GetRequiredService<ISearchPipeline>();

            return Results.Ok(await pipeline.ExecuteAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Suggest(SuggestRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        if (request is null)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["body"] = ["A suggest request body is required."] });
        }

        try
        {
            var suggest = context.RequestServices.GetRequiredService<ISuggestService>();

            return Results.Ok(await suggest.SuggestAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Events(EventRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        var errors = ValidateEvent(request);

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        try
        {
            var sink = context.RequestServices.GetRequiredService<ISearchEventSink>();
            await sink.HandleAsync(request!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Analytics is best-effort: a failing sink must not fail the caller's page.
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(XpSearchEndpoints).FullName!)
                .LogWarning(exception, "A search event could not be handled.");
        }

        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    private static Dictionary<string, string[]> ValidateEvent(EventRequest? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request is null)
        {
            errors["body"] = ["An event request body is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.ObjectId))
        {
            errors["objectID"] = ["objectID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.QueryId))
        {
            errors["queryId"] = ["queryId is required."];
        }

        if (request.EventType == EventType.Click && request.Position is null or < 1)
        {
            errors["position"] = ["position is required for a click event and must be one or greater."];
        }

        return errors;
    }

    private static void SetVersionHeader(HttpContext context) =>
        context.Response.Headers[ContractConstants.ApiVersionHeader] = ContractConstants.ApiVersion;
}

/// <summary>
/// Maps the library's exceptions onto RFC 9457 Problem Details responses.
/// </summary>
internal static class Problems
{
    internal static IResult From(Exception exception, HttpContext context)
    {
        switch (exception)
        {
            case SearchValidationException validation:
                return Validation(validation.Errors.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));

            case IndexNotFoundException notFound:
                return Results.Problem(
                    title: "Index not found.",
                    detail: $"Search index '{notFound.IndexName}' is not registered.",
                    statusCode: StatusCodes.Status404NotFound);

            case OperationCanceledException:
                throw exception;

            default:
                // Nothing about the exception reaches the caller; the details go to the log.
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(XpSearchEndpoints).FullName!)
                    .LogError(exception, "A search request failed.");

                return Results.Problem(
                    title: "The search request could not be processed.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    internal static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "The request is not valid.");
}
