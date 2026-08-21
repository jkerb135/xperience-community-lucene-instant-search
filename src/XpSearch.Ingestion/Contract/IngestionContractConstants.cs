namespace XpSearch.Ingestion.Contract;

/// <summary>
/// The frozen transport details of the ingestion API (spec §10.1). All routes sit under
/// <c>/api/xpsearch/admin/</c> and are authenticated with a bearer API key, separately from the
/// public query endpoint.
/// </summary>
public static class IngestionContractConstants
{
    /// <summary>Common prefix of every ingestion route.</summary>
    public const string RoutePrefix = "/api/xpsearch/admin";

    /// <summary>Route of the index listing: GET, <c>IndexListResponse</c> out.</summary>
    public const string IndexesRoute = RoutePrefix + "/indexes";

    /// <summary>Route of the index status: GET, <c>IndexStatus</c> out.</summary>
    public const string StatusRoute = IndexesRoute + "/{index}/status";

    /// <summary>Route of the upsert endpoint: POST, <c>UpsertRequest</c> in, <c>UpsertResponse</c> out.</summary>
    public const string DocumentsRoute = IndexesRoute + "/{index}/documents";

    /// <summary>Route of the single-document endpoints: DELETE and PATCH.</summary>
    public const string DocumentRoute = DocumentsRoute + "/{id}";

    /// <summary>Route of the batch delete: POST, <c>BatchDeleteRequest</c> in, <c>DeleteResponse</c> out.</summary>
    public const string BatchDeleteRoute = DocumentsRoute + "/delete";

    /// <summary>Route of the scoped clear: POST with an optional <c>?source=</c>, <c>DeleteResponse</c> out.</summary>
    public const string ClearRoute = IndexesRoute + "/{index}/clear";

    /// <summary>Route of the rebuild trigger: POST, <c>UpsertResponse</c> out with the replay's task identifier.</summary>
    public const string RebuildRoute = IndexesRoute + "/{index}/rebuild";

    /// <summary>Name of the rate limiting policy applied to every ingestion route, partitioned per key.</summary>
    public const string RateLimitPolicy = "XpSearchIngestion";

    /// <summary>Scope name of a write operation.</summary>
    public const string WriteOperation = "write";

    /// <summary>Scope name of a delete operation.</summary>
    public const string DeleteOperation = "delete";

    /// <summary>Scope name of a rebuild operation.</summary>
    public const string RebuildOperation = "rebuild";

    /// <summary>Scope name of a read operation.</summary>
    public const string ReadOperation = "read";
}
