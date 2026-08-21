using Kentico.Xperience.Lucene.Core;
using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

namespace XpSearch.Ingestion.Tests.Fixtures;

/// <summary>
/// A real Lucene index that behaves the way <c>Kentico.Xperience.Lucene</c>'s
/// <c>DefaultLuceneClient</c> behaves, so ingestion is exercised against the semantics it will meet in
/// production rather than a mock:
/// <list type="bullet">
/// <item><description><c>UpsertRecords</c> deletes by <c>ItemGuid</c> AND <c>LanguageName</c> before adding, and only when the document carries both (<c>UpsertRecordsInternal</c>).</description></item>
/// <item><description><c>DeleteRecords</c> deletes by a <c>Term("ItemGuid", …)</c> per identifier (<c>DeleteRecordsInternal</c>).</description></item>
/// <item><description><c>Rebuild</c> resets the index with <c>OpenMode.CREATE</c> and re-indexes Xperience content only (<c>RebuildInternal</c> → <c>ILuceneIndexService.ResetIndex</c>), which is exactly what wipes externally pushed documents.</description></item>
/// </list>
/// </summary>
internal sealed class TestLuceneIndex : ILuceneIndexAccessor, ILuceneClient, IDisposable
{
    private readonly RAMDirectory directory = new();
    private readonly Analyzer analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
    private readonly List<Document> xperienceContent;

    private DirectoryReader? reader;

    internal TestLuceneIndex(string indexName, IEnumerable<Document>? xperienceContent = null)
    {
        IndexName = indexName;
        this.xperienceContent = [.. xperienceContent ?? []];

        Reset(this.xperienceContent);
    }

    internal string IndexName { get; }

    internal int RebuildCount { get; private set; }

    /// <summary>Builds the kind of document <c>XpSearchIndexingStrategy</c> writes for Xperience content.</summary>
    internal static Document XperienceDocument(string itemGuid, string language, string title)
    {
        var document = new Document
        {
            new StringField(BaseDocumentProperties.ID, XpSearchIndexingStrategy.ComposeResultId(itemGuid, language), Field.Store.YES),
            new StringField(BaseDocumentProperties.ITEM_GUID, itemGuid, Field.Store.YES),
            new StringField(BaseDocumentProperties.LANGUAGE_NAME, language, Field.Store.YES),
            new StringField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource, Field.Store.YES),
            new TextField(IndexSchemaProvider.TitleField, title, Field.Store.YES)
        };

        return document;
    }

    public bool Exists(string indexName) => string.Equals(indexName, IndexName, StringComparison.OrdinalIgnoreCase);

    public Analyzer GetAnalyzer(string indexName) => analyzer;

    public FacetsConfig? GetFacetsConfig(string indexName) => null;

    public TResult UseSearcher<TResult>(string indexName, Func<IndexSearcher, TResult> use) => use(new IndexSearcher(OpenReader()));

    public TResult UseSearcherWithDrillSideways<TResult>(string indexName, Func<IndexSearcher, DrillSideways, TResult> use) =>
        throw new InvalidOperationException("The test index has no taxonomy sidecar.");

    public Task<int> UpsertRecords(IEnumerable<Document> documents, string indexName, CancellationToken cancellationToken)
    {
        int written = 0;

        Write(writer =>
        {
            foreach (var document in documents)
            {
                string? itemGuid = document.Get(BaseDocumentProperties.ITEM_GUID);
                string? language = document.Get(BaseDocumentProperties.LANGUAGE_NAME);

                if (itemGuid is not null && language is not null)
                {
                    var query = new BooleanQuery
                    {
                        { new TermQuery(new Term(BaseDocumentProperties.ITEM_GUID, itemGuid)), Occur.MUST },
                        { new TermQuery(new Term(BaseDocumentProperties.LANGUAGE_NAME, language)), Occur.MUST },
                    };

                    writer.DeleteDocuments(query);
                }

                writer.AddDocument(document);
                written++;
            }
        });

        return Task.FromResult(written);
    }

    public Task<int> DeleteRecords(IEnumerable<string> itemGuids, string indexName)
    {
        var ids = itemGuids.ToList();

        if (ids.Count == 0)
        {
            return Task.FromResult(0);
        }

        Write(writer =>
        {
            var query = new BooleanQuery();

            foreach (string id in ids)
            {
                query.Add(new TermQuery(new Term(BaseDocumentProperties.ITEM_GUID, id)), Occur.SHOULD);
            }

            writer.DeleteDocuments(query);
        });

        return Task.FromResult(ids.Count);
    }

    public Task Rebuild(string indexName, CancellationToken? cancellationToken)
    {
        RebuildCount++;
        Reset(xperienceContent);

        return Task.CompletedTask;
    }

    public Task<bool> DeleteIndex(LuceneIndex luceneIndex) => Task.FromResult(true);

    public Task<ICollection<LuceneIndexStatisticsModel>> GetStatistics(CancellationToken cancellationToken) =>
        Task.FromResult<ICollection<LuceneIndexStatisticsModel>>(
            [new LuceneIndexStatisticsModel { Name = IndexName, Entries = Count(), UpdatedAt = DateTime.UtcNow }]);

    /// <summary>Reads back every document identifier in the index, paired with its <c>_source</c>.</summary>
    internal IReadOnlyList<(string Id, string Source)> Documents() =>
        UseSearcher(IndexName, searcher =>
        {
            var hits = searcher.Search(new MatchAllDocsQuery(), Math.Max(1, searcher.IndexReader.NumDocs));

            return hits.ScoreDocs
                .Select(hit => searcher.Doc(hit.Doc))
                .Select(document => (document.Get(BaseDocumentProperties.ID), document.Get(LuceneFieldNames.SourceField)))
                .ToList();
        });

    /// <summary>Reads one stored field of one document, by identifier.</summary>
    internal string? Stored(string id, string field) =>
        UseSearcher(IndexName, searcher =>
        {
            var hits = searcher.Search(new TermQuery(new Term(BaseDocumentProperties.ID, id)), 1);

            return hits.ScoreDocs.Length == 0 ? null : searcher.Doc(hits.ScoreDocs[0].Doc).Get(field);
        });

    /// <summary>Counts documents matching a free-text term on a field.</summary>
    internal int Matching(string field, string term) =>
        UseSearcher(IndexName, searcher => searcher.Search(new TermQuery(new Term(field, term)), 1000).TotalHits);

    internal int Count() => UseSearcher(IndexName, searcher => searcher.IndexReader.NumDocs);

    public void Dispose()
    {
        reader?.Dispose();
        analyzer.Dispose();
        directory.Dispose();
    }

    private void Reset(IEnumerable<Document> documents)
    {
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE }))
        {
            foreach (var document in documents)
            {
                writer.AddDocument(document);
            }

            writer.Commit();
        }

        Refresh();
    }

    private void Write(Action<IndexWriter> write)
    {
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND }))
        {
            write(writer);
            writer.Commit();
        }

        Refresh();
    }

    private void Refresh()
    {
        reader?.Dispose();
        reader = null;
    }

    private DirectoryReader OpenReader() => reader ??= DirectoryReader.Open(directory);
}
