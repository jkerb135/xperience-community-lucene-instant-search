namespace XpSearch.Ingestion.Abstractions;

/// <summary>
/// What a queued ingestion work item does to Lucene.
/// </summary>
public enum IngestionOperation
{
    /// <summary>Write the named stored documents to Lucene.</summary>
    Upsert,

    /// <summary>Remove the named documents from Lucene.</summary>
    Delete,

    /// <summary>Write every stored document of the index to Lucene again, after a rebuild wiped them.</summary>
    Replay
}

/// <summary>
/// One unit of background index work (spec §10.2). Rows are persisted before the item is queued, so
/// losing the queue costs nothing but time.
/// </summary>
/// <param name="TaskId">Identifier the caller can poll the index status with.</param>
/// <param name="IndexName">Code name of the index to write to.</param>
/// <param name="Operation">What to do.</param>
/// <param name="Ids">The document identifiers involved. Empty for <see cref="IngestionOperation.Replay"/>.</param>
public sealed record IngestionWorkItem(string TaskId, string IndexName, IngestionOperation Operation, IReadOnlyList<string> Ids)
{
    /// <summary>Creates a work item with a fresh task identifier.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="operation">What to do.</param>
    /// <param name="ids">The document identifiers involved.</param>
    /// <returns>The work item.</returns>
    public static IngestionWorkItem New(string indexName, IngestionOperation operation, IReadOnlyList<string>? ids = null) =>
        new(Guid.NewGuid().ToString("N"), indexName, operation, ids ?? []);
}

/// <summary>
/// Hands index work to the background thread. The production implementation enqueues onto a
/// <c>ThreadQueueWorker</c>, the pattern Xperience prescribes for integration batches
/// (https://docs.kentico.com/guides/development/customizations-and-integrations/tools-for-integrations).
/// </summary>
public interface IIngestionQueue
{
    /// <summary>Queues an item for background processing.</summary>
    /// <param name="item">The work to do.</param>
    void Enqueue(IngestionWorkItem item);

    /// <summary>
    /// Gets the number of consecutive work items that failed to reach Lucene, for the index status's
    /// health. Work merely waiting in the queue is not a failure: it is the normal state of an
    /// asynchronous write, and the counter goes back to zero as soon as an item succeeds.
    /// </summary>
    int FailedCount { get; }
}

/// <summary>
/// Executes one queued work item against Lucene. Kept separate from the queue so the work can also
/// run inline, which is what <c>waitForIndex: true</c> does.
/// </summary>
public interface IIngestionWorkProcessor
{
    /// <summary>Runs one work item.</summary>
    /// <param name="item">The work to do.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many Lucene documents the item wrote or removed.</returns>
    Task<int> ProcessAsync(IngestionWorkItem item, CancellationToken cancellationToken);
}
