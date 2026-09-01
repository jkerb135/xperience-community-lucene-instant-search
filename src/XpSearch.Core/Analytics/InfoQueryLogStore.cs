using CMS.DataEngine;

namespace XpSearch.Core.Analytics;

/// <summary>
/// Stores the query log in the <c>XpSearch.QueryLog</c> module class through the ObjectQuery API
/// (https://docs.kentico.com/documentation/developers-and-admins/api/objectquery-api), which is also
/// what keeps the queries parameterized.
/// </summary>
/// <remarks>
/// Info providers are synchronous for writes, so writes complete synchronously; reads use the
/// asynchronous ObjectQuery overloads and honour the cancellation token.
/// </remarks>
public sealed class InfoQueryLogStore : IQueryLogStore
{
    private readonly IInfoProvider<XpSearchQueryLogInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="InfoQueryLogStore"/> class.</summary>
    /// <param name="provider">Provider of the module class objects.</param>
    public InfoQueryLogStore(IInfoProvider<XpSearchQueryLogInfo> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
    }

    /// <inheritdoc />
    public Task AppendAsync(QueryLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        provider.Set(new XpSearchQueryLogInfo
        {
            LogQueryID = entry.QueryId,
            LogIndexName = entry.IndexName,
            LogQueryText = entry.QueryText,
            LogResultCount = entry.ResultCount,
            LogTimestamp = entry.Timestamp,
            LogChannelName = entry.ChannelName,
            LogLanguage = entry.Language,
            LogProcessingTimeMs = entry.ProcessingTimeMs,
            LogClickedPosition = entry.ClickedPosition ?? 0,
            LogClickedResultID = entry.ClickedResultId ?? string.Empty,
            LogExperimentID = entry.ExperimentId,
            LogVariant = entry.Variant ?? string.Empty
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> SetClickAsync(string queryId, int position, string resultId, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchQueryLogInfo.LogQueryID), queryId)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var row = rows.FirstOrDefault();

        if (row is null)
        {
            return false;
        }

        row.LogClickedPosition = position;
        row.LogClickedResultID = resultId ?? string.Empty;
        provider.Set(row);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueryLogEntry>> ReadAsync(string indexName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var query = provider.Get()
            .WhereGreaterOrEquals(nameof(XpSearchQueryLogInfo.LogTimestamp), fromUtc)
            .WhereLessOrEquals(nameof(XpSearchQueryLogInfo.LogTimestamp), toUtc)
            .OrderByAscending(nameof(XpSearchQueryLogInfo.LogTimestamp));

        if (!string.IsNullOrWhiteSpace(indexName))
        {
            query = query.WhereEquals(nameof(XpSearchQueryLogInfo.LogIndexName), indexName);
        }

        var rows = await query.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(ToEntry)];
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereLessThan(nameof(XpSearchQueryLogInfo.LogTimestamp), cutoffUtc)
            .OrderByAscending(nameof(XpSearchQueryLogInfo.LogTimestamp))
            .TopN(batchSize)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        int deleted = 0;

        foreach (var row in rows)
        {
            provider.Delete(row);
            deleted++;
        }

        return deleted;
    }

    private static QueryLogEntry ToEntry(XpSearchQueryLogInfo row) =>
        new(
            row.LogQueryID,
            row.LogIndexName,
            row.LogQueryText,
            row.LogResultCount,
            row.LogTimestamp,
            row.LogChannelName,
            row.LogLanguage,
            row.LogProcessingTimeMs,
            row.LogClickedPosition > 0 ? row.LogClickedPosition : null,
            row.LogExperimentID > 0 ? row.LogExperimentID : null,
            string.IsNullOrEmpty(row.LogVariant) ? null : row.LogVariant,
            string.IsNullOrEmpty(row.LogClickedResultID) ? null : row.LogClickedResultID);
}
