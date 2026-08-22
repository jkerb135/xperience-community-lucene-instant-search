using CMS.Websites.Routing;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Analytics;

/// <summary>
/// The last stage of the pipeline (spec §4.4, slot 1200): writes the search activity for the current
/// contact and queues the anonymous query log row.
/// </summary>
/// <remarks>
/// The two are deliberately independent. The activity is consent-gated and skipped for a visitor who
/// has not consented; the query log row holds no personal data and is written either way (spec §9.1,
/// §9.2). Nothing here can fail a search: every failure is swallowed and logged at Debug.
/// </remarks>
public sealed class LogActivityStage : ISearchStage
{
    private readonly ISearchActivityLogger activityLogger;
    private readonly IQueryContextMap queryContexts;
    private readonly IQueryLogQueue queue;
    private readonly IWebsiteChannelContext channelContext;
    private readonly ILogger<LogActivityStage> logger;

    /// <summary>Initializes a new instance of the <see cref="LogActivityStage"/> class.</summary>
    /// <param name="activityLogger">Writes the Xperience activity.</param>
    /// <param name="queryContexts">Remembers what each <c>queryId</c> searched for.</param>
    /// <param name="queue">Queues the query log row.</param>
    /// <param name="channelContext">Supplies the website channel the search came from.</param>
    /// <param name="logger">Logger.</param>
    public LogActivityStage(
        ISearchActivityLogger activityLogger,
        IQueryContextMap queryContexts,
        IQueryLogQueue queue,
        IWebsiteChannelContext channelContext,
        ILogger<LogActivityStage> logger)
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
    public int Order => SearchStageOrder.LogActivity;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            string queryId = context.Response?.QueryId ?? string.Empty;
            string queryText = context.QueryText;

            activityLogger.LogSearch(queryText, context.Total);

            if (!string.IsNullOrEmpty(queryId))
            {
                queryContexts.Set(queryId, new QueryContext(queryText, context.Request.Index));
            }

            queue.Enqueue(QueryLogWorkItem.Append(new QueryLogEntry(
                queryId,
                context.Request.Index,
                queryText,
                context.Total,
                DateTime.UtcNow,
                ChannelName(),
                context.Request.Language ?? string.Empty,
                (int)context.Elapsed.TotalMilliseconds)));
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The search could not be logged.");
        }

        return Task.CompletedTask;
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
