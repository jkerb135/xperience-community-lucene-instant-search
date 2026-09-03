using Microsoft.Extensions.Options;

using NSubstitute;

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

/// <summary>
/// The per-index settings of AR-2 derived from one <see cref="XpSearchOptions"/> instance, for every
/// name: the consumers take <see cref="IOptionsMonitor{TOptions}"/> of them, and a test that mutates
/// the options after wiring still sees its change, exactly like a save in the administration.
/// </summary>
internal sealed class PerIndexSettings : IOptionsMonitor<XpSearchIndexSettings>
{
    private readonly XpSearchOptions options;
    private readonly Dictionary<string, XpSearchIndexSettings> overrides = new(StringComparer.Ordinal);

    internal PerIndexSettings(XpSearchOptions options) => this.options = options;

    public XpSearchIndexSettings CurrentValue => XpSearchIndexSettings.FromOptions(options);

    /// <summary>Sets what one index answers with, the way a stored row would.</summary>
    internal XpSearchIndexSettings this[string indexName]
    {
        set => overrides[indexName] = value;
    }

    public XpSearchIndexSettings Get(string? name) =>
        overrides.GetValueOrDefault(name ?? string.Empty) ?? CurrentValue;

    public IDisposable? OnChange(Action<XpSearchIndexSettings, string?> listener) => null;
}

/// <summary>An index registry that knows exactly the given index names, and nothing else (AR-2).</summary>
internal static class TestIndexRegistry
{
    /// <summary>Builds the registry.</summary>
    /// <param name="indexNames">The registered index code names.</param>
    /// <returns>The accessor.</returns>
    internal static ILuceneIndexAccessor Of(params string[] indexNames)
    {
        var accessor = Substitute.For<ILuceneIndexAccessor>();

        accessor.IndexNames().Returns(indexNames);
        accessor.Exists(Arg.Any<string>()).Returns(call => Resolve(indexNames, call.Arg<string>()) is not null);
        accessor.ResolveName(Arg.Any<string>()).Returns(call => Resolve(indexNames, call.Arg<string>()));

        return accessor;
    }

    private static string? Resolve(string[] indexNames, string asked) =>
        indexNames.FirstOrDefault(name => string.Equals(name, asked, StringComparison.OrdinalIgnoreCase));
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
        var perIndex = new PerIndexSettings(Options);

        Pipeline = new SearchPipeline(
            Index,
            new StaticSchemaProvider(TestCorpus.Schema),
            [
                new NormalizeRequestStage(wrapped, perIndex),
                new QueryRewriteStage(tuning ?? new EmptyRelevanceTuningSource(), time ?? TimeProvider.System),
                new SynonymExpansionStage(tuning ?? new EmptyRelevanceTuningSource()),
                new StopwordRemovalStage(),
                new BuildQueryStage(new FixedTypoToleranceSource(typoTolerance)),
                new FacetFilterStage(),
                new NumericFilterStage(),
                new BoostRulesStage(),
                new ExecuteSearchStage(Index),
                new PinnedAndBuriedStage(Index),
                new CollectFacetsStage(new TaxonomyFacetProvider(Index), perIndex),
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
