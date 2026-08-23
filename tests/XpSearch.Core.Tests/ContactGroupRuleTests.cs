using CMS.MacroEngine;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.ContactGroups;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the definitions of the search contact group conditions (spec §9.2): what the installer
/// writes into <c>CMS_MacroRule</c> and that every rule points at a macro method that exists.
/// Everything downstream of that - the activity query and the installer's writes - needs an
/// initialized Xperience container and a database, and is on the host checklist instead.
/// </summary>
[TestFixture]
internal sealed class ContactGroupRuleTests
{
    [Test]
    public void EveryRule_IsATextRuleUsableInContactGroups()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var rule in XpSearchContactGroupRules.All)
            {
                Assert.That(rule.Text, Does.Contain("{text}"), rule.Name);
                Assert.That(rule.Condition, Is.EqualTo($"Contact.{rule.MacroMethod}(\"{{text}}\")"));
                Assert.That(rule.Parameters, Does.Contain("column=\"text\""));
                Assert.That(rule.Parameters, Does.Contain("Kentico.Administration.TextInput"));
                Assert.That(rule.ActivityTypes, Is.Not.Empty);
            }

            Assert.That(
                XpSearchContactGroupRules.All.Select(rule => rule.Name),
                Is.EqualTo(new[]
                {
                    "XpSearchContactSearchedFor",
                    "XpSearchContactSearchedWithoutResultsFor",
                    "XpSearchContactClickedSearchResultFor"
                }).AsCollection);
            Assert.That(
                XpSearchContactGroupRules.SearchedFor.ActivityTypes,
                Is.EqualTo(new[] { XpSearchActivityTypes.Query, XpSearchActivityTypes.NoResults }).AsCollection);
            Assert.That(
                XpSearchContactGroupRules.SearchedWithoutResultsFor.ActivityTypes,
                Is.EqualTo(new[] { XpSearchActivityTypes.NoResults }).AsCollection);
            Assert.That(
                XpSearchContactGroupRules.ClickedSearchResultFor.ActivityTypes,
                Is.EqualTo(new[] { XpSearchActivityTypes.Click }).AsCollection);
        }
    }

    [Test]
    public void EveryRule_CallsAMacroMethodTheMacroEngineCanSee()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var rule in XpSearchContactGroupRules.All)
            {
                var method = typeof(XpSearchContactMacroMethods).GetMethod(rule.MacroMethod);

                Assert.That(method, Is.Not.Null, $"{rule.Name} names a macro method that does not exist.");
                Assert.That(method!.IsStatic, Is.True, rule.MacroMethod);
                Assert.That(
                    method.GetCustomAttributes(typeof(MacroMethodAttribute), inherit: false),
                    Is.Not.Empty,
                    $"{rule.MacroMethod} is not marked as a macro method.");
                Assert.That(
                    method.GetCustomAttributes(typeof(MacroMethodParamAttribute), inherit: false),
                    Has.Length.EqualTo(2),
                    $"{rule.MacroMethod} takes the contact and the searched text.");
            }
        }
    }
}
