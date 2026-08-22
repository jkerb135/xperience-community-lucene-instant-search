namespace XpSearch.Core.Contract;

public partial class SearchResponse
{
    /// <summary>
    /// Returns a copy of this response carrying <paramref name="queryId"/>, leaving this instance
    /// untouched. The copy is shallow and is cloned rather than built property by property so a
    /// member added to the contract is carried over without anyone remembering to copy it.
    /// </summary>
    /// <param name="queryId">The correlation id for the caller being served.</param>
    /// <returns>The copy.</returns>
    internal SearchResponse WithQueryId(string queryId)
    {
        var copy = (SearchResponse)MemberwiseClone();
        copy.QueryId = queryId;

        return copy;
    }
}
