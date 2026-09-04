using System.Globalization;

using Lucene.Net.Facet;
using Lucene.Net.Index;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Facets;

/// <summary>
/// Reads counts from the <c>Lucene.Net.Facet</c> taxonomy sidecar that
/// <c>Kentico.Xperience.Lucene</c> maintains next to every index (ADR-0001, option A), and pairs
/// each value with the label a widget displays for it and, for a taxonomy dimension, its ancestry
/// (ADR-0018).
/// </summary>
public sealed class TaxonomyFacetProvider : IFacetProvider
{
    /// <summary>What a dimension with no tag titles - anything but a taxonomy field - has.</summary>
    private static readonly IReadOnlyDictionary<string, (string Title, string[] Path)> NoLabels =
        new Dictionary<string, (string Title, string[] Path)>(StringComparer.Ordinal);

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

        if (context.Facets is not { } counted)
        {
            return facets;
        }

        foreach (string attribute in dimensions)
        {
            var field = context.Schema.Find(attribute);
            string dimension = field?.LuceneName ?? attribute;

            var values = Read(counted, dimension, Math.Max(1, maxValues));
            var selected = Selected(context, dimension);
            var labels = values.Count > 0 || selected.Count > 0
                ? ReadLabels(field, context.Request.Index)
                : NoLabels;

            ApplyLabels(labels, values);

            // The contract promises that every ancestor a path names is itself in the list, and the
            // top-N cut above could in principle have dropped one; re-reading is lazy because
            // nothing is missing for an index this library wrote (see EnsureAncestors).
            EnsureAncestors(values, () => Read(counted, dimension, int.MaxValue));

            // Count descending, then value ascending, so the order is stable between searches.
            values.Sort((left, right) => right.Count != left.Count
                ? right.Count.CompareTo(left.Count)
                : string.CompareOrdinal(left.Value, right.Value));

            AppendSelected(values, selected, labels);

            facets[attribute] = [.. values];
        }

        return facets;
    }

    /// <summary>
    /// Appends the values the request refines this dimension by that the filtered result set has no
    /// hit for, at count 0 (FC-1). A refinement a visitor arrived with must always come back named,
    /// or the UI that has to offer "remove it" can only print the stored code.
    /// </summary>
    /// <param name="values">The counted values, already ordered. Missing selections are appended in request order.</param>
    /// <param name="selected">The values the request refines this dimension by, in request order.</param>
    /// <param name="labels">The dimension's tag titles and ancestry; empty for a non-taxonomy attribute.</param>
    internal static void AppendSelected(
        List<FacetValue> values,
        IReadOnlyList<string> selected,
        IReadOnlyDictionary<string, (string Title, string[] Path)> labels)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(labels);

        if (selected.Count == 0)
        {
            return;
        }

        var present = new HashSet<string>(values.Select(value => value.Value), StringComparer.Ordinal);

        foreach (string value in selected)
        {
            // Ancestors first: the contract promises every ancestor a path names is itself in the
            // same facet's values, and an ancestor with no hits either is not in the counted list.
            foreach (string name in Ancestry(value, labels).Append(value))
            {
                if (present.Add(name))
                {
                    (string title, string[] path) = labels.TryGetValue(name, out var label) ? label : (name, []);

                    values.Add(new FacetValue
                    {
                        Value = name,
                        Label = title,
                        Count = 0,
                        Path = path.Length == 0 ? null : path
                    });
                }
            }
        }
    }

    private static string[] Ancestry(string value, IReadOnlyDictionary<string, (string Title, string[] Path)> labels) =>
        labels.TryGetValue(value, out var label) ? label.Path : [];

    /// <summary>The values the request refines <paramref name="dimension"/> by, in request order.</summary>
    private static List<string> Selected(SearchContext context, string dimension)
    {
        var selected = new List<string>();

        foreach (var filter in context.FacetFilters)
        {
            // NormalizeRequestStage resolved every filter's attribute to the schema field's Lucene
            // name, which is the dimension the counts were read from.
            if (string.Equals(filter.Attribute, dimension, StringComparison.Ordinal))
            {
                selected.AddRange(filter.Values ?? []);
            }
        }

        return selected;
    }

    /// <summary>
    /// Guarantees what the contract promises about <c>path</c>: every ancestor named in an emitted
    /// value's path is itself emitted, so a client can build the tree from the values alone.
    /// </summary>
    /// <param name="values">The values that survived the top-N cut. Missing ancestors are appended.</param>
    /// <param name="all">Every counted value of the dimension; called only when something is missing.</param>
    /// <remarks>
    /// Writing ancestors before their descendants (see <c>XpSearchIndexingStrategy.WriteTag</c>)
    /// makes this a no-op for an index this library wrote: an ancestor's count is never lower than
    /// its descendant's and its taxonomy ordinal is lower, so it always survives the cut. It is not
    /// a no-op for an index written before a tag was moved, which is why the promise is kept here
    /// rather than assumed.
    /// </remarks>
    internal static void EnsureAncestors(List<FacetValue> values, Func<IReadOnlyList<FacetValue>> all)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(all);

        var present = new HashSet<string>(values.Select(value => value.Value), StringComparer.Ordinal);

        if (!values.Any(value => (value.Path ?? []).Any(ancestor => !present.Contains(ancestor))))
        {
            return;
        }

        var counted = new Dictionary<string, FacetValue>(StringComparer.Ordinal);

        foreach (var value in all())
        {
            counted[value.Value] = value;
        }

        // Indexed loop: appending an ancestor can pull in an ancestor of its own.
        for (int i = 0; i < values.Count; i++)
        {
            foreach (string ancestor in values[i].Path ?? [])
            {
                if (present.Add(ancestor) && counted.TryGetValue(ancestor, out var value))
                {
                    values.Add(value);
                }
            }
        }
    }

    private static List<FacetValue> Read(Lucene.Net.Facet.Facets facets, string dimension, int topN)
    {
        // GetTopChildren returns null for a dimension that has no match in the current result
        // set; the contract wants the attribute present with no values rather than missing.
        var result = facets.GetTopChildren(topN, dimension);
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

        return values;
    }

    /// <summary>
    /// Replaces the code names of a taxonomy dimension with the tag titles the indexing strategy
    /// wrote next to them, and hands each value the ancestry written beside it.
    /// </summary>
    /// <param name="labels">The dimension's tag titles and ancestry; empty for a non-taxonomy attribute.</param>
    /// <param name="values">The values to label.</param>
    private static void ApplyLabels(
        IReadOnlyDictionary<string, (string Title, string[] Path)> labels,
        List<FacetValue> values)
    {
        foreach (var value in values)
        {
            if (labels.TryGetValue(value.Value, out var label))
            {
                value.Label = label.Title;

                // Null, not an empty array: the contract says a root-level value has no path.
                value.Path = label.Path.Length == 0 ? null : label.Path;
            }
        }
    }

    /// <summary>
    /// The tag titles and ancestry of a taxonomy dimension, or nothing for any other attribute -
    /// where the label of a value is the value itself.
    /// </summary>
    /// <remarks>
    /// The labels are read from the term dictionary of the dimension's label field rather than from
    /// the matched documents: the term dictionary already holds one entry per distinct tag, so the
    /// whole map costs one enumeration, and it also covers values that the current result set does
    /// not contain - which is what names a selected value with no hits (FC-1). A dimension written
    /// by a strategy that predates the label field simply has no terms, and every label stays equal
    /// to its value with no path.
    /// </remarks>
    private IReadOnlyDictionary<string, (string Title, string[] Path)> ReadLabels(SchemaField? field, string indexName) =>
        field is { Kind: SearchFieldKind.Taxonomy }
            ? ReadLabels(indexName, LuceneFieldNames.LabelFieldName(field))
            : NoLabels;

    private Dictionary<string, (string Title, string[] Path)> ReadLabels(string indexName, string labelField) =>
        accessor.UseSearcher(indexName, searcher =>
        {
            var labels = new Dictionary<string, (string Title, string[] Path)>(StringComparer.Ordinal);
            var terms = MultiFields.GetTerms(searcher.IndexReader, labelField);

            if (terms is null)
            {
                return labels;
            }

            var enumerator = terms.GetEnumerator();

            while (enumerator.MoveNext())
            {
                (string? value, string? title, string[] path) =
                    LuceneFieldNames.SplitLabel(enumerator.Term.Utf8ToString());

                if (value is not null && title is not null)
                {
                    labels[value] = (title, path);
                }
            }

            return labels;
        });
}
