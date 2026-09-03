using XpSearch.Core.Popularity;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>The mined synonym candidates in memory, so a task run can be tested without a database (SY-1).</summary>
internal sealed class FakeSynonymSuggestionStore : ISynonymSuggestionStore
{
    internal List<(string IndexName, IReadOnlyList<ReformulationPair> Pairs, DateTime ComputedUtc)> Written { get; } = [];

    public Task ReplaceAsync(
        string indexName,
        IReadOnlyList<ReformulationPair> pairs,
        DateTime computedUtc,
        CancellationToken cancellationToken)
    {
        Written.Add((indexName, pairs, computedUtc));

        return Task.CompletedTask;
    }

    /// <summary>Every retention call: the index it pruned and how many rows it could delete (AR-2).</summary>
    internal List<(string IndexName, DateTime CutoffUtc, int BatchSize)> Pruned { get; } = [];

    /// <summary>How many answered rows the store pretends to hold, per index.</summary>
    internal Dictionary<string, int> Answered { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> IndexNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. Answered.Keys]);

    public Task<int> DeleteAnsweredOlderThanAsync(string indexName, DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        Pruned.Add((indexName, cutoffUtc, batchSize));

        int deleted = Math.Min(Answered.GetValueOrDefault(indexName), batchSize);
        Answered[indexName] = Answered.GetValueOrDefault(indexName) - deleted;

        return Task.FromResult(deleted);
    }
}

/// <summary>The popularity signal in memory, so the boost and the cache key can be tested without a database.</summary>
internal sealed class FakePopularitySignalStore : IPopularitySignalStore
{
    internal Dictionary<string, PopularitySignal> Signals { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal List<(string IndexName, PopularityAggregate Aggregate, DateTime ComputedUtc)> Written { get; } = [];

    /// <summary>Puts one index's signal in place, the way a task run would.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="version">The signal version.</param>
    /// <param name="scores">Damped click mass per result id.</param>
    internal void Set(string indexName, long version, params (string DocumentId, double Score)[] scores) =>
        Signals[indexName] = new PopularitySignal(
            indexName,
            version,
            scores.ToDictionary(entry => entry.DocumentId, entry => entry.Score, StringComparer.Ordinal));

    public Task<PopularitySignal> GetSignalAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(Signals.GetValueOrDefault(indexName ?? string.Empty, PopularitySignal.Empty));

    public Task ReplaceAsync(string indexName, PopularityAggregate aggregate, DateTime computedUtc, CancellationToken cancellationToken)
    {
        Written.Add((indexName, aggregate, computedUtc));
        Signals[indexName] = new PopularitySignal(indexName, computedUtc.Ticks, aggregate.Scores);

        return Task.CompletedTask;
    }

    /// <summary>Every retention call: the index it pruned and how many rows it could delete (AR-2).</summary>
    internal List<(string IndexName, DateTime CutoffUtc, int BatchSize)> Pruned { get; } = [];

    /// <summary>How many answered rows the store pretends to hold, per index.</summary>
    internal Dictionary<string, int> Answered { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> SuggestionIndexNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. Answered.Keys]);

    public Task<int> DeleteAnsweredOlderThanAsync(string indexName, DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        Pruned.Add((indexName, cutoffUtc, batchSize));

        int deleted = Math.Min(Answered.GetValueOrDefault(indexName), batchSize);
        Answered[indexName] = Answered.GetValueOrDefault(indexName) - deleted;

        return Task.FromResult(deleted);
    }
}
