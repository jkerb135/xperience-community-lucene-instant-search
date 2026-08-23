namespace XpSearch.Core.Analytics;

/// <summary>
/// Writes the search activities of spec §9.1 for the current contact.
/// </summary>
/// <remarks>
/// Every method is synchronous and must be called from inside the HTTP request: custom activities are
/// logged for the <em>current contact</em>, which a worker thread has no access to
/// (https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/set-up-activities/custom-activities).
/// No method ever throws, and none of them logs anything for a visitor who has not consented to
/// tracking.
/// </remarks>
public interface ISearchActivityLogger
{
    /// <summary>
    /// Logs <c>xpsearch_query</c> when <paramref name="total"/> is above zero, <c>xpsearch_noresults</c>
    /// otherwise.
    /// </summary>
    /// <param name="query">The normalized query text, used as the activity value.</param>
    /// <param name="total">How many documents matched.</param>
    void LogSearch(string query, int total);

    /// <summary>
    /// Logs <c>xpsearch_click</c> with the query as its value, the result id in
    /// <c>ActivityComment</c> and the position in <c>ActivityItemDetailID</c>.
    /// </summary>
    /// <param name="query">The normalized text of the search that produced the result.</param>
    /// <param name="resultId">Id of the clicked result.</param>
    /// <param name="position">One-based position of the result in the list.</param>
    void LogClick(string query, string resultId, int position);

    /// <summary>
    /// Logs <c>xpsearch_conversion</c> with the query as its value and the result id in
    /// <c>ActivityComment</c>.
    /// </summary>
    /// <param name="query">The normalized text of the search that produced the result.</param>
    /// <param name="resultId">Id of the result the goal is attributed to.</param>
    void LogConversion(string query, string resultId);
}
