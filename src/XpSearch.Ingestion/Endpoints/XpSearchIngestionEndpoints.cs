using System.Diagnostics;
using System.Text.Json;

using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Schema;

using CoreSchemaField = XpSearch.Core.Abstractions.SchemaField;

namespace XpSearch.Ingestion.Endpoints;

/// <summary>
/// Maps the ingestion endpoints of spec §10.1 onto an ASP.NET Core route builder. Minimal APIs, a
/// separate route prefix and separate authentication from the query API, exactly as the spec asks.
/// </summary>
public static class XpSearchIngestionEndpoints
{
    /// <summary>Maps every route under <c>/api/xpsearch/admin/</c>.</summary>
    /// <param name="endpoints">The route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapXpSearchIngestion(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        Map(endpoints.MapGet(IngestionContractConstants.IndexesRoute, ListIndexes), IngestionContractConstants.ReadOperation, "XpSearchIngestionIndexes");
        Map(endpoints.MapGet(IngestionContractConstants.StatusRoute, Status), IngestionContractConstants.ReadOperation, "XpSearchIngestionStatus");
        Map(endpoints.MapPost(IngestionContractConstants.DocumentsRoute, Upsert), IngestionContractConstants.WriteOperation, "XpSearchIngestionUpsert");
        Map(endpoints.MapPatch(IngestionContractConstants.DocumentRoute, Patch), IngestionContractConstants.WriteOperation, "XpSearchIngestionPatch");
        Map(endpoints.MapDelete(IngestionContractConstants.DocumentRoute, Delete), IngestionContractConstants.DeleteOperation, "XpSearchIngestionDelete");
        Map(endpoints.MapPost(IngestionContractConstants.BatchDeleteRoute, BatchDelete), IngestionContractConstants.DeleteOperation, "XpSearchIngestionBatchDelete");
        Map(endpoints.MapPost(IngestionContractConstants.ClearRoute, Clear), IngestionContractConstants.DeleteOperation, "XpSearchIngestionClear");
        Map(endpoints.MapPost(IngestionContractConstants.RebuildRoute, Rebuild), IngestionContractConstants.RebuildOperation, "XpSearchIngestionRebuild");

        return endpoints;
    }

    private static void Map(RouteHandlerBuilder route, string operation, string name) =>
        route.AddEndpointFilter(new ApiKeyEndpointFilter(operation))
            .RequireRateLimiting(IngestionContractConstants.RateLimitPolicy)
            .WithName(name);

    private static async Task<IResult> ListIndexes(HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        try
        {
            var strategies = context.RequestServices.GetRequiredService<IIndexStrategySource>();
            var schemas = context.RequestServices.GetRequiredService<IIngestionSchemaProvider>();
            var summaries = new List<IndexSummary>();

            foreach (string index in strategies.GetIndexNames())
            {
                var schema = await schemas.GetSchemaAsync(index, cancellationToken).ConfigureAwait(false);

                summaries.Add(new IndexSummary
                {
                    Name = index,
                    AllowDynamicFields = schema.AllowDynamicFields,
                    Schema = [.. schema.Fields.Fields.Select(ToContract)],
                });
            }

            return Results.Ok(new IndexListResponse { Indexes = [.. summaries] });
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Status(string index, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        try
        {
            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();

            return Results.Ok(await indexer.GetStatusAsync(index, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Upsert(string index, UpsertRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        if (request?.Documents is null or { Length: 0 })
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["documents"] = ["At least one document is required."] });
        }

        try
        {
            var options = context.RequestServices.GetRequiredService<IOptions<XpSearchIngestionOptions>>().Value;

            EnsureWithinLimits(context, request.Documents.Length, options);

            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();
            var documents = request.Documents.Select(document => new SearchDocument(document.Id, document.Source ?? options.DefaultSource, document.Attributes));

            var response = await indexer
                .UpsertAsync(index, documents, request.WaitForIndex ?? false, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(response);
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Patch(string index, string id, PatchRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        if (request is null || request.Attributes.Count == 0)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["body"] = ["At least one attribute to change is required."] });
        }

        if (request.Source is not null)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["_source"] = ["A document's source cannot be changed; delete it and push it again."] });
        }

        try
        {
            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();

            return Results.Ok(await indexer.PatchAsync(index, id, request.Attributes, WaitRequested(context), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Delete(string index, string id, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        try
        {
            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();

            return Results.Ok(await indexer.DeleteAsync(index, [id], WaitRequested(context), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> BatchDelete(string index, BatchDeleteRequest? request, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        bool hasIds = request?.Ids is { Length: > 0 };
        bool hasFilter = request?.Filter is not null;

        if (hasIds == hasFilter)
        {
            return Problems.Validation(new Dictionary<string, string[]>
            {
                ["body"] = ["Send either ids or filter, not both and not neither."],
            });
        }

        try
        {
            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();
            bool wait = WaitRequested(context);

            var response = hasIds
                ? await indexer.DeleteAsync(index, request!.Ids!, wait, cancellationToken).ConfigureAwait(false)
                : await indexer.DeleteBySourceAsync(index, request!.Filter!.Source, wait, cancellationToken).ConfigureAwait(false);

            return Results.Ok(response);
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Clear(string index, string? source, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        try
        {
            var indexer = context.RequestServices.GetRequiredService<IXpSearchIndexer>();

            // No source clears every external source; Xperience-managed content is never in scope.
            return Results.Ok(await indexer.DeleteBySourceAsync(index, source, WaitRequested(context), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static async Task<IResult> Rebuild(string index, HttpContext context, CancellationToken cancellationToken)
    {
        SetVersionHeader(context);

        long started = Stopwatch.GetTimestamp();

        try
        {
            var accessor = context.RequestServices.GetRequiredService<ILuceneIndexAccessor>();

            if (!accessor.Exists(index))
            {
                throw new IndexNotFoundException(index);
            }

            // The decorated client queues the replay of this index's external documents behind the
            // integration's own rebuild, so the rebuild cannot drop them (spec §10.2).
            await context.RequestServices.GetRequiredService<ILuceneClient>().Rebuild(index, cancellationToken).ConfigureAwait(false);

            await context.RequestServices.GetRequiredService<IIngestionLog>().WriteAsync(
                new IngestionLogEntry(
                    context.RequestServices.GetRequiredService<IIngestionCaller>().KeyPrefix,
                    index,
                    "rebuild",
                    0,
                    Succeeded: true,
                    "Rebuild triggered; external documents replay afterwards.",
                    DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);

            return Results.Accepted(value: new UpsertResponse
            {
                Indexed = 0,
                Failed = 0,
                Errors = [],
                TookMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            });
        }
        catch (Exception exception)
        {
            return Problems.From(exception, context);
        }
    }

    private static void EnsureWithinLimits(HttpContext context, int documents, XpSearchIngestionOptions options)
    {
        if (documents > options.MaxDocumentsPerRequest)
        {
            throw new IngestionTooLargeException(
                $"A request may carry at most {options.MaxDocumentsPerRequest} documents; this one carries {documents}. Split the batch.");
        }

        if (context.Request.ContentLength > options.MaxRequestBytes)
        {
            throw new IngestionTooLargeException(
                $"A request body may be at most {options.MaxRequestBytes / (1024 * 1024)} MB; this one is {context.Request.ContentLength} bytes. Split the batch.");
        }
    }

    private static bool WaitRequested(HttpContext context) =>
        string.Equals(context.Request.Query["waitForIndex"], "true", StringComparison.OrdinalIgnoreCase);

    private static Contract.SchemaField ToContract(CoreSchemaField field) => new()
    {
        Name = field.Name,
        Type = field.Kind switch
        {
            SearchFieldKind.Text => TypeEnum.Text,
            SearchFieldKind.Number => TypeEnum.Number,
            SearchFieldKind.Date => TypeEnum.Date,
            SearchFieldKind.Boolean => TypeEnum.Boolean,
            SearchFieldKind.Taxonomy => TypeEnum.TypeString,
            _ => TypeEnum.String,
        },
        Searchable = field.Searchable,
        Facetable = field.Facetable,
        Sortable = field.Sortable,
        Retrievable = field.Retrievable,
    };

    private static void SetVersionHeader(HttpContext context) =>
        context.Response.Headers[ContractConstants.ApiVersionHeader] = ContractConstants.ApiVersion;
}

/// <summary>
/// Maps ingestion failures onto RFC 9457 Problem Details responses.
/// </summary>
internal static class Problems
{
    internal static IResult From(Exception exception, HttpContext context)
    {
        switch (exception)
        {
            case IngestionValidationException validation:
                return Validation(validation.Errors.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));

            case IngestionTooLargeException tooLarge:
                return Results.Problem(
                    title: "The request is too large.",
                    detail: tooLarge.Message,
                    statusCode: StatusCodes.Status413PayloadTooLarge);

            case IndexNotFoundException notFound:
                return Results.Problem(
                    title: "Index not found.",
                    detail: $"Search index '{notFound.IndexName}' is not registered.",
                    statusCode: StatusCodes.Status404NotFound);

            case DocumentNotFoundException document:
                return Results.Problem(
                    title: "Document not found.",
                    detail: document.Message,
                    statusCode: StatusCodes.Status404NotFound);

            case JsonException json:
                return Validation(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["body"] = [json.Message] });

            case OperationCanceledException:
                throw exception;

            default:
                // Nothing about the exception reaches the caller; the details go to the log.
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(XpSearchIngestionEndpoints).FullName!)
                    .LogError(exception, "An ingestion request failed.");

                return Results.Problem(
                    title: "The ingestion request could not be processed.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    internal static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "The request is not valid.");
}
