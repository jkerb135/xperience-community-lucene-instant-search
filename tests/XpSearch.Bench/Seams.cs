using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;
using XpSearch.Core.Fuzzy;
using XpSearch.Core.Indexing;
using XpSearch.Core.Personalization;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

namespace XpSearch.Bench;

/// <summary>Serves the synthetic index's schema without the Xperience content-type sources.</summary>
internal sealed class StaticSchemaProvider : IIndexSchemaProvider
{
    public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(BenchIndex.Schema);
}

/// <summary>One fixed typo-tolerance answer, so the fuzzy-on and fuzzy-off pipelines differ in nothing else.</summary>
internal sealed class FixedTypoToleranceSource : ITypoToleranceSource
{
    private readonly bool enabled;

    internal FixedTypoToleranceSource(bool enabled) => this.enabled = enabled;

    public Task<bool> IsEnabledAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(enabled);
}

/// <summary>
/// Tuning as a modest production site actually configures it: a handful of synonym groups, one
/// always-on boost rule and non-default field weights. Present but light, so the tuning stages do
/// real work on every request without the numbers becoming a measurement of a pathological ruleset.
/// </summary>
internal sealed class BenchTuningSource : IRelevanceTuningSource
{
    private static readonly IReadOnlyList<TuningSynonym> SynonymGroups =
    [
        .. Enumerable.Range(0, 5).Select(i => new TuningSynonym(
            SynonymDirection.TwoWay,
            [Corpus.Vocabulary[i * 2], Corpus.Vocabulary[(i * 2) + 1]],
            []))
    ];

    private static readonly IReadOnlyList<TuningRule> Rules =
    [
        new TuningRule(
            1,
            "Favour articles",
            Enabled: true,
            Priority: 10,
            ValidFrom: null,
            ValidTo: null,
            new RuleConditions(new QueryCondition(QueryOperator.Contains, string.Empty, MatchAnalyzed: false), [], string.Empty, string.Empty),
            [new RuleAction.Boost(string.Empty, "contentType:Article", 1.5)])
    ];

    private static readonly IReadOnlyList<FieldWeight> Weights =
    [
        new FieldWeight(IndexSchemaProvider.TitleAttribute, 3.0),
        new FieldWeight(BenchIndex.BodyAttribute, 1.0)
    ];

    public Task<IReadOnlyList<TuningRule>> GetRulesAsync(string indexName, CancellationToken cancellationToken, TuningVariant variant = default) =>
        Task.FromResult(Rules);

    public Task<IReadOnlyList<TuningSynonym>> GetSynonymsAsync(string indexName, CancellationToken cancellationToken, TuningVariant variant = default) =>
        Task.FromResult(SynonymGroups);

    public Task<IReadOnlyList<string>> GetStopwordsAsync(string indexName, CancellationToken cancellationToken, TuningVariant variant = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<FieldWeight>> GetFieldWeightsAsync(string indexName, CancellationToken cancellationToken, TuningVariant variant = default) =>
        Task.FromResult(Weights);
}

/// <summary>The visitor is in no contact group, which is what an anonymous first-time visitor is.</summary>
internal sealed class NoContactGroups : IContactGroupResolver
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlySet<string>> GetContactGroupsAsync(CancellationToken cancellationToken) => Task.FromResult(None);
}

/// <summary>No experiment is running.</summary>
internal sealed class NoExperiment : IExperimentAssignmentResolver
{
    public Task<ExperimentAssignment> GetAssignmentAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(new ExperimentAssignment(0, SearchVariant.A));
}

/// <summary>Journaling is a fire-and-forget database write; it is not what this bench measures.</summary>
internal sealed class NoJournal : ISearchRequestJournal
{
    public void Record(string queryId, string queryText, string indexName, int total, TimeSpan elapsed, string language, ExperimentAssignment? experiment = null)
    {
    }
}

/// <summary>
/// One options instance behind <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>,
/// which is what the pipeline takes since AR-1.
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
internal sealed class StaticOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    internal StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>No click evidence, which is what an index that has not opted into popularity reports.</summary>
internal sealed class NoPopularity : IPopularitySignalStore
{
    public Task<PopularitySignal> GetSignalAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(PopularitySignal.Empty);

    public Task ReplaceAsync(string indexName, PopularityAggregate aggregate, DateTime computedUtc, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<int> DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

/// <summary>No query log, so <c>/suggest</c> measures the document path only.</summary>
internal sealed class NoQuerySuggestions : IQuerySuggestionSource
{
    public Task<IReadOnlyList<string>> SuggestAsync(string indexName, string prefix, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>
/// The response cache in a dictionary. <c>ProgressiveSearchCache</c> needs a running Xperience
/// application; what the cache-hit row measures is the decorator's own work - key computation and
/// dictionary lookup - not Kentico's cache implementation.
/// </summary>
internal sealed class MemorySearchCache : ISearchCache
{
    private readonly Dictionary<string, SearchResponse> entries = new(StringComparer.Ordinal);

    public async Task<SearchResponse> GetOrAddAsync(
        string indexName,
        string key,
        Func<CancellationToken, Task<SearchResponse>> factory,
        CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var response = await factory(cancellationToken).ConfigureAwait(false);
        entries[key] = response;

        return response;
    }

    public void Evict(string indexName) => entries.Clear();
}
