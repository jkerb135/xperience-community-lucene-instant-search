namespace XpSearch.Core.Tuning;

/// <summary>
/// The little expression language a <see cref="RuleConsequence.FilterResults"/> rule is written in:
/// comma-separated <c>field:value</c> pairs, all of which must hold.
/// </summary>
/// <remarks>
/// Deliberately not Lucene query syntax. The values come from a marketer typing into the admin UI,
/// facet values are stored verbatim, and an exact term match is both what they mean and the thing
/// that cannot go wrong. <c>Category:coffee, Tags:brewing</c> is the whole language. The field of a
/// pair is an attribute name as a request writes it; <c>BoostRulesStage</c> resolves it through the
/// index schema, so <c>contentType</c> reaches the <c>ContentTypeName</c> field the documents carry.
/// </remarks>
public static class RuleFilterExpression
{
    /// <summary>Parses a filter expression.</summary>
    /// <param name="expression">The stored expression; may be null or blank.</param>
    /// <returns>The field/value pairs, in the order they were written. Malformed pairs are dropped.</returns>
    public static IReadOnlyList<(string Field, string Value)> Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        var pairs = new List<(string, string)>();

        foreach (string part in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = part.IndexOf(':', StringComparison.Ordinal);

            if (separator <= 0 || separator == part.Length - 1)
            {
                continue;
            }

            pairs.Add((part[..separator].Trim(), part[(separator + 1)..].Trim()));
        }

        return pairs;
    }
}
