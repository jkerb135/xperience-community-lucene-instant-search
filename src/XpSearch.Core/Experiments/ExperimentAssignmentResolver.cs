using CMS.Helpers;

using Kentico.Web.Mvc;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace XpSearch.Core.Experiments;

/// <summary>
/// Answers "which experiment and which variant applies to this request?", once per request per index
/// (XP-1).
/// </summary>
public interface IExperimentAssignmentResolver
{
    /// <summary>Gets the assignment of the current visitor for one index.</summary>
    /// <param name="indexName">Code name of the index being searched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assignment, or <see cref="ExperimentAssignment.None"/> when nothing is running.</returns>
    Task<ExperimentAssignment> GetAssignmentAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>
/// The production resolver: the index's running experiment, bucketed by a first-party cookie.
/// </summary>
/// <remarks>
/// <para>
/// The cookie holds nothing but a random id and is registered at <c>CookieLevel.Essential</c>, so
/// bucketing works for anonymous visitors who have not consented to tracking - which is the point of
/// the amendment. It is written through <see cref="ICookieAccessor"/> so Xperience still enforces the
/// visitor's chosen level
/// (https://docs.kentico.com/documentation/developers-and-admins/data-protection/cookies).
/// </para>
/// <para>
/// When the cookie is absent and cannot be assigned - no HTTP context, a response that has already
/// started streaming (DX-2's server-side widget render), or a visitor below the Essential level - the
/// request is bucketed into A and nothing is written. Bucketing on a throwaway id instead would make
/// the same visitor flip variants request by request. See KNOWN-LIMITATIONS.
/// </para>
/// <para>
/// The answer is memoized on <see cref="HttpContext.Items"/>: the caching decorator needs it before
/// the pipeline runs (it is part of the cache key and of the journal), and the stage that puts it on
/// the context must not resolve - or assign a second cookie for - the same request again.
/// </para>
/// </remarks>
public sealed class ExperimentAssignmentResolver : IExperimentAssignmentResolver
{
    private const string ItemsKeyPrefix = "xpsearch.experiment|";

    private readonly IRunningExperimentSource experiments;
    private readonly IVisitorBucketProvider buckets;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ILogger<ExperimentAssignmentResolver> logger;

    /// <summary>Initializes a new instance of the <see cref="ExperimentAssignmentResolver"/> class.</summary>
    /// <param name="experiments">Supplies the running experiment of an index.</param>
    /// <param name="cookies">Reads and writes the bucket cookie.</param>
    /// <param name="cookieLevelProvider">Supplies the visitor's current cookie level.</param>
    /// <param name="httpContextAccessor">Gives access to the request the answer is memoized on.</param>
    /// <param name="logger">Logger.</param>
    public ExperimentAssignmentResolver(
        IRunningExperimentSource experiments,
        ICookieAccessor cookies,
        ICurrentCookieLevelProvider cookieLevelProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ExperimentAssignmentResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(cookies);
        ArgumentNullException.ThrowIfNull(cookieLevelProvider);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        this.experiments = experiments;
        buckets = new VisitorBucketProvider(cookies, cookieLevelProvider, httpContextAccessor);
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    /// <summary>
    /// Tells whether a bucket cookie can still be appended to this response.
    /// </summary>
    /// <param name="context">The current HTTP context, if any.</param>
    /// <returns><see langword="true"/> when appending a Set-Cookie header is still allowed.</returns>
    /// <remarks>
    /// Appending a cookie after the response body has started throws, and the pipeline does run while
    /// a server-rendered widget is streaming (DX-2).
    /// </remarks>
    public static bool CanAssignCookie(HttpContext? context) => VisitorBucketProvider.CanAssignCookie(context);

    /// <inheritdoc />
    public async Task<ExperimentAssignment> GetAssignmentAsync(string indexName, CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext;
        string itemsKey = ItemsKeyPrefix + indexName;

        if (request is not null
            && request.Items.TryGetValue(itemsKey, out object? memoized)
            && memoized is ExperimentAssignment already)
        {
            return already;
        }

        var resolved = await ResolveAsync(indexName, cancellationToken).ConfigureAwait(false);

        if (request is not null)
        {
            request.Items[itemsKey] = resolved;
        }

        return resolved;
    }

    private async Task<ExperimentAssignment> ResolveAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return ExperimentAssignment.None;
            }

            if (await experiments.GetRunningExperimentAsync(indexName, cancellationToken).ConfigureAwait(false) is not { } experiment)
            {
                return ExperimentAssignment.None;
            }

            if (buckets.GetBucketId() is not { Length: > 0 } bucketId)
            {
                logger.LogDebug("No bucket cookie could be read or assigned; bucketing this request into variant A.");

                return new ExperimentAssignment(experiment.Id, SearchVariant.A);
            }

            return new ExperimentAssignment(
                experiment.Id,
                ExperimentBucketing.Variant(bucketId, experiment.Guid, experiment.SplitPercent));
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The search experiment of '{Index}' could not be resolved; the live tuning applies.", indexName);

            return ExperimentAssignment.None;
        }
    }
}
