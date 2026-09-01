using System.Globalization;

using Kentico.Xperience.Admin.Base;

using XpSearch.Admin.Tuning;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.UIPages.RuleBuilder;

/// <summary>One <c>attribute is value</c> row of a condition, as the side panel edits it.</summary>
public class RuleFilterDto
{
    /// <summary>Gets or sets the attribute name, as it appears in <c>filters.facets</c>.</summary>
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the value that must be selected on it.</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The <c>if</c> of a rule in the shape the side panel edits: the three toggles of design canvas 5f,
/// flattened so the query condition's "off" is a boolean rather than a null object.
/// </summary>
public class RuleConditionsDto
{
    /// <summary>Gets or sets whether the Query toggle is on.</summary>
    public bool QueryEnabled { get; set; }

    /// <summary>Gets or sets how the pattern is compared: <c>is</c>, <c>contains</c> or <c>startsWith</c>.</summary>
    public string QueryOperator { get; set; } = "contains";

    /// <summary>Gets or sets the pattern the query is compared against.</summary>
    public string QueryPattern { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the comparison sees the analyzed query, so plurals and synonyms match.</summary>
    public bool MatchAnalyzed { get; set; }

    /// <summary>Gets or sets the facet refinements that must all be selected on the request.</summary>
    public IList<RuleFilterDto> Filters { get; set; } = [];

    /// <summary>Gets or sets the code name of the contact group the rule is scoped to; empty for anyone.</summary>
    public string ContactGroup { get; set; } = string.Empty;

    /// <summary>Gets or sets the language the request must ask for; empty for any language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Reads the model's conditions into the editable shape.</summary>
    /// <param name="conditions">The conditions.</param>
    /// <returns>The editable shape.</returns>
    public static RuleConditionsDto From(RuleConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        return new RuleConditionsDto
        {
            QueryEnabled = conditions.Query is not null,
            QueryOperator = Discriminator(conditions.Query?.Operator ?? Core.Tuning.QueryOperator.Contains),
            QueryPattern = conditions.Query?.Pattern ?? string.Empty,
            MatchAnalyzed = conditions.Query?.MatchAnalyzed ?? false,
            Filters = [.. (conditions.Filters ?? []).Select(filter => new RuleFilterDto { Attribute = filter.Attribute, Value = filter.Value })],
            ContactGroup = conditions.ContactGroup ?? string.Empty,
            Language = conditions.Language ?? string.Empty,
        };
    }

    /// <summary>Builds the model's conditions from what the side panel submitted.</summary>
    /// <returns>The conditions.</returns>
    public RuleConditions ToModel() =>
        new(
            QueryEnabled ? new QueryCondition(ParseOperator(QueryOperator), QueryPattern ?? string.Empty, MatchAnalyzed) : null,
            [
                .. (Filters ?? [])
                    .Where(filter => !string.IsNullOrWhiteSpace(filter.Attribute) || !string.IsNullOrWhiteSpace(filter.Value))
                    .Select(filter => new AttributeIs((filter.Attribute ?? string.Empty).Trim(), (filter.Value ?? string.Empty).Trim()))
            ],
            (ContactGroup ?? string.Empty).Trim(),
            (Language ?? string.Empty).Trim());

    /// <summary>The wire name of a query operator. It is the same vocabulary the stored JSON uses.</summary>
    /// <param name="value">The operator.</param>
    /// <returns>The wire name.</returns>
    public static string Discriminator(QueryOperator value) =>
        value switch
        {
            Core.Tuning.QueryOperator.Is => "is",
            Core.Tuning.QueryOperator.StartsWith => "startsWith",
            _ => "contains"
        };

    /// <summary>Reads a wire operator name, falling back to <c>contains</c> for anything unknown.</summary>
    /// <param name="value">The wire name.</param>
    /// <returns>The operator.</returns>
    public static QueryOperator ParseOperator(string? value) =>
        value switch
        {
            "is" => Core.Tuning.QueryOperator.Is,
            "startsWith" => Core.Tuning.QueryOperator.StartsWith,
            _ => Core.Tuning.QueryOperator.Contains
        };
}

/// <summary>
/// One <c>then</c> card, flattened: one shape with every field any action type needs, tagged
/// with the same <c>type</c> discriminator the stored JSON uses.
/// </summary>
/// <remarks>
/// One flat shape rather than ten, because the client renders one card component that switches on
/// <see cref="Type"/>, and a discriminated union would have to be written twice - once in C# and
/// once in TypeScript - to buy nothing.
/// </remarks>
public class RuleActionDto
{
    /// <summary>The <c>type</c> values the add menu of design canvas 5b offers, in its order.</summary>
    public static IReadOnlyList<string> Types { get; } =
    [
        "pin", "hide", "boost", "bury", "filterResults", "removeWord", "replaceWord", "replaceQuery", "redirect", "customData"
    ];

    /// <summary>Gets or sets which action this is; one of <see cref="Types"/>.</summary>
    public string Type { get; set; } = "pin";

    /// <summary>Gets or sets the result id of the document to pin, hide, boost or bury.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-based position a pinned document is moved to.</summary>
    public int Position { get; set; } = 1;

    /// <summary>Gets or sets the comma-separated <c>attribute:value</c> pairs of a boost or filter.</summary>
    public string FilterExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the score multiplier of a boost.</summary>
    public double Multiplier { get; set; } = 2;

    /// <summary>Gets or sets the word a rewrite removes or replaces.</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>Gets or sets what a word replacement puts in its place.</summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>Gets or sets the query a whole-query replacement searches for instead.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Gets or sets the destination of a redirect.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON object a custom-data action attaches to the response.</summary>
    public string Json { get; set; } = string.Empty;

    /// <summary>Reads one of the model's actions into the editable shape.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The editable shape.</returns>
    public static RuleActionDto From(RuleAction action) =>
        action switch
        {
            RuleAction.Pin pin => new RuleActionDto { Type = "pin", TargetId = pin.TargetId, Position = pin.Position },
            RuleAction.Hide hide => new RuleActionDto { Type = "hide", TargetId = hide.TargetId },
            RuleAction.Boost boost => new RuleActionDto
            {
                Type = "boost",
                TargetId = boost.TargetId,
                FilterExpression = boost.FilterExpression,
                Multiplier = boost.Multiplier,
            },
            RuleAction.Bury bury => new RuleActionDto { Type = "bury", TargetId = bury.TargetId, FilterExpression = bury.FilterExpression },
            RuleAction.FilterResults filter => new RuleActionDto { Type = "filterResults", FilterExpression = filter.FilterExpression },
            RuleAction.RemoveWord remove => new RuleActionDto { Type = "removeWord", Word = remove.Word },
            RuleAction.ReplaceWord replace => new RuleActionDto { Type = "replaceWord", Word = replace.Word, Replacement = replace.Replacement },
            RuleAction.ReplaceQuery replace => new RuleActionDto { Type = "replaceQuery", Query = replace.Query },
            RuleAction.Redirect redirect => new RuleActionDto { Type = "redirect", Url = redirect.Url },
            RuleAction.CustomData data => new RuleActionDto { Type = "customData", Json = data.Json },
            _ => new RuleActionDto()
        };

    /// <summary>Builds one of the model's actions from what the card submitted.</summary>
    /// <returns>The action, or <see langword="null"/> when the type is not one this version knows.</returns>
    public RuleAction? ToModel() =>
        Type switch
        {
            "pin" => new RuleAction.Pin(Trim(TargetId), Position),
            "hide" => new RuleAction.Hide(Trim(TargetId)),
            "boost" => new RuleAction.Boost(Trim(TargetId), Trim(FilterExpression), Multiplier),
            "bury" => new RuleAction.Bury(Trim(TargetId), Trim(FilterExpression)),
            "filterResults" => new RuleAction.FilterResults(Trim(FilterExpression)),
            "removeWord" => new RuleAction.RemoveWord(Trim(Word)),
            "replaceWord" => new RuleAction.ReplaceWord(Trim(Word), Trim(Replacement)),
            "replaceQuery" => new RuleAction.ReplaceQuery(Trim(Query)),
            "redirect" => new RuleAction.Redirect(Trim(Url)),

            // The JSON is stored as typed: whitespace inside an object is the author's formatting.
            "customData" => new RuleAction.CustomData(Json ?? string.Empty),
            _ => null
        };

    private static string Trim(string? value) => (value ?? string.Empty).Trim();
}

/// <summary>A whole rule, as the builder loads and saves it.</summary>
public class RuleDto
{
    /// <summary>How the two schedule fields travel: a date, in UTC, or an empty string for "always".</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Gets or sets the database identifier, or zero for a rule that does not exist yet.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the display name, which is what the ranking explanation shows.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the rule is live.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the conflict resolution order; lower runs first.</summary>
    public int Priority { get; set; } = 100;

    /// <summary>Gets or sets the first day the rule applies, as <see cref="DateFormat"/>, or empty for "already".</summary>
    public string ValidFrom { get; set; } = string.Empty;

    /// <summary>Gets or sets the last day the rule applies, as <see cref="DateFormat"/>, or empty for "forever".</summary>
    public string ValidTo { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>if</c>.</summary>
    public RuleConditionsDto Conditions { get; set; } = new();

    /// <summary>Gets or sets the <c>then</c>, in the order it is applied.</summary>
    public IList<RuleActionDto> Actions { get; set; } = [];

    /// <summary>Reads a rule into the editable shape.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The editable shape.</returns>
    public static RuleDto From(TuningRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new RuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            Priority = rule.Priority,
            ValidFrom = Day(rule.ValidFrom),
            ValidTo = Day(rule.ValidTo),
            Conditions = RuleConditionsDto.From(rule.Conditions),
            Actions = [.. rule.Actions.Select(RuleActionDto.From)],
        };
    }

    /// <summary>Formats a stored moment as the day the builder shows.</summary>
    /// <param name="moment">The moment, or <see langword="null"/>.</param>
    /// <returns>The day, or an empty string.</returns>
    public static string Day(DateTime? moment) =>
        moment?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Reads a day the builder submitted back into a moment, in UTC.</summary>
    /// <param name="value">The day.</param>
    /// <returns>The moment, or <see langword="null"/> when the field was left empty or is not a date.</returns>
    public static DateTime? Moment(string? value) =>
        DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>Builds the whole rule from what the builder submitted.</summary>
    /// <returns>The conditions and the actions.</returns>
    public (RuleConditions Conditions, IReadOnlyList<RuleAction> Actions) ToModel() =>
        ((Conditions ?? new()).ToModel(),
            [.. (Actions ?? []).Select(action => action.ToModel()).OfType<RuleAction>()]);
}

/// <summary>What one failed field of a save looks like on the wire.</summary>
public class RuleErrorDto
{
    /// <summary>Gets or sets which control is at fault. See <see cref="RuleError.Field"/>.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Gets or sets what to tell the marketer.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>The answer to a Save: either the errors that stopped it, or the rule as it was stored.</summary>
public class RuleSaveResult
{
    /// <summary>Gets or sets the errors, empty when the rule was saved.</summary>
    public IReadOnlyList<RuleErrorDto> Errors { get; set; } = [];

    /// <summary>Gets or sets the saved rule, so the builder shows what was actually stored.</summary>
    public RuleDto? Rule { get; set; }

    /// <summary>Gets or sets why the save could not even be attempted, or an empty string.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Builds a refusal from the validation errors.</summary>
    /// <param name="errors">The errors.</param>
    /// <returns>The result.</returns>
    public static RuleSaveResult Refused(IReadOnlyList<RuleError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new RuleSaveResult
        {
            Errors = [.. errors.Select(error => new RuleErrorDto { Field = error.Field, Message = error.Message })],
        };
    }

    /// <summary>Builds a refusal that has nothing to do with any one field.</summary>
    /// <param name="message">What went wrong.</param>
    /// <returns>The result.</returns>
    public static RuleSaveResult Failed(string message) => new() { Error = message };
}

/// <summary>Initial state of the rule builder client template.</summary>
public class RuleBuilderClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code name of the index the rule belongs to. It comes from the URL.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the rule being edited, or an empty one on the create page.</summary>
    public RuleDto Rule { get; set; } = new();

    /// <summary>Gets or sets the contact groups the Context toggle offers (ADR-0021).</summary>
    public IEnumerable<ContactGroupOption> ContactGroups { get; set; } = [];

    /// <summary>Gets or sets the content languages the index is configured for.</summary>
    public IEnumerable<string> Languages { get; set; } = [];

    /// <summary>Gets or sets whether this is a new rule, which hides Delete and changes the headline.</summary>
    public bool IsNew { get; set; }

    /// <summary>
    /// Gets or sets whether the rule was converted from the pre-CR-4b storage, which shows the
    /// migration note of design canvas 5d once.
    /// </summary>
    public bool Migrated { get; set; }

    /// <summary>Gets or sets why the page has nothing to edit, or an empty string.</summary>
    public string Error { get; set; } = string.Empty;
}
