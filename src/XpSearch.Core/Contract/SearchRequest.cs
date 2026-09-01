namespace XpSearch.Core.Contract;

public partial class SearchRequest
{
    /// <summary>
    /// Returns a copy of this request for <paramref name="query"/>, marked as a probe so it is never
    /// journaled, leaving this instance untouched. Everything else - filters, language, sort, page -
    /// is carried over, because the question being asked is whether the corrected spelling finds
    /// anything <em>for this visitor's search</em> (SG-1). The copy is shallow and cloned rather than
    /// built property by property, like <see cref="SearchResponse.WithQueryId"/>.
    /// </summary>
    /// <param name="query">The query text to ask about.</param>
    /// <returns>The copy.</returns>
    internal SearchRequest AsProbeFor(string query)
    {
        var copy = (SearchRequest)MemberwiseClone();
        copy.Query = query;
        copy.Probe = true;

        return copy;
    }
}
