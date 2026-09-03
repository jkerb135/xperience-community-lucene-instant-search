using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Fuzzy;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>Serves a fixed schema; the real provider is exercised separately.</summary>
internal sealed class StaticSchemaProvider : IIndexSchemaProvider
{
    private readonly IndexSchema schema;

    internal StaticSchemaProvider(IndexSchema schema) => this.schema = schema;

    public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(schema);
}

/// <summary>
/// One options instance behind <see cref="IOptionsMonitor{TOptions}"/>, which is what the consumers
/// take since AR-1. Mutating the instance is visible at once, exactly like a save in the administration.
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    internal StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Answers one fixed typo tolerance setting for every index (FZ-1).</summary>
internal sealed class FixedTypoToleranceSource : ITypoToleranceSource
{
    private readonly bool enabled;

    internal FixedTypoToleranceSource(bool enabled) => this.enabled = enabled;

    public Task<bool> IsEnabledAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(enabled);
}

/// <summary>
/// Wires the production stages around <see cref="TestSearchIndex"/> so tests run the whole pipeline,
/// not a mock of it.
/// </summary>
internal sealed class TestHarness : IDisposable
{
    internal TestHarness(
        XpSearchOptions? options = null,
        bool withTaxonomy = true,
        IRelevanceTuningSource? tuning = null,
        TimeProvider? time = null,
        bool typoTolerance = false,
        params ISearchStage[] extraStages)
    {
        Options = options ?? new XpSearchOptions();
        Index = new TestSearchIndex(TestCorpus.IndexName, TestCorpus.Documents, withTaxonomy);

        var wrapped = new StaticOptionsMonitor<XpSearchOptions>(Options);

        Pipeline = new SearchPipeline(
            Index,
            new StaticSchemaProvider(TestCorpus.Schema),
            [
                new NormalizeRequestStage(wrapped),
                new QueryRewriteStage(tuning ?? new EmptyRelevanceTuningSource(), time ?? TimeProvider.System),
                new SynonymExpansionStage(tuning ?? new EmptyRelevanceTuningSource()),
                new StopwordRemovalStage(),
                new BuildQueryStage(new FixedTypoToleranceSource(typoTolerance)),
                new FacetFilterStage(),
                new NumericFilterStage(),
                new BoostRulesStage(),
                new ExecuteSearchStage(Index),
                new PinnedAndBuriedStage(Index),
                new CollectFacetsStage(new TaxonomyFacetProvider(Index), wrapped),
                new HighlightStage(new LuceneHighlighter()),
                new ProjectResponseStage(),
                .. extraStages
            ]);
    }

    internal XpSearchOptions Options { get; }

    internal TestSearchIndex Index { get; }

    internal ISearchPipeline Pipeline { get; }

    internal Task<SearchResponse> Search(SearchRequest request) => Pipeline.ExecuteAsync(request, CancellationToken.None);

    internal static SearchRequest Request(string query = "") => new() { Index = TestCorpus.IndexName, Query = query };

    public void Dispose() => Index.Dispose();
}
