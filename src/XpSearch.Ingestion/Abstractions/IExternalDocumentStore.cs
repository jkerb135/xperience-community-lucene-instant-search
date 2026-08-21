namespace XpSearch.Ingestion.Abstractions;

/// <summary>
/// Where a stored external document is in its journey to Lucene.
/// </summary>
public enum ExternalDocumentStatus
{
    /// <summary>Persisted, not yet written to Lucene. Re-queued on the next application start.</summary>
    Pending = 0,

    /// <summary>Written to Lucene.</summary>
    Indexed = 1
}

/// <summary>
/// One externally pushed document as it is persisted (spec §10.2, ADR-0005). The database row is the
/// source of truth; the Lucene document is derived from it and is rebuilt from it after a rebuild.
/// </summary>
/// <param name="IndexName">Code name of the index the document belongs to.</param>
/// <param name="Source">Provenance, written to the reserved <c>_source</c> attribute.</param>
/// <param name="Id">Caller-owned identifier, unique within the index.</param>
/// <param name="Json">The document body as pushed, a JSON object with the attributes only.</param>
/// <param name="ContentHash">Hash of <paramref name="Json"/>, so an unchanged re-push is cheap to spot.</param>
/// <param name="CreatedAt">When the document was first pushed.</param>
/// <param name="UpdatedAt">When the document was last pushed.</param>
/// <param name="Status">Whether the row has reached Lucene.</param>
public sealed record ExternalDocumentRecord(
    string IndexName,
    string Source,
    string Id,
    string Json,
    string ContentHash,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ExternalDocumentStatus Status);

/// <summary>
/// Persistence of externally pushed documents. The production implementation stores them in the
/// <c>XpSearchExternalDocument</c> custom module class.
/// </summary>
public interface IExternalDocumentStore
{
    /// <summary>Reads one document.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="id">The document's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored document, or <see langword="null"/> when the index holds no such document.</returns>
    Task<ExternalDocumentRecord?> GetAsync(string indexName, string id, CancellationToken cancellationToken);

    /// <summary>Reads several documents at once.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="ids">The identifiers to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored documents that exist, in no particular order.</returns>
    Task<IReadOnlyList<ExternalDocumentRecord>> GetManyAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces documents, keyed by index and identifier.</summary>
    /// <param name="records">The documents to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were written.</returns>
    Task<int> UpsertAsync(IReadOnlyList<ExternalDocumentRecord> records, CancellationToken cancellationToken);

    /// <summary>Deletes documents by identifier.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="ids">The identifiers to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    Task<int> DeleteAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>Lists documents of an index, optionally narrowed to one source.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="source">The source to list, or <see langword="null"/> for every source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored documents.</returns>
    Task<IReadOnlyList<ExternalDocumentRecord>> ListAsync(string indexName, string? source, CancellationToken cancellationToken);

    /// <summary>Lists every document that has not reached Lucene yet, across all indexes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pending documents.</returns>
    Task<IReadOnlyList<ExternalDocumentRecord>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>Marks documents as written to Lucene.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="ids">The identifiers that reached Lucene.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the rows are updated.</returns>
    Task MarkIndexedAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>Reads when an external document was last written to an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The timestamp, or <see langword="null"/> when the index holds no external documents.</returns>
    Task<DateTime?> GetLastWriteAsync(string indexName, CancellationToken cancellationToken);
}
