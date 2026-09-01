using CMS.DataEngine;

using XpSearch.Admin.Persistence;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tuning;

/// <summary>
/// The one place that decides which stored tuning rows belong to a variant (XP-1): live rows carry no
/// experiment, a variant-B row carries its experiment's identifier.
/// </summary>
/// <remarks>
/// Every read of a tuning object type goes through <see cref="Condition"/>. A query path that forgets
/// it would show an experiment's draft rows to live traffic, which is the one thing an experiment must
/// never do.
/// </remarks>
public static class VariantScope
{
    /// <summary>The experiment column of each tuning object type, keyed by object type.</summary>
    public static IReadOnlyDictionary<string, string> ExperimentColumns { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [XpSearchRuleInfo.OBJECT_TYPE] = nameof(XpSearchRuleInfo.RuleExperimentID),
            [XpSearchSynonymInfo.OBJECT_TYPE] = nameof(XpSearchSynonymInfo.SynonymExperimentID),
            [XpSearchStopwordListInfo.OBJECT_TYPE] = nameof(XpSearchStopwordListInfo.StopwordListExperimentID),
            [XpSearchFieldWeightInfo.OBJECT_TYPE] = nameof(XpSearchFieldWeightInfo.WeightExperimentID)
        };

    /// <summary>Builds the condition that restricts a query to one variant's rows.</summary>
    /// <param name="column">Name of the object type's experiment column.</param>
    /// <param name="variant">The variant being read.</param>
    /// <returns>The condition.</returns>
    public static WhereCondition Condition(string column, TuningVariant variant) =>
        variant.IsLive
            ? new WhereCondition().WhereNull(column)
            : new WhereCondition().WhereEquals(column, variant.ExperimentId);
}
