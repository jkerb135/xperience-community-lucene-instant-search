using System.Globalization;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Facets;

/// <summary>
/// Reads counts from the <c>Lucene.Net.Facet</c> taxonomy sidecar that
/// <c>Kentico.Xperience.Lucene</c> maintains next to every index (ADR-0001, option A).
/// </summary>
public sealed class TaxonomyFacetProvider : IFacetProvider
{
    /// <inheritdoc />
    public Dictionary<string, Dictionary<string, long>> GetCounts(
        SearchContext context,
        IReadOnlyList<string> dimensions,
        int maxValues)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dimensions);

        var counts = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

        if (context.Facets is null)
        {
            return counts;
        }

        foreach (string dimension in dimensions)
        {
            // GetTopChildren returns null for a dimension that has no match in the current result
            // set; the contract wants the attribute present with no values rather than missing.
            var result = context.Facets.GetTopChildren(Math.Max(1, maxValues), dimension);
            var values = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (var labelAndValue in result?.LabelValues ?? [])
            {
                long count = Convert.ToInt64(labelAndValue.Value, CultureInfo.InvariantCulture);

                if (count > 0)
                {
                    values[labelAndValue.Label] = count;
                }
            }

            counts[dimension] = values;
        }

        return counts;
    }
}
