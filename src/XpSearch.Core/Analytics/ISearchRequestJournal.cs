using CMS.Websites.Routing;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Experiments;

namespace XpSearch.Core.Analytics;

/// <summary>
/// Records one answered search for analytics (spec §9.1, §9.2): the search activity, the
/// <c>queryId</c> to query text mapping a later click is attributed through, and the anonymous query
/// log row.
/// </summary>
/// <remarks>
/// It is called by the caching decorator rather than by a pipeline stage, because a search answered
/// from the cache never enters the pipeline and would otherwise be invisible to analytics, and because
/// only the decorator knows the <c>queryId</c> the caller actually receives.
/// </remarks>
public interface ISearchRequestJournal
{
    /// <summary>
    /// Records one answered search. Never throws. A <paramref name="queryId"/> that was already
    /// recorded is the same search reaching the journal twice and is not recorded again.
    /// </summary>
    /// <param name="queryId">The correlation id returned to the caller.</param>
    /// <param name="queryText">The normalized query text.</param>
    /// <param name="indexName">Code name of the index that was searched.</param>
    /// <param name="total">How many documents matched.</param>
    /// <param name="elapsed">How long answering the request took.</param>
    /// <param name="language">Language of the request, or empty.</param>
    /// <param name="experiment">
    /// The running experiment and variant that answered the request (XP-1), or <see langword="null"/>.
    /// Stamping the query log splits every metric it already carries by variant; the activity is
    /// deliberately left alone.
    /// </param>
    void Record(
        string queryId,
        string queryText,
        string indexName,
        int total,
        TimeSpan elapsed,
        string language,
        ExperimentAssignment? experiment = null);
}

/// <summary>
/// The default <see cref="ISearchRequestJournal"/>.
/// </summary>
/// <remarks>
/// The activity and the query log row are deliberately independent. The activity is consent-gated and
/// skipped for a visitor who has not consented; the query log row holds no personal data and is
/// written either way (spec §9.1, §9.2). Nothing here can fail a search: every failure is swallowed
/// and logged at Debug.
/// </remarks>
public sealed class SearchRequestJournal : ISearchRequestJournal
{
    private readonly ISearchActivityLogger activityLogger;
    private readonly IQueryContextMap queryContexts;
    private readonly IQueryLogQueue queue;
    private readonly IWebsiteChannelContext channelContext;
    private readonly ILogger<SearchRequestJournal> logger;

    /// <summary>Initializes a new instance of the <see cref="SearchRequestJournal"/> class.</summary>
    /// <param name="activityLogger">Writes the Xperience activity.</param>
    /// <param name="queryContexts">Remembers what each <c>queryId</c> searched for.</param>
    /// <param name="queue">Queues the query log row.</param>
    /// <param name="channelContext">Supplies the website channel the search came from.</param>
    /// <param name="logger">Logger.</param>
    public SearchRequestJournal(
        ISearchActivityLogger activityLogger,
        IQueryContextMap queryContexts,
        IQueryLogQueue queue,
        IWebsiteChannelContext channelContext,
        ILogger<SearchRequestJournal> logger)
    {
        ArgumentNullException.ThrowIfNull(activityLogger);
        ArgumentNullException.ThrowIfNull(queryContexts);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(channelContext);
        ArgumentNullException.ThrowIfNull(logger);

        this.activityLogger = activityLogger;
        this.queryContexts = queryContexts;
        this.queue = queue;
        this.channelContext = channelContext;
        this.logger = logger;
    }

    /// <inheritdoc />
    public void Record(
        string queryId,
        string queryText,
        string indexName,
        int total,
        TimeSpan elapsed,
        string language,
        ExperimentAssignment? experiment = null)
    {
        try
        {
            // The same queryId reaching the journal twice is one search answered twice: the results
            // widget's server-rendered first paint and the hydration query that carries its id back
            // (spec §5.8). Recording it again would double the query volume of every such page load
            // and halve its click-through rate, so the repeat is dropped.
            if (!string.IsNullOrEmpty(queryId) && queryContexts.Get(queryId) is not null)
            {
                return;
            }

            activityLogger.LogSearch(queryText, total);

            if (!string.IsNullOrEmpty(queryId))
            {
                queryContexts.Set(queryId, new QueryContext(queryText, indexName));
            }

            queue.Enqueue(QueryLogWorkItem.Append(new QueryLogEntry(
                queryId ?? string.Empty,
                indexName,
                queryText,
                total,
                DateTime.UtcNow,
                ChannelName(),
                language ?? string.Empty,
                (int)elapsed.TotalMilliseconds,
                null,
                experiment is { IsActive: true } ? experiment.ExperimentId : null,
                experiment is { IsActive: true } ? experiment.Variant.ToString() : null)));
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The search could not be logged.");
        }
    }

    /// <summary>
    /// Reads the current website channel. <c>IWebsiteChannelContext</c> only resolves a channel inside
    /// website channel pages, so an API call from elsewhere - or from the administration - logs no
    /// channel rather than failing
    /// (https://docs.kentico.com/documentation/developers-and-admins/development/content-retrieval/retrieve-page-content).
    /// </summary>
    private string ChannelName()
    {
        try
        {
            return channelContext.WebsiteChannelName ?? string.Empty;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The website channel of the search could not be resolved.");

            return string.Empty;
        }
    }
}
