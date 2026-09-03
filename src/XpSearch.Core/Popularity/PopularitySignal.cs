namespace XpSearch.Core.Popularity;

/// <summary>
/// The popularity signal of one index: how much click evidence each document accumulated in the
/// aggregation window, and which run produced it (RK-1).
/// </summary>
/// <param name="IndexName">Code name of the index the signal belongs to.</param>
/// <param name="Version">
/// Ticks of the run that computed the signal, or zero when there is none - which is also what an
/// index that has not opted in reports, so the version only ever joins a cache key when the boost
/// is actually applied.
/// </param>
/// <param name="Scores">The damped click mass per result id.</param>
public sealed record PopularitySignal(string IndexName, long Version, IReadOnlyDictionary<string, double> Scores)
{
    /// <summary>The largest factor popularity may reach, for the single most clicked document.</summary>
    public const double MaxFactor = 2.0;

    /// <summary>Gets the signal of an index with no evidence, or one that has not opted in: a no-op.</summary>
    public static PopularitySignal Empty { get; } =
        new(string.Empty, 0, new Dictionary<string, double>(StringComparer.Ordinal));

    /// <summary>
    /// The boost factor of every document that has one, normalized against the strongest document of
    /// this index.
    /// </summary>
    /// <returns>Result id and factor, ordered by factor descending then id, or nothing when the signal is empty.</returns>
    /// <remarks>
    /// <c>factor = 1 + (score / top) * (MaxFactor - 1)</c>: the most clicked document reaches
    /// <see cref="MaxFactor"/>, everything else scales linearly down towards 1.0, and a document with
    /// no evidence keeps its text score untouched. The cap is what keeps popularity from drowning
    /// relevance (ADR-0025).
    /// </remarks>
    public IReadOnlyList<(string DocumentId, double Factor)> Boosts()
    {
        double top = Scores.Count == 0 ? 0 : Scores.Values.Max();

        if (top <= 0)
        {
            return [];
        }

        return
        [
            .. Scores
                .Where(entry => entry.Value > 0 && !string.IsNullOrWhiteSpace(entry.Key))
                .Select(entry => (entry.Key, 1.0 + (((MaxFactor - 1.0) * entry.Value) / top)))
                .OrderByDescending(entry => entry.Item2)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
        ];
    }
}

/// <summary>
/// Where the popularity signal and its suggested rules are stored. The scheduled task writes it, the
/// boost stage and the response cache read it.
/// </summary>
public interface IPopularitySignalStore
{
    /// <summary>Reads one index's signal.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The signal, or <see cref="PopularitySignal.Empty"/> when the index has not opted in or no run
    /// has produced one yet.
    /// </returns>
    Task<PopularitySignal> GetSignalAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces everything one run computed for an index: the signal rows and the pending suggestions.
    /// Running it twice over the same window leaves the same rows.
    /// </summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="aggregate">What the run computed.</param>
    /// <param name="computedUtc">When the run ran; its ticks become the signal version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    Task ReplaceAsync(string indexName, PopularityAggregate aggregate, DateTime computedUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of suggestions a human already answered and that are older than the retention
    /// window of its index (AR-2). Pending suggestions are never touched.
    /// </summary>
    /// <param name="indexName">Code name of the index whose rows are pruned.</param>
    /// <param name="cutoffUtc">Rows computed before this instant are deleted.</param>
    /// <param name="batchSize">How many rows to delete at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted; fewer than <paramref name="batchSize"/> means there are no more.</returns>
    Task<int> DeleteAnsweredOlderThanAsync(string indexName, DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Gets the distinct index names the suggestions are stored for, registered or not (AR-2).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The index names.</returns>
    Task<IReadOnlyList<string>> SuggestionIndexNamesAsync(CancellationToken cancellationToken);
}

/// <summary>Whether retention may delete a suggestion row (AR-1).</summary>
public static class SuggestionRetention
{
    /// <summary>Decides whether a suggestion row is prunable.</summary>
    /// <param name="state">The row's state, as stored.</param>
    /// <param name="lastSeenUtc">When the row was last computed or seen.</param>
    /// <param name="cutoffUtc">The retention cutoff.</param>
    /// <returns><see langword="true"/> when the row was answered and is older than the cutoff.</returns>
    public static bool IsPrunable(int state, DateTime lastSeenUtc, DateTime cutoffUtc) =>
        state != (int)PopularitySuggestionState.Pending && lastSeenUtc < cutoffUtc;
}

