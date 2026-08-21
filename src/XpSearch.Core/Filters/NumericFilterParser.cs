using System.Globalization;
using System.Text.RegularExpressions;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Filters;

/// <summary>Comparison operator of a <see cref="NumericFilter"/>.</summary>
public enum NumericOperator
{
    /// <summary>Strictly less than.</summary>
    LessThan,

    /// <summary>Less than or equal.</summary>
    LessThanOrEqual,

    /// <summary>Strictly greater than.</summary>
    GreaterThan,

    /// <summary>Greater than or equal.</summary>
    GreaterThanOrEqual,

    /// <summary>Equal.</summary>
    Equal,

    /// <summary>Not equal.</summary>
    NotEqual
}

/// <summary>One parsed entry of the request's <c>numericFilters</c> array.</summary>
/// <param name="Attribute">The attribute the filter applies to.</param>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="Value">The right-hand side, always a <see cref="double"/>; dates are epoch seconds.</param>
public sealed record NumericFilter(string Attribute, NumericOperator Operator, double Value);

/// <summary>
/// Parser for the <c>numericFilters</c> grammar frozen in <c>contract/xpsearch-api.schema.json</c>:
/// an attribute starting with a letter or underscore, one of the six comparison operators, and an
/// optionally negative integer or decimal, with optional whitespace around the operator.
/// </summary>
public static partial class NumericFilterParser
{
    [GeneratedRegex(@"^\s*(?<attr>[A-Za-z_][\w.]*)\s*(?<op><=|>=|!=|<|>|=)\s*(?<value>-?\d+(\.\d+)?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();

    /// <summary>Parses one filter expression.</summary>
    /// <param name="expression">The expression, for example <c>price&lt;=50</c>.</param>
    /// <param name="filter">The parsed filter when parsing succeeded.</param>
    /// <returns><see langword="true"/> when <paramref name="expression"/> matches the grammar.</returns>
    public static bool TryParse(string? expression, out NumericFilter? filter)
    {
        filter = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var match = Grammar().Match(expression);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        var op = match.Groups["op"].Value switch
        {
            "<=" => NumericOperator.LessThanOrEqual,
            ">=" => NumericOperator.GreaterThanOrEqual,
            "!=" => NumericOperator.NotEqual,
            "<" => NumericOperator.LessThan,
            ">" => NumericOperator.GreaterThan,
            _ => NumericOperator.Equal
        };

        filter = new NumericFilter(match.Groups["attr"].Value, op, value);
        return true;
    }

    /// <summary>Parses every expression of a request, validating each attribute against the schema.</summary>
    /// <param name="expressions">The request's <c>numericFilters</c>, possibly <see langword="null"/>.</param>
    /// <param name="schema">Schema of the index being searched.</param>
    /// <returns>The parsed filters, in request order.</returns>
    /// <exception cref="SearchValidationException">An expression is malformed or names a non-numeric attribute.</exception>
    public static IReadOnlyList<NumericFilter> ParseAll(IEnumerable<string>? expressions, IndexSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (expressions is null)
        {
            return [];
        }

        var parsed = new List<NumericFilter>();

        foreach (string expression in expressions)
        {
            if (!TryParse(expression, out var filter))
            {
                throw new SearchValidationException("numericFilters", $"'{expression}' is not a valid numeric filter.");
            }

            var field = schema.Find(filter!.Attribute);
            if (field is null || (field.Kind != SearchFieldKind.Number && field.Kind != SearchFieldKind.Date))
            {
                throw new SearchValidationException(
                    "numericFilters",
                    $"'{filter.Attribute}' is not a numeric attribute of index '{schema.IndexName}'.");
            }

            parsed.Add(filter with { Attribute = field.Name });
        }

        return parsed;
    }
}
