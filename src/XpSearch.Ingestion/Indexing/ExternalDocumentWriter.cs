using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.Extensions.Logging;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Schema;

namespace XpSearch.Ingestion.Indexing;

/// <summary>
/// Waits until an index the integration has just started rebuilding has settled, so replayed external
/// documents are written into the generation the rebuild published rather than the one it replaced.
/// </summary>
/// <remarks>
/// <c>DefaultLuceneClient.Rebuild</c> resets the index and only <em>queues</em> the content items;
/// the integration's own <c>LuceneQueueWorker</c> indexes them and publishes a new index generation
/// afterwards. Writing external documents before that publish would put them in the outgoing
/// generation, which is exactly the silent catalogue loss spec §10.2 warns about.
/// </remarks>
public interface IRebuildCompletionWaiter
{
    /// <summary>Waits for the index to stop changing, or for the configured timeout.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is safe to write.</returns>
    Task WaitAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>
/// Writes stored external documents to Lucene and removes them again. This is the only place in the
/// library that talks to <see cref="ILuceneClient"/> for writes, so every path - queued, inline
/// (<c>waitForIndex</c>), startup re-queue and post-rebuild replay - behaves identically.
/// </summary>
public sealed class ExternalDocumentWriter : IIngestionWorkProcessor
{
    private readonly IExternalDocumentStore store;
    private readonly ILuceneClient client;
    private readonly IIngestionSchemaProvider schemas;
    private readonly IRebuildCompletionWaiter waiter;
    private readonly ILogger<ExternalDocumentWriter> logger;

    /// <summary>Initializes a new instance of the <see cref="ExternalDocumentWriter"/> class.</summary>
    /// <param name="store">Where the documents are persisted.</param>
    /// <param name="client">The Lucene integration's write client.</param>
    /// <param name="schemas">Supplies the schema each document is encoded with.</param>
    /// <param name="waiter">Delays a replay until a rebuild has published.</param>
    /// <param name="logger">Logger.</param>
    public ExternalDocumentWriter(
        IExternalDocumentStore store,
        ILuceneClient client,
        IIngestionSchemaProvider schemas,
        IRebuildCompletionWaiter waiter,
        ILogger<ExternalDocumentWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.client = client;
        this.schemas = schemas;
        this.waiter = waiter;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ProcessAsync(IngestionWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        switch (item.Operation)
        {
            case IngestionOperation.Delete:
                return await client.DeleteRecords(item.Ids, item.IndexName).ConfigureAwait(false);

            case IngestionOperation.Replay:
                await waiter.WaitAsync(item.IndexName, cancellationToken).ConfigureAwait(false);
                var all = await store.ListAsync(item.IndexName, source: null, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Replaying {Count} external documents into rebuilt index {Index}.", all.Count, item.IndexName);

                return await WriteAsync(item.IndexName, all, cancellationToken).ConfigureAwait(false);

            default:
                var records = await store.GetManyAsync(item.IndexName, item.Ids, cancellationToken).ConfigureAwait(false);

                return await WriteAsync(item.IndexName, records, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> WriteAsync(string indexName, IReadOnlyList<ExternalDocumentRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return 0;
        }

        var schema = await schemas.GetSchemaAsync(indexName, cancellationToken).ConfigureAwait(false);
        var documents = records.Select(record => ExternalDocumentFactory.Create(record, schema.Fields)).ToList();
        var ids = records.Select(record => record.Id).ToList();

        // UpsertRecords only replaces a document when it can read both ItemGuid and LanguageName off
        // it (DefaultLuceneClient.UpsertRecordsInternal); external documents have no language, so the
        // previous copy is removed explicitly first. Delete-then-add is what upsert does anyway.
        await client.DeleteRecords(ids, indexName).ConfigureAwait(false);
        int written = await client.UpsertRecords(documents, indexName, cancellationToken).ConfigureAwait(false);

        await store.MarkIndexedAsync(indexName, ids, cancellationToken).ConfigureAwait(false);

        return written;
    }
}
