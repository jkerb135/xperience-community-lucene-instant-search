using XpSearch.Core.Analytics;

namespace XpSearch.Core.Popularity;

/// <summary>
/// One mined reformulation (SY-1): visitors who searched <paramref name="FailedQuery"/> without
/// clicking anything went on to search <paramref name="SucceededQuery"/> and clicked a result.
/// </summary>
/// <param name="FailedQuery">The query that got no click, normalized the way the log normalizes.</param>
/// <param name="SucceededQuery">The following query that got a click, normalized the same way.</param>
/// <param name="Occurrences">How often the pair happened in the window.</param>
/// <param name="LastSeenUtc">When the pair last happened, in UTC.</param>
public sealed record ReformulationPair(string FailedQuery, string SucceededQuery, int Occurrences, DateTime LastSeenUtc);

/// <summary>
/// Turns one lookback window of query log rows into candidate synonym pairs (SY-1). Pure: the
/// scheduled task reads the rows, this decides what they mean - the sibling of
/// <see cref="PopularityAggregator.Aggregate"/> its remarks reserve.
/// </summary>
/// <remarks>
/// The query log carries no visitor or session identifier and deliberately never will (it is written
/// for consenting and non-consenting visitors alike), so "the same visitor searched again" is
/// approximated by time adjacency inside one index: a search with no click, then the next search that
/// did get a click, within <c>XpSearchOptions.Analytics.SynonymWindowSeconds</c>. Two visitors
/// searching at the same second produce a pair nobody made; the occurrence threshold is what keeps
/// that noise out of the suggestions. See ADR-0026.
/// </remarks>
public static class SynonymMiner
{
    /// <summary>Mines one index's rows of the window.</summary>
    /// <param name="rows">The window's rows for one index, in any order.</param>
    /// <param name="minimumOccurrences">How often a pair has to happen before it is suggested.</param>
    /// <param name="windowSeconds">How long after a failed search a click still counts as the same reformulation.</param>
    /// <returns>The candidate pairs, most frequent first.</returns>
    public static IReadOnlyList<ReformulationPair> Mine(
        IEnumerable<QueryLogEntry> rows,
        int minimumOccurrences,
        int windowSeconds)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var ordered = rows.OrderBy(row => row.Timestamp).ToList();
        var window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds));
        var pairs = new Dictionary<(string, string), (int Occurrences, DateTime LastSeen)>(PopularitySuggestionMerge.Comparer);

        for (int i = 0; i < ordered.Count; i++)
        {
            var failure = ordered[i];

            if (!string.IsNullOrWhiteSpace(failure.ClickedResultId))
            {
                continue;
            }

            string failed = Text(failure.QueryText);

            // Only the nearest following click counts: a later one belongs to a later reformulation.
            var success = ordered
                .Skip(i + 1)
                .TakeWhile(row => row.Timestamp - failure.Timestamp <= window)
                .FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.ClickedResultId));

            if (success is null)
            {
                continue;
            }

            string succeeded = Text(success.QueryText);

            if (!IsReformulation(failed, succeeded))
            {
                continue;
            }

            var key = (failed, succeeded);
            var seen = pairs.GetValueOrDefault(key);

            pairs[key] = (seen.Occurrences + 1, seen.LastSeen > success.Timestamp ? seen.LastSeen : success.Timestamp);
        }

        return
        [
            .. pairs
                .Where(pair => pair.Value.Occurrences >= Math.Max(1, minimumOccurrences))
                .Select(pair => new ReformulationPair(pair.Key.Item1, pair.Key.Item2, pair.Value.Occurrences, pair.Value.LastSeen))
                .OrderByDescending(pair => pair.Occurrences)
                .ThenBy(pair => pair.FailedQuery, StringComparer.Ordinal)
                .ThenBy(pair => pair.SucceededQuery, StringComparer.Ordinal)
        ];
    }

    /// <summary>Drops the candidates a human has already approved or dismissed, so they never resurface.</summary>
    /// <param name="candidates">What the run mined.</param>
    /// <param name="decided">The two queries of every pair already answered.</param>
    /// <returns>The candidates worth storing.</returns>
    public static IReadOnlyList<ReformulationPair> Pending(
        IEnumerable<ReformulationPair> candidates,
        IEnumerable<(string FailedQuery, string SucceededQuery)> decided)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(decided);

        var answered = new HashSet<(string, string)>(
            decided.Select(entry => (entry.FailedQuery ?? string.Empty, entry.SucceededQuery ?? string.Empty)),
            PopularitySuggestionMerge.Comparer);

        return [.. candidates.Where(candidate => !answered.Contains((candidate.FailedQuery, candidate.SucceededQuery)))];
    }

    /// <summary>
    /// Whether two adjacent queries are worth suggesting as synonyms: both non-empty, and neither one
    /// contained in the other. Containment is how autocomplete typing looks ("coff" then "coffee") and
    /// how a narrowed search looks ("sofa" then "red sofa") - neither is a synonym. Equal texts are
    /// containment too, which is what excludes a repeat of the same search.
    /// </summary>
    /// <param name="failed">The normalized query that got no click.</param>
    /// <param name="succeeded">The normalized query that got one.</param>
    /// <returns><see langword="true"/> when the pair is a candidate.</returns>
    public static bool IsReformulation(string failed, string succeeded) =>
        !string.IsNullOrEmpty(failed)
        && !string.IsNullOrEmpty(succeeded)
        && !failed.Contains(succeeded, StringComparison.Ordinal)
        && !succeeded.Contains(failed, StringComparison.Ordinal);

    /// <summary>
    /// Normalizes one query text the way the log does - trimmed and lowercased - and collapses runs of
    /// whitespace, so two texts differing only in spacing are the same pair.
    /// </summary>
    /// <param name="query">The logged query text.</param>
    /// <returns>The comparable text.</returns>
    public static string Text(string? query) =>
        string.Join(' ', (query ?? string.Empty).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
