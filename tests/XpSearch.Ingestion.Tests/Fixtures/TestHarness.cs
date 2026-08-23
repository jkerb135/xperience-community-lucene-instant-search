using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Schema;
using XpSearch.Ingestion.Security;

namespace XpSearch.Ingestion.Tests.Fixtures;

/// <summary>An in-memory stand-in for the module class store; the production one needs a database.</summary>
internal sealed class InMemoryDocumentStore : IExternalDocumentStore
{
    private readonly Dictionary<(string Index, string Id), ExternalDocumentRecord> rows = new();

    internal IReadOnlyCollection<ExternalDocumentRecord> Rows => rows.Values;

    public Task<ExternalDocumentRecord?> GetAsync(string indexName, string id, CancellationToken cancellationToken) =>
        Task.FromResult(rows.TryGetValue((indexName, id), out var record) ? record : null);

    public Task<IReadOnlyList<ExternalDocumentRecord>> GetManyAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExternalDocumentRecord>>(
            [.. ids.Select(id => rows.TryGetValue((indexName, id), out var record) ? record : null).OfType<ExternalDocumentRecord>()]);

    public Task<int> UpsertAsync(IReadOnlyList<ExternalDocumentRecord> records, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            var created = rows.TryGetValue((record.IndexName, record.Id), out var existing) ? existing.CreatedAt : record.CreatedAt;
            rows[(record.IndexName, record.Id)] = record with { CreatedAt = created };
        }

        return Task.FromResult(records.Count);
    }

    public Task<int> DeleteAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
        Task.FromResult(ids.Count(id => rows.Remove((indexName, id))));

    public Task<IReadOnlyList<ExternalDocumentRecord>> ListAsync(string indexName, string? source, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExternalDocumentRecord>>(
        [
            .. rows.Values.Where(record =>
                string.Equals(record.IndexName, indexName, StringComparison.OrdinalIgnoreCase)
                && (source is null || string.Equals(record.Source, source, StringComparison.OrdinalIgnoreCase)))
        ]);

    public Task<IReadOnlyList<ExternalDocumentRecord>> ListPendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExternalDocumentRecord>>([.. rows.Values.Where(record => record.Status == ExternalDocumentStatus.Pending)]);

    public Task MarkIndexedAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        foreach (string id in ids.Where(id => rows.ContainsKey((indexName, id))))
        {
            rows[(indexName, id)] = rows[(indexName, id)] with { Status = ExternalDocumentStatus.Indexed };
        }

        return Task.CompletedTask;
    }

    public Task<DateTime?> GetLastWriteAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(rows.Values
            .Where(record => string.Equals(record.IndexName, indexName, StringComparison.OrdinalIgnoreCase))
            .Select(record => (DateTime?)record.UpdatedAt)
            .OrderDescending()
            .FirstOrDefault());
}

/// <summary>An in-memory stand-in for the API key module class.</summary>
internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly List<ApiKeyRecord> keys = [];

    internal IReadOnlyList<ApiKeyRecord> Keys => keys;

    internal void Replace(ApiKeyRecord key)
    {
        keys.RemoveAll(existing => existing.Id == key.Id);
        keys.Add(key);
    }

    public Task<IReadOnlyList<ApiKeyRecord>> FindByPrefixAsync(string prefix, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApiKeyRecord>>([.. keys.Where(key => key.Prefix == prefix)]);

    public Task<ApiKeyRecord> CreateAsync(ApiKeyRecord record, CancellationToken cancellationToken)
    {
        var stored = record with { Id = keys.Count + 1 };
        keys.Add(stored);

        return Task.FromResult(stored);
    }

    public Task TouchAsync(int id, DateTime usedAt, CancellationToken cancellationToken)
    {
        int at = keys.FindIndex(key => key.Id == id);

        if (at >= 0)
        {
            keys[at] = keys[at] with { LastUsedAt = usedAt };
        }

        return Task.CompletedTask;
    }
}

/// <summary>Records log entries instead of writing them to the ingestion log module class.</summary>
internal sealed class RecordingIngestionLog : IIngestionLog
{
    internal List<IngestionLogEntry> Entries { get; } = [];

    public Task WriteAsync(IngestionLogEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IngestionLogEntry>> ReadRecentAsync(string indexName, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IngestionLogEntry>>(
            Entries.Where(entry => entry.IndexName == indexName)
                .OrderByDescending(entry => entry.At)
                .Take(count)
                .ToList());
}

/// <summary>
/// Holds queued work instead of handing it to the Xperience worker thread, so a test can decide when
/// - and whether - the Lucene half of a write happens.
/// </summary>
internal sealed class ManualIngestionQueue : IIngestionQueue
{
    private readonly IIngestionWorkProcessor processor;

    internal ManualIngestionQueue(IIngestionWorkProcessor processor) => this.processor = processor;

    internal List<IngestionWorkItem> Queued { get; } = [];

    /// <summary>Set by a test to stand in for work the worker thread could not write to Lucene.</summary>
    public int FailedCount { get; set; }

    public void Enqueue(IngestionWorkItem item) => Queued.Add(item);

    /// <summary>Runs everything the queue is holding, the way the worker thread would.</summary>
    internal async Task DrainAsync()
    {
        var items = Queued.ToList();
        Queued.Clear();

        foreach (var item in items)
        {
            await processor.ProcessAsync(item, CancellationToken.None);
        }
    }
}

/// <summary>Serves one fixed schema.</summary>
internal sealed class StaticSchemaProvider : IIngestionSchemaProvider
{
    private readonly IngestionSchema schema;

    internal StaticSchemaProvider(IngestionSchema schema) => this.schema = schema;

    public Task<IngestionSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(schema);
}

/// <summary>The rebuild replay runs immediately; the production waiter polls index statistics.</summary>
internal sealed class ImmediateRebuildWaiter : IRebuildCompletionWaiter
{
    public Task WaitAsync(string indexName, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Caches nothing; the response cache has its own tests.</summary>
internal sealed class NullSearchCache : ISearchCache
{
    public Task<SearchResponse> GetOrAddAsync(
        string indexName,
        string key,
        Func<CancellationToken, Task<SearchResponse>> factory,
        CancellationToken cancellationToken) => factory(cancellationToken);

    public void Evict(string indexName)
    {
    }
}

/// <summary>Serves the harness's schema to the query pipeline.</summary>
internal sealed class FixedSchemaProvider(IndexSchema schema) : IIndexSchemaProvider
{
    public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(schema);
}

/// <summary>Names the caller in the ingestion log.</summary>
internal sealed class FixedCaller(string prefix) : IIngestionCaller
{
    public string KeyPrefix { get; } = prefix;
}

/// <summary>
/// Wires the production ingestion services around a real Lucene index and in-memory persistence, so
/// tests exercise the shipped code paths rather than mocks of them.
/// </summary>
internal sealed class TestHarness : IDisposable
{
    internal const string IndexName = "products";

    internal TestHarness(IngestionSchema? schema = null, XpSearchIngestionOptions? options = null, IEnumerable<Lucene.Net.Documents.Document>? xperienceContent = null)
    {
        Options = options ?? new XpSearchIngestionOptions();
        Index = new TestLuceneIndex(IndexName, xperienceContent);
        Store = new InMemoryDocumentStore();
        Log = new RecordingIngestionLog();
        Schema = schema ?? TestSchema.Products();

        // AddXpSearch decorates ILuceneClient before AddXpSearchIngestion does, so every ingestion
        // write really goes through CacheEvictingLuceneClient - which is what invalidates the
        // integration's cached searcher. The rebuild replay lives in the outer decorator that Client
        // exposes, the only path a rebuild ever takes.
        var evicting = new CacheEvictingLuceneClient(Index, new NullSearchCache(), Index);

        Writer = new ExternalDocumentWriter(
            Store,
            evicting,
            new StaticSchemaProvider(Schema),
            new ImmediateRebuildWaiter(),
            NullLogger<ExternalDocumentWriter>.Instance);

        Queue = new ManualIngestionQueue(Writer);
        Client = new ExternalDocumentReplayLuceneClient(evicting, Queue, NullLogger<ExternalDocumentReplayLuceneClient>.Instance);

        Indexer = new XpSearchIndexer(
            Store,
            new StaticSchemaProvider(Schema),
            Queue,
            Writer,
            new FieldTypeGuard(Index),
            Index,
            Log,
            new FixedCaller("test1234"),
            Microsoft.Extensions.Options.Options.Create(Options),
            TimeProvider.System);
    }

    internal XpSearchIngestionOptions Options { get; }

    internal TestLuceneIndex Index { get; }

    internal InMemoryDocumentStore Store { get; }

    internal RecordingIngestionLog Log { get; }

    internal IngestionSchema Schema { get; }

    internal ExternalDocumentWriter Writer { get; }

    internal ManualIngestionQueue Queue { get; }

    internal XpSearchIndexer Indexer { get; }

    /// <summary>The production write client as a host would resolve it: the replay decorator in front of the index.</summary>
    internal Kentico.Xperience.Lucene.Core.Indexing.ILuceneClient Client { get; }

    /// <summary>
    /// The query pipeline a host would resolve, over this harness's index and schema: the ingestion
    /// tests that assert what a pushed document looks like to a search read it through the real
    /// stages rather than through Lucene directly.
    /// </summary>
    /// <returns>The pipeline.</returns>
    internal ISearchPipeline Pipeline()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new XpSearchOptions());

        return new SearchPipeline(
            Index,
            new FixedSchemaProvider(Schema.Fields),
            [
                new NormalizeRequestStage(options),
                new BuildQueryStage(),
                new FacetFilterStage(),
                new NumericFilterStage(),
                new ExecuteSearchStage(Index),
                new CollectFacetsStage(new TaxonomyFacetProvider(Index), options),
                new HighlightStage(new LuceneHighlighter()),
                new ProjectResponseStage()
            ]);
    }

    internal static SearchDocument Document(string id, string source = "pim", params (string Name, object? Value)[] attributes) =>
        SearchDocument.Create(id, source, attributes.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.OrdinalIgnoreCase));

    internal static JsonElement Value(object? value) => JsonSerializer.SerializeToElement(value);

    public void Dispose() => Index.Dispose();
}

/// <summary>The schema the tests validate against: a small external product catalogue.</summary>
internal static class TestSchema
{
    internal static IngestionSchema Products(bool allowDynamicFields = false) =>
        new(
            new IndexSchema(
                TestHarness.IndexName,
                [
                    // Declared fields first, the precedence IngestionSchemaProvider applies: a declared
                    // field wins over the detected base field of the same name.
                    new SchemaField("title", SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: true, Retrievable: true),
                    new SchemaField("sku", SearchFieldKind.Keyword, Searchable: false, Facetable: true, Sortable: false, Retrievable: true),
                    new SchemaField("price", SearchFieldKind.Number, Searchable: false, Facetable: false, Sortable: true, Retrievable: true),
                    new SchemaField("publishedAt", SearchFieldKind.Date, Searchable: false, Facetable: false, Sortable: true, Retrievable: true),
                    new SchemaField("inStock", SearchFieldKind.Boolean, Searchable: false, Facetable: true, Sortable: false, Retrievable: true),
                    new SchemaField("tags", SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
                    .. IndexSchemaProvider.BaseFields(),
                ]),
            allowDynamicFields);
}
