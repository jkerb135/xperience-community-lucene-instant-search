using XpSearch.Ingestion.Abstractions;

namespace XpSearch.Ingestion.Indexing;

/// <summary>What the ingestion log says about the state of a rebuild.</summary>
public enum RebuildPhase
{
    /// <summary>No rebuild was ever recorded for the index.</summary>
    None,

    /// <summary>A rebuild started and has not been recorded as finished yet.</summary>
    Running,

    /// <summary>A rebuild started long enough ago that it should have finished, and never did.</summary>
    Stuck,

    /// <summary>The last recorded rebuild finished.</summary>
    Finished,
}

/// <summary>
/// The state of the last rebuild of one index, derived from the ingestion log: the started row the
/// admin page writes and the finished row <see cref="ExternalDocumentWriter"/> writes when the
/// post-rebuild replay completes.
/// </summary>
/// <remarks>
/// <c>Kentico.Xperience.Lucene</c> 15.0.5 answers no question about a rebuild - <c>ILuceneClient</c>
/// is <c>Rebuild</c> / <c>UpsertRecords</c> / <c>DeleteRecords</c> / <c>DeleteIndex</c> /
/// <c>GetStatistics</c>, and <c>LuceneIndexStatisticsModel</c> carries a live entry count, never a
/// target - so the log rows are the only durable record that survives a page reload.
/// </remarks>
/// <param name="Phase">Where the rebuild is.</param>
/// <param name="StartedAt">When it started, or <see langword="null"/> when no start was recorded.</param>
/// <param name="FinishedAt">When it finished, or <see langword="null"/> while it runs.</param>
/// <param name="Documents">How many documents the index held when it finished, or <see langword="null"/>.</param>
public sealed record RebuildProgress(RebuildPhase Phase, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, long? Documents)
{
    /// <summary>The ingestion log operation of the row written when a rebuild is triggered.</summary>
    public const string StartedOperation = "rebuild";

    /// <summary>The ingestion log operation of the row written when the post-rebuild replay completed.</summary>
    public const string FinishedOperation = "rebuild-finished";

    /// <summary>No rebuild was ever recorded.</summary>
    public static readonly RebuildProgress Never = new(RebuildPhase.None, null, null, null);

    /// <summary>Gets a value indicating whether a rebuild is going on now, stuck or not.</summary>
    public bool Running => Phase is RebuildPhase.Running or RebuildPhase.Stuck;

    /// <summary>Derives the state from the two log rows that describe a rebuild.</summary>
    /// <param name="started">The latest started row, or <see langword="null"/>.</param>
    /// <param name="finished">The latest finished row, or <see langword="null"/>.</param>
    /// <param name="now">The current moment.</param>
    /// <param name="stuckAfter">How long a started rebuild may stay unfinished before it is suspect.</param>
    /// <returns>The state.</returns>
    public static RebuildProgress From(IngestionLogEntry? started, IngestionLogEntry? finished, DateTimeOffset now, TimeSpan stuckAfter)
    {
        var startedAt = Moment(started);
        var finishedAt = Moment(finished);

        if (startedAt is { } start && (finishedAt is null || finishedAt < start))
        {
            return new RebuildProgress(now - start > stuckAfter ? RebuildPhase.Stuck : RebuildPhase.Running, start, null, null);
        }

        // A rebuild started from Kentico's own Search application never writes a started row, so a
        // finished row on its own is a complete answer: "finished at ...", with no elapsed time.
        return finishedAt is null
            ? Never
            : new RebuildProgress(RebuildPhase.Finished, startedAt, finishedAt, finished!.DocumentCount);
    }

    /// <summary>Reads the two rows that describe the last rebuild of an index and derives its state.</summary>
    /// <param name="log">The ingestion log.</param>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="now">The current moment.</param>
    /// <param name="stuckAfter">How long a started rebuild may stay unfinished before it is suspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state.</returns>
    public static async Task<RebuildProgress> ReadAsync(
        IIngestionLog log,
        string indexName,
        DateTimeOffset now,
        TimeSpan stuckAfter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        var started = await log.ReadLatestAsync(indexName, StartedOperation, cancellationToken).ConfigureAwait(false);
        var finished = await log.ReadLatestAsync(indexName, FinishedOperation, cancellationToken).ConfigureAwait(false);

        return From(started, finished, now, stuckAfter);
    }

    /// <summary>The log stores UTC in a column without an offset, so the kind is stated rather than assumed.</summary>
    private static DateTimeOffset? Moment(IngestionLogEntry? entry) =>
        entry is null ? null : new DateTimeOffset(DateTime.SpecifyKind(entry.At, DateTimeKind.Utc));
}
