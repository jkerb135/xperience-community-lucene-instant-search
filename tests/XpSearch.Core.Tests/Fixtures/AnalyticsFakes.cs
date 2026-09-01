using XpSearch.Core.Analytics;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>The query log in a list, so the analytics code can be tested without a database.</summary>
internal sealed class InMemoryQueryLogStore : IQueryLogStore
{
    internal List<QueryLogEntry> Rows { get; } = [];

    public Task AppendAsync(QueryLogEntry entry, CancellationToken cancellationToken)
    {
        Rows.Add(entry);

        return Task.CompletedTask;
    }

    public Task<bool> SetClickAsync(string queryId, int position, string resultId, CancellationToken cancellationToken)
    {
        int index = Rows.FindIndex(row => string.Equals(row.QueryId, queryId, StringComparison.Ordinal));

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Rows[index] = Rows[index] with
        {
            ClickedPosition = position,
            ClickedResultId = string.IsNullOrEmpty(resultId) ? null : resultId
        };

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<QueryLogEntry>> ReadAsync(string indexName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<QueryLogEntry> rows =
        [
            .. Rows
                .Where(row => (string.IsNullOrEmpty(indexName) || string.Equals(row.IndexName, indexName, StringComparison.OrdinalIgnoreCase))
                    && row.Timestamp >= fromUtc
                    && row.Timestamp <= toUtc)
                .OrderBy(row => row.Timestamp)
        ];

        return Task.FromResult(rows);
    }

    public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var doomed = Rows.Where(row => row.Timestamp < cutoffUtc).OrderBy(row => row.Timestamp).Take(batchSize).ToList();

        foreach (var row in doomed)
        {
            Rows.Remove(row);
        }

        return Task.FromResult(doomed.Count);
    }
}

/// <summary>Records what the pipeline and the event sink queue, without a worker thread.</summary>
internal sealed class RecordingQueryLogQueue : IQueryLogQueue
{
    internal List<QueryLogWorkItem> Items { get; } = [];

    public void Enqueue(QueryLogWorkItem item) => Items.Add(item);
}

/// <summary>Answers query suggestions from a fixed list.</summary>
internal sealed class FakeQuerySuggestionSource : IQuerySuggestionSource
{
    internal List<string> Suggestions { get; } = [];

    public Task<IReadOnlyList<string>> SuggestAsync(string indexName, string prefix, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. Suggestions.Take(limit)]);
}
