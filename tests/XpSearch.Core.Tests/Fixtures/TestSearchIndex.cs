using Kentico.Xperience.Lucene.Core;

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

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>
/// A real Lucene index with a taxonomy sidecar, standing in for
/// <c>Kentico.Xperience.Lucene</c>'s reader side. Built exactly the way
/// <c>DefaultLuceneClient.UpsertRecordsInternal</c> builds one:
/// <c>facetsConfig.Build(taxonomyWriter, document)</c> into an index writer, with the taxonomy
/// committed alongside.
/// </summary>
internal sealed class TestSearchIndex : ILuceneIndexAccessor, IDisposable
{
    private readonly RAMDirectory main = new();
    private readonly RAMDirectory taxonomy = new();
    private readonly FacetsConfig config = new();
    private readonly Analyzer analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
    private readonly string indexName;
    private readonly bool withTaxonomy;

    private DirectoryReader? reader;
    private DirectoryTaxonomyReader? taxonomyReader;

    internal TestSearchIndex(string indexName, IEnumerable<TestDocument> documents, bool withTaxonomy = true)
    {
        this.indexName = indexName;
        this.withTaxonomy = withTaxonomy;

        config.SetMultiValued(TestCorpus.CategoryField, true);
        config.SetMultiValued(TestCorpus.TagsField, true);

        Write(documents);
    }

    public bool Exists(string name) => string.Equals(name, indexName, StringComparison.OrdinalIgnoreCase);

    public Analyzer GetAnalyzer(string name) => analyzer;

    public FacetsConfig? GetFacetsConfig(string name) => withTaxonomy ? config : null;

    public TResult UseSearcher<TResult>(string name, Func<IndexSearcher, TResult> use) =>
        use(new IndexSearcher(OpenReader()));

    public TResult UseSearcherWithDrillSideways<TResult>(string name, Func<IndexSearcher, DrillSideways, TResult> use)
    {
        if (!withTaxonomy)
        {
            throw new InvalidOperationException("The index has no taxonomy sidecar.");
        }

        var searcher = new IndexSearcher(OpenReader());

        return use(searcher, new DrillSideways(searcher, config, taxonomyReader));
    }

    public void Dispose()
    {
        reader?.Dispose();
        taxonomyReader?.Dispose();
        analyzer.Dispose();
        main.Dispose();
        taxonomy.Dispose();
    }

    private DirectoryReader OpenReader()
    {
        reader ??= DirectoryReader.Open(main);

        if (withTaxonomy)
        {
            taxonomyReader ??= new DirectoryTaxonomyReader(taxonomy);
        }

        return reader;
    }

    private void Write(IEnumerable<TestDocument> documents)
    {
        using var writer = new IndexWriter(main, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
        using var taxonomyWriter = new DirectoryTaxonomyWriter(taxonomy);

        foreach (var document in documents)
        {
            var lucene = ToLuceneDocument(document, withTaxonomy);
            writer.AddDocument(withTaxonomy ? config.Build(taxonomyWriter, lucene) : lucene);
        }

        taxonomyWriter.Commit();
        writer.Commit();
    }

    private static Document ToLuceneDocument(TestDocument source, bool withTaxonomy)
    {
        var document = new Document
        {
            new StringField(BaseDocumentProperties.ID, source.ObjectId, Field.Store.YES),
            new StringField(BaseDocumentProperties.ITEM_GUID, source.ObjectId, Field.Store.YES),
            new TextField(IndexSchemaProvider.TitleField, source.Title, Field.Store.YES),
            new SortedDocValuesField(IndexSchemaProvider.TitleField + LuceneFieldNames.SortSuffix, new BytesRef(source.Title)),
            new TextField(TestCorpus.BodyField, source.Body, Field.Store.YES),
            new StringField(BaseDocumentProperties.CONTENT_TYPE_NAME, source.ContentType, Field.Store.YES),
            new StringField(BaseDocumentProperties.LANGUAGE_NAME, source.Language, Field.Store.YES),
            new StringField(BaseDocumentProperties.URL, source.Url, Field.Store.YES)
        };

        if (withTaxonomy)
        {
            // A strategy whose FacetsConfigFactory returns null adds no FacetField either: without a
            // taxonomy writer the index writer cannot consume one.
            document.Add(new FacetField(BaseDocumentProperties.CONTENT_TYPE_NAME, source.ContentType));
            document.Add(new FacetField(BaseDocumentProperties.LANGUAGE_NAME, source.Language));
        }

        AddTaxonomy(document, TestCorpus.CategoryField, source.Categories, withTaxonomy);
        AddTaxonomy(document, TestCorpus.TagsField, source.Tags, withTaxonomy);

        if (source.Price is { } price)
        {
            document.Add(new DoubleField(TestCorpus.PriceField, price, Field.Store.YES));
            document.Add(new DoubleDocValuesField(TestCorpus.PriceField, price));
        }

        if (source.PublishedAt is { } publishedAt)
        {
            document.Add(new Int64Field(TestCorpus.PublishedAtField, publishedAt, Field.Store.YES));
            document.Add(new NumericDocValuesField(TestCorpus.PublishedAtField, publishedAt));
        }

        return document;
    }

    private static void AddTaxonomy(Document document, string dimension, IReadOnlyList<string> values, bool withTaxonomy)
    {
        foreach (string value in values)
        {
            if (withTaxonomy)
            {
                document.Add(new FacetField(dimension, value));
            }

            document.Add(new StringField(dimension, value, Field.Store.YES));
            document.Add(new TextField(dimension + LuceneFieldNames.TextSuffix, value, Field.Store.NO));
        }
    }
}
