using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>How a rule's pattern is matched, in the vocabulary the tests are written in.</summary>
internal enum FlatCondition
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

/// <summary>What a rule does, in the vocabulary the tests are written in.</summary>
internal enum FlatConsequence
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
/// Builds a <see cref="TuningRule"/> with one query condition and one consequence, which is what most
/// of the pipeline tests need and all they used to be able to express.
/// </summary>
/// <remarks>
/// A test fixture, not a compatibility layer: the storage shim it reads like lived in Core until
/// unit CR-4b moved the real one into <c>XpSearch.Admin.Persistence.RuleStorageMigration</c>, where
/// it is tested against the columns it converts. This only keeps the call sites of the pipeline
/// tests short.
/// </remarks>
internal static class LegacyRuleShapes
{
    internal static TuningRule Build(
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
        var query = condition switch
        {
            FlatCondition.Always => new QueryCondition(QueryOperator.Contains, string.Empty, false),
            FlatCondition.Exact => new QueryCondition(QueryOperator.Is, pattern, false),
            FlatCondition.StartsWith => new QueryCondition(QueryOperator.StartsWith, pattern, false),
            _ => new QueryCondition(QueryOperator.Contains, pattern, false)
        };

        RuleConsequence applied = consequence switch
        {
            FlatConsequence.Pin => new RuleConsequence.Pin(targetId, targetPosition),
            FlatConsequence.Bury => new RuleConsequence.Bury(targetId, string.Empty),
            FlatConsequence.Filter => new RuleConsequence.FilterResults(filterExpression),
            FlatConsequence.Redirect => new RuleConsequence.Redirect(redirectUrl),
            _ => new RuleConsequence.Boost(targetId, filterExpression, boostValue)
        };

        // A blank pattern under anything but "always" matched nothing, so such a rule is dead.
        bool alive = enabled && !(string.IsNullOrWhiteSpace(pattern) && condition != FlatCondition.Always);

        return new TuningRule(id, name, alive, priority, validFrom, validTo, new RuleConditions(query, [], contactGroup, string.Empty), [applied]);
    }
}
