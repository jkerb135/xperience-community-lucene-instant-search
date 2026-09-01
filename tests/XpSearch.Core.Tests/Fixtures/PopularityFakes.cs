using XpSearch.Core.Popularity;

namespace XpSearch.Core.Tests.Fixtures;

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
}
