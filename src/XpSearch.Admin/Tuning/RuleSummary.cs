using System.Globalization;

using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tuning;

/// <summary>
/// The one-line reading of a rule's <c>if</c> and of each <c>then</c>, as the rule builder's summary
/// rows and the rule listing show them (design canvas 5a).
/// </summary>
/// <remarks>
/// Only the parts a rule actually sets appear, joined with <c>·</c>; a rule that says nothing reads
/// as <see cref="Anything"/>. The rule builder repeats this formatting in TypeScript, because a
/// summary row has to change the moment the side panel's Apply is clicked, with nothing persisted -
/// see the KNOWN-LIMITATIONS entry.
/// </remarks>
public static class RuleSummary
{
    /// <summary>What a rule with no conditions at all reads as. Such a rule never fires.</summary>
    public const string Anything = "Anything";

    /// <summary>What the parts of a summary are joined with.</summary>
    public const string Separator = " · ";

    /// <summary>Describes the <c>if</c> of a rule.</summary>
    /// <param name="conditions">The conditions.</param>
    /// <param name="contactGroupLabel">
    /// Turns a contact group code name into what the marketer called it, or <see langword="null"/> to
    /// leave the contact group out - which is what the listing does, having a column of its own for it.
    /// </param>
    /// <returns>The summary.</returns>
    public static string Describe(RuleConditions? conditions, Func<string, string>? contactGroupLabel = null)
    {
        if (conditions is null)
        {
            return Anything;
        }

        var parts = new List<string>();

        if (conditions.Query is { } query)
        {
            parts.Add($"Query {Operator(query.Operator)} “{query.Pattern}”"
                + (query.MatchAnalyzed ? " (plurals & synonyms)" : string.Empty));
        }

        parts.AddRange((conditions.Filters ?? []).Select(filter => $"Filter {filter.Attribute} is {filter.Value}"));

        if (contactGroupLabel is not null && !string.IsNullOrWhiteSpace(conditions.ContactGroup))
        {
            parts.Add($"Contact group {contactGroupLabel(conditions.ContactGroup)}");
        }

        if (!string.IsNullOrWhiteSpace(conditions.Language))
        {
            parts.Add($"Language {conditions.Language}");
        }

        return parts.Count == 0 ? Anything : string.Join(Separator, parts);
    }

    /// <summary>Describes one <c>then</c> of a rule.</summary>
    /// <param name="consequence">The consequence.</param>
    /// <returns>The summary.</returns>
    public static string Describe(RuleConsequence consequence) =>
        consequence switch
        {
            RuleConsequence.Pin pin => $"Pin {pin.TargetId} to position {pin.Position.ToString(CultureInfo.InvariantCulture)}",
            RuleConsequence.Hide hide => $"Hide {hide.TargetId}",
            RuleConsequence.Boost boost =>
                $"Boost {(string.IsNullOrWhiteSpace(boost.TargetId) ? boost.FilterExpression : boost.TargetId)} ×{boost.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}",
            RuleConsequence.Bury bury => $"Bury {bury.TargetId}",
            RuleConsequence.FilterResults filter => $"Filter results to {filter.FilterExpression}",
            RuleConsequence.RemoveWord remove => $"Remove the word “{remove.Word}”",
            RuleConsequence.ReplaceWord replace => $"Replace “{replace.Word}” with “{replace.Replacement}”",
            RuleConsequence.ReplaceQuery replace => $"Search instead for “{replace.Query}”",
            RuleConsequence.Redirect redirect => $"Redirect to {redirect.Url}",
            RuleConsequence.CustomData => "Return custom data",
            _ => string.Empty
        };

    /// <summary>Describes the whole <c>then</c> of a rule, in the order it is applied.</summary>
    /// <param name="consequences">The consequences.</param>
    /// <returns>The summary, or "Nothing" when the rule does nothing.</returns>
    public static string Describe(IReadOnlyList<RuleConsequence>? consequences) =>
        consequences is null || consequences.Count == 0
            ? "Nothing"
            : string.Join(Separator, consequences.Select(Describe));

    private static string Operator(QueryOperator value) =>
        value switch
        {
            QueryOperator.Is => "is",
            QueryOperator.StartsWith => "starts with",
            _ => "contains"
        };
}
