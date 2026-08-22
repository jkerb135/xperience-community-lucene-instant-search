using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;

namespace XpSearch.Core.Caching;

/// <summary>
/// Wraps the query pipeline in the short-TTL response cache (spec §4.7).
/// </summary>
/// <remarks>
/// The decorator, not a stage, owns caching, so a cache hit costs nothing beyond the lookup and no
/// stage can accidentally be skipped on a miss. <c>queryId</c> is re-issued on every hit because it
/// correlates one client's search with its click events and must not be shared between callers.
/// </remarks>
public sealed class CachedSearchPipeline : ISearchPipeline
{
    private readonly ISearchPipeline inner;
    private readonly ISearchCache cache;
    private readonly XpSearchOptions options;

    /// <summary>Initializes a new instance of the <see cref="CachedSearchPipeline"/> class.</summary>
    /// <param name="inner">The pipeline that does the work on a cache miss.</param>
    /// <param name="cache">The response cache.</param>
    /// <param name="options">The configured search options.</param>
    public CachedSearchPipeline(ISearchPipeline inner, ISearchCache cache, IOptions<XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        this.inner = inner;
        this.cache = cache;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (options.CacheTtl <= TimeSpan.Zero || string.IsNullOrWhiteSpace(request.Index))
        {
            return await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }

        string key = SearchCacheKey.Compute(request, NormalizeRequestStage.Normalize(request.Query, options.MaxQueryLength));

        var cached = await cache
            .GetOrAddAsync(request.Index, key, token => inner.ExecuteAsync(request, token), cancellationToken)
            .ConfigureAwait(false);

        return cached.WithQueryId(
            string.IsNullOrWhiteSpace(request.QueryId) ? Guid.NewGuid().ToString() : request.QueryId);
    }
}
