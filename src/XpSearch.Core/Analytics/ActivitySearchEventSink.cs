using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;

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
/// which is what a contact group condition can be built on. Only an event that names a
/// <c>queryId</c> this server issued, stays within that query's event budget and claims a position
/// the query actually returned is recorded (SC-1); anything else is dropped and logged at Debug -
/// including an id the query log cannot resolve because the search has not been drained to it yet.
/// The sink never throws: <c>/events</c> answers 202 Accepted, which means accepted, not recorded.
/// </remarks>
public sealed class ActivitySearchEventSink : ISearchEventSink
{
    private readonly ISearchActivityLogger activityLogger;
    private readonly IQueryContextMap queryContexts;
    private readonly IQueryLogQueue queue;
    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly ILogger<ActivitySearchEventSink> logger;

    /// <summary>Initializes a new instance of the <see cref="ActivitySearchEventSink"/> class.</summary>
    /// <param name="activityLogger">Writes the Xperience activity.</param>
    /// <param name="queryContexts">Resolves the query behind a <c>queryId</c>.</param>
    /// <param name="queue">Queues the query log update.</param>
    /// <param name="options">Supplies the per-query event budget and the page size ceiling.</param>
    /// <param name="logger">Logger.</param>
    public ActivitySearchEventSink(
        ISearchActivityLogger activityLogger,
        IQueryContextMap queryContexts,
        IQueryLogQueue queue,
        IOptionsMonitor<XpSearchOptions> options,
        ILogger<ActivitySearchEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(activityLogger);
        ArgumentNullException.ThrowIfNull(queryContexts);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.activityLogger = activityLogger;
        this.queryContexts = queryContexts;
        this.queue = queue;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(EventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await ContextOfAsync(request.QueryId, cancellationToken).ConfigureAwait(false);

        if (context is null)
        {
            logger.LogDebug("A {EventType} event named an unknown queryId and was dropped.", request.Type);

            return;
        }

        try
        {
            var settings = options.CurrentValue;

            // Counted before the event is judged, so a caller replaying one queryId burns its budget
            // whether the replays are plausible or not.
            if (queryContexts.CountEvent(request.QueryId) > settings.MaxEventsPerQuery)
            {
                logger.LogDebug("A {EventType} event exceeded the {Budget} event budget of its queryId and was dropped.", request.Type, settings.MaxEventsPerQuery);

                return;
            }

            int position = (int)Math.Clamp(request.Position ?? 1, 1, int.MaxValue);

            // Precedence: the page window the journal recorded, then the log row's result count for a
            // context resolved from the database (which knows no page window), then the index ceiling.
            int highest = context.MaxPosition > 0
                ? context.MaxPosition
                : context.ResultCount > 0 ? context.ResultCount : settings.MaxPageSize;

            // The contract ignores position on a conversion, so only a click claims one.
            if (request.Type == EventType.Click && position > highest)
            {
                logger.LogDebug("A click event claimed position {Position} of a query that returned {Highest} results at most and was dropped.", position, highest);

                return;
            }

            string query = context.Query;

            if (request.Type == EventType.Click)
            {
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
    /// Resolves the context of an event. Its own try/catch: the lookup reaches the database, and a
    /// database that is down must not throw out of a route that answers 202 - an unresolved id is
    /// treated as unknown, which is what SC-1 does with it anyway.
    /// </summary>
    private async Task<QueryContext?> ContextOfAsync(string queryId, CancellationToken cancellationToken)
    {
        try
        {
            return await queryContexts.GetAsync(queryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The query behind the search event could not be resolved.");

            return null;
        }
    }
}
