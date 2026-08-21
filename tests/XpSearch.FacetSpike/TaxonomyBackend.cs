using System.Diagnostics;

using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Facet.Taxonomy;
using Lucene.Net.Facet.Taxonomy.Directory;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace XpSearch.FacetSpike;

/// <summary>
/// Option A - <c>Lucene.Net.Facet</c> taxonomy sidecar, mirroring what
/// <c>Kentico.Xperience.Lucene</c> 15.0.5 already does natively.
/// Write path: https://github.com/Kentico/xperience-by-kentico-lucene/blob/master/src/Kentico.Xperience.Lucene.Core/Indexing/DefaultLuceneClient.cs
/// (<c>UseIndexAndTaxonomyWriter</c> -&gt; delete-by-term -&gt; <c>writer.AddDocument(facetsConfig.Build(taxonomyWriter, doc))</c>
/// -&gt; <c>taxonomyWriter.Commit()</c> every 1000 docs and at the end).
/// Read path: https://github.com/Kentico/xperience-by-kentico-lucene/blob/master/src/Kentico.Xperience.Lucene.Core/Search/DefaultLuceneSearchService.cs
/// </summary>
internal sealed class TaxonomyBackend : IFacetBackend
{
    private readonly string mainPath;
    private readonly string taxonomyPath;
    private readonly FSDirectory mainDir;
    private readonly FSDirectory taxonomyDir;
    private readonly FacetsConfig config = new();

    private DirectoryReader? reader;
    private DirectoryTaxonomyReader? taxonomyReader;
    private IndexSearcher? searcher;

    internal TaxonomyBackend(string root)
    {
        mainPath = Path.Combine(root, "index");
        taxonomyPath = Path.Combine(root, "index_taxonomy");
        System.IO.Directory.CreateDirectory(mainPath);
        System.IO.Directory.CreateDirectory(taxonomyPath);
        mainDir = FSDirectory.Open(mainPath);
        taxonomyDir = FSDirectory.Open(taxonomyPath);

        config.SetMultiValued(Dims.Tags, true);
        config.SetHierarchical(Dims.Category, true);
    }

    public string Name => "A (taxonomy)";

    public long MainBytes => SpikeIo.DirectorySize(mainPath);

    public long TaxonomyBytes => SpikeIo.DirectorySize(taxonomyPath);

    public void Build(IReadOnlyList<Doc> docs) => Write(docs, OpenMode.CREATE, deleteFirst: false);

    public void Upsert(IReadOnlyList<Doc> docs) => Write(docs, OpenMode.CREATE_OR_APPEND, deleteFirst: true);

    private void Write(IReadOnlyList<Doc> docs, OpenMode openMode, bool deleteFirst)
    {
        var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))
        {
            OpenMode = openMode
        };

        using var writer = new IndexWriter(mainDir, indexConfig);
        using var taxonomyWriter = new DirectoryTaxonomyWriter(taxonomyDir);

        int count = 0;
        foreach (var doc in docs)
        {
            if (deleteFirst)
            {
                writer.DeleteDocuments(new Term(SpikeIo.IdField, doc.Id));
            }

            writer.AddDocument(config.Build(taxonomyWriter, ToDocument(doc)));

            if (++count % 1000 == 0)
            {
                taxonomyWriter.Commit();
            }
        }

        taxonomyWriter.Commit();
        writer.Commit();
    }

    private static Document ToDocument(Doc doc)
    {
        var document = SpikeIo.BaseDocument(doc);
        document.Add(new FacetField(Dims.ContentType, doc.ContentType));
        document.Add(new FacetField(Dims.Language, doc.Language));
        document.Add(new FacetField(Dims.Category, doc.Category));
        foreach (string tag in doc.Tags)
        {
            document.Add(new FacetField(Dims.Tags, tag));
        }

        return document;
    }

    public TimeSpan OpenReader()
    {
        var sw = Stopwatch.StartNew();

        if (reader is null)
        {
            reader = DirectoryReader.Open(mainDir);
            taxonomyReader = new DirectoryTaxonomyReader(taxonomyDir);
        }
        else
        {
            var newReader = DirectoryReader.OpenIfChanged(reader);
            if (newReader is not null)
            {
                reader.Dispose();
                reader = newReader;
            }

            var newTaxonomyReader = DirectoryTaxonomyReader.OpenIfChanged(taxonomyReader);
            if (newTaxonomyReader is not null)
            {
                taxonomyReader!.Dispose();
                taxonomyReader = newTaxonomyReader;
            }
        }

        searcher = new IndexSearcher(reader);
        sw.Stop();
        return sw.Elapsed;
    }

    private Facets Count(Query query, int hits)
    {
        var collector = new FacetsCollector();
        FacetsCollector.Search(searcher, query, hits, collector);

        // DocValuesOrdinalsReader over FacetsConfig.DEFAULT_INDEX_FIELD_NAME because that is exactly what
        // DefaultLuceneSearchService.UseSearcherWithFacets does; the spike must measure the integration's pattern.
        OrdinalsReader ordinalsReader = new DocValuesOrdinalsReader(FacetsConfig.DEFAULT_INDEX_FIELD_NAME);
        return new TaxonomyFacetCounts(ordinalsReader, taxonomyReader, config, collector);
    }

    public FacetCounts TopCounts(Query query, IReadOnlyList<string> dims, int topN) =>
        SpikeIo.Collect(Count(query, topN), dims, topN);

    public FacetCounts DrillSideways(Query query, string dim, string value, IReadOnlyList<string> dims, int topN)
    {
        var drillSideways = new DrillSideways(searcher, config, taxonomyReader);
        var drillDown = new DrillDownQuery(config, query);
        drillDown.Add(dim, value);
        var result = drillSideways.Search(drillDown, topN);
        return SpikeIo.Collect(result.Facets, dims, topN);
    }

    public Dictionary<string, int> CategoryLeafCounts(Query query)
    {
        var facets = Count(query, 10);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string[] path in Corpus.CategoryPaths())
        {
            // GetSpecificValue resolves the full hierarchical path to a single taxonomy ordinal;
            // GetTopChildren on a hierarchical dimension would only return the rolled-up top level.
            float value = facets.GetSpecificValue(Dims.Category, path);
            if (value > 0)
            {
                counts[string.Join('/', path)] = (int)value;
            }
        }

        return counts;
    }

    public void Dispose()
    {
        reader?.Dispose();
        taxonomyReader?.Dispose();
        mainDir.Dispose();
        taxonomyDir.Dispose();
    }
}
