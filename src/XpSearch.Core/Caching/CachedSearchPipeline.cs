using System.Diagnostics;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;

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
    private readonly XpSearchOptions options;
    private readonly IContactGroupResolver contactGroups;
    private readonly ISearchRequestJournal journal;

    /// <summary>Initializes a new instance of the <see cref="CachedSearchPipeline"/> class.</summary>
    /// <param name="inner">The pipeline that does the work on a cache miss.</param>
    /// <param name="cache">The response cache.</param>
    /// <param name="options">The configured search options.</param>
    /// <param name="contactGroups">
    /// Answers which contact groups the visitor is in. The decorator needs them before the pipeline
    /// runs, because they belong in the cache key; the answer is memoized for the request, so the
    /// stage that puts them on the context costs no second query.
    /// </param>
    /// <param name="journal">Records the answered search for analytics.</param>
    public CachedSearchPipeline(
        ISearchPipeline inner,
        ISearchCache cache,
        IOptions<XpSearchOptions> options,
        IContactGroupResolver contactGroups,
        ISearchRequestJournal journal)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contactGroups);
        ArgumentNullException.ThrowIfNull(journal);

        this.inner = inner;
        this.cache = cache;
        this.options = options.Value;
        this.contactGroups = contactGroups;
        this.journal = journal;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string queryText = NormalizeRequestStage.Normalize(request.Query, options.MaxQueryLength);
        long start = Stopwatch.GetTimestamp();

        if (options.CacheTtl <= TimeSpan.Zero || string.IsNullOrWhiteSpace(request.Index))
        {
            var uncached = await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

            Journal(uncached, uncached.QueryId ?? string.Empty, request, queryText, start);

            return uncached;
        }

        var groups = await contactGroups.GetContactGroupsAsync(cancellationToken).ConfigureAwait(false);

        string key = SearchCacheKey.Compute(request, queryText, groups);

        var cached = await cache
            .GetOrAddAsync(request.Index, key, token => inner.ExecuteAsync(request, token), cancellationToken)
            .ConfigureAwait(false);

        string queryId = string.IsNullOrWhiteSpace(request.QueryId) ? Guid.NewGuid().ToString() : request.QueryId;
        var response = cached.WithQueryId(queryId);

        Journal(response, queryId, request, queryText, start);

        return response;
    }

    /// <summary>
    /// Journals the answered search once, hit or miss. The elapsed time is this decorator's own, so a
    /// cache hit truthfully reports the near-zero cost of the lookup.
    /// </summary>
    private void Journal(SearchResponse response, string queryId, SearchRequest request, string queryText, long start) =>
        journal.Record(
            queryId,
            queryText,
            request.Index ?? string.Empty,
            (int)response.Total,
            Stopwatch.GetElapsedTime(start),
            request.Language ?? string.Empty);
}
