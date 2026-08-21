using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

namespace XpSearch.Core.Search;

/// <summary>
/// The default <see cref="ISearchEventSink"/>: logs the event at Debug and does nothing else.
/// </summary>
/// <remarks>
/// Writing an Xperience custom activity for click and conversion events is Phase 6 (spec §9.1) and is
/// gated on the visitor's tracking consent. Replacing this registration is how a project opts in
/// early.
/// </remarks>
public sealed class LoggingSearchEventSink : ISearchEventSink
{
    private readonly ILogger<LoggingSearchEventSink> logger;

    /// <summary>Initializes a new instance of the <see cref="LoggingSearchEventSink"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public LoggingSearchEventSink(ILogger<LoggingSearchEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(EventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogDebug(
            "Search event {EventType} for {ObjectId} on query {QueryId} (index {Index}, position {Position}).",
            request.EventType,
            request.ObjectId,
            request.QueryId,
            request.Index,
            request.Position);

        return Task.CompletedTask;
    }
}
