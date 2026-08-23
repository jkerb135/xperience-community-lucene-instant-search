using CMS;
using CMS.Activities;
using CMS.ContactManagement;
using CMS.Core;
using CMS.DataEngine;
using CMS.MacroEngine;

using XpSearch.Core.ContactGroups;

[assembly: RegisterExtension(typeof(XpSearchContactMacroMethods), typeof(ContactInfo))]

namespace XpSearch.Core.ContactGroups;

/// <summary>
/// The macro methods the search contact group conditions call, registered as an extension of
/// <see cref="ContactInfo"/> so they can be written as <c>Contact.XpSearchSearchedFor("mugs")</c>
/// (https://docs.kentico.com/documentation/developers-and-admins/api/macro-expressions/registering-custom-macro-methods).
/// </summary>
/// <remarks>
/// Macro methods are static, so the activity provider is resolved from the service container instead
/// of being injected.
/// </remarks>
public class XpSearchContactMacroMethods : MacroMethodContainer
{
    /// <summary>Whether the contact ran a search whose text contains the parameter.</summary>
    /// <param name="context">Evaluation context supplied by the macro engine.</param>
    /// <param name="parameters">The contact and the searched text.</param>
    /// <returns><see langword="true"/> when a matching activity exists.</returns>
    [MacroMethod(typeof(bool), "Returns true if the contact has searched for text containing the given value.", 2)]
    [MacroMethodParam(0, "contact", typeof(ContactInfo), "Contact to check.")]
    [MacroMethodParam(1, "text", typeof(string), "Searched text, or empty for any search.")]
    public static object XpSearchSearchedFor(EvaluationContext context, params object[] parameters) =>
        Evaluate(XpSearchContactGroupRules.SearchedFor, parameters);

    /// <summary>Whether the contact ran a search that returned nothing for text containing the parameter.</summary>
    /// <param name="context">Evaluation context supplied by the macro engine.</param>
    /// <param name="parameters">The contact and the searched text.</param>
    /// <returns><see langword="true"/> when a matching activity exists.</returns>
    [MacroMethod(typeof(bool), "Returns true if the contact has searched without results for text containing the given value.", 2)]
    [MacroMethodParam(0, "contact", typeof(ContactInfo), "Contact to check.")]
    [MacroMethodParam(1, "text", typeof(string), "Searched text, or empty for any search.")]
    public static object XpSearchSearchedWithoutResultsFor(EvaluationContext context, params object[] parameters) =>
        Evaluate(XpSearchContactGroupRules.SearchedWithoutResultsFor, parameters);

    /// <summary>Whether the contact opened a result after searching for text containing the parameter.</summary>
    /// <param name="context">Evaluation context supplied by the macro engine.</param>
    /// <param name="parameters">The contact and the searched text.</param>
    /// <returns><see langword="true"/> when a matching activity exists.</returns>
    [MacroMethod(typeof(bool), "Returns true if the contact has clicked a search result after searching for text containing the given value.", 2)]
    [MacroMethodParam(0, "contact", typeof(ContactInfo), "Contact to check.")]
    [MacroMethodParam(1, "text", typeof(string), "Searched text, or empty for any search.")]
    public static object XpSearchClickedSearchResultFor(EvaluationContext context, params object[] parameters) =>
        Evaluate(XpSearchContactGroupRules.ClickedSearchResultFor, parameters);

    private static object Evaluate(XpSearchContactGroupRule rule, params object[] parameters)
    {
        if (parameters is not [ContactInfo contact, ..] || contact.ContactID <= 0)
        {
            return false;
        }

        var text = parameters.Length > 1 ? parameters[1]?.ToString() : null;

        return XpSearchActivityQuery
            .ContactIds(Service.Resolve<IInfoProvider<ActivityInfo>>(), rule, text)
            .WhereEquals(nameof(ActivityInfo.ActivityContactID), contact.ContactID)
            .TopN(1)
            .Any();
    }
}
