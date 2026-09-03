using CMS.DataEngine;
using CMS.Helpers;

namespace XpSearch.Core.Popularity;

/// <summary>
/// Stores the popularity signal in the <c>XpSearch.Popularity*</c> module classes, behind one cache
/// entry per index so a search never queries them (RK-1).
/// </summary>
/// <remarks>
/// The cache entry depends on all three object types, which all touch their dummy cache keys, so a
/// task run and the opt-in toggle both invalidate it
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
/// </remarks>
public sealed class InfoPopularitySignalStore : IPopularitySignalStore
{
    /// <summary>How long a signal entry survives without a change touching it.</summary>
    public const int CacheMinutes = 30;

    private readonly IInfoProvider<XpSearchPopularityIndexInfo> indexes;
    private readonly IInfoProvider<XpSearchPopularityScoreInfo> scores;
    private readonly IInfoProvider<XpSearchPopularitySuggestionInfo> suggestions;
    private readonly IProgressiveCache cache;
    private readonly ICacheDependencyBuilderFactory dependencies;

    /// <summary>Initializes a new instance of the <see cref="InfoPopularitySignalStore"/> class.</summary>
    /// <param name="indexes">Provider of the per-index settings rows.</param>
    /// <param name="scores">Provider of the signal rows.</param>
    /// <param name="suggestions">Provider of the suggested rule rows.</param>
    /// <param name="cache">The progressive cache.</param>
    /// <param name="dependencies">Factory of cache dependency builders.</param>
    public InfoPopularitySignalStore(
        IInfoProvider<XpSearchPopularityIndexInfo> indexes,
        IInfoProvider<XpSearchPopularityScoreInfo> scores,
        IInfoProvider<XpSearchPopularitySuggestionInfo> suggestions,
        IProgressiveCache cache,
        ICacheDependencyBuilderFactory dependencies)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(suggestions);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(dependencies);

        this.indexes = indexes;
        this.scores = scores;
        this.suggestions = suggestions;
        this.cache = cache;
        this.dependencies = dependencies;
    }

    /// <summary>Builds the cache key parts of one index's signal entry.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The parts, which the cache joins with <c>|</c>.</returns>
    public static string[] CacheKeyParts(string indexName) => ["xpsearch", "popularity", indexName ?? string.Empty];

    /// <inheritdoc />
    public Task<PopularitySignal> GetSignalAsync(string indexName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Task.FromResult(PopularitySignal.Empty);
        }

        return cache.LoadAsync(
            async settings =>
            {
                settings.CacheDependency = dependencies.Create()
                    .ForInfoObjects<XpSearchPopularityIndexInfo>().All().Builder()
                    .ForInfoObjects<XpSearchPopularityScoreInfo>().All().Builder()
                    .Build();

                return await ReadAsync(indexName, cancellationToken).ConfigureAwait(false);
            },
            new CacheSettings(CacheMinutes, CacheKeyParts(indexName)));
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(
        string indexName,
        PopularityAggregate aggregate,
        DateTime computedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentNullException.ThrowIfNull(aggregate);

        await ReplaceScoresAsync(indexName, aggregate, computedUtc, cancellationToken).ConfigureAwait(false);
        await ReplaceSuggestionsAsync(indexName, aggregate, computedUtc, cancellationToken).ConfigureAwait(false);

        var settings = await SettingsAsync(indexName, cancellationToken).ConfigureAwait(false)
            ?? new XpSearchPopularityIndexInfo
            {
                PopularityIndexGuid = Guid.NewGuid(),
                PopularityIndexName = indexName,
                PopularityIndexEnabled = false
            };

        settings.PopularityIndexComputed = computedUtc;
        indexes.Set(settings);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await suggestions.Get()
            .WhereNotEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionState), (int)PopularitySuggestionState.Pending)
            .WhereLessThan(nameof(XpSearchPopularitySuggestionInfo.SuggestionComputed), cutoffUtc)
            .OrderByAscending(nameof(XpSearchPopularitySuggestionInfo.SuggestionComputed))
            .TopN(batchSize)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        int deleted = 0;

        foreach (var row in rows.Where(row =>
            SuggestionRetention.IsPrunable(row.SuggestionState, row.SuggestionComputed, cutoffUtc)))
        {
            suggestions.Delete(row);
            deleted++;
        }

        return deleted;
    }

    /// <summary>Reads one index's settings row, or <see langword="null"/> when it has none yet.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The row, or <see langword="null"/>.</returns>
    public async Task<XpSearchPopularityIndexInfo?> SettingsAsync(string indexName, CancellationToken cancellationToken)
    {
        var rows = await indexes.Get()
            .WhereEquals(nameof(XpSearchPopularityIndexInfo.PopularityIndexName), indexName)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.FirstOrDefault();
    }

    private async Task<PopularitySignal> ReadAsync(string indexName, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(indexName, cancellationToken).ConfigureAwait(false);

        if (settings is not { PopularityIndexEnabled: true })
        {
            return PopularitySignal.Empty;
        }

        var rows = await scores.Get()
            .WhereEquals(nameof(XpSearchPopularityScoreInfo.ScoreIndexName), indexName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var values = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ScoreDocumentID))
            .GroupBy(row => row.ScoreDocumentID, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(row => row.ScoreValue), StringComparer.Ordinal);

        return values.Count == 0
            ? PopularitySignal.Empty
            : new PopularitySignal(indexName, settings.PopularityIndexComputed.Ticks, values);
    }

    private async Task ReplaceScoresAsync(
        string indexName,
        PopularityAggregate aggregate,
        DateTime computedUtc,
        CancellationToken cancellationToken)
    {
        var existing = await scores.Get()
            .WhereEquals(nameof(XpSearchPopularityScoreInfo.ScoreIndexName), indexName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in existing)
        {
            scores.Delete(row);
        }

        foreach ((string documentId, double score) in aggregate.Scores)
        {
            scores.Set(new XpSearchPopularityScoreInfo
            {
                ScoreGuid = Guid.NewGuid(),
                ScoreIndexName = indexName,
                ScoreDocumentID = documentId,
                ScoreValue = score,
                ScoreComputed = computedUtc
            });
        }
    }

    /// <summary>
    /// Replaces the pending suggestions, keeping every one a human already answered - which is also
    /// what stops an answered suggestion from resurfacing on the next run.
    /// </summary>
    private async Task ReplaceSuggestionsAsync(
        string indexName,
        PopularityAggregate aggregate,
        DateTime computedUtc,
        CancellationToken cancellationToken)
    {
        var existing = await suggestions.Get()
            .WhereEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionIndexName), indexName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var rows = existing.ToList();

        foreach (var row in rows.Where(row => row.SuggestionState == (int)PopularitySuggestionState.Pending))
        {
            suggestions.Delete(row);
        }

        var decided = rows
            .Where(row => row.SuggestionState != (int)PopularitySuggestionState.Pending)
            .Select(row => (row.SuggestionQuery, row.SuggestionDocumentID));

        foreach (var suggestion in PopularitySuggestionMerge.Pending(aggregate.Suggestions, decided))
        {
            suggestions.Set(new XpSearchPopularitySuggestionInfo
            {
                SuggestionGuid = Guid.NewGuid(),
                SuggestionIndexName = indexName,
                SuggestionQuery = suggestion.Query,
                SuggestionDocumentID = suggestion.DocumentId,
                SuggestionClicks = suggestion.Clicks,
                SuggestionSharePercent = suggestion.SharePercent,
                SuggestionComputed = computedUtc,
                SuggestionState = (int)PopularitySuggestionState.Pending
            });
        }
    }
}
