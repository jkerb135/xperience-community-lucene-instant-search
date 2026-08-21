using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Abstractions;

/// <summary>
/// Reads facet counts out of an executed search.
/// </summary>
/// <remarks>
/// ADR-0001 picked the native taxonomy sidecar the Lucene integration already maintains, so
/// <c>TaxonomyFacetProvider</c> is the only implementation that ships. The interface stays because a
/// flat, doc-values based provider is a plausible later addition for indexes built without a
/// taxonomy (spec §4.5).
/// </remarks>
public interface IFacetProvider
{
    /// <summary>Gets the counts for the requested dimensions.</summary>
    /// <param name="context">The executed search.</param>
    /// <param name="dimensions">The dimensions the request asked for.</param>
    /// <param name="maxValues">Maximum number of values to return per dimension.</param>
    /// <returns>
    /// Counts keyed by dimension and then by value. Only requested dimensions appear and only values
    /// with a non-zero count, as the contract requires.
    /// </returns>
    Dictionary<string, Dictionary<string, long>> GetCounts(
        SearchContext context,
        IReadOnlyList<string> dimensions,
        int maxValues);
}
