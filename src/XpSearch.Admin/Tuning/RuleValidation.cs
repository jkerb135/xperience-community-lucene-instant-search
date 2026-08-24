using System.Globalization;

using XpSearch.Admin.Persistence;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tuning;

/// <summary>One thing wrong with a rule, addressed to the field the marketer has to fix.</summary>
/// <param name="Field">
/// Which control is at fault: <c>name</c>, <c>conditions</c>, <c>query</c>, <c>filters</c>, or
/// <c>consequence:{index}</c> for the card at that position in the <c>then</c> column.
/// </param>
/// <param name="Message">What to tell them, as the design canvas words it.</param>
public sealed record RuleError(string Field, string Message);

/// <summary>
/// What a rule has to be for Save to be allowed (design canvas 5d). Server-side, because the rule
/// builder's Save is a page command and a client check is a convenience, not a guarantee.
/// </summary>
public static class RuleValidation
{
    /// <summary>The field name of the whole <c>if</c> column, which the empty-rule error belongs to.</summary>
    public const string ConditionsField = "conditions";

    /// <summary>What a rule with nothing in its <c>if</c> is refused with.</summary>
    public const string NoConditions =
        "A rule needs at least one condition — what the visitor searched, the filters on the request, or who they are (contact group, language).";

    /// <summary>Finds everything wrong with a rule.</summary>
    /// <param name="name">The rule's display name.</param>
    /// <param name="conditions">The <c>if</c>.</param>
    /// <param name="consequences">The <c>then</c>, in the order it is applied.</param>
    /// <returns>The errors, empty when the rule can be saved.</returns>
    public static IReadOnlyList<RuleError> Validate(
        string? name,
        RuleConditions? conditions,
        IReadOnlyList<RuleConsequence>? consequences)
    {
        var errors = new List<RuleError>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new RuleError("name", "Give the rule a name — it is what the ranking explanation shows."));
        }

        var said = conditions ?? RuleJson.NoConditions;

        if (said.IsEmpty)
        {
            errors.Add(new RuleError(ConditionsField, NoConditions));
        }

        if (said.Query is { } query && string.IsNullOrWhiteSpace(query.Pattern))
        {
            errors.Add(new RuleError("query", "The Query toggle is on, so a text is required — or turn the toggle off."));
        }

        foreach (var filter in said.Filters ?? [])
        {
            if (string.IsNullOrWhiteSpace(filter.Attribute) || string.IsNullOrWhiteSpace(filter.Value))
            {
                errors.Add(new RuleError("filters", "A filter needs both an attribute and a value — or remove the row."));

                break;
            }
        }

        for (int index = 0; index < (consequences?.Count ?? 0); index++)
        {
            string field = Field(index);

            foreach (string message in Wrong(consequences![index]))
            {
                errors.Add(new RuleError(field, message));
            }
        }

        return errors;
    }

    /// <summary>The field name of the consequence card at a position.</summary>
    /// <param name="index">Zero-based position in the <c>then</c> column.</param>
    /// <returns>The field name.</returns>
    public static string Field(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"consequence:{index}");

    private static IEnumerable<string> Wrong(RuleConsequence consequence)
    {
        switch (consequence)
        {
            case RuleConsequence.Pin pin:
                if (string.IsNullOrWhiteSpace(pin.TargetId))
                {
                    yield return "Choose the item to pin.";
                }

                if (pin.Position < 1)
                {
                    yield return "Position counts from 1, the top of the first page.";
                }

                break;

            case RuleConsequence.Hide hide when string.IsNullOrWhiteSpace(hide.TargetId):
                yield return "Choose the item to hide.";

                break;

            case RuleConsequence.Bury bury when string.IsNullOrWhiteSpace(bury.TargetId):
                yield return "Choose the item to bury.";

                break;

            case RuleConsequence.Boost boost:
                if (string.IsNullOrWhiteSpace(boost.TargetId) && string.IsNullOrWhiteSpace(boost.FilterExpression))
                {
                    yield return "Choose an item to boost, or an attribute:value expression to boost by.";
                }

                if (boost.Multiplier <= 0)
                {
                    yield return "A multiplier of 0 or less would switch the rule off — use a number above 0.";
                }

                break;

            case RuleConsequence.FilterResults filter when string.IsNullOrWhiteSpace(filter.FilterExpression):
                yield return "Enter the attribute:value pairs to keep.";

                break;

            case RuleConsequence.RemoveWord remove when string.IsNullOrWhiteSpace(remove.Word):
                yield return "Enter the word to remove from the query.";

                break;

            case RuleConsequence.ReplaceWord replace:
                if (string.IsNullOrWhiteSpace(replace.Word) || string.IsNullOrWhiteSpace(replace.Replacement))
                {
                    yield return "Enter both the word to replace and what to put in its place.";
                }

                break;

            case RuleConsequence.ReplaceQuery replace when string.IsNullOrWhiteSpace(replace.Query):
                yield return "Enter the query to search for instead.";

                break;

            case RuleConsequence.Redirect redirect when string.IsNullOrWhiteSpace(redirect.Url):
                yield return "Enter where the visitor should be sent.";

                break;

            case RuleConsequence.CustomData data when !RuleJson.IsJsonObject(data.Json):
                yield return "Not valid JSON — custom data has to be a JSON object, for example {\"banner\": \"…\"}.";

                break;

            default:
                break;
        }
    }
}
