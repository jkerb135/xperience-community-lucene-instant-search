using XpSearch.Core.Options;

namespace XpSearch.Widgets.Sorting;

/// <summary>One entry of the sort selector: the sort key sent to the API and its editor-chosen label.</summary>
/// <param name="Value">The sort key, e.g. <c>relevance</c>, <c>newest</c> or <c>price_asc</c>.</param>
/// <param name="Label">What the visitor sees.</param>
public sealed record SortOption(string Value, string Label);

/// <summary>
/// Parses and validates the "Sort options" text an editor types into the sort selector widget: one
/// option per line, <c>key;Label</c> (spec §7.3).
/// </summary>
public static class SortOptionsValidation
{
    /// <summary>The always-available sort key that means "by relevance".</summary>
    public const string RelevanceKey = "relevance";

    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>Parses the editor's text. Lines without a label use the key as the label.</summary>
    /// <param name="text">The raw text, one <c>key;Label</c> per line.</param>
    /// <returns>The parsed options, in the order they were written.</returns>
    public static IReadOnlyList<SortOption> Parse(string? text)
    {
        var options = new List<SortOption>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return options;
        }

        foreach (string line in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int at = line.IndexOf(';', StringComparison.Ordinal);
            string key = (at < 0 ? line : line[..at]).Trim();
            string label = at < 0 ? key : line[(at + 1)..].Trim();

            if (key.Length != 0)
            {
                options.Add(new SortOption(key, label.Length == 0 ? key : label));
            }
        }

        return options;
    }

    /// <summary>
    /// Whether a sort key is one the API will accept for an index: <c>relevance</c>, a key configured
    /// in <see cref="XpSearchIndexOptions.SortKeys"/>, or a field named with the <c>_asc</c> /
    /// <c>_desc</c> convention.
    /// </summary>
    /// <param name="key">The sort key.</param>
    /// <param name="indexOptions">The options of the selected index, or <see langword="null"/> when the index has none.</param>
    /// <returns>Whether the key is valid.</returns>
    public static bool IsValidKey(string? key, XpSearchIndexOptions? indexOptions)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        key = key.Trim();

        if (string.Equals(key, RelevanceKey, StringComparison.OrdinalIgnoreCase)
            || indexOptions?.SortKeys.ContainsKey(key) == true)
        {
            return true;
        }

        // "<field>_asc" / "<field>_desc" - the suffix convention the query pipeline accepts directly.
        foreach (string suffix in new[] { "_asc", "_desc" })
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && key.Length > suffix.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Keeps only the options whose key <see cref="IsValidKey"/> accepts.</summary>
    /// <param name="text">The raw text, one <c>key;Label</c> per line.</param>
    /// <param name="indexOptions">The options of the selected index, or <see langword="null"/>.</param>
    /// <returns>The usable options.</returns>
    public static IReadOnlyList<SortOption> ParseValid(string? text, XpSearchIndexOptions? indexOptions) =>
        Parse(text).Where(option => IsValidKey(option.Value, indexOptions)).ToList();

    /// <summary>Lists the keys the API would reject, so the widget can tell the editor about them.</summary>
    /// <param name="text">The raw text, one <c>key;Label</c> per line.</param>
    /// <param name="indexOptions">The options of the selected index, or <see langword="null"/>.</param>
    /// <returns>The invalid keys, in the order they were written.</returns>
    public static IReadOnlyList<string> InvalidKeys(string? text, XpSearchIndexOptions? indexOptions) =>
        Parse(text).Where(option => !IsValidKey(option.Value, indexOptions)).Select(option => option.Value).ToList();
}
