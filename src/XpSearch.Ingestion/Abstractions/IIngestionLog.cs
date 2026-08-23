namespace XpSearch.Ingestion.Abstractions;

/// <summary>
/// One write operation, as recorded for the audit trail spec §10.4 asks for: "clients will ask 'who
/// deleted our catalogue' eventually, and the answer needs to exist".
/// </summary>
/// <param name="KeyPrefix">First characters of the API key that made the request, or <c>in-process</c> for a direct call.</param>
/// <param name="IndexName">Code name of the index written to.</param>
/// <param name="Operation">What was asked for: <c>upsert</c>, <c>patch</c>, <c>delete</c>, <c>clear</c> or <c>rebuild</c>.</param>
/// <param name="DocumentCount">How many documents the operation touched.</param>
/// <param name="Succeeded">Whether the operation was accepted.</param>
/// <param name="Message">A short outcome description, including the first rejection when there was one.</param>
/// <param name="At">When the operation happened, in UTC.</param>
public sealed record IngestionLogEntry(
    string KeyPrefix,
    string IndexName,
    string Operation,
    int DocumentCount,
    bool Succeeded,
    string Message,
    DateTime At);

/// <summary>
/// Records write operations. The production implementation stores them in the
/// <c>XpSearchIngestionLog</c> custom module class, which the admin surface (spec §10.8) lists.
/// </summary>
public interface IIngestionLog
{
    /// <summary>Records one operation.</summary>
    /// <param name="entry">The operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is stored.</returns>
    Task WriteAsync(IngestionLogEntry entry, CancellationToken cancellationToken);

    /// <summary>Reads the most recent operations recorded against one index, newest first.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="count">How many entries to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries, newest first.</returns>
    Task<IReadOnlyList<IngestionLogEntry>> ReadRecentAsync(string indexName, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Carries who is making the current ingestion request, so the log can name them. Set by the API key
/// filter; defaults to an in-process caller.
/// </summary>
public interface IIngestionCaller
{
    /// <summary>Gets the identifying prefix of the API key in play, or <c>in-process</c>.</summary>
    string KeyPrefix { get; }
}
