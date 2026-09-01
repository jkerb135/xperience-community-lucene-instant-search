using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Search;

/// <summary>An indexed document as an id-resolving caller sees it.</summary>
/// <param name="Id">The result id the caller asked for.</param>
/// <param name="Title">The document's title, or an empty string when the index does not store one.</param>
/// <param name="Url">The document's link, or an empty string when the index does not store one.</param>
public sealed record IndexedDocument(string Id, string Title, string Url);

/// <summary>
/// Turns stored result ids back into documents, so an admin screen holding an id can show what it
/// points at.
/// </summary>
public interface IIndexDocumentLookup
{
    /// <summary>Resolves result ids against an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="ids">The result ids. Blanks and duplicates are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per id the index still holds, in the order asked for. An id the index no longer
    /// holds is absent rather than a placeholder, so the caller can tell the two apart.
    /// </returns>
    /// <exception cref="IndexNotFoundException">The index is not registered.</exception>
    Task<IReadOnlyList<IndexedDocument>> ResolveAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IIndexDocumentLookup"/>: one term query per id against the index's
/// identifier field, the same lookup <c>PinnedAndBuriedStage</c> injects a pinned document with.
/// </summary>
/// <remarks>
/// It reads the index directly rather than running the query pipeline, because the pipeline has no
/// "fetch this id" shape - <c>filters</c> only refines facetable and numeric attributes. Like the pin
/// stage it matches on <see cref="BaseDocumentProperties.ID"/>, so a hand-written indexing strategy
/// that omits that field resolves to nothing; the caller then shows the raw id.
/// </remarks>
public sealed class IndexDocumentLookup : IIndexDocumentLookup
{
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;

    /// <summary>Initializes a new instance of the <see cref="IndexDocumentLookup"/> class.</summary>
    /// <param name="accessor">The Lucene seam.</param>
    /// <param name="schemaProvider">Supplies the schema, which names the title and url fields.</param>
    public IndexDocumentLookup(ILuceneIndexAccessor accessor, IIndexSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedDocument>> ResolveAsync(
        string indexName,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var schema = await schemaProvider.GetSchemaAsync(indexName, cancellationToken).ConfigureAwait(false);

        string titleField = schema.Find(IndexSchemaProvider.TitleAttribute)?.LuceneName ?? IndexSchemaProvider.TitleField;
        string urlField = schema.Find(IndexSchemaProvider.UrlAttribute)?.LuceneName ?? BaseDocumentProperties.URL;

        return accessor.UseSearcher(indexName, searcher =>
        {
            var found = new List<IndexedDocument>(wanted.Count);

            foreach (string id in wanted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hits = searcher.Search(new TermQuery(new Term(BaseDocumentProperties.ID, id)), 1);

                if (hits.ScoreDocs.Length == 0)
                {
                    continue;
                }

                var document = searcher.Doc(hits.ScoreDocs[0].Doc);

                found.Add(new IndexedDocument(id, document.Get(titleField) ?? string.Empty, document.Get(urlField) ?? string.Empty));
            }

            return (IReadOnlyList<IndexedDocument>)found;
        });
    }
}
