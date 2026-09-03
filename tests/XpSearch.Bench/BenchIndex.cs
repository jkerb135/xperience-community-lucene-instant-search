using System.Diagnostics;

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

namespace XpSearch.Bench;

/// <summary>What one index build cost.</summary>
/// <param name="BuildMs">Wall time to write and commit every document, including corpus generation.</param>
/// <param name="MainBytes">Size of the main index directory on disk.</param>
/// <param name="TaxonomyBytes">Size of the taxonomy sidecar on disk.</param>
/// <param name="ReaderOpenMs">Time to open the index and taxonomy readers cold, after the writers closed.</param>
internal readonly record struct BuildResult(double BuildMs, long MainBytes, long TaxonomyBytes, double ReaderOpenMs);

/// <summary>
/// A real on-disk Lucene index with a taxonomy sidecar, written the way
/// <c>XpSearchIndexingStrategy</c> writes one and read through the same seam the production pipeline
/// uses. This is what makes the bench a measurement of the product's pipeline rather than of raw
/// Lucene: everything above <see cref="ILuceneIndexAccessor"/> is the shipping code.
/// </summary>
/// <remarks>
/// <see cref="FSDirectory"/> under the system temp directory, because the 1M index does not fit in a
/// <c>RAMDirectory</c> and on-disk size is one of the reported numbers.
/// </remarks>
internal sealed class BenchIndex : ILuceneIndexAccessor, IDisposable
{
    internal const string IndexName = "BenchIndex";
    internal const string BodyAttribute = "body";
    internal const string SectionAttribute = "section";
    internal const string TopicAttribute = "topic";
    internal const string PriceAttribute = "price";

    private readonly string mainPath;
    private readonly string taxonomyPath;
    private readonly FSDirectory main;
    private readonly FSDirectory taxonomy;
    private readonly FacetsConfig config = new();
    private readonly Analyzer analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

    private DirectoryReader? reader;
    private DirectoryTaxonomyReader? taxonomyReader;

    internal BenchIndex(string root)
    {
        mainPath = Path.Combine(root, "main");
        taxonomyPath = Path.Combine(root, "taxonomy");
        System.IO.Directory.CreateDirectory(mainPath);
        System.IO.Directory.CreateDirectory(taxonomyPath);

        main = FSDirectory.Open(mainPath);
        taxonomy = FSDirectory.Open(taxonomyPath);
        config.SetMultiValued(TopicAttribute, true);
    }

    /// <summary>The schema of the synthetic index: the base fields plus a body, two facet dimensions and a sortable number.</summary>
    internal static IndexSchema Schema { get; } = new(
        IndexName,
        [
            .. IndexSchemaProvider.BaseFields(),
            new SchemaField(BodyAttribute, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true),
            new SchemaField(SectionAttribute, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
            new SchemaField(TopicAttribute, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
            new SchemaField(PriceAttribute, SearchFieldKind.Number, Searchable: false, Facetable: false, Sortable: true, Retrievable: true)
        ]);

    public bool Exists(string name) => string.Equals(name, IndexName, StringComparison.OrdinalIgnoreCase);

    public string? ResolveName(string name) => Exists(name) ? IndexName : null;

    public IReadOnlyList<string> IndexNames() => [IndexName];

    public Analyzer GetAnalyzer(string name) => analyzer;

    public IReadOnlyList<string> IndexNamesForStrategy(Type strategyType) => [IndexName];

    public FacetsConfig? GetFacetsConfig(string name) => config;

    public void Invalidate(string name)
    {
        reader?.Dispose();
        reader = null;
        taxonomyReader?.Dispose();
        taxonomyReader = null;
    }

    public TResult UseSearcher<TResult>(string name, Func<IndexSearcher, TResult> use) =>
        use(new IndexSearcher(OpenReader()));

    public TResult UseSearcherWithDrillSideways<TResult>(string name, Func<IndexSearcher, DrillSideways, TResult> use)
    {
        var searcher = new IndexSearcher(OpenReader());

        return use(searcher, new DrillSideways(searcher, config, taxonomyReader));
    }

    /// <summary>Writes the corpus with the stock writer configuration and reports what it cost.</summary>
    /// <param name="documents">The documents, consumed as they are generated.</param>
    /// <returns>Build time, on-disk size and cold reader-open time.</returns>
    internal BuildResult Build(IEnumerable<BenchDocument> documents)
    {
        var sw = Stopwatch.StartNew();

        // Stock IndexWriterConfig on purpose: a bigger RAM buffer would make this number prettier
        // and less like what the Lucene integration actually does.
        using (var writer = new IndexWriter(main, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE }))
        using (var taxonomyWriter = new DirectoryTaxonomyWriter(taxonomy, OpenMode.CREATE))
        {
            foreach (var document in documents)
            {
                writer.AddDocument(config.Build(taxonomyWriter, ToLuceneDocument(document)));
            }

            taxonomyWriter.Commit();
            writer.Commit();
        }

        sw.Stop();

        long openStart = Stopwatch.GetTimestamp();
        OpenReader();
        double openMs = Stopwatch.GetElapsedTime(openStart).TotalMilliseconds;

        return new BuildResult(sw.Elapsed.TotalMilliseconds, DirectorySize(mainPath), DirectorySize(taxonomyPath), openMs);
    }

    public void Dispose()
    {
        reader?.Dispose();
        taxonomyReader?.Dispose();
        analyzer.Dispose();
        main.Dispose();
        taxonomy.Dispose();
    }

    private static long DirectorySize(string path) =>
        new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private DirectoryReader OpenReader()
    {
        reader ??= DirectoryReader.Open(main);
        taxonomyReader ??= new DirectoryTaxonomyReader(taxonomy);

        return reader;
    }

    private Document ToLuceneDocument(BenchDocument source)
    {
        var document = new Document
        {
            new StringField(BaseDocumentProperties.ID, source.Id, Field.Store.YES),
            new StringField(BaseDocumentProperties.ITEM_GUID, source.Id, Field.Store.YES),
            new TextField(IndexSchemaProvider.TitleField, source.Title, Field.Store.YES),
            new TextField(BodyAttribute, source.Body, Field.Store.YES),
            new StringField(BaseDocumentProperties.CONTENT_TYPE_NAME, source.ContentType, Field.Store.YES),
            new StringField(BaseDocumentProperties.LANGUAGE_NAME, source.Language, Field.Store.YES),
            new StringField(BaseDocumentProperties.URL, source.Url, Field.Store.YES),
            new StringField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource, Field.Store.YES),
            new FacetField(BaseDocumentProperties.CONTENT_TYPE_NAME, source.ContentType),
            new FacetField(BaseDocumentProperties.LANGUAGE_NAME, source.Language),
            new FacetField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource),
            new DoubleField(PriceAttribute, source.Price, Field.Store.YES),
            new DoubleDocValuesField(PriceAttribute, source.Price)
        };

        AddTaxonomy(document, SectionAttribute, [source.Section]);
        AddTaxonomy(document, TopicAttribute, source.Topics);

        return document;
    }

    /// <summary>
    /// Writes a taxonomy dimension the way <c>XpSearchIndexingStrategy.WriteTag</c> does - facet
    /// field, verbatim term, analyzed title and the label term the facet stage reads value, path and
    /// label out of. The synthetic dimensions are flat, so every value's path is empty.
    /// </summary>
    private static void AddTaxonomy(Document document, string dimension, IReadOnlyList<string> values)
    {
        var field = Schema.Find(dimension)!;
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (string value in values)
        {
            if (!written.Add(value))
            {
                continue;
            }

            document.Add(new FacetField(field.LuceneName, value));
            document.Add(new StringField(field.LuceneName, value, Field.Store.YES));
            document.Add(new TextField(LuceneFieldNames.SearchFieldName(field), value, Field.Store.NO));
            document.Add(new StringField(LuceneFieldNames.LabelFieldName(field), LuceneFieldNames.ComposeLabel(value, value), Field.Store.NO));
        }
    }
}
