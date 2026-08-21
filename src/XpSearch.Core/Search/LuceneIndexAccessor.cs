using Kentico.Xperience.Lucene.Core.Indexing;
using Kentico.Xperience.Lucene.Core.Search;

using Lucene.Net.Analysis;
using Lucene.Net.Facet;
using Lucene.Net.Search;

using Microsoft.Extensions.DependencyInjection;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Search;

/// <summary>
/// The production <see cref="ILuceneIndexAccessor"/>: the only type in this library that touches
/// <see cref="ILuceneIndexManager"/> and <see cref="ILuceneSearchService"/>.
/// </summary>
public sealed class LuceneIndexAccessor : ILuceneIndexAccessor
{
    private readonly ILuceneIndexManager indexManager;
    private readonly ILuceneSearchService searchService;
    private readonly IServiceProvider serviceProvider;

    /// <summary>Initializes a new instance of the <see cref="LuceneIndexAccessor"/> class.</summary>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="searchService">The integration's searcher lease provider.</param>
    /// <param name="serviceProvider">Used to resolve the index's indexing strategy, whose type is only known at runtime.</param>
    public LuceneIndexAccessor(
        ILuceneIndexManager indexManager,
        ILuceneSearchService searchService,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(indexManager);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this.indexManager = indexManager;
        this.searchService = searchService;
        this.serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public bool Exists(string indexName) => indexManager.GetIndex(indexName) is not null;

    /// <inheritdoc />
    public Analyzer GetAnalyzer(string indexName) => Require(indexName).LuceneAnalyzer;

    /// <inheritdoc />
    public FacetsConfig? GetFacetsConfig(string indexName)
    {
        var index = Require(indexName);

        // The strategy type is per index and chosen in the admin UI, so it can only be resolved at
        // runtime. The integration does this internally in ServiceProviderExtensions.GetRequiredStrategy,
        // which is not public:
        // https://github.com/Kentico/xperience-by-kentico-lucene/blob/master/src/Kentico.Xperience.Lucene.Core/ServiceProviderExtensions.cs
        var strategy = (ILuceneIndexingStrategy)serviceProvider.GetRequiredService(index.LuceneIndexingStrategyType);

        return strategy.FacetsConfigFactory();
    }

    /// <inheritdoc />
    public TResult UseSearcher<TResult>(string indexName, Func<IndexSearcher, TResult> use) =>
        searchService.UseSearcher(Require(indexName), use);

    /// <inheritdoc />
    public TResult UseSearcherWithDrillSideways<TResult>(string indexName, Func<IndexSearcher, DrillSideways, TResult> use) =>
        searchService.UseSearcherWithDrillSideways(Require(indexName), use);

    private LuceneIndex Require(string indexName)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);

        return indexManager.GetIndex(indexName) ?? throw new IndexNotFoundException(indexName);
    }
}
