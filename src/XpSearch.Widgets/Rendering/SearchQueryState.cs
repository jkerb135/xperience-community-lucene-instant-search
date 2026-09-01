using System.Globalization;

using Microsoft.AspNetCore.Http;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

namespace XpSearch.Widgets.Rendering;

/// <summary>
/// Reads the search state a visitor arrived with out of the request's query string, using the same
/// mapping the JavaScript client writes it with (<c>defaultRouteToState</c> in
/// <c>Client/src/routing.ts</c>, spec §5.5): <c>q</c>, <c>page</c>, <c>sort</c>, a facet attribute
/// carrying comma-joined <c>encodeURIComponent</c>-escaped values with an optional
/// <c>&lt;attribute&gt;_op</c>, and <c>&lt;attribute&gt;_&lt;operator&gt;</c> for a numeric
/// comparison. This is what lets the server render the first paint of a shared result URL.
/// </summary>
public static class SearchQueryState
{
    private const string QueryParam = "q";
    private const string PageParam = "page";
    private const string SortParam = "sort";

    private static readonly string[] Reserved = [QueryParam, PageParam, SortParam];

    private static readonly Dictionary<string, NumericOperator> Operators = new(StringComparer.Ordinal)
    {
        ["lt"] = NumericOperator.Lt,
        ["lte"] = NumericOperator.Lte,
        ["eq"] = NumericOperator.Eq,
        ["ne"] = NumericOperator.Ne,
        ["gte"] = NumericOperator.Gte,
        ["gt"] = NumericOperator.Gt
    };

    /// <summary>
    /// Applies the state in <paramref name="query"/> to <paramref name="request"/>. A parameter that
    /// is absent is left alone, so the widget's own defaults survive.
    /// </summary>
    /// <param name="request">The request to fill in.</param>
    /// <param name="query">The request's query string.</param>
    /// <param name="schema">
    /// The schema of the index being searched. When supplied, only attributes the index actually has
    /// become filters: a page URL carries foreign parameters (Kentico's <c>uh</c>, <c>utm_*</c>) that
    /// the query endpoint would reject. When <see langword="null"/> every parameter is read as a
    /// filter, as before.
    /// </param>
    public static void Apply(SearchRequest request, IQueryCollection query, IndexSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);

        var facets = new List<FacetFilter>();
        var numeric = new List<NumericFilter>();
        var operators = new Dictionary<string, FacetOperator>(StringComparer.Ordinal);

        foreach (var (key, raw) in query)
        {
            if (Array.IndexOf(Reserved, key) >= 0 || raw.Count == 0)
            {
                continue;
            }

            int separator = key.LastIndexOf('_');
            string suffix = separator > 0 ? key[(separator + 1)..] : string.Empty;
            string attribute = separator > 0 ? key[..separator] : key;

            if (suffix == "op")
            {
                if (string.Equals(raw[0], "and", StringComparison.Ordinal))
                {
                    operators[attribute] = FacetOperator.And;
                }
                else if (string.Equals(raw[0], "or", StringComparison.Ordinal))
                {
                    operators[attribute] = FacetOperator.Or;
                }

                continue;
            }

            // A `<attribute>_gte` whose values are not all numbers is not a comparison; the client
            // falls through to reading it as a facet, and a shared URL must mean the same thing here.
            if (Operators.TryGetValue(suffix, out var comparison) && raw.All(IsNumber))
            {
                if (schema is not null && schema.Find(attribute) is null)
                {
                    continue;
                }

                numeric.AddRange(raw.Select(value => new NumericFilter
                {
                    Attribute = attribute,
                    Operator = comparison,
                    Value = ToNumber(value!)
                }));

                continue;
            }

            if (schema is not null && schema.Find(key) is null)
            {
                continue;
            }

            string[] values = raw
                .SelectMany(value => (value ?? string.Empty).Split(','))
                .Where(value => value.Length > 0)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            if (values.Length > 0)
            {
                facets.Add(new FacetFilter { Attribute = key, Values = values });
            }
        }

        foreach (var facet in facets)
        {
            if (operators.TryGetValue(facet.Attribute, out var facetOperator))
            {
                facet.Operator = facetOperator;
            }
        }

        if (query.TryGetValue(QueryParam, out var text))
        {
            request.Query = text[0] ?? string.Empty;
        }

        if (query.TryGetValue(SortParam, out var sort) && sort[0] is { Length: > 0 } sortKey)
        {
            request.Sort = sortKey;
        }

        if (query.TryGetValue(PageParam, out var page)
            && long.TryParse(page[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            && number > 1)
        {
            request.Page = number;
        }

        if (facets.Count > 0 || numeric.Count > 0)
        {
            request.Filters = new Filters
            {
                Facets = facets.Count > 0 ? [.. facets] : null,
                Numeric = numeric.Count > 0 ? [.. numeric] : null
            };
        }
    }

    private static bool IsNumber(string? value) =>
        value is { Length: > 0 }
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static double ToNumber(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
