using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Helpers;

using XpSearch.Core.Tuning;

using QueryOperator = XpSearch.Core.Tuning.QueryOperator;

namespace XpSearch.Admin.Persistence;

/// <summary>How a pre-CR-4b rule's pattern was matched. The numbers are the stored column values.</summary>
public enum LegacyCondition
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

/// <summary>What a pre-CR-4b rule did. The numbers are the stored column values.</summary>
public enum LegacyConsequence
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
/// The one-time conversion of the flat one-condition/one-action rule columns (ADR-0014) into
/// the two JSON columns of the if/then model (ADR-0022 addendum, unit CR-4b).
/// </summary>
/// <remarks>
/// <para>
/// Runs from <see cref="XpSearchTuningModuleInstaller.Install"/> on every application start, after
/// the classes are installed and before any page reads them. It is idempotent and crash-safe by
/// construction rather than by a flag: the marker is the row itself. A row whose
/// <see cref="XpSearchRuleInfo.RuleConditions"/> column is empty has not been converted yet, and a
/// converted row is saved with that column filled, so a crash halfway through a table leaves the
/// converted rows converted and the rest to be picked up on the next start.
/// </para>
/// <para>
/// The same pass carries a row forward from the interim <c>RuleConsequences</c> column, which is
/// what a build between CR-4b and the action rename wrote the <c>then</c> into. Its marker is the
/// row too: an empty <see cref="XpSearchRuleInfo.RuleActions"/> next to a column that still holds
/// something.
/// </para>
/// <para>
/// Only once no row is left to convert or to carry forward are the legacy columns removed from the
/// class - see <see cref="RetireLegacyColumns"/> for why they cannot simply be left in place.
/// </para>
/// </remarks>
public static class RuleStorageMigration
{
    /// <summary>The columns the flat model stored a rule in, retired once every row is converted.</summary>
    public static IReadOnlyList<string> LegacyColumns { get; } =
    [
        "RuleConditionType",
        "RulePattern",
        "RuleConsequenceType",
        "RuleTargetObjectID",
        "RuleTargetPosition",
        "RuleBoostValue",
        "RuleFilterExpression",
        "RuleRedirectUrl",
        "RuleContactGroup"
    ];

    /// <summary>
    /// The column the <c>then</c> of a rule was stored in before it was renamed to
    /// <see cref="XpSearchRuleInfo.RuleActions"/>, retired once every row has been carried forward.
    /// </summary>
    public const string LegacyActionsColumn = "RuleConsequences";

    /// <summary>
    /// Maps one flat row onto the if/then model, behaviour for behaviour.
    /// </summary>
    /// <param name="id">Rule identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="enabled">Whether the rule is live.</param>
    /// <param name="condition">How the pattern is matched.</param>
    /// <param name="pattern">The query pattern.</param>
    /// <param name="action">What the rule does.</param>
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
    /// <see cref="LegacyCondition.Always"/> becomes <c>Contains ""</c>, which matches every query and
    /// keeps such a rule out of the "no conditions at all" hole; and a blank pattern under any other
    /// operator - which never matched anything under the flat model - comes back **disabled**, so it
    /// stays as dead as it was instead of becoming a rule with no query condition.
    /// </remarks>
    public static TuningRule FromFlat(
        int id,
        string name,
        bool enabled,
        LegacyCondition condition,
        string pattern,
        LegacyConsequence action,
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
            LegacyCondition.Always => new QueryCondition(QueryOperator.Contains, string.Empty, false),
            LegacyCondition.Exact => new QueryCondition(QueryOperator.Is, pattern ?? string.Empty, false),
            LegacyCondition.StartsWith => new QueryCondition(QueryOperator.StartsWith, pattern ?? string.Empty, false),
            _ => new QueryCondition(QueryOperator.Contains, pattern ?? string.Empty, false)
        };

        RuleAction applied = action switch
        {
            LegacyConsequence.Pin => new RuleAction.Pin(targetId ?? string.Empty, targetPosition),
            LegacyConsequence.Bury => new RuleAction.Bury(targetId ?? string.Empty, string.Empty),
            LegacyConsequence.Filter => new RuleAction.FilterResults(filterExpression ?? string.Empty),
            LegacyConsequence.Redirect => new RuleAction.Redirect(redirectUrl ?? string.Empty),
            _ => new RuleAction.Boost(targetId ?? string.Empty, filterExpression ?? string.Empty, boostValue)
        };

        return new TuningRule(
            id,
            name,
            enabled && !(blankPattern && condition != LegacyCondition.Always),
            priority,
            validFrom,
            validTo,
            new RuleConditions(query, [], contactGroup ?? string.Empty, string.Empty),
            [applied]);
    }

    /// <summary>Reads a stored row's flat columns and maps them, for a row that still carries them.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The rule the flat columns describe.</returns>
    public static TuningRule FromFlatRow(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return FromFlat(
            row.RuleID,
            row.RuleName,
            row.RuleEnabled,
            (LegacyCondition)Legacy(row, "RuleConditionType", 0),
            ValidationHelper.GetString(Legacy(row, "RulePattern", string.Empty), string.Empty),
            (LegacyConsequence)Legacy(row, "RuleConsequenceType", 0),
            ValidationHelper.GetString(Legacy(row, "RuleTargetObjectID", string.Empty), string.Empty),
            Legacy(row, "RuleTargetPosition", 0),
            (double)ValidationHelper.GetDecimal(Legacy(row, "RuleBoostValue", 1m), 1m),
            ValidationHelper.GetString(Legacy(row, "RuleFilterExpression", string.Empty), string.Empty),
            ValidationHelper.GetString(Legacy(row, "RuleRedirectUrl", string.Empty), string.Empty),
            row.RuleValidFrom,
            row.RuleValidTo,
            row.RulePriority,
            ValidationHelper.GetString(Legacy(row, "RuleContactGroup", string.Empty), string.Empty));
    }

    /// <summary>
    /// Tells whether a row still has to be converted: its <c>if</c> column is empty, which no row
    /// written by the rule builder ever is.
    /// </summary>
    /// <param name="row">The stored row.</param>
    /// <returns><see langword="true"/> when the row is still in the flat shape.</returns>
    public static bool NeedsConversion(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return NeedsConversion(row.RuleConditions);
    }

    /// <summary>
    /// The marker itself: an empty <c>if</c> column, which is a state no rule the builder writes can
    /// be in - even a rule that matches anything stores an object.
    /// </summary>
    /// <param name="storedConditions">The value of the row's <c>RuleConditions</c> column.</param>
    /// <returns><see langword="true"/> when the row is still in the flat shape.</returns>
    public static bool NeedsConversion(string? storedConditions) => string.IsNullOrWhiteSpace(storedConditions);

    /// <summary>
    /// The <c>then</c> of a row as it is stored, taken from the interim <c>RuleConsequences</c>
    /// column when <see cref="XpSearchRuleInfo.RuleActions"/> has not been filled yet.
    /// </summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The stored JSON, or an empty string when neither column holds anything.</returns>
    public static string StoredActions(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return StoredActions(row.RuleActions, ValidationHelper.GetString(Legacy(row, LegacyActionsColumn, string.Empty), string.Empty));
    }

    /// <summary>The choice itself: the current column when it holds anything, else the legacy one, verbatim.</summary>
    /// <param name="storedActions">The value of the row's <c>RuleActions</c> column.</param>
    /// <param name="legacyActions">The value of the row's <c>RuleConsequences</c> column.</param>
    /// <returns>The stored JSON, or an empty string when neither column holds anything.</returns>
    public static string StoredActions(string? storedActions, string? legacyActions) =>
        string.IsNullOrWhiteSpace(storedActions) ? legacyActions ?? string.Empty : storedActions;

    /// <summary>
    /// Tells whether a row still has to be carried forward from the interim <c>RuleConsequences</c>
    /// column.
    /// </summary>
    /// <param name="row">The stored row.</param>
    /// <returns><see langword="true"/> when the row's <c>then</c> is only in the legacy column.</returns>
    public static bool NeedsActionCopy(XpSearchRuleInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return NeedsActionCopy(row.RuleActions, ValidationHelper.GetString(Legacy(row, LegacyActionsColumn, string.Empty), string.Empty));
    }

    /// <summary>
    /// The marker: an empty <c>RuleActions</c> next to a legacy column that still holds something. A
    /// row carried forward has <c>RuleActions</c> filled, so the next pass leaves it alone, and a row
    /// whose <c>then</c> is genuinely empty stores <c>[]</c> rather than nothing.
    /// </summary>
    /// <param name="storedActions">The value of the row's <c>RuleActions</c> column.</param>
    /// <param name="legacyActions">The value of the row's <c>RuleConsequences</c> column.</param>
    /// <returns><see langword="true"/> when the row's <c>then</c> is only in the legacy column.</returns>
    public static bool NeedsActionCopy(string? storedActions, string? legacyActions) =>
        string.IsNullOrWhiteSpace(storedActions) && !string.IsNullOrWhiteSpace(legacyActions);

    /// <summary>Fills a row's two JSON columns from its own flat columns.</summary>
    /// <param name="row">The row to convert, in place.</param>
    /// <returns>The same row.</returns>
    public static XpSearchRuleInfo Convert(XpSearchRuleInfo row)
    {
        var rule = FromFlatRow(row);

        row.RuleEnabled = rule.Enabled;
        row.RuleConditions = RuleJson.Write(rule.Conditions);
        row.RuleActions = RuleJson.Write(rule.Actions);

        // Drives the builder's "converted from the previous format" note; the first save clears it.
        row.RuleMigrated = true;

        return row;
    }

    /// <summary>
    /// Converts every row that is still flat, carries forward every row whose <c>then</c> is still
    /// in the interim <c>RuleConsequences</c> column, then retires the columns they came from.
    /// </summary>
    /// <param name="rules">Provider of rule objects.</param>
    /// <returns>How many rows were converted or carried forward.</returns>
    public static int Run(IInfoProvider<XpSearchRuleInfo> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        int converted = 0;

        // Every rule, not just the flat ones: the legacy columns are gone from the class after the
        // first successful pass, so they cannot be filtered on in SQL.
        foreach (var row in rules.Get().GetEnumerableTypedResult())
        {
            if (NeedsConversion(row))
            {
                rules.Set(Convert(row));
                converted++;

                continue;
            }

            if (!NeedsActionCopy(row))
            {
                continue;
            }

            // Verbatim: the array is the same contract, only the column it lives in changed.
            row.RuleActions = StoredActions(row);
            rules.Set(row);
            converted++;
        }

        RetireLegacyColumns();

        return converted;
    }

    /// <summary>
    /// Removes the flat columns and the interim <c>RuleConsequences</c> column from the rule class,
    /// which drops them from the table.
    /// </summary>
    /// <remarks>
    /// They cannot be left orphaned-but-unread: several of them are <c>NOT NULL</c> with no default,
    /// so a rule created by the new builder - which sets none of them - would fail to insert.
    /// <c>CombineWithForm</c>, which <see cref="XpSearchTuningModuleInstaller"/> uses to add columns
    /// to an installed class, only ever adds, so the removal is an explicit
    /// <see cref="FormInfo.RemoveFormField"/> on the installed definition. It runs after the
    /// conversion loop, so the old values are already in the current columns when they go.
    /// </remarks>
    public static void RetireLegacyColumns()
    {
        var dataClass = DataClassInfoProvider.GetDataClassInfo(XpSearchRuleInfo.CLASS_NAME);

        if (dataClass is null || dataClass.ClassID <= 0)
        {
            return;
        }

        var form = new FormInfo(dataClass.ClassFormDefinition);
        var present = form.GetFields(true, true).Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = LegacyColumns.Append(LegacyActionsColumn).Where(present.Contains).ToList();

        if (stale.Count == 0)
        {
            return;
        }

        foreach (string column in stale)
        {
            form.RemoveFormField(column);
        }

        dataClass.ClassFormDefinition = form.GetXmlDefinition();

        DataClassInfoProvider.SetDataClassInfo(dataClass);
    }

    /// <summary>Reads a column that may already have been retired from the class.</summary>
    private static T Legacy<T>(XpSearchRuleInfo row, string column, T fallback) =>
        row.ContainsColumn(column) && row.GetValue(column) is T value ? value : fallback;
}
