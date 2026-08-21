using XpSearch.Core.Contract;

namespace XpSearch.Core.Abstractions;

/// <summary>
/// Answers <c>POST /api/xpsearch/suggest</c> (spec §4.3).
/// </summary>
public interface ISuggestService
{
    /// <summary>Produces autocomplete entries for a partial input.</summary>
    /// <param name="request">The suggest request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The suggestions, at most <c>maxItems</c> of them.</returns>
    /// <exception cref="IndexNotFoundException">The requested index is not registered.</exception>
    /// <exception cref="SearchValidationException">The request is not valid.</exception>
    Task<SuggestResponse> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Receives the analytics events posted to <c>POST /api/xpsearch/events</c>.
/// </summary>
/// <remarks>
/// Writing Xperience activities is Phase 6 and consent-gated; until then the default implementation
/// only logs. An implementation must never throw: the endpoint answers 202 Accepted, which means the
/// event was accepted, not that anything was recorded.
/// </remarks>
public interface ISearchEventSink
{
    /// <summary>Hands one validated event off for recording.</summary>
    /// <param name="request">The event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the hand-off is done.</returns>
    Task HandleAsync(EventRequest request, CancellationToken cancellationToken);
}
