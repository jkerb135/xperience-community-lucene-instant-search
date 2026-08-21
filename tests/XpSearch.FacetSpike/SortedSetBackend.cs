using System.Diagnostics;

using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Facet;
using Lucene.Net.Facet.SortedSet;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace XpSearch.FacetSpike;

/// <summary>
/// Option B - SortedSet DocValues faceting, no taxonomy sidecar. This bypasses the integration's facet
/// API: it is the equivalent of <c>ILuceneSearchService.UseSearcher</c> (plain <see cref="IndexSearcher"/>)
/// plus our own facet plumbing, because <c>UseSearcherWithFacets</c> throws without a taxonomy reader.
/// <c>category</c> is stored as a flat <c>a/b/c</c> label - SortedSet faceting has no drill-down tree.
/// </summary>
internal sealed class SortedSetBackend : IFacetBackend
{
    private readonly string mainPath;
    private readonly FSDirectory mainDir;
    private readonly FacetsConfig config = new();

    private DirectoryReader? reader;
    private IndexSearcher? searcher;
    private SortedSetDocValuesReaderState? state;

    internal SortedSetBackend(string root)
    {
        mainPath = Path.Combine(root, "index");
        System.IO.Directory.CreateDirectory(mainPath);
        mainDir = FSDirectory.Open(mainPath);

        config.SetMultiValued(Dims.Tags, true);
    }

    public string Name => "B (docvalues)";

    public long MainBytes => SpikeIo.DirectorySize(mainPath);

    public long TaxonomyBytes => 0;

    /// <summary>Time spent in the most recent <see cref="DefaultSortedSetDocValuesReaderState"/> construction.</summary>
    internal TimeSpan LastStateBuild { get; private set; }

    public void Build(IReadOnlyList<Doc> docs) => Write(docs, OpenMode.CREATE, deleteFirst: false);

    public void Upsert(IReadOnlyList<Doc> docs) => Write(docs, OpenMode.CREATE_OR_APPEND, deleteFirst: true);

    private void Write(IReadOnlyList<Doc> docs, OpenMode openMode, bool deleteFirst)
    {
        var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, new StandardAnalyzer(LuceneVersion.LUCENE_48))
        {
            OpenMode = openMode
        };

        using var writer = new IndexWriter(mainDir, indexConfig);

        foreach (var doc in docs)
        {
            if (deleteFirst)
            {
                writer.DeleteDocuments(new Term(SpikeIo.IdField, doc.Id));
            }

            writer.AddDocument(config.Build(ToDocument(doc)));
        }

        writer.Commit();
    }

    private static Document ToDocument(Doc doc)
    {
        var document = SpikeIo.BaseDocument(doc);
        document.Add(new SortedSetDocValuesFacetField(Dims.ContentType, doc.ContentType));
        document.Add(new SortedSetDocValuesFacetField(Dims.Language, doc.Language));
        document.Add(new SortedSetDocValuesFacetField(Dims.Category, string.Join('/', doc.Category)));
        foreach (string tag in doc.Tags)
        {
            document.Add(new SortedSetDocValuesFacetField(Dims.Tags, tag));
        }

        return document;
    }

    public TimeSpan OpenReader()
    {
        var sw = Stopwatch.StartNew();

        if (reader is null)
        {
            reader = DirectoryReader.Open(mainDir);
        }
        else
        {
            var newReader = DirectoryReader.OpenIfChanged(reader);
            if (newReader is not null)
            {
                reader.Dispose();
                reader = newReader;
            }
        }

        searcher = new IndexSearcher(reader);

        // Documented as expensive and meant to be cached per IndexReader. The integration's searcher
        // provider exposes no hook for that cache, so this cost is on us on every reader generation.
        var stateSw = Stopwatch.StartNew();
        state = new DefaultSortedSetDocValuesReaderState(reader);
        stateSw.Stop();
        LastStateBuild = stateSw.Elapsed;

        sw.Stop();
        return sw.Elapsed;
    }

    private Facets Count(Query query, int hits)
    {
        var collector = new FacetsCollector();
        FacetsCollector.Search(searcher, query, hits, collector);
        return new SortedSetDocValuesFacetCounts(state, collector);
    }

    public FacetCounts TopCounts(Query query, IReadOnlyList<string> dims, int topN) =>
        SpikeIo.Collect(Count(query, topN), dims, topN);

    public FacetCounts DrillSideways(Query query, string dim, string value, IReadOnlyList<string> dims, int topN)
    {
        var drillSideways = new DrillSideways(searcher, config, state);
        var drillDown = new DrillDownQuery(config, query);
        drillDown.Add(dim, value);
        var result = drillSideways.Search(drillDown, topN);
        return SpikeIo.Collect(result.Facets, dims, topN);
    }

    public Dictionary<string, int> CategoryLeafCounts(Query query)
    {
        var facets = Count(query, 10);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = facets.GetTopChildren(SpikeIo.AllValues, Dims.Category);
        if (result is not null)
        {
            foreach (var labelValue in result.LabelValues)
            {
                counts[labelValue.Label] = (int)labelValue.Value;
            }
        }

        return counts;
    }

    public void Dispose()
    {
        reader?.Dispose();
        mainDir.Dispose();
    }
}
