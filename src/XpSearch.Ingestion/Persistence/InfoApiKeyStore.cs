using CMS.DataEngine;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Security;

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// Stores API keys in the <c>XpSearch.ApiKey</c> module class.
/// </summary>
public sealed class InfoApiKeyStore : IApiKeyStore
{
    private readonly IInfoProvider<XpSearchApiKeyInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="InfoApiKeyStore"/> class.</summary>
    /// <param name="provider">Provider of the module class objects.</param>
    public InfoApiKeyStore(IInfoProvider<XpSearchApiKeyInfo> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeyRecord>> FindByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchApiKeyInfo.KeyPrefix), prefix)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToRecord)];
    }

    /// <inheritdoc />
    public Task<ApiKeyRecord> CreateAsync(ApiKeyRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var row = new XpSearchApiKeyInfo
        {
            KeyGuid = Guid.NewGuid(),
            KeyName = record.Name,
            KeyHash = record.Hash,
            KeyPrefix = record.Prefix,
            KeyScopes = ApiKeyService.Serialize(record.Scopes),
            KeyEnabled = record.Enabled,
            KeyExpiresAt = record.ExpiresAt,
        };

        provider.Set(row);

        return Task.FromResult(record with { Id = row.KeyID });
    }

    /// <inheritdoc />
    public async Task TouchAsync(int id, DateTime usedAt, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchApiKeyInfo.KeyID), id)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (rows.FirstOrDefault() is { } row)
        {
            row.KeyLastUsedAt = usedAt;
            provider.Set(row);
        }
    }

    private static ApiKeyRecord ToRecord(XpSearchApiKeyInfo row) =>
        new(
            row.KeyID,
            row.KeyName,
            row.KeyHash,
            row.KeyPrefix,
            ApiKeyService.Deserialize(row.KeyScopes),
            row.KeyEnabled,
            row.KeyExpiresAt,
            row.KeyLastUsedAt);
}

/// <summary>
/// Records write operations in the <c>XpSearch.IngestionLog</c> module class (spec §10.4).
/// </summary>
public sealed class InfoIngestionLog : IIngestionLog
{
    private readonly IInfoProvider<XpSearchIngestionLogInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="InfoIngestionLog"/> class.</summary>
    /// <param name="provider">Provider of the module class objects.</param>
    public InfoIngestionLog(IInfoProvider<XpSearchIngestionLogInfo> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
    }

    /// <inheritdoc />
    public Task WriteAsync(IngestionLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        provider.Set(new XpSearchIngestionLogInfo
        {
            LogGuid = Guid.NewGuid(),
            LogKeyPrefix = entry.KeyPrefix,
            LogIndexName = entry.IndexName,
            LogOperation = entry.Operation,
            LogDocumentCount = entry.DocumentCount,
            LogSucceeded = entry.Succeeded,
            LogMessage = entry.Message,
            LogCreatedAt = entry.At,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionLogEntry>> ReadRecentAsync(string indexName, int count, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchIngestionLogInfo.LogIndexName), indexName)
            .OrderByDescending(nameof(XpSearchIngestionLogInfo.LogCreatedAt))
            .TopN(count)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => new IngestionLogEntry(
                row.LogKeyPrefix,
                row.LogIndexName,
                row.LogOperation,
                row.LogDocumentCount,
                row.LogSucceeded,
                row.LogMessage,
                // Stored as UTC in a column without an offset, so the kind is stated rather than assumed.
                DateTime.SpecifyKind(row.LogCreatedAt, DateTimeKind.Utc)))
            .ToList();
    }
}
