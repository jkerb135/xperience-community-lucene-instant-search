namespace XpSearch.Core.Tuning;

/// <summary>How a flat-storage rule's pattern is matched. The numbers are the stored column values.</summary>
public enum FlatCondition
{
    /// <summary>The query contains the pattern.</summary>
    Contains = 0,

    /// <summary>The query equals the pattern.</summary>
    Exact = 1,

    /// <summary>The query starts with the pattern.</summary>
    StartsWith = 2,

    /// <summary>The rule applies to every query, whatever the pattern.</summary>
    Always = 3
}

/// <summary>What a flat-storage rule does. The numbers are the stored column values.</summary>
public enum FlatConsequence
{
    /// <summary>Move a document to a fixed position.</summary>
    Pin = 0,

    /// <summary>Push a document out of the results.</summary>
    Bury = 1,

    /// <summary>Raise the score of a document or of a group of documents.</summary>
    Boost = 2,

    /// <summary>Restrict the results to documents matching an expression.</summary>
    Filter = 3,

    /// <summary>Send the visitor to a URL.</summary>
    Redirect = 4
}

/// <summary>
/// Builds a <see cref="TuningRule"/> from the flat one-condition/one-consequence columns the Search
/// tuning application still stores (ADR-0014). Temporary: unit CR-4b moves that storage to the
/// if/then shape of ADR-0022 and this type goes with it.
/// </summary>
public static class TuningRuleCompat
{
    /// <summary>Maps one stored row onto the if/then model, behaviour for behaviour.</summary>
    /// <param name="id">Rule identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="enabled">Whether the rule is live.</param>
    /// <param name="condition">How the pattern is matched.</param>
    /// <param name="pattern">The query pattern.</param>
    /// <param name="consequence">What the rule does.</param>
    /// <param name="targetId">Result id to pin, bury or boost.</param>
    /// <param name="targetPosition">One-based position for a pin.</param>
    /// <param name="boostValue">Score multiplier for a boost.</param>
    /// <param name="filterExpression">Comma-separated <c>attribute:value</c> pairs.</param>
    /// <param name="redirectUrl">Destination of a redirect rule.</param>
    /// <param name="validFrom">First moment the rule applies, in UTC.</param>
    /// <param name="validTo">Last moment the rule applies, in UTC.</param>
    /// <param name="priority">Conflict resolution order; lower runs first.</param>
    /// <param name="contactGroup">Contact group code name, or empty for everyone.</param>
    /// <returns>The rule.</returns>
    /// <remarks>
    /// Two mappings carry the old edge cases:
    /// <see cref="FlatCondition.Always"/> becomes <c>Contains ""</c>, which matches every query and
    /// keeps such a rule out of the "no conditions at all" hole; and a blank pattern under any other
    /// operator - which never matched anything under the flat model - comes back **disabled**, so it
    /// stays as dead as it was instead of becoming a rule with no query condition.
    /// </remarks>
    public static TuningRule FromFlat(
        int id,
        string name,
        bool enabled,
        FlatCondition condition,
        string pattern,
        FlatConsequence consequence,
        string targetId,
        int targetPosition,
        double boostValue,
        string filterExpression,
        string redirectUrl,
        DateTime? validFrom,
        DateTime? validTo,
        int priority,
        string contactGroup)
    {
        bool blankPattern = string.IsNullOrWhiteSpace(pattern);

        var query = condition switch
        {
            FlatCondition.Always => new QueryCondition(QueryOperator.Contains, string.Empty, false),
            FlatCondition.Exact => new QueryCondition(QueryOperator.Is, pattern ?? string.Empty, false),
            FlatCondition.StartsWith => new QueryCondition(QueryOperator.StartsWith, pattern ?? string.Empty, false),
            _ => new QueryCondition(QueryOperator.Contains, pattern ?? string.Empty, false)
        };

        RuleConsequence applied = consequence switch
        {
            FlatConsequence.Pin => new RuleConsequence.Pin(targetId ?? string.Empty, targetPosition),
            FlatConsequence.Bury => new RuleConsequence.Bury(targetId ?? string.Empty, string.Empty),
            FlatConsequence.Filter => new RuleConsequence.FilterResults(filterExpression ?? string.Empty),
            FlatConsequence.Redirect => new RuleConsequence.Redirect(redirectUrl ?? string.Empty),
            _ => new RuleConsequence.Boost(targetId ?? string.Empty, filterExpression ?? string.Empty, boostValue)
        };

        return new TuningRule(
            id,
            name,
            enabled && !(blankPattern && condition != FlatCondition.Always),
            priority,
            validFrom,
            validTo,
            new RuleConditions(query, [], contactGroup ?? string.Empty, string.Empty),
            [applied]);
    }
}
