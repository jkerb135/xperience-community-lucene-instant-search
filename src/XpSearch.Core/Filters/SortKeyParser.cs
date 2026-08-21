using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Filters;

/// <summary>
/// Parser for the request's <c>sort</c> value.
/// </summary>
/// <remarks>
/// The convention is a suffix on the attribute name: <c>price_asc</c>, <c>publishedAt_desc</c>. The
/// literal <c>relevance</c> - and an omitted value - means score descending. The attribute must be
/// marked sortable in the index schema; anything else is a 400.
/// </remarks>
public static class SortKeyParser
{
    /// <summary>The value that selects Lucene relevance ordering.</summary>
    public const string Relevance = "relevance";

    /// <summary>Suffix that selects ascending order.</summary>
    public const string AscendingSuffix = "_asc";

    /// <summary>Suffix that selects descending order.</summary>
    public const string DescendingSuffix = "_desc";

    /// <summary>Parses a sort key.</summary>
    /// <param name="sort">The request's <c>sort</c> value, possibly <see langword="null"/>.</param>
    /// <param name="schema">Schema of the index being searched.</param>
    /// <param name="descending">Set to whether the caller asked for descending order.</param>
    /// <returns>The schema field to sort on, or <see langword="null"/> for relevance ordering.</returns>
    /// <exception cref="SearchValidationException">The key is malformed or the attribute is not sortable.</exception>
    public static SchemaField? Parse(string? sort, IndexSchema schema, out bool descending)
    {
        ArgumentNullException.ThrowIfNull(schema);
        descending = false;

        if (string.IsNullOrWhiteSpace(sort) || string.Equals(sort, Relevance, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string key = sort.Trim();
        string? fieldName = null;

        if (key.EndsWith(DescendingSuffix, StringComparison.OrdinalIgnoreCase))
        {
            descending = true;
            fieldName = key[..^DescendingSuffix.Length];
        }
        else if (key.EndsWith(AscendingSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fieldName = key[..^AscendingSuffix.Length];
        }

        var field = fieldName is null ? null : schema.Find(fieldName);

        if (field is null || !field.Sortable)
        {
            throw new SearchValidationException(
                "sort",
                $"'{sort}' is not a valid sort key for index '{schema.IndexName}'; use 'relevance' or a sortable attribute suffixed with '_asc' or '_desc'.");
        }

        return field;
    }
}
