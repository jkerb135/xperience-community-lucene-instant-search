using XpSearch.Core.Contract;
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
    /// <summary>Gets the facet values for the requested dimensions.</summary>
    /// <param name="context">The executed search.</param>
    /// <param name="dimensions">The dimensions the request asked for.</param>
    /// <param name="maxValues">Maximum number of values to return per dimension.</param>
    /// <returns>
    /// The values keyed by dimension, each list ordered by count descending then value ascending,
    /// followed by the values the request refines the dimension by that the result set has no hit
    /// for, at count 0 and in request order (FC-1). Only requested dimensions appear; each value
    /// carries the label a widget displays for it.
    /// </returns>
    Dictionary<string, FacetValue[]> GetFacets(
        SearchContext context,
        IReadOnlyList<string> dimensions,
        int maxValues);
}
