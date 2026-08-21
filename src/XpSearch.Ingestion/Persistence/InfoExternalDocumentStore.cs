using CMS.DataEngine;

using XpSearch.Ingestion.Abstractions;

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// Stores external documents in the <c>XpSearch.ExternalDocument</c> module class through the
/// ObjectQuery API (https://docs.kentico.com/documentation/developers-and-admins/api/objectquery-api),
/// which is also what keeps the queries parameterized.
/// </summary>
/// <remarks>
/// Info providers are synchronous for writes, so the write methods complete synchronously; reads use
/// the asynchronous ObjectQuery overloads and honour the cancellation token.
/// </remarks>
public sealed class InfoExternalDocumentStore : IExternalDocumentStore
{
    private readonly IInfoProvider<XpSearchExternalDocumentInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="InfoExternalDocumentStore"/> class.</summary>
    /// <param name="provider">Provider of the module class objects.</param>
    public InfoExternalDocumentStore(IInfoProvider<XpSearchExternalDocumentInfo> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
    }

    /// <inheritdoc />
    public async Task<ExternalDocumentRecord?> GetAsync(string indexName, string id, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentKey), id)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToRecord).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalDocumentRecord>> GetManyAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .WhereIn(nameof(XpSearchExternalDocumentInfo.DocumentKey), [.. ids])
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToRecord)];
    }

    /// <inheritdoc />
    public async Task<int> UpsertAsync(IReadOnlyList<ExternalDocumentRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        int written = 0;

        foreach (var record in records)
        {
            var existing = await FindAsync(record.IndexName, record.Id, cancellationToken).ConfigureAwait(false)
                ?? new XpSearchExternalDocumentInfo
                {
                    DocumentGuid = Guid.NewGuid(),
                    DocumentIndexName = record.IndexName,
                    DocumentKey = record.Id,
                    DocumentCreatedAt = record.CreatedAt,
                };

            existing.DocumentSource = record.Source;
            existing.DocumentBody = record.Json;
            existing.DocumentHash = record.ContentHash;
            existing.DocumentUpdatedAt = record.UpdatedAt;
            existing.DocumentStatus = (int)record.Status;

            provider.Set(existing);
            written++;
        }

        return written;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .WhereIn(nameof(XpSearchExternalDocumentInfo.DocumentKey), [.. ids])
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalDocumentRecord>> ListAsync(string indexName, string? source, CancellationToken cancellationToken)
    {
        var query = provider.Get().WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName);

        if (source is not null)
        {
            query = query.WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentSource), source);
        }

        var rows = await query.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(ToRecord)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalDocumentRecord>> ListPendingAsync(CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentStatus), (int)ExternalDocumentStatus.Pending)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToRecord)];
    }

    /// <inheritdoc />
    public async Task MarkIndexedAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return;
        }

        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .WhereIn(nameof(XpSearchExternalDocumentInfo.DocumentKey), [.. ids])
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows.Where(row => row.DocumentStatus != (int)ExternalDocumentStatus.Indexed))
        {
            row.DocumentStatus = (int)ExternalDocumentStatus.Indexed;
            provider.Set(row);
        }
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLastWriteAsync(string indexName, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .OrderByDescending(nameof(XpSearchExternalDocumentInfo.DocumentUpdatedAt))
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => (DateTime?)row.DocumentUpdatedAt).FirstOrDefault();
    }

    private static ExternalDocumentRecord ToRecord(XpSearchExternalDocumentInfo row) =>
        new(
            row.DocumentIndexName,
            row.DocumentSource,
            row.DocumentKey,
            row.DocumentBody,
            row.DocumentHash,
            row.DocumentCreatedAt,
            row.DocumentUpdatedAt,
            (ExternalDocumentStatus)row.DocumentStatus);

    private async Task<XpSearchExternalDocumentInfo?> FindAsync(string indexName, string id, CancellationToken cancellationToken)
    {
        var rows = await provider.Get()
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentIndexName), indexName)
            .WhereEquals(nameof(XpSearchExternalDocumentInfo.DocumentKey), id)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.FirstOrDefault();
    }
}
