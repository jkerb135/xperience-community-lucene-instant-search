using Lucene.Net.Search;

namespace XpSearch.FacetSpike;

/// <summary>Facet counts for one query: dimension -&gt; label -&gt; count.</summary>
internal sealed class FacetCounts : Dictionary<string, Dictionary<string, int>>
{
    internal FacetCounts()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>
/// The minimum surface the spike needs to run the identical workload against option A
/// (taxonomy sidecar) and option B (SortedSet DocValues).
/// </summary>
internal interface IFacetBackend : IDisposable
{
    /// <summary>Short label used in the result tables.</summary>
    string Name { get; }

    /// <summary>Builds the index from scratch (<c>OpenMode.CREATE</c>) and commits.</summary>
    void Build(IReadOnlyList<Doc> docs);

    /// <summary>Re-upserts documents the integration's way: delete by id term, then add. Commits.</summary>
    void Upsert(IReadOnlyList<Doc> docs);

    /// <summary>
    /// Opens or reopens the reader(s) and returns the elapsed time. For B this includes rebuilding
    /// <c>DefaultSortedSetDocValuesReaderState</c>, which is the cost the spike exists to expose.
    /// </summary>
    TimeSpan OpenReader();

    /// <summary>Top-<paramref name="topN"/> facet counts for each dimension, the natural way for this backend.</summary>
    FacetCounts TopCounts(Query query, IReadOnlyList<string> dims, int topN);

    /// <summary>Drill-sideways counts for all <paramref name="dims"/> with one facet filter applied.</summary>
    FacetCounts DrillSideways(Query query, string dim, string value, IReadOnlyList<string> dims, int topN);

    /// <summary>Leaf-level <c>category</c> counts keyed by the flat <c>a/b/c</c> path. Correctness proof only.</summary>
    Dictionary<string, int> CategoryLeafCounts(Query query);

    /// <summary>Bytes on disk in the main index directory.</summary>
    long MainBytes { get; }

    /// <summary>Bytes on disk in the taxonomy sidecar directory; 0 for backends without one.</summary>
    long TaxonomyBytes { get; }
}

/// <summary>Dimension names shared by both backends.</summary>
internal static class Dims
{
    internal const string ContentType = "contentType";
    internal const string Language = "language";
    internal const string Tags = "tags";
    internal const string Category = "category";

    internal static readonly string[] All = [ContentType, Language, Tags, Category];

    /// <summary>Dimensions whose values are directly comparable between A and B (no hierarchy).</summary>
    internal static readonly string[] Flat = [ContentType, Language, Tags];
}
