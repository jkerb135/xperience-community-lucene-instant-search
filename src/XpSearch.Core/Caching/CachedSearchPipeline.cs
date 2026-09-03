using System.Diagnostics;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;
using XpSearch.Core.Fuzzy;
using XpSearch.Core.Options;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Popularity;

namespace XpSearch.Core.Caching;

/// <summary>
/// Wraps the query pipeline in the short-TTL response cache (spec §4.7).
/// </summary>
/// <remarks>
/// <para>
/// The decorator, not a stage, owns caching, so a cache hit costs nothing beyond the lookup and no
/// stage can accidentally be skipped on a miss. <c>queryId</c> is re-issued on every hit because it
/// correlates one client's search with its click events and must not be shared between callers.
/// The visitor's contact groups are part of the key, so a response shaped by a group-scoped rule
/// (ADR-0021) is only ever reused for visitors in the same groups.
/// </para>
/// <para>
/// It also journals the search for analytics, for the same reason: a cache hit never enters the
/// pipeline, and only this layer knows the <c>queryId</c> the caller receives - the one a later click
/// event is attributed through.
/// </para>
/// </remarks>
public sealed class CachedSearchPipeline : ISearchPipeline
{
    private readonly ISearchPipeline inner;
    private readonly ISearchCache cache;
    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly IContactGroupResolver contactGroups;
    private readonly IExperimentAssignmentResolver experiments;
    private readonly ISearchRequestJournal journal;
    private readonly IPopularitySignalStore popularity;
    private readonly ITypoToleranceSource typoTolerance;

    /// <summary>Initializes a new instance of the <see cref="CachedSearchPipeline"/> class.</summary>
    /// <param name="inner">The pipeline that does the work on a cache miss.</param>
    /// <param name="cache">The response cache.</param>
    /// <param name="options">The current search options.</param>
    /// <param name="contactGroups">
    /// Answers which contact groups the visitor is in. The decorator needs them before the pipeline
    /// runs, because they belong in the cache key; the answer is memoized for the request, so the
    /// stage that puts them on the context costs no second query.
    /// </param>
    /// <param name="experiments">
    /// Answers which experiment and variant apply (XP-1). Asked here for the same reason as the
    /// contact groups: the variant belongs in the cache key and in the journal, and a cache hit never
    /// reaches the stage that resolves it. The answer is memoized for the request.
    /// </param>
    /// <param name="journal">Records the answered search for analytics.</param>
    /// <param name="popularity">
    /// Supplies the popularity signal version of the index (RK-1). Asked here because a cache hit
    /// never reaches the boost stage, and a response boosted by one run of the signal must not be
    /// served after the next one; an index that has not opted in reports version zero, which changes
    /// no key at all.
    /// </param>
    /// <param name="typoTolerance">
    /// Answers whether the index matches near-spellings (FZ-1). Asked here because the flag decides
    /// what the pipeline would have found, so a response answered with it on must never be served
    /// after it is turned off; the answer is one cache read, like the signal above.
    /// </param>
    public CachedSearchPipeline(
        ISearchPipeline inner,
        ISearchCache cache,
        IOptionsMonitor<XpSearchOptions> options,
        IContactGroupResolver contactGroups,
        IExperimentAssignmentResolver experiments,
        ISearchRequestJournal journal,
        IPopularitySignalStore popularity,
        ITypoToleranceSource typoTolerance)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contactGroups);
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(popularity);
        ArgumentNullException.ThrowIfNull(typoTolerance);

        this.popularity = popularity;
        this.typoTolerance = typoTolerance;
        this.inner = inner;
        this.cache = cache;
        this.options = options;
        this.contactGroups = contactGroups;
        this.experiments = experiments;
        this.journal = journal;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string queryText = NormalizeRequestStage.Normalize(request.Query, options.CurrentValue.MaxQueryLength);
        long start = Stopwatch.GetTimestamp();

        var experiment = await experiments
            .GetAssignmentAsync(request.Index ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (options.CurrentValue.CacheTtl <= TimeSpan.Zero || string.IsNullOrWhiteSpace(request.Index))
        {
            var uncached = await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

            Journal(uncached, uncached.QueryId ?? string.Empty, request, queryText, start, experiment);

            return uncached;
        }

        var groups = await contactGroups.GetContactGroupsAsync(cancellationToken).ConfigureAwait(false);
        var signal = await popularity.GetSignalAsync(request.Index, cancellationToken).ConfigureAwait(false);
        bool fuzzy = await typoTolerance.IsEnabledAsync(request.Index, cancellationToken).ConfigureAwait(false);

        string key = SearchCacheKey.Compute(request, queryText, groups, experiment, signal.Version, fuzzy);

        var cached = await cache
            .GetOrAddAsync(request.Index, key, token => inner.ExecuteAsync(request, token), cancellationToken)
            .ConfigureAwait(false);

        string queryId = string.IsNullOrWhiteSpace(request.QueryId) ? Guid.NewGuid().ToString() : request.QueryId;
        var response = cached.WithQueryId(queryId);

        Journal(response, queryId, request, queryText, start, experiment);

        return response;
    }

    /// <summary>
    /// Journals the answered search once, hit or miss. The elapsed time is this decorator's own, so a
    /// cache hit truthfully reports the near-zero cost of the lookup.
    /// </summary>
    /// <remarks>
    /// A probe request (<c>SearchRequest.probe</c>) is answered like any other but never journaled: it
    /// is a count the client asked for on the visitor's behalf - the sheet's "Show N results", the
    /// empty state's unfiltered count - not a search anyone performed. The skip lives here because
    /// this is the single place the journal is called from, so it covers the cached and the uncached
    /// path alike, and with it every report, suggestion miner and popularity signal downstream of the
    /// journal's outputs.
    /// </remarks>
    private void Journal(
        SearchResponse response,
        string queryId,
        SearchRequest request,
        string queryText,
        long start,
        ExperimentAssignment experiment)
    {
        if (request.Probe == true)
        {
            return;
        }

        journal.Record(
            queryId,
            queryText,
            request.Index ?? string.Empty,
            (int)response.Total,
            Stopwatch.GetElapsedTime(start),
            request.Language ?? string.Empty,
            experiment);
    }
}
