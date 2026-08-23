using XpSearch.Core.Analytics;

namespace XpSearch.Core.ContactGroups;

/// <summary>
/// One contact group condition this library adds to the condition picker (<em>Digital marketing →
/// Contact groups → New → Edit conditions</em>).
/// </summary>
/// <param name="Name">Code name of the <c>MacroRuleInfo</c> row.</param>
/// <param name="DisplayName">Name shown in the rule designer.</param>
/// <param name="Text">
/// Rule text shown in the condition picker. <c>{text}</c> is the placeholder the parameter editor
/// replaces with the text input.
/// </param>
/// <param name="MacroMethod">
/// Name of the <see cref="XpSearchContactMacroMethods"/> method the rule's macro condition calls.
/// </param>
/// <param name="ParameterFieldGuid">Stable GUID of the <c>text</c> field in <see cref="Parameters"/>.</param>
/// <param name="ActivityTypes">Activity types a contact must have performed to match the rule.</param>
public sealed record XpSearchContactGroupRule(
    string Name,
    string DisplayName,
    string Text,
    string MacroMethod,
    string ParameterFieldGuid,
    IReadOnlyList<string> ActivityTypes)
{
    /// <summary>Gets the macro the rule evaluates, for example <c>Contact.XpSearchSearchedFor("{text}")</c>.</summary>
    public string Condition => $"Contact.{MacroMethod}(\"{{text}}\")";

    /// <summary>
    /// Gets the parameter form of the rule: a single optional text input, the same
    /// <c>Kentico.Administration.TextInput</c> field the system rule
    /// <c>CMSContactHasPerformedCustomActivityWithValue</c> uses for its value.
    /// </summary>
    public string Parameters =>
        "<form><field allowempty=\"true\" column=\"text\" columnsize=\"200\" columntype=\"text\" guid=\"" +
        ParameterFieldGuid +
        "\" visible=\"true\"><properties><fieldcaption>enter text</fieldcaption></properties><settings>" +
        "<controlname>Kentico.Administration.TextInput</controlname><FilterMode>False</FilterMode>" +
        "<Trim>False</Trim><WatermarkText>enter text</WatermarkText></settings></field></form>";
}

/// <summary>
/// The three search contact group conditions (spec §9.2). They exist because the system rule
/// <c>CMSContactHasPerformedCustomActivityWithValue</c> is not offered in contact groups - its
/// <c>MacroRuleUsageLocation</c> is <c>AutomationConditionStep</c>, not
/// <c>ContactGroupCondition</c> - so a marketer cannot segment on search activities out of the box.
/// </summary>
public static class XpSearchContactGroupRules
{
    /// <summary>Code name of the macro rule category the rules are listed under (<em>Web activity</em>),
    /// the one the system activity rules use.</summary>
    public const string CategoryName = "WebActivity";

    /// <summary>Name of the parameter that holds the searched text.</summary>
    public const string TextParameter = "text";

    /// <summary>Contact ran any search whose text contains the parameter.</summary>
    public static XpSearchContactGroupRule SearchedFor { get; } = new(
        "XpSearchContactSearchedFor",
        "Contact has searched for text",
        "Contact has searched for text containing {text}",
        nameof(XpSearchContactMacroMethods.XpSearchSearchedFor),
        "1f6ac0c4-2b3c-4c8a-9f0a-9e0f0d6f5a01",
        [XpSearchActivityTypes.Query, XpSearchActivityTypes.NoResults]);

    /// <summary>Contact ran a search that returned nothing.</summary>
    public static XpSearchContactGroupRule SearchedWithoutResultsFor { get; } = new(
        "XpSearchContactSearchedWithoutResultsFor",
        "Contact has searched without results",
        "Contact has searched without results for text containing {text}",
        nameof(XpSearchContactMacroMethods.XpSearchSearchedWithoutResultsFor),
        "1f6ac0c4-2b3c-4c8a-9f0a-9e0f0d6f5a02",
        [XpSearchActivityTypes.NoResults]);

    /// <summary>Contact opened a result after searching.</summary>
    public static XpSearchContactGroupRule ClickedSearchResultFor { get; } = new(
        "XpSearchContactClickedSearchResultFor",
        "Contact has clicked a search result",
        "Contact has clicked a search result after searching for text containing {text}",
        nameof(XpSearchContactMacroMethods.XpSearchClickedSearchResultFor),
        "1f6ac0c4-2b3c-4c8a-9f0a-9e0f0d6f5a03",
        [XpSearchActivityTypes.Click]);

    /// <summary>Gets all three rules.</summary>
    public static IReadOnlyList<XpSearchContactGroupRule> All { get; } =
        [SearchedFor, SearchedWithoutResultsFor, ClickedSearchResultFor];
}
