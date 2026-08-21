using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Options;

namespace XpSearch.Ingestion.Indexing;

/// <summary>
/// Decorates <see cref="ILuceneClient"/> and replays externally pushed documents into an index after
/// the integration rebuilds it (spec §10.2: "A rebuild of Xperience content must never delete
/// externally pushed documents").
/// </summary>
/// <remarks>
/// <c>DefaultLuceneClient.Rebuild</c> calls <c>ILuceneIndexService.ResetIndex</c>, which opens a new
/// index generation with <c>OpenMode.CREATE</c> and then re-queues Xperience content only - anything
/// pushed through the ingestion API is gone from Lucene the moment an editor presses Rebuild. The
/// database rows survive (ADR-0005), so the fix is to queue a replay of them behind the rebuild.
/// Decoration is Kentico's documented substitute for the rebuild event the integration does not
/// raise (https://docs.kentico.com/documentation/developers-and-admins/customization/decorate-system-services).
/// </remarks>
public sealed class ExternalDocumentReplayLuceneClient : ILuceneClient
{
    private readonly ILuceneClient inner;
    private readonly IIngestionQueue queue;
    private readonly ILogger<ExternalDocumentReplayLuceneClient> logger;

    /// <summary>Initializes a new instance of the <see cref="ExternalDocumentReplayLuceneClient"/> class.</summary>
    /// <param name="inner">The previously registered client, resolved by the container.</param>
    /// <param name="queue">The ingestion queue the replay is scheduled on.</param>
    /// <param name="logger">Logger.</param>
    public ExternalDocumentReplayLuceneClient(ILuceneClient inner, IIngestionQueue queue, ILogger<ExternalDocumentReplayLuceneClient> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(logger);

        this.inner = inner;
        this.queue = queue;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task Rebuild(string indexName, CancellationToken? cancellationToken)
    {
        await inner.Rebuild(indexName, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Index {Index} was rebuilt; queueing a replay of its external documents.", indexName);
        queue.Enqueue(IngestionWorkItem.New(indexName, IngestionOperation.Replay));
    }

    /// <inheritdoc />
    public Task<int> UpsertRecords(IEnumerable<Document> documents, string indexName, CancellationToken cancellationToken) =>
        inner.UpsertRecords(documents, indexName, cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteRecords(IEnumerable<string> itemGuids, string indexName) =>
        inner.DeleteRecords(itemGuids, indexName);

    /// <inheritdoc />
    public Task<bool> DeleteIndex(LuceneIndex luceneIndex) => inner.DeleteIndex(luceneIndex);

    /// <inheritdoc />
    public Task<ICollection<LuceneIndexStatisticsModel>> GetStatistics(CancellationToken cancellationToken) =>
        inner.GetStatistics(cancellationToken);
}

/// <summary>
/// Waits for a rebuilt index to stop changing by watching the integration's own statistics, which
/// report the index storage's last write time.
/// </summary>
/// <remarks>
/// There is no API that reports "the rebuild queue is drained": <c>LuceneQueueWorker</c> is internal
/// and publishes the new generation at the end of its batch. Quiescence - two consecutive polls with
/// the same last-write time, after at least one change - is the observable proxy, bounded by
/// <see cref="XpSearchIngestionOptions.ReplayTimeout"/>. The cost of being wrong is bounded too: the
/// documents stay in the database and the next push or replay writes them again.
/// </remarks>
public sealed class LuceneQuiescenceWaiter : IRebuildCompletionWaiter
{
    private readonly ILuceneClient client;
    private readonly XpSearchIngestionOptions options;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="LuceneQuiescenceWaiter"/> class.</summary>
    /// <param name="client">The Lucene client, read for index statistics.</param>
    /// <param name="options">Ingestion configuration.</param>
    /// <param name="time">Clock and delays, substitutable in tests.</param>
    public LuceneQuiescenceWaiter(ILuceneClient client, IOptions<XpSearchIngestionOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        this.client = client;
        this.options = options.Value;
        this.time = time;
    }

    /// <summary>Gets how long the waiter sleeps between two polls.</summary>
    public static TimeSpan PollInterval => TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task WaitAsync(string indexName, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + options.ReplayTimeout;
        DateTime? previous = null;

        while (time.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, time, cancellationToken).ConfigureAwait(false);

            var statistics = await client.GetStatistics(cancellationToken).ConfigureAwait(false);
            var current = statistics.FirstOrDefault(entry => string.Equals(entry.Name, indexName, StringComparison.OrdinalIgnoreCase))?.UpdatedAt;

            if (previous is not null && previous == current)
            {
                return;
            }

            previous = current;
        }
    }
}
