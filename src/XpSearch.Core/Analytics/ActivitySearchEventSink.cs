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
/// <see cref="IQueryContextMap"/>. An event whose id is unknown - because it expired, or because the
/// search was answered by another application instance - is still recorded, only with an empty query.
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
    public Task HandleAsync(EventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            string query = queryContexts.Get(request.QueryId)?.Query ?? string.Empty;

            if (request.Type == EventType.Click)
            {
                int position = (int)Math.Clamp(request.Position ?? 1, 1, int.MaxValue);

                activityLogger.LogClick(query, request.ResultId, position);
                queue.Enqueue(QueryLogWorkItem.Click(request.QueryId, position));
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

        return Task.CompletedTask;
    }
}
