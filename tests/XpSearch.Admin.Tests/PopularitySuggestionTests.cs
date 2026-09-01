using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Tests what approving a suggested boost rule writes (RK-1). Approval creates an ordinary rule
/// through the existing storage, so it is checked the way any stored rule is: by reading its JSON
/// back.
/// </summary>
[TestFixture]
internal sealed class PopularitySuggestionTests
{
    [Test]
    public void AnApprovedSuggestion_BecomesAnOrdinaryQueryScopedBoost()
    {
        var (name, conditions, actions) = PopularitySuggestionRule.For(" espresso ", "doc-1:en");

        var storedConditions = RuleJson.ReadConditions(RuleJson.Write(conditions));
        var storedActions = RuleJson.ReadActions(RuleJson.Write(actions));

        Expect.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo("Popular for 'espresso'"));
            Assert.That(storedConditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.Is, "espresso", false)));
            Assert.That(storedConditions.ContactGroup, Is.Empty, "a suggestion is about everybody's clicks");
            Assert.That(
                storedActions.Single(),
                Is.EqualTo(new RuleAction.Boost("doc-1:en", string.Empty, PopularitySignal.MaxFactor)));
        });
    }

    /// <summary>
    /// Approval must not be able to push a document further than the automatic boost would have: the
    /// rule it writes uses the same ceiling.
    /// </summary>
    [Test]
    public void TheApprovedRulesMultiplier_IsTheSameCapTheSignalIsBoundedBy() =>
        Assert.That(PopularitySuggestionRule.Multiplier, Is.EqualTo(PopularitySignal.MaxFactor));

    [Test]
    public void TheEvidenceColumn_ShowsTheClicksAndTheShare() =>
        Assert.That(
            PopularitySuggestionListing.Evidence(7, 86),
            Is.EqualTo("7 clicks, 86% of the query's clicks"));
}
