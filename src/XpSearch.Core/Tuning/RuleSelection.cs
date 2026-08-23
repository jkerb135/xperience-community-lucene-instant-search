using System.Globalization;

namespace XpSearch.Core.Tuning;

/// <summary>
/// Decides which rules apply to a query and in what order (spec §8.3). Pure, so the precedence and
/// scheduling behaviour the guide documents is unit-testable without an index.
/// </summary>
public static class RuleSelection
{
    /// <summary>
    /// Selects the rules that apply: enabled, inside their schedule, scoped to a contact group the
    /// visitor belongs to and matching the query, ordered by <see cref="TuningRule.Priority"/>
    /// ascending, then by <see cref="TuningRule.Id"/> ascending.
    /// </summary>
    /// <param name="rules">Every configured rule of the index.</param>
    /// <param name="query">The normalized query text (already trimmed and lowercased).</param>
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
        IReadOnlySet<string>? contactGroups = null)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return
        [
            .. rules
                .Where(rule => rule.Enabled
                    && InSchedule(rule, utcNow)
                    && InContactGroup(rule, contactGroups)
                    && Matches(rule, query ?? string.Empty))
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.Id)
        ];
    }

    /// <summary>Determines whether a rule's contact group scope covers a visitor.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="contactGroups">Code names of the contact groups the visitor belongs to.</param>
    /// <returns><see langword="true"/> when the rule is unscoped, or the visitor is in its group.</returns>
    public static bool InContactGroup(TuningRule rule, IReadOnlySet<string>? contactGroups)
    {
        ArgumentNullException.ThrowIfNull(rule);

        string group = rule.ContactGroup?.Trim() ?? string.Empty;

        return group.Length == 0 || (contactGroups is not null && contactGroups.Contains(group));
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

    /// <summary>Determines whether a rule's condition matches a query.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="query">The normalized query text.</param>
    /// <returns><see langword="true"/> when the rule's pattern matches.</returns>
    /// <remarks>
    /// Matching is case-insensitive on the trimmed pattern, because the pattern is typed by a marketer
    /// in the admin UI and the query has already been lowercased by the normalize stage. A rule whose
    /// pattern is empty only ever matches under <see cref="RuleCondition.Always"/>.
    /// </remarks>
    public static bool Matches(TuningRule rule, string query)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(query);

        if (rule.Condition == RuleCondition.Always)
        {
            return true;
        }

        string pattern = rule.Pattern.Trim();

        if (pattern.Length == 0)
        {
            return false;
        }

        return rule.Condition switch
        {
            RuleCondition.Exact => query.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            RuleCondition.StartsWith => query.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            _ => query.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
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

        string group = rule.ContactGroup?.Trim() ?? string.Empty;

        return group.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"rule:{rule.Name}")
            : string.Create(CultureInfo.InvariantCulture, $"rule:{rule.Name} (contact group {group})");
    }
}
