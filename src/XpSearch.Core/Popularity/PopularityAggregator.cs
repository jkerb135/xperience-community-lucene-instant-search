using XpSearch.Core.Analytics;

namespace XpSearch.Core.Popularity;

/// <summary>One document that clearly wins a frequent query's clicks, offered for a human to approve (RK-1).</summary>
/// <param name="Query">The query text, as the query log normalized it.</param>
/// <param name="DocumentId">Result id of the document that wins the clicks.</param>
/// <param name="Clicks">How many clicks it took in the window.</param>
/// <param name="SharePercent">Its share of the query's damped click mass, rounded to whole percent.</param>
public sealed record PopularitySuggestion(string Query, string DocumentId, int Clicks, int SharePercent);

/// <summary>What one aggregation run computed for one index.</summary>
/// <param name="Scores">Damped click mass per result id, already limited to the strongest documents.</param>
/// <param name="Suggestions">The suggested boost rules, strongest query first.</param>
public sealed record PopularityAggregate(
    IReadOnlyDictionary<string, double> Scores,
    IReadOnlyList<PopularitySuggestion> Suggestions)
{
    /// <summary>Gets the result of aggregating a window with no clicks in it.</summary>
    public static PopularityAggregate Empty { get; } = new(new Dictionary<string, double>(StringComparer.Ordinal), []);
}

/// <summary>
/// Turns one lookback window of query log rows into the popularity signal and its suggested rules
/// (RK-1). Pure: the scheduled task reads the rows, this decides what they mean.
/// </summary>
/// <remarks>
/// The window's rows are read once and handed to this aggregator per index. A second aggregation over
/// the same rows - SY-1's mined synonyms - is a sibling of <see cref="Aggregate"/> called from the
/// same loop, not a change to it.
/// </remarks>
public static class PopularityAggregator
{
    /// <summary>How many clicks a document needs on one query before it can be suggested.</summary>
    public const int MinimumSuggestionClicks = 5;

    /// <summary>What share of a query's damped click mass a document needs before it can be suggested.</summary>
    public const double MinimumSuggestionShare = 0.5;

    /// <summary>
    /// The position-damped worth of one click: <c>log2(position + 1)</c>.
    /// </summary>
    /// <param name="position">One-based position of the clicked result, or <see langword="null"/> when it is unknown.</param>
    /// <returns>The click's weight; 1.0 at position 1, about 3.2 at position 8.</returns>
    /// <remarks>
    /// A click far down the list means more than a click on the first result, which the visitor would
    /// likely have made anyway: the damping removes the ranking's own bias rather than repeating it.
    /// An unknown position counts as position 1, which is the weakest weight this can produce - the
    /// most conservative reading of the evidence. See ADR-0025 on the shape of the discount.
    /// </remarks>
    public static double Damp(int? position) => Math.Log2(Math.Max(1, position ?? 1) + 1);

    /// <summary>Aggregates one index's rows of the window.</summary>
    /// <param name="rows">The window's rows for one index.</param>
    /// <param name="documentLimit">How many documents the signal keeps, strongest first.</param>
    /// <param name="suggestionQueries">How many of the window's most frequent queries are examined for a suggestion.</param>
    /// <returns>The signal and the suggestions.</returns>
    public static PopularityAggregate Aggregate(IEnumerable<QueryLogEntry> rows, int documentLimit, int suggestionQueries)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var all = rows.ToList();
        var clicks = all.Where(row => !string.IsNullOrWhiteSpace(row.ClickedResultId)).ToList();

        if (clicks.Count == 0)
        {
            return PopularityAggregate.Empty;
        }

        var scores = clicks
            .GroupBy(row => row.ClickedResultId!, StringComparer.Ordinal)
            .Select(group => (DocumentId: group.Key, Mass: group.Sum(row => Damp(row.ClickedPosition))))
            .OrderByDescending(entry => entry.Mass)
            .ThenBy(entry => entry.DocumentId, StringComparer.Ordinal)
            .Take(Math.Max(1, documentLimit))
            .ToDictionary(entry => entry.DocumentId, entry => entry.Mass, StringComparer.Ordinal);

        return new PopularityAggregate(scores, Suggest(all, clicks, suggestionQueries));
    }

    /// <summary>
    /// Suggests a boost for the frequent queries where one document clearly wins: at least
    /// <see cref="MinimumSuggestionClicks"/> clicks and at least <see cref="MinimumSuggestionShare"/>
    /// of the query's damped click mass. Everything else is left alone - a suggestion nobody would act
    /// on is worse than none.
    /// </summary>
    private static IReadOnlyList<PopularitySuggestion> Suggest(
        IReadOnlyList<QueryLogEntry> all,
        IReadOnlyList<QueryLogEntry> clicks,
        int suggestionQueries)
    {
        var frequent = all
            .Where(row => !string.IsNullOrWhiteSpace(row.QueryText))
            .GroupBy(row => row.QueryText, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, suggestionQueries))
            .Select(group => group.Key);

        var suggestions = new List<PopularitySuggestion>();

        foreach (string query in frequent)
        {
            var clicked = clicks
                .Where(row => string.Equals(row.QueryText, query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (clicked.Count == 0)
            {
                continue;
            }

            double total = clicked.Sum(row => Damp(row.ClickedPosition));

            var winner = clicked
                .GroupBy(row => row.ClickedResultId!, StringComparer.Ordinal)
                .Select(group => (DocumentId: group.Key, Clicks: group.Count(), Mass: group.Sum(row => Damp(row.ClickedPosition))))
                .OrderByDescending(entry => entry.Mass)
                .ThenBy(entry => entry.DocumentId, StringComparer.Ordinal)
                .First();

            if (winner.Clicks < MinimumSuggestionClicks || total <= 0 || winner.Mass / total < MinimumSuggestionShare)
            {
                continue;
            }

            suggestions.Add(new PopularitySuggestion(
                query,
                winner.DocumentId,
                winner.Clicks,
                (int)Math.Round(100 * winner.Mass / total, MidpointRounding.AwayFromZero)));
        }

        return suggestions;
    }
}

/// <summary>
/// Keeps a recomputed suggestion from resurfacing once a human has answered it (RK-1).
/// </summary>
public static class PopularitySuggestionMerge
{
    /// <summary>Drops the candidates whose query and document a human has already approved or dismissed.</summary>
    /// <param name="candidates">What the run computed.</param>
    /// <param name="decided">The query and document of every suggestion already answered.</param>
    /// <returns>The candidates worth storing.</returns>
    public static IReadOnlyList<PopularitySuggestion> Pending(
        IEnumerable<PopularitySuggestion> candidates,
        IEnumerable<(string Query, string DocumentId)> decided)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(decided);

        var answered = new HashSet<(string, string)>(
            decided.Select(entry => (entry.Query ?? string.Empty, entry.DocumentId ?? string.Empty)),
            Comparer);

        return [.. candidates.Where(candidate => !answered.Contains((candidate.Query, candidate.DocumentId)))];
    }

    private static IEqualityComparer<(string, string)> Comparer { get; } = new PairComparer();

    private sealed class PairComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) left, (string, string) right) =>
            string.Equals(left.Item1, right.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Item2, right.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1 ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2 ?? string.Empty));
    }
}

