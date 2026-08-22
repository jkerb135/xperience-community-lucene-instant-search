using System.Globalization;

using Lucene.Net.Index;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Facets;

/// <summary>
/// Reads counts from the <c>Lucene.Net.Facet</c> taxonomy sidecar that
/// <c>Kentico.Xperience.Lucene</c> maintains next to every index (ADR-0001, option A), and pairs
/// each value with the label a widget displays for it.
/// </summary>
public sealed class TaxonomyFacetProvider : IFacetProvider
{
    private readonly ILuceneIndexAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="TaxonomyFacetProvider"/> class.</summary>
    /// <param name="accessor">The Lucene seam, used to read the tag titles out of the index.</param>
    public TaxonomyFacetProvider(ILuceneIndexAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        this.accessor = accessor;
    }

    /// <inheritdoc />
    public Dictionary<string, FacetValue[]> GetFacets(
        SearchContext context,
        IReadOnlyList<string> dimensions,
        int maxValues)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dimensions);

        var facets = new Dictionary<string, FacetValue[]>(StringComparer.Ordinal);

        if (context.Facets is null)
        {
            return facets;
        }

        foreach (string attribute in dimensions)
        {
            var field = context.Schema.Find(attribute);
            string dimension = field?.LuceneName ?? attribute;

            // GetTopChildren returns null for a dimension that has no match in the current result
            // set; the contract wants the attribute present with no values rather than missing.
            var result = context.Facets.GetTopChildren(Math.Max(1, maxValues), dimension);
            var values = new List<FacetValue>();

            foreach (var labelAndValue in result?.LabelValues ?? [])
            {
                long count = Convert.ToInt64(labelAndValue.Value, CultureInfo.InvariantCulture);

                if (count > 0)
                {
                    // Label defaults to the value: it is only different for a taxonomy dimension,
                    // where the tag has a title as well as a code name.
                    values.Add(new FacetValue { Value = labelAndValue.Label, Label = labelAndValue.Label, Count = count });
                }
            }

            ApplyTitles(field, context.Request.Index, values);

            // Count descending, then value ascending, so the order is stable between searches.
            values.Sort((left, right) => right.Count != left.Count
                ? right.Count.CompareTo(left.Count)
                : string.CompareOrdinal(left.Value, right.Value));

            facets[attribute] = [.. values];
        }

        return facets;
    }

    /// <summary>
    /// Replaces the code names of a taxonomy dimension with the tag titles the indexing strategy
    /// wrote next to them.
    /// </summary>
    /// <remarks>
    /// The titles are read from the term dictionary of the dimension's label field rather than from
    /// the matched documents: the term dictionary already holds one entry per distinct tag, so the
    /// whole map costs one enumeration, and it also covers values that the current page does not
    /// contain. A dimension written by a strategy that predates the label field simply has no terms,
    /// and every label stays equal to its value.
    /// </remarks>
    private void ApplyTitles(SchemaField? field, string indexName, List<FacetValue> values)
    {
        if (values.Count == 0 || field is null || field.Kind != SearchFieldKind.Taxonomy)
        {
            return;
        }

        var titles = ReadTitles(indexName, LuceneFieldNames.LabelFieldName(field));

        foreach (var value in values)
        {
            if (titles.TryGetValue(value.Value, out string? title))
            {
                value.Label = title;
            }
        }
    }

    private Dictionary<string, string> ReadTitles(string indexName, string labelField) =>
        accessor.UseSearcher(indexName, searcher =>
        {
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            var terms = MultiFields.GetTerms(searcher.IndexReader, labelField);

            if (terms is null)
            {
                return titles;
            }

            var enumerator = terms.GetEnumerator();

            while (enumerator.MoveNext())
            {
                (string? value, string? title) = LuceneFieldNames.SplitLabel(enumerator.Term.Utf8ToString());

                if (value is not null && title is not null)
                {
                    titles[value] = title;
                }
            }

            return titles;
        });
}
