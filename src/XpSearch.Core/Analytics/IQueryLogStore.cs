namespace XpSearch.Core.Analytics;

/// <summary>One row of the aggregate query log (spec §9.2).</summary>
/// <param name="QueryId">Correlation id of the search, so a later click can find this row.</param>
/// <param name="IndexName">Code name of the index that was searched.</param>
/// <param name="QueryText">The searched text, normalized and lowercased.</param>
/// <param name="ResultCount">How many documents matched.</param>
/// <param name="Timestamp">When the search ran, in UTC.</param>
/// <param name="ChannelName">Code name of the website channel, or an empty string.</param>
/// <param name="Language">Language the search asked for, or an empty string.</param>
/// <param name="ProcessingTimeMs">Server-side processing time of the search, in milliseconds.</param>
/// <param name="ClickedPosition">One-based position of the clicked result, or <see langword="null"/>.</param>
/// <param name="ExperimentId">Identifier of the experiment that answered the search, or <see langword="null"/> (XP-1).</param>
/// <param name="Variant">Variant the visitor was bucketed into, or <see langword="null"/> when no experiment ran.</param>
/// <param name="ClickedResultId">
/// Result id of the clicked document, or <see langword="null"/>. It is what makes the click evidence
/// attributable to a document, which is all the popularity signal is built from (RK-1); the
/// <c>xpsearch_click</c> activity carries the same id but is consent-gated and per contact.
/// </param>
public sealed record QueryLogEntry(
    string QueryId,
    string IndexName,
    string QueryText,
    int ResultCount,
    DateTime Timestamp,
    string ChannelName,
    string Language,
    int ProcessingTimeMs,
    int? ClickedPosition = null,
    int? ExperimentId = null,
    string? Variant = null,
    string? ClickedResultId = null);

/// <summary>
/// Reads and writes the aggregate query log. Nothing here is personal data, so it works the same for
/// a visitor who consented to tracking and one who did not (spec §9.2).
/// </summary>
public interface IQueryLogStore
{
    /// <summary>Appends one logged search.</summary>
    /// <param name="entry">The row to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task AppendAsync(QueryLogEntry entry, CancellationToken cancellationToken);

    /// <summary>Records which result was clicked on an already logged search.</summary>
    /// <param name="queryId">Correlation id of the search.</param>
    /// <param name="position">One-based position of the clicked result.</param>
    /// <param name="resultId">Result id of the clicked document, or an empty string when the client sent none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a row was updated, <see langword="false"/> when the id is unknown.</returns>
    Task<bool> SetClickAsync(string queryId, int position, string resultId, CancellationToken cancellationToken);

    /// <summary>Reads the rows of one index in a time range, oldest first.</summary>
    /// <param name="indexName">Code name of the index, or an empty string for every index.</param>
    /// <param name="fromUtc">Start of the range, inclusive.</param>
    /// <param name="toUtc">End of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching rows.</returns>
    Task<IReadOnlyList<QueryLogEntry>> ReadAsync(string indexName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>Deletes at most one batch of rows older than a cut-off.</summary>
    /// <param name="cutoffUtc">Rows with an older timestamp are deleted.</param>
    /// <param name="batchSize">The largest number of rows to delete in this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);
}
