using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Ingestion.Schema;

namespace XpSearch.Ingestion.Indexing;

/// <summary>
/// Reads the registered indexes and their strategy classes off the Lucene integration's index manager.
/// </summary>
public sealed class LuceneIndexStrategySource : IIndexStrategySource
{
    private readonly ILuceneIndexManager manager;

    /// <summary>Initializes a new instance of the <see cref="LuceneIndexStrategySource"/> class.</summary>
    /// <param name="manager">The integration's index manager.</param>
    public LuceneIndexStrategySource(ILuceneIndexManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        this.manager = manager;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetIndexNames() => [.. manager.GetAllIndices().Select(index => index.IndexName)];

    /// <inheritdoc />
    public Type? GetStrategyType(string indexName) => manager.GetIndex(indexName)?.LuceneIndexingStrategyType;
}
