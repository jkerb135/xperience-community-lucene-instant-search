using Kentico.Xperience.Lucene.Core.Indexing;

namespace XpSearch.Widgets.Options;

/// <summary>
/// The search indexes an editor may pick from. A seam over Kentico's index manager so the drop-down
/// and the "only one index" default are testable without an Xperience application.
/// </summary>
public interface IXpSearchIndexCatalog
{
    /// <summary>Gets the code names of every registered index.</summary>
    /// <returns>The index code names.</returns>
    IReadOnlyList<string> GetIndexNames();
}

/// <summary>
/// <see cref="IXpSearchIndexCatalog"/> over <c>ILuceneIndexManager.GetAllIndices()</c>.
/// </summary>
internal sealed class LuceneIndexCatalog : IXpSearchIndexCatalog
{
    private readonly ILuceneIndexManager manager;

    public LuceneIndexCatalog(ILuceneIndexManager manager) => this.manager = manager;

    public IReadOnlyList<string> GetIndexNames() =>
        manager.GetAllIndices().Select(index => index.IndexName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
}
