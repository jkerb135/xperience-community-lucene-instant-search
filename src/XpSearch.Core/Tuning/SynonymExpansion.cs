namespace XpSearch.Core.Tuning;

/// <summary>
/// Turns a query into slots of interchangeable terms using the configured synonyms (spec §8.3).
/// </summary>
/// <remarks>
/// A slot is one position of the query and every term that may stand in that position, the original
/// first. The query builder ORs the alternatives inside a slot and ANDs the slots, so
/// <c>red sofa</c> with <c>sofa = couch</c> matches "red couch" without also matching every document
/// that merely mentions a couch. Multi-word synonym inputs are matched greedily, longest first, so
/// <c>sofa bed</c> beats <c>sofa</c> when both are configured.
/// </remarks>
public static class SynonymExpansion
{
    /// <summary>Splits an admin's comma-separated synonym input into trimmed, lowercased terms.</summary>
    /// <param name="value">The stored value, for example <c>sofa, couch, settee</c>.</param>
    /// <returns>The terms, with blanks and duplicates removed.</returns>
    public static IReadOnlyList<string> SplitTerms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return
        [
            .. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(term => term.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
        ];
    }

    /// <summary>Expands a query into slots of alternatives.</summary>
    /// <param name="query">The normalized query text.</param>
    /// <param name="synonyms">The configured synonym groups.</param>
    /// <returns>
    /// One slot per query position, or an empty list when no synonym applies - in which case the
    /// caller parses the query text as it stands.
    /// </returns>
    public static IReadOnlyList<IReadOnlyList<string>> Expand(string query, IReadOnlyList<TuningSynonym> synonyms)
    {
        ArgumentNullException.ThrowIfNull(synonyms);

        string[] tokens = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0 || synonyms.Count == 0)
        {
            return [];
        }

        var lookup = BuildLookup(synonyms, out int longestPhrase);
        var slots = new List<IReadOnlyList<string>>();
        bool expanded = false;

        for (int i = 0; i < tokens.Length;)
        {
            int length = Math.Min(longestPhrase, tokens.Length - i);
            bool matched = false;

            for (; length >= 1; length--)
            {
                string phrase = string.Join(' ', tokens, i, length);

                if (!lookup.TryGetValue(phrase, out var alternatives))
                {
                    continue;
                }

                slots.Add([phrase, .. alternatives]);
                i += length;
                matched = true;
                expanded = true;
                break;
            }

            if (!matched)
            {
                slots.Add([tokens[i]]);
                i++;
            }
        }

        return expanded ? slots : [];
    }

    /// <summary>Formats a synonym alternative for the <c>ranking.boosts</c> explanation of a hit.</summary>
    /// <param name="term">The term the query was expanded with.</param>
    /// <returns>An entry of the form <c>synonym:&lt;term&gt;</c>.</returns>
    public static string Explain(string term) => $"synonym:{term}";

    private static Dictionary<string, List<string>> BuildLookup(IReadOnlyList<TuningSynonym> synonyms, out int longestPhrase)
    {
        var lookup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        longestPhrase = 1;

        foreach (var synonym in synonyms)
        {
            var inputs = synonym.Input;
            var outputs = synonym.Direction == SynonymDirection.OneWay ? synonym.Output : inputs;

            foreach (string input in inputs)
            {
                longestPhrase = Math.Max(longestPhrase, input.Count(character => character == ' ') + 1);

                if (!lookup.TryGetValue(input, out var alternatives))
                {
                    alternatives = [];
                    lookup[input] = alternatives;
                }

                foreach (string output in outputs)
                {
                    if (!string.Equals(output, input, StringComparison.Ordinal) && !alternatives.Contains(output, StringComparer.Ordinal))
                    {
                        alternatives.Add(output);
                    }
                }
            }
        }

        // An input with no usable alternative would produce a one-element slot and count as an
        // expansion, which would change how the query is parsed for no gain.
        foreach (string empty in lookup.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key).ToList())
        {
            lookup.Remove(empty);
        }

        return lookup;
    }
}
