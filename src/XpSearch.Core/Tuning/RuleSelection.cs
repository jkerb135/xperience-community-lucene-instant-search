using System.Globalization;

namespace XpSearch.Core.Tuning;

/// <summary>
/// What one request looks like to a rule's conditions (ADR-0022). Built once per search by
/// <c>QueryRewriteStage</c>; pure, so the whole condition matrix is testable without an index.
/// </summary>
/// <param name="Query">The normalized query text: trimmed, lowercased, length-capped.</param>
/// <param name="AnalyzedQuery">
/// One entry per query position, each holding the analyzed forms of the term in that position and of
/// every synonym of it. Empty when nothing analyzed the query, which makes
/// <see cref="QueryCondition.MatchAnalyzed"/> fall back to the raw comparison.
/// </param>
/// <param name="Analyze">
/// Runs the index's analyzer over a pattern, returning its terms. Used to analyze the rule's own
/// pattern so the two sides are comparable.
/// </param>
/// <param name="Filters">The values selected per facet attribute in <c>filters.facets</c>.</param>
/// <param name="ContactGroups">Code names of the contact groups the visitor belongs to (ADR-0021).</param>
/// <param name="Language">The language the request asked for, or empty.</param>
public sealed record RuleMatchContext(
    string Query,
    IReadOnlyList<IReadOnlySet<string>> AnalyzedQuery,
    Func<string, IReadOnlyList<string>> Analyze,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Filters,
    IReadOnlySet<string> ContactGroups,
    string Language)
{
    /// <summary>Builds a context for a query alone: no analysis, no filters, no language.</summary>
    /// <param name="query">The normalized query text.</param>
    /// <param name="contactGroups">Code names of the visitor's contact groups.</param>
    /// <returns>The context. A condition with <c>matchAnalyzed</c> falls back to the raw comparison.</returns>
    public static RuleMatchContext ForQuery(string query, IReadOnlySet<string>? contactGroups = null) =>
        new(
            query ?? string.Empty,
            [],
            _ => [],
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
            contactGroups ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            string.Empty);
}

/// <summary>
/// Decides which rules apply to a request and in what order (ADR-0022). Pure, so the precedence,
/// scheduling and condition behaviour the guide documents is unit-testable without an index.
/// </summary>
public static class RuleSelection
{
    /// <summary>
    /// Selects the rules that fire: enabled, inside their schedule and with every condition holding,
    /// ordered by <see cref="TuningRule.Priority"/> ascending, then by <see cref="TuningRule.Id"/>
    /// ascending.
    /// </summary>
    /// <param name="rules">Every configured rule of the index.</param>
    /// <param name="match">What the request looks like to a condition.</param>
    /// <param name="utcNow">The current time, in UTC.</param>
    /// <returns>The applicable rules, in precedence order.</returns>
    public static IReadOnlyList<TuningRule> Active(IEnumerable<TuningRule> rules, RuleMatchContext match, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(match);

        return
        [
            .. rules
                .Where(rule => rule.Enabled && InSchedule(rule, utcNow) && Matches(rule.Conditions, match))
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.Id)
        ];
    }

    /// <summary>Selects the rules that fire, judging them on the query and contact groups alone.</summary>
    /// <param name="rules">Every configured rule of the index.</param>
    /// <param name="query">The normalized query text.</param>
    /// <param name="utcNow">The current time, in UTC.</param>
    /// <param name="contactGroups">
    /// Code names of the contact groups the visitor belongs to. <see langword="null"/> or empty means
    /// "no contact, or no consent to know", which leaves only the unscoped rules.
    /// </param>
    /// <returns>The applicable rules, in precedence order.</returns>
    public static IReadOnlyList<TuningRule> Active(
        IEnumerable<TuningRule> rules,
        string query,
        DateTime utcNow,
        IReadOnlySet<string>? contactGroups = null) =>
        Active(rules, RuleMatchContext.ForQuery(query, contactGroups), utcNow);

    /// <summary>Determines whether every condition of a rule holds.</summary>
    /// <param name="conditions">The rule's conditions.</param>
    /// <param name="match">What the request looks like.</param>
    /// <returns>
    /// <see langword="true"/> when they all hold. Conditions that say nothing at all never hold: a
    /// rule with no <c>if</c> would apply to every search, so it is treated as unfinished.
    /// </returns>
    public static bool Matches(RuleConditions conditions, RuleMatchContext match)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(match);

        if (conditions.IsEmpty)
        {
            return false;
        }

        return MatchesQuery(conditions.Query, match)
            && conditions.Filters.All(filter => MatchesFilter(filter, match))
            && InContactGroup(conditions.ContactGroup, match.ContactGroups)
            && MatchesLanguage(conditions.Language, match.Language);
    }

    /// <summary>Determines whether a rule's contact group scope covers a visitor.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="contactGroups">Code names of the contact groups the visitor belongs to.</param>
    /// <returns><see langword="true"/> when the rule is unscoped, or the visitor is in its group.</returns>
    public static bool InContactGroup(TuningRule rule, IReadOnlySet<string>? contactGroups)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return InContactGroup(rule.Conditions.ContactGroup, contactGroups);
    }

    /// <summary>Determines whether a rule's schedule covers a moment.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="utcNow">The moment, in UTC.</param>
    /// <returns><see langword="true"/> when the rule is live. Both bounds are inclusive.</returns>
    public static bool InSchedule(TuningRule rule, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return (rule.ValidFrom is not { } from || from <= utcNow)
            && (rule.ValidTo is not { } to || utcNow <= to);
    }

    /// <summary>Formats a rule for the <c>ranking.boosts</c> explanation of a hit.</summary>
    /// <param name="rule">The applied rule.</param>
    /// <returns>
    /// An entry of the form <c>rule:&lt;name&gt;</c>, with <c> (contact group &lt;code name&gt;)</c>
    /// appended when the rule only applied because the visitor is in that group.
    /// </returns>
    public static string Explain(TuningRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        string group = rule.Conditions.ContactGroup?.Trim() ?? string.Empty;

        return group.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"rule:{rule.Name}")
            : string.Create(CultureInfo.InvariantCulture, $"rule:{rule.Name} (contact group {group})");
    }

    private static bool InContactGroup(string? scope, IReadOnlySet<string>? contactGroups)
    {
        string group = scope?.Trim() ?? string.Empty;

        return group.Length == 0 || (contactGroups is not null && contactGroups.Contains(group));
    }

    private static bool MatchesLanguage(string? scope, string? requested) =>
        string.IsNullOrWhiteSpace(scope)
        || string.Equals(scope.Trim(), (requested ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(AttributeIs filter, RuleMatchContext match) =>
        !string.IsNullOrWhiteSpace(filter.Attribute)
        && match.Filters.TryGetValue(filter.Attribute.Trim(), out var values)
        && values.Contains((filter.Value ?? string.Empty).Trim());

    /// <summary>
    /// The query half of the <c>if</c>. A rule with no query condition applies to any query; a
    /// pattern is trimmed and compared case-insensitively, because a marketer typed it into a form.
    /// </summary>
    private static bool MatchesQuery(QueryCondition? condition, RuleMatchContext match)
    {
        if (condition is null)
        {
            return true;
        }

        string pattern = (condition.Pattern ?? string.Empty).Trim();

        return condition.MatchAnalyzed && match.AnalyzedQuery.Count > 0
            ? MatchesAnalyzed(condition.Operator, pattern, match)
            : MatchesRaw(condition.Operator, pattern, match.Query);
    }

    private static bool MatchesRaw(QueryOperator op, string pattern, string query) =>
        op switch
        {
            QueryOperator.Is => query.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            QueryOperator.StartsWith => query.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            _ => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };

    /// <summary>
    /// Compares the analyzed pattern against the analyzed query, position by position. Each position
    /// of the query holds the analyzer's terms for the word that stands there and for every synonym
    /// of it, so <c>shoes</c> matches a <c>shoe</c> query under a stemming analyzer and a
    /// <c>trainers</c> one under a synonym group. There is no typo tolerance anywhere in this.
    /// </summary>
    private static bool MatchesAnalyzed(QueryOperator op, string pattern, RuleMatchContext match)
    {
        var terms = match.Analyze(pattern);

        if (terms.Count == 0)
        {
            // The analyzer threw the whole pattern away (all stopwords, or it was empty): only
            // "contains nothing" and "starts with nothing" are true of every query, as in the raw case.
            return op != QueryOperator.Is || match.AnalyzedQuery.Count == 0;
        }

        var positions = match.AnalyzedQuery;

        if (terms.Count > positions.Count || (op == QueryOperator.Is && terms.Count != positions.Count))
        {
            return false;
        }

        int last = op == QueryOperator.Contains ? positions.Count - terms.Count : 0;

        for (int offset = 0; offset <= last; offset++)
        {
            bool all = true;

            for (int i = 0; i < terms.Count && all; i++)
            {
                all = positions[offset + i].Contains(terms[i]);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }
}
