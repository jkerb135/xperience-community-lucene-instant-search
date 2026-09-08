using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

namespace XpSearch.Core.Analytics;

/// <summary>
/// The production <see cref="ISearchEventSink"/> (spec §9.1): turns a click into the
/// <c>xpsearch_click</c> activity plus the clicked position on the query log row, and a conversion
/// into the <c>xpsearch_conversion</c> activity.
/// </summary>
/// <remarks>
/// The query text of both activities is resolved from the <c>queryId</c> through
/// <see cref="IQueryContextMap.GetAsync"/> - memory first, the query log second, so an event that
/// lands on another instance of a web farm still resolves (WF-1) - and becomes the activity's value,
/// which is what a contact group condition can be built on. An event whose id is unknown, because it
/// expired or because the search it belongs to has not reached the log yet, is still recorded, only
/// with an empty value.
/// The sink never throws: <c>/events</c> answers 202 Accepted, which means accepted, not recorded.
/// </remarks>
public sealed class ActivitySearchEventSink : ISearchEventSink
{
    private readonly ISearchActivityLogger activityLogger;
    private readonly IQueryContextMap queryContexts;
    private readonly IQueryLogQueue queue;
    private readonly ILogger<ActivitySearchEventSink> logger;

    /// <summary>Initializes a new instance of the <see cref="ActivitySearchEventSink"/> class.</summary>
    /// <param name="activityLogger">Writes the Xperience activity.</param>
    /// <param name="queryContexts">Resolves the query behind a <c>queryId</c>.</param>
    /// <param name="queue">Queues the query log update.</param>
    /// <param name="logger">Logger.</param>
    public ActivitySearchEventSink(
        ISearchActivityLogger activityLogger,
        IQueryContextMap queryContexts,
        IQueryLogQueue queue,
        ILogger<ActivitySearchEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(activityLogger);
        ArgumentNullException.ThrowIfNull(queryContexts);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(logger);

        this.activityLogger = activityLogger;
        this.queryContexts = queryContexts;
        this.queue = queue;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(EventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string query = await QueryOfAsync(request.QueryId, cancellationToken).ConfigureAwait(false);

        try
        {
            if (request.Type == EventType.Click)
            {
                int position = (int)Math.Clamp(request.Position ?? 1, 1, int.MaxValue);

                activityLogger.LogClick(query, request.ResultId, position);
                queue.Enqueue(QueryLogWorkItem.Click(request.QueryId, position, request.ResultId ?? string.Empty));
            }
            else
            {
                activityLogger.LogConversion(query, request.ResultId);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The {EventType} search event could not be recorded.", request.Type);
        }
    }

    /// <summary>
    /// Resolves the query text of an event. Its own try/catch: the lookup now reaches the database, and
    /// a database that is down must not cost the click its activity and its query log update.
    /// </summary>
    private async Task<string> QueryOfAsync(string queryId, CancellationToken cancellationToken)
    {
        try
        {
            var context = await queryContexts.GetAsync(queryId, cancellationToken).ConfigureAwait(false);

            return context?.Query ?? string.Empty;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The query behind the search event could not be resolved.");

            return string.Empty;
        }
    }
}
