using XpSearch.Client.Contract;

namespace XpSearch.Client;

/// <summary>
/// The ingestion verbs scoped to one index, as returned by <see cref="XpSearchIngestionClient.Index"/>.
/// Each one maps to exactly one route of the frozen ingestion contract.
/// </summary>
public sealed class XpSearchIngestionIndexClient
{
    private readonly XpSearchIngestionClient client;
    private readonly string index;
    private readonly string documentsRoute;

    internal XpSearchIngestionIndexClient(XpSearchIngestionClient client, string index)
    {
        this.client = client;
        this.index = index;

        documentsRoute = $"{XpSearchIngestionClient.RoutePrefix}/indexes/{XpSearchIngestionClient.Segment(index)}/documents";
    }

    /// <summary>Gets the code name of the index these verbs act on.</summary>
    public string Name => index;

    /// <summary>
    /// Writes documents, splitting the sequence into requests that stay under both server caps
    /// (<see cref="XpSearchIngestionClientOptions.MaxDocumentsPerRequest"/> and
    /// <see cref="XpSearchIngestionClientOptions.MaxRequestBytes"/>) and adding the answers up.
    /// Upsert is idempotent by contract — a document whose <c>id</c> exists is replaced — so a
    /// retried or re-run batch cannot duplicate anything.
    /// </summary>
    /// <param name="documents">The documents to write; enumerated once.</param>
    /// <param name="waitForIndex">
    /// When <see langword="true"/> each batch waits until it is searchable. A foot-gun for bulk
    /// imports: it serializes the caller against index writes.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The totals, every per-document error and one task id per batch.</returns>
    /// <exception cref="XpSearchIngestionException">
    /// A batch failed. <see cref="XpSearchIngestionException.PartialUpsert"/> carries what the
    /// earlier batches had already written, so a caller knows where to resume.
    /// </exception>
    public async Task<UpsertResult> UpsertAsync(IEnumerable<PushDocument> documents, bool waitForIndex = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var result = new UpsertResult();

        foreach (var batch in Batches(documents))
        {
            var request = new UpsertRequest { Documents = batch, WaitForIndex = waitForIndex ? true : null };

            try
            {
                result.Add(await client.SendAsync<UpsertResponse>(
                    HttpMethod.Post,
                    documentsRoute,
                    XpSearchIngestionClient.Serialize(request),
                    cancellationToken).ConfigureAwait(false));
            }
            catch (XpSearchIngestionException exception)
            {
                exception.PartialUpsert = result;

                throw;
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces some attributes of one document, read-modify-rewrite. A <see langword="null"/> value
    /// removes the attribute. The document's <c>_source</c> cannot be patched.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="attributes">The attributes to change.</param>
    /// <param name="waitForIndex">Whether to wait until the change is searchable.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's answer.</returns>
    public async Task<UpsertResponse> PatchAsync(string id, IReadOnlyDictionary<string, object?> attributes, bool waitForIndex = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes.Count == 0)
        {
            throw new ArgumentException("At least one attribute to change is required.", nameof(attributes));
        }

        // The patch body IS the attribute bag: PatchRequest carries them as extension data, so the
        // dictionary serializes to the same JSON object.
        return await client.SendAsync<UpsertResponse>(
            HttpMethod.Patch,
            $"{documentsRoute}/{XpSearchIngestionClient.Segment(id)}{XpSearchIngestionClient.WaitQuery(waitForIndex)}",
            XpSearchIngestionClient.Serialize(attributes),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes one document.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="waitForIndex">Whether to wait until the delete is searchable.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's answer.</returns>
    public async Task<DeleteResponse> DeleteAsync(string id, bool waitForIndex = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await client.SendAsync<DeleteResponse>(
            HttpMethod.Delete,
            $"{documentsRoute}/{XpSearchIngestionClient.Segment(id)}{XpSearchIngestionClient.WaitQuery(waitForIndex)}",
            body: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes many documents by id, split into requests of at most
    /// <see cref="XpSearchIngestionClientOptions.MaxDocumentsPerRequest"/> ids.
    /// </summary>
    /// <param name="ids">The document ids; enumerated once.</param>
    /// <param name="waitForIndex">Whether to wait until the deletes are searchable.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The totals and one task id per batch.</returns>
    public async Task<DeleteResult> DeleteManyAsync(IEnumerable<string> ids, bool waitForIndex = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var result = new DeleteResult();
        string route = $"{documentsRoute}/delete{XpSearchIngestionClient.WaitQuery(waitForIndex)}";
        var batch = new List<string>();

        foreach (string id in ids)
        {
            batch.Add(id);

            if (batch.Count == client.Options.MaxDocumentsPerRequest)
            {
                result.Add(await Send(batch).ConfigureAwait(false));
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            result.Add(await Send(batch).ConfigureAwait(false));
        }

        return result;

        Task<DeleteResponse> Send(List<string> chunk) => client.SendAsync<DeleteResponse>(
            HttpMethod.Post,
            route,
            XpSearchIngestionClient.Serialize(new BatchDeleteRequest { Ids = [.. chunk] }),
            cancellationToken);
    }

    /// <summary>
    /// Deletes every document pushed under one source, or every external document when no source is
    /// named. Xperience-managed content is never in scope.
    /// </summary>
    /// <param name="source">The <c>_source</c> to clear, or <see langword="null"/> for all of them.</param>
    /// <param name="waitForIndex">Whether to wait until the clear is searchable.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's answer.</returns>
    public async Task<DeleteResponse> ClearAsync(string? source = null, bool waitForIndex = false, CancellationToken cancellationToken = default)
    {
        string query = source is null
            ? XpSearchIngestionClient.WaitQuery(waitForIndex)
            : $"?source={Uri.EscapeDataString(source)}{(waitForIndex ? "&waitForIndex=true" : string.Empty)}";

        return await client.SendAsync<DeleteResponse>(
            HttpMethod.Post,
            $"{XpSearchIngestionClient.RoutePrefix}/indexes/{XpSearchIngestionClient.Segment(index)}/clear{query}",
            body: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers a rebuild of the Xperience content, after which the pushed documents are replayed
    /// into the new index generation. Answered with <c>202 Accepted</c>: the work is asynchronous.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's answer.</returns>
    public async Task<UpsertResponse> RebuildAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync<UpsertResponse>(
            HttpMethod.Post,
            $"{XpSearchIngestionClient.RoutePrefix}/indexes/{XpSearchIngestionClient.Segment(index)}/rebuild",
            body: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads the index's document counts by source, last write and health.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The status.</returns>
    public async Task<IndexStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync<IndexStatus>(
            HttpMethod.Get,
            $"{XpSearchIngestionClient.RoutePrefix}/indexes/{XpSearchIngestionClient.Segment(index)}/status",
            body: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Splits the documents into batches that stay under both caps. The size cap is measured on the
    /// serialized documents plus the request envelope, which is what the server weighs.
    /// </summary>
    private IEnumerable<PushDocument[]> Batches(IEnumerable<PushDocument> documents)
    {
        // {"documents":[]} plus the longest waitForIndex form, rounded up.
        const int EnvelopeBytes = 48;

        var options = client.Options;
        var batch = new List<PushDocument>();
        long bytes = EnvelopeBytes;

        foreach (var document in documents)
        {
            long size = XpSearchIngestionClient.Serialize(document).LongLength + 1; // + the separating comma

            // A single oversized document still goes out alone: the server owns the limit and
            // answers 413 naming it, which beats a client-side rule the host may have changed.
            if (batch.Count > 0 && (batch.Count == options.MaxDocumentsPerRequest || bytes + size > options.MaxRequestBytes))
            {
                yield return [.. batch];
                batch.Clear();
                bytes = EnvelopeBytes;
            }

            batch.Add(document);
            bytes += size;
        }

        if (batch.Count > 0)
        {
            yield return [.. batch];
        }
    }
}
