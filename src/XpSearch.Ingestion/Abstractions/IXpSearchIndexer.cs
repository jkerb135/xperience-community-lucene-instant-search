using System.Text.Json;

using XpSearch.Ingestion.Contract;

namespace XpSearch.Ingestion.Abstractions;

/// <summary>
/// One document to push into an index (spec §10.6).
/// </summary>
/// <param name="Id">Caller-owned, stable identifier. Pushing the same identifier again replaces the document.</param>
/// <param name="Source">Provenance, written to the reserved <c>_source</c> attribute. Never <c>xperience</c>.</param>
/// <param name="Attributes">The document body, keyed by attribute name. Validated against the index schema.</param>
public sealed record SearchDocument(string Id, string Source, IReadOnlyDictionary<string, JsonElement> Attributes)
{
    /// <summary>Builds a document out of plain CLR values, which is what in-process callers have.</summary>
    /// <param name="id">Caller-owned, stable identifier.</param>
    /// <param name="source">Provenance of the document.</param>
    /// <param name="attributes">The document body. Values are serialized with the default options.</param>
    /// <returns>The document.</returns>
    public static SearchDocument Create(string id, string source, IReadOnlyDictionary<string, object?> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return new SearchDocument(
            id,
            source,
            attributes.ToDictionary(entry => entry.Key, entry => JsonSerializer.SerializeToElement(entry.Value), StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The in-process ingestion API (spec §10.6): the same operations the HTTP endpoints expose, without
/// the HTTP. Inject it into scheduled tasks, custom modules, event handlers or automation steps.
/// </summary>
/// <remarks>
/// Every write is persisted in the <c>XpSearchExternalDocument</c> module class before it is queued
/// to Lucene (ADR-0005), so a restart loses nothing and a rebuild of Xperience content cannot drop
/// externally pushed documents. The <c>waitForIndex</c> flag is a foot-gun for bulk imports: it
/// runs the Lucene write on the calling thread instead of the ingestion queue.
/// </remarks>
public interface IXpSearchIndexer
{
    /// <summary>Writes documents, replacing any that already exist under the same identifiers.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <param name="documents">The documents to write.</param>
    /// <param name="waitForIndex">Whether to block until the documents are searchable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many documents were written, and why the rest were not.</returns>
    Task<UpsertResponse> UpsertAsync(string index, IEnumerable<SearchDocument> documents, bool waitForIndex = false, CancellationToken cancellationToken = default);

    /// <summary>Applies a partial update to one document, as a read-modify-rewrite of the stored body.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <param name="id">Identifier of the document to change.</param>
    /// <param name="attributes">The attributes to set. Attributes that are not named keep their stored value.</param>
    /// <param name="waitForIndex">Whether to block until the change is searchable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The write's outcome.</returns>
    Task<UpsertResponse> PatchAsync(string index, string id, IReadOnlyDictionary<string, JsonElement> attributes, bool waitForIndex = false, CancellationToken cancellationToken = default);

    /// <summary>Deletes documents by identifier.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <param name="ids">Identifiers of the documents to delete.</param>
    /// <param name="waitForIndex">Whether to block until the deletion is searchable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many stored documents were deleted.</returns>
    Task<DeleteResponse> DeleteAsync(string index, IEnumerable<string> ids, bool waitForIndex = false, CancellationToken cancellationToken = default);

    /// <summary>Deletes every document of one source, or every externally pushed document.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <param name="source">The source to clear, or <see langword="null"/> for every external source. Never touches Xperience content.</param>
    /// <param name="waitForIndex">Whether to block until the deletion is searchable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many stored documents were deleted.</returns>
    Task<DeleteResponse> DeleteBySourceAsync(string index, string? source, bool waitForIndex = false, CancellationToken cancellationToken = default);

    /// <summary>Reads document counts by source, the last external write and the index's health.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    Task<IndexStatus> GetStatusAsync(string index, CancellationToken cancellationToken = default);
}
