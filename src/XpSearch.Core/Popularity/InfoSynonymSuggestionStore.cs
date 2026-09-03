using CMS.DataEngine;

namespace XpSearch.Core.Popularity;

/// <summary>Stores the mined synonym candidates of one index (SY-1).</summary>
/// <remarks>
/// Write-only from the library's side: nothing but the administration reads these rows, because a
/// suggestion changes no search until a human approves it into an ordinary synonym group.
/// </remarks>
public interface ISynonymSuggestionStore
{
    /// <summary>Replaces one index's pending suggestions with what a run mined.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="pairs">The mined pairs.</param>
    /// <param name="computedUtc">When the run happened, in UTC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    Task ReplaceAsync(
        string indexName,
        IReadOnlyList<ReformulationPair> pairs,
        DateTime computedUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of suggestions a human already answered and that were last seen before the
    /// retention window (AR-1). Pending suggestions are never touched.
    /// </summary>
    /// <param name="cutoffUtc">Rows last seen before this instant are deleted.</param>
    /// <param name="batchSize">How many rows to delete at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted; fewer than <paramref name="batchSize"/> means there are no more.</returns>
    Task<int> DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);
}

/// <summary>Stores the mined synonym candidates in the <c>XpSearch.SynonymSuggestion</c> module class (SY-1).</summary>
public sealed class InfoSynonymSuggestionStore : ISynonymSuggestionStore
{
    private readonly IInfoProvider<XpSearchSynonymSuggestionInfo> suggestions;

    /// <summary>Initializes a new instance of the <see cref="InfoSynonymSuggestionStore"/> class.</summary>
    /// <param name="suggestions">Provider of the suggestion rows.</param>
    public InfoSynonymSuggestionStore(IInfoProvider<XpSearchSynonymSuggestionInfo> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);

        this.suggestions = suggestions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Replaces the pending rows, keeping every one a human already answered - which is also what stops
    /// an answered pair from resurfacing on the next run (RK-1's precedent).
    /// </remarks>
    public async Task ReplaceAsync(
        string indexName,
        IReadOnlyList<ReformulationPair> pairs,
        DateTime computedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentNullException.ThrowIfNull(pairs);

        var existing = await suggestions.Get()
            .WhereEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionIndexName), indexName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var rows = existing.ToList();

        foreach (var row in rows.Where(row => row.SynonymSuggestionState == (int)PopularitySuggestionState.Pending))
        {
            suggestions.Delete(row);
        }

        var decided = rows
            .Where(row => row.SynonymSuggestionState != (int)PopularitySuggestionState.Pending)
            .Select(row => (row.SynonymSuggestionFailed, row.SynonymSuggestionSucceeded));

        foreach (var pair in SynonymMiner.Pending(pairs, decided))
        {
            suggestions.Set(new XpSearchSynonymSuggestionInfo
            {
                SynonymSuggestionGuid = Guid.NewGuid(),
                SynonymSuggestionIndexName = indexName,
                SynonymSuggestionFailed = pair.FailedQuery,
                SynonymSuggestionSucceeded = pair.SucceededQuery,
                SynonymSuggestionOccurrences = pair.Occurrences,
                SynonymSuggestionLastSeen = pair.LastSeenUtc,
                SynonymSuggestionState = (int)PopularitySuggestionState.Pending
            });
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await suggestions.Get()
            .WhereNotEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionState), (int)PopularitySuggestionState.Pending)
            .WhereLessThan(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionLastSeen), cutoffUtc)
            .OrderByAscending(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionLastSeen))
            .TopN(batchSize)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        int deleted = 0;

        foreach (var row in rows.Where(row =>
            SuggestionRetention.IsPrunable(row.SynonymSuggestionState, row.SynonymSuggestionLastSeen, cutoffUtc)))
        {
            suggestions.Delete(row);
            deleted++;
        }

        return deleted;
    }
}
