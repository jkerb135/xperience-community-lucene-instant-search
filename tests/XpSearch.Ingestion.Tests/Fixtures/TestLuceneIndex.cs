using Kentico.Xperience.Lucene.Core;
using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Facet.Taxonomy;
using Lucene.Net.Facet.Taxonomy.Directory;
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
/// <item><description>The reader is opened once and reused until <see cref="Invalidate"/> is called, the way the integration's cached <c>SearcherManager</c> behaves - so a write that forgets to invalidate is invisible here too.</description></item>
/// </list>
/// </summary>
internal sealed class TestLuceneIndex : ILuceneIndexAccessor, ILuceneClient, IDisposable
{
    private readonly RAMDirectory directory = new();
    private readonly RAMDirectory taxonomy = new();
    private readonly FacetsConfig config = new();
    private readonly Analyzer analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
    private readonly List<Document> xperienceContent;

    private DirectoryReader? reader;
    private DirectoryTaxonomyReader? taxonomyReader;

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
            new FacetField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource),
            new TextField(IndexSchemaProvider.TitleField, title, Field.Store.YES)
        };

        return document;
    }

    public bool Exists(string indexName) => string.Equals(indexName, IndexName, StringComparison.OrdinalIgnoreCase);

    public string? ResolveName(string indexName) => Exists(indexName) ? IndexName : null;

    public IReadOnlyList<string> IndexNames() => [IndexName];

    public Analyzer GetAnalyzer(string indexName) => analyzer;

    public IReadOnlyList<string> IndexNamesForStrategy(Type strategyType) => [IndexName];

    public FacetsConfig? GetFacetsConfig(string indexName) => config;

    public void Invalidate(string indexName) => Refresh();

    public TResult UseSearcher<TResult>(string indexName, Func<IndexSearcher, TResult> use) => use(new IndexSearcher(OpenReader()));

    public TResult UseSearcherWithDrillSideways<TResult>(string indexName, Func<IndexSearcher, DrillSideways, TResult> use)
    {
        var searcher = new IndexSearcher(OpenReader());

        return use(searcher, new DrillSideways(searcher, config, taxonomyReader));
    }

    public Task<int> UpsertRecords(IEnumerable<Document> documents, string indexName, CancellationToken cancellationToken)
    {
        int written = 0;

        Write((writer, taxonomyWriter) =>
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

                // What DefaultLuceneClient.UpsertRecordsInternal does with every document it writes,
                // whatever produced it: the facet fields become taxonomy ordinals here or nowhere.
                writer.AddDocument(config.Build(taxonomyWriter, document));
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

        Write((writer, _) =>
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
        taxonomyReader?.Dispose();
        analyzer.Dispose();
        directory.Dispose();
        taxonomy.Dispose();
    }

    private void Reset(IEnumerable<Document> documents)
    {
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE }))
        using (var taxonomyWriter = new DirectoryTaxonomyWriter(taxonomy, OpenMode.CREATE))
        {
            foreach (var document in documents)
            {
                writer.AddDocument(config.Build(taxonomyWriter, document));
            }

            taxonomyWriter.Commit();
            writer.Commit();
        }

        Refresh();
    }

    private void Write(Action<IndexWriter, DirectoryTaxonomyWriter> write)
    {
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND }))
        using (var taxonomyWriter = new DirectoryTaxonomyWriter(taxonomy, OpenMode.CREATE_OR_APPEND))
        {
            write(writer, taxonomyWriter);
            taxonomyWriter.Commit();
            writer.Commit();
        }

        // Deliberately no Refresh: an in-place write leaves the cached reader on the previous commit
        // point until something invalidates it, which is the production behaviour being guarded.
    }

    private void Refresh()
    {
        reader?.Dispose();
        reader = null;
        taxonomyReader?.Dispose();
        taxonomyReader = null;
    }

    private DirectoryReader OpenReader()
    {
        taxonomyReader ??= new DirectoryTaxonomyReader(taxonomy);

        return reader ??= DirectoryReader.Open(directory);
    }
}
