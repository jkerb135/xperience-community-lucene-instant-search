using Lucene.Net.Analysis;
using Lucene.Net.Facet;
using Lucene.Net.Search;

namespace XpSearch.Core.Abstractions;

/// <summary>
/// The single seam between this library and <c>Kentico.Xperience.Lucene</c>'s reader side.
/// </summary>
/// <remarks>
/// It exists because <c>Kentico.Xperience.Lucene.Core.Indexing.LuceneIndex</c> has no public
/// constructor, so neither it nor <c>ILuceneSearchService</c> can be stood up outside a running
/// Xperience application. Everything downstream of this interface is therefore testable against a
/// plain Lucene directory; the production implementation
/// (<c>XpSearch.Core.Search.LuceneIndexAccessor</c>) is the only type that touches
/// <c>ILuceneIndexManager</c> and <c>ILuceneSearchService</c>.
/// The searcher handed to the callbacks comes from a cached lease and must never escape them.
/// </remarks>
public interface ILuceneIndexAccessor
{
    /// <summary>Determines whether an index with the given code name is registered.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns><see langword="true"/> when the index exists.</returns>
    bool Exists(string indexName);

    /// <summary>Gets the analyzer the index was built with, used for both querying and highlighting.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The index's analyzer.</returns>
    Analyzer GetAnalyzer(string indexName);

    /// <summary>Gets the facet configuration of the index's strategy.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The configuration, or <see langword="null"/> when the index has no taxonomy sidecar.</returns>
    FacetsConfig? GetFacetsConfig(string indexName);

    /// <summary>
    /// Drops the integration's cached searcher for the index, so the next search opens a reader over
    /// the current commit point.
    /// </summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <remarks>
    /// The integration caches a <c>SearcherManager</c> per index and only rebuilds it when it is
    /// invalidated; its own client invalidates on rebuild and index deletion but not on in-place
    /// upserts and deletes, so a document written through <c>ILuceneClient.UpsertRecords</c> stays
    /// invisible for the lifetime of the process unless this is called after the write.
    /// </remarks>
    void Invalidate(string indexName);

    /// <summary>Runs a callback against a searcher for the index.</summary>
    /// <typeparam name="TResult">Type the callback returns.</typeparam>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="use">The callback. The searcher is only valid for its duration.</param>
    /// <returns>Whatever the callback returned.</returns>
    TResult UseSearcher<TResult>(string indexName, Func<IndexSearcher, TResult> use);

    /// <summary>
    /// Runs a callback against a searcher and a <see cref="DrillSideways"/> for the index, so facet
    /// counts for a drilled dimension keep the "what if I picked another value" semantics that
    /// refinement lists need.
    /// </summary>
    /// <typeparam name="TResult">Type the callback returns.</typeparam>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="use">The callback. Neither argument is valid after it returns.</param>
    /// <returns>Whatever the callback returned.</returns>
    /// <exception cref="InvalidOperationException">The index has no taxonomy sidecar.</exception>
    TResult UseSearcherWithDrillSideways<TResult>(string indexName, Func<IndexSearcher, DrillSideways, TResult> use);
}
