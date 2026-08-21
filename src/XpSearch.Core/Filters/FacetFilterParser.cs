using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Filters;

/// <summary>
/// One <c>attribute:value</c> refinement. Values inside a group are ORed, groups are ANDed.
/// </summary>
/// <param name="Attribute">The facetable attribute, i.e. the taxonomy dimension.</param>
/// <param name="Value">The facet value, i.e. the tag code name.</param>
public sealed record FacetRefinement(string Attribute, string Value);

/// <summary>
/// Parser for the request's <c>facetFilters</c>: an outer array that is ANDed over inner arrays that
/// are ORed, each entry formatted <c>attribute:value</c>.
/// </summary>
public static class FacetFilterParser
{
    /// <summary>Parses and validates the whole <c>facetFilters</c> structure.</summary>
    /// <param name="groups">The request's <c>facetFilters</c>, possibly <see langword="null"/>.</param>
    /// <param name="schema">Schema of the index being searched.</param>
    /// <returns>The refinement groups, empty groups removed.</returns>
    /// <exception cref="SearchValidationException">An entry is malformed or names a non-facetable attribute.</exception>
    public static IReadOnlyList<IReadOnlyList<FacetRefinement>> ParseAll(string[][]? groups, IndexSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (groups is null)
        {
            return [];
        }

        var parsed = new List<IReadOnlyList<FacetRefinement>>();

        foreach (string[] group in groups)
        {
            if (group is null || group.Length == 0)
            {
                continue;
            }

            var refinements = new List<FacetRefinement>(group.Length);

            foreach (string entry in group)
            {
                // Only the first colon separates - tag code names may legitimately contain one.
                int separator = entry?.IndexOf(':', StringComparison.Ordinal) ?? -1;
                if (separator <= 0 || separator == entry!.Length - 1)
                {
                    throw new SearchValidationException(
                        "facetFilters",
                        $"'{entry}' is not a valid facet filter; the expected form is 'attribute:value'.");
                }

                string attribute = entry[..separator];
                var field = schema.Find(attribute);

                if (field is null || !field.Facetable)
                {
                    throw new SearchValidationException(
                        "facetFilters",
                        $"'{attribute}' is not a facetable attribute of index '{schema.IndexName}'.");
                }

                refinements.Add(new FacetRefinement(field.Name, entry[(separator + 1)..]));
            }

            parsed.Add(refinements);
        }

        return parsed;
    }

    /// <summary>Validates the request's <c>facets</c> list and resolves it to schema field names.</summary>
    /// <param name="facets">The requested facet attributes, possibly <see langword="null"/>.</param>
    /// <param name="schema">Schema of the index being searched.</param>
    /// <returns>The requested dimensions, in request order.</returns>
    /// <exception cref="SearchValidationException">An attribute is unknown or not facetable.</exception>
    public static IReadOnlyList<string> ParseRequestedFacets(IEnumerable<string>? facets, IndexSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (facets is null)
        {
            return [];
        }

        var names = new List<string>();

        foreach (string facet in facets)
        {
            var field = schema.Find(facet);
            if (field is null || !field.Facetable)
            {
                throw new SearchValidationException(
                    "facets",
                    $"'{facet}' is not a facetable attribute of index '{schema.IndexName}'.");
            }

            names.Add(field.Name);
        }

        return names;
    }
}
