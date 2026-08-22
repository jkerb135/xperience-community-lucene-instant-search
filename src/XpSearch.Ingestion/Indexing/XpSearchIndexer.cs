using System.Diagnostics;
using System.Text.Json;

using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Schema;

namespace XpSearch.Ingestion.Indexing;

/// <summary>
/// The in-process ingestion API (spec §10.6), and the implementation the HTTP endpoints call.
/// </summary>
/// <remarks>
/// Every write persists first and indexes second (ADR-0005): the row is committed before the work is
/// queued, so an application restart mid-queue costs latency rather than data. <c>waitForIndex</c>
/// only decides whether the Lucene half runs on the caller's thread or on the ingestion queue.
/// </remarks>
public sealed class XpSearchIndexer : IXpSearchIndexer
{
    private readonly IExternalDocumentStore store;
    private readonly IIngestionSchemaProvider schemas;
    private readonly IIngestionQueue queue;
    private readonly IIngestionWorkProcessor processor;
    private readonly IFieldTypeGuard fieldTypes;
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIngestionLog log;
    private readonly IIngestionCaller caller;
    private readonly XpSearchIngestionOptions options;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIndexer"/> class.</summary>
    /// <param name="store">Where documents are persisted.</param>
    /// <param name="schemas">Supplies the schema documents are validated against.</param>
    /// <param name="queue">The background ingestion queue.</param>
    /// <param name="processor">Runs index work inline when <c>waitForIndex</c> is set.</param>
    /// <param name="fieldTypes">Detects a field whose type contradicts the live index.</param>
    /// <param name="accessor">The Lucene reader seam, used for document counts.</param>
    /// <param name="log">The ingestion audit log.</param>
    /// <param name="caller">Who is making the request.</param>
    /// <param name="options">Ingestion configuration.</param>
    /// <param name="time">Clock, substitutable in tests.</param>
    public XpSearchIndexer(
        IExternalDocumentStore store,
        IIngestionSchemaProvider schemas,
        IIngestionQueue queue,
        IIngestionWorkProcessor processor,
        IFieldTypeGuard fieldTypes,
        ILuceneIndexAccessor accessor,
        IIngestionLog log,
        IIngestionCaller caller,
        IOptions<XpSearchIngestionOptions> options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(fieldTypes);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        this.store = store;
        this.schemas = schemas;
        this.queue = queue;
        this.processor = processor;
        this.fieldTypes = fieldTypes;
        this.accessor = accessor;
        this.log = log;
        this.caller = caller;
        this.options = options.Value;
        this.time = time;
    }

    /// <inheritdoc />
    public async Task<UpsertResponse> UpsertAsync(
        string index,
        IEnumerable<SearchDocument> documents,
        bool waitForIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);
        ArgumentNullException.ThrowIfNull(documents);

        var started = Stopwatch.GetTimestamp();
        var schema = await schemas.GetSchemaAsync(index, cancellationToken).ConfigureAwait(false);
        var pushed = documents.ToList();
        var errors = new List<IngestionError>();
        var accepted = new List<ExternalDocumentRecord>();
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var document in pushed)
        {
            var validated = DocumentValidator.Validate(schema, document.Id, document.Source, document.Attributes, options.DefaultSource);

            if (validated.Errors.Count > 0)
            {
                errors.AddRange(validated.Errors);
                continue;
            }

            accepted.Add(ToRecord(index, validated, now));
        }

        // A field whose type no longer matches the index is not a per-document problem: writing any
        // document that carries it would corrupt sorting and range filters, so the whole batch stops.
        var typeChanges = fieldTypes.Check(index, schema.Fields, accepted.SelectMany(record => AttributeNames(record.Json)));

        if (typeChanges.Count > 0)
        {
            await LogAsync(index, "upsert", pushed.Count, succeeded: false, typeChanges[0].Message, cancellationToken).ConfigureAwait(false);

            return new UpsertResponse
            {
                Indexed = 0,
                Failed = pushed.Count,
                Errors = [.. typeChanges, .. errors],
                TookMs = Elapsed(started),
            };
        }

        await store.UpsertAsync(accepted, cancellationToken).ConfigureAwait(false);

        string? taskId = await ScheduleAsync(
            IngestionWorkItem.New(index, IngestionOperation.Upsert, accepted.Select(record => record.Id).ToList()),
            waitForIndex,
            cancellationToken).ConfigureAwait(false);

        await LogAsync(index, "upsert", accepted.Count, errors.Count == 0, Outcome(errors), cancellationToken).ConfigureAwait(false);

        return new UpsertResponse
        {
            Indexed = accepted.Count,
            Failed = errors.Count,
            Errors = [.. errors],
            TaskId = taskId,
            TookMs = Elapsed(started),
        };
    }

    /// <inheritdoc />
    public async Task<UpsertResponse> PatchAsync(
        string index,
        string id,
        IReadOnlyDictionary<string, JsonElement> attributes,
        bool waitForIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(attributes);

        var existing = await store.GetAsync(index, id, cancellationToken).ConfigureAwait(false)
            ?? throw new DocumentNotFoundException(index, id);

        // Read-modify-rewrite: Lucene has no in-place update, and neither does the stored row.
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        using (var body = JsonDocument.Parse(existing.Json))
        {
            foreach (var property in body.RootElement.EnumerateObject())
            {
                merged[property.Name] = property.Value.Clone();
            }
        }

        foreach (var (name, value) in attributes)
        {
            if (value.ValueKind is JsonValueKind.Null)
            {
                merged.Remove(name);
                continue;
            }

            merged[name] = value;
        }

        return await UpsertAsync(index, [new SearchDocument(id, existing.Source, merged)], waitForIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DeleteResponse> DeleteAsync(
        string index,
        IEnumerable<string> ids,
        bool waitForIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);
        ArgumentNullException.ThrowIfNull(ids);

        var started = Stopwatch.GetTimestamp();
        var identifiers = ids.Distinct(StringComparer.Ordinal).ToList();
        int deleted = await store.DeleteAsync(index, identifiers, cancellationToken).ConfigureAwait(false);

        string? taskId = await ScheduleAsync(
            IngestionWorkItem.New(index, IngestionOperation.Delete, identifiers),
            waitForIndex,
            cancellationToken).ConfigureAwait(false);

        await LogAsync(index, "delete", deleted, succeeded: true, $"{deleted} document(s) deleted.", cancellationToken).ConfigureAwait(false);

        return new DeleteResponse { Deleted = deleted, TaskId = taskId, TookMs = Elapsed(started) };
    }

    /// <inheritdoc />
    public async Task<DeleteResponse> DeleteBySourceAsync(
        string index,
        string? source,
        bool waitForIndex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);

        if (string.Equals(source, LuceneFieldNames.XperienceSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new IngestionValidationException(
                "source",
                $"'{LuceneFieldNames.XperienceSource}' is content managed by Xperience; the ingestion API never deletes it.");
        }

        var started = Stopwatch.GetTimestamp();

        // Only stored external documents are ever named, so no path through here can reach a document
        // the Lucene integration owns - that is the isolation spec §10.2 requires.
        var records = await store.ListAsync(index, source, cancellationToken).ConfigureAwait(false);
        var identifiers = records.Select(record => record.Id).ToList();
        int deleted = await store.DeleteAsync(index, identifiers, cancellationToken).ConfigureAwait(false);

        string? taskId = await ScheduleAsync(
            IngestionWorkItem.New(index, IngestionOperation.Delete, identifiers),
            waitForIndex,
            cancellationToken).ConfigureAwait(false);

        await LogAsync(index, "clear", deleted, succeeded: true, $"{deleted} document(s) of source '{source ?? "*"}' deleted.", cancellationToken).ConfigureAwait(false);

        return new DeleteResponse { Deleted = deleted, TaskId = taskId, TookMs = Elapsed(started) };
    }

    /// <inheritdoc />
    public async Task<IndexStatus> GetStatusAsync(string index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);

        if (!accessor.Exists(index))
        {
            throw new Core.Abstractions.IndexNotFoundException(index);
        }

        var lastWrite = await store.GetLastWriteAsync(index, cancellationToken).ConfigureAwait(false);
        var counts = CountBySource(index);

        return new IndexStatus
        {
            Index = index,
            Documents = new DocumentCounts { Total = counts.Total, BySource = counts.BySource },
            // Stored as UTC; the column has no offset, so the kind is stated rather than assumed.
            LastWrite = lastWrite is { } written ? new DateTimeOffset(DateTime.SpecifyKind(written, DateTimeKind.Utc)) : null,
            Health = queue.PendingCount > 0 ? Health.Degraded : Health.Healthy,
        };
    }

    private static IEnumerable<string> AttributeNames(string json)
    {
        using var body = JsonDocument.Parse(json);

        return body.RootElement.EnumerateObject().Select(property => property.Name).ToList();
    }

    private static int Elapsed(long started) => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Outcome(List<IngestionError> errors) =>
        errors.Count == 0 ? "Accepted." : $"{errors.Count} document(s) rejected; first: {errors[0].Message}";

    /// <summary>
    /// Counts the live documents of the index, in total and per <c>_source</c>.
    /// </summary>
    /// <remarks>
    /// The per-source counts are searches, not <c>IndexReader.DocFreq</c>: a term's document
    /// frequency includes documents that have been deleted but whose segment has not been merged away
    /// yet, so every replaced document was counted twice and the sources added up to more than
    /// <c>NumDocs</c> (a reindexed 32-document index reported 64 under <c>xperience</c>). A search
    /// applies the segments' live-document bits, so both figures count the same documents.
    /// </remarks>
    private (long Total, Dictionary<string, long> BySource) CountBySource(string index) =>
        accessor.UseSearcher(index, searcher =>
        {
            var reader = searcher.IndexReader;
            var bySource = new Dictionary<string, long>(StringComparer.Ordinal);
            var terms = MultiFields.GetTerms(reader, LuceneFieldNames.SourceField);

            if (terms is not null)
            {
                var enumerator = terms.GetEnumerator();

                while (enumerator.MoveNext())
                {
                    var term = new Term(LuceneFieldNames.SourceField, BytesRef.DeepCopyOf(enumerator.Term));
                    var counter = new TotalHitCountCollector();

                    searcher.Search(new TermQuery(term), counter);

                    if (counter.TotalHits > 0)
                    {
                        bySource[term.Text] = counter.TotalHits;
                    }
                }
            }

            return ((long)reader.NumDocs, bySource);
        });

    private ExternalDocumentRecord ToRecord(string index, ValidatedDocument document, DateTime now)
    {
        string json = ExternalDocumentFactory.ToJson(document.Attributes);

        return new ExternalDocumentRecord(
            index,
            document.Source,
            document.Id,
            json,
            ExternalDocumentFactory.Hash(json),
            now,
            now,
            ExternalDocumentStatus.Pending);
    }

    private async Task<string?> ScheduleAsync(IngestionWorkItem item, bool waitForIndex, CancellationToken cancellationToken)
    {
        if (item.Ids.Count == 0)
        {
            return null;
        }

        if (waitForIndex)
        {
            await processor.ProcessAsync(item, cancellationToken).ConfigureAwait(false);

            return null;
        }

        queue.Enqueue(item);

        return item.TaskId;
    }

    private Task LogAsync(string index, string operation, int count, bool succeeded, string message, CancellationToken cancellationToken) =>
        log.WriteAsync(
            new IngestionLogEntry(caller.KeyPrefix, index, operation, count, succeeded, message, time.GetUtcNow().UtcDateTime),
            cancellationToken);
}
