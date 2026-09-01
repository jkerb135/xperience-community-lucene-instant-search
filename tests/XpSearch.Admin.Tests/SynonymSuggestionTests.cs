using NUnit.Framework;

using XpSearch.Admin.UIPages;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Tests what approving a mined synonym writes (SY-1). Approval creates an ordinary synonym group
/// through the existing storage, so it is checked the way the tuning source reads one back: by
/// splitting the stored terms.
/// </summary>
[TestFixture]
internal sealed class SynonymSuggestionTests
{
    [Test]
    public void AnApprovedSuggestion_BecomesAnOrdinaryTwoWayGroup()
    {
        var (direction, input, output) = SynonymSuggestionGroup.For(" SETTEE ", "red  sofa");

        Expect.Multiple(() =>
        {
            Assert.That(direction, Is.EqualTo(SynonymDirection.TwoWay));
            Assert.That(
                SynonymExpansion.SplitTerms(input),
                Is.EqualTo(new[] { "settee", "red sofa" }).AsCollection,
                "both phrases are terms of one group, normalized like the log");
            Assert.That(output, Is.Empty, "a two-way group has no replacements");
        });
    }

    /// <summary>
    /// A comma is the term separator of the stored value, so a mined query containing one must not
    /// split into two terms of its own.
    /// </summary>
    [Test]
    public void ACommaInAMinedQuery_DoesNotSplitTheGroup()
    {
        var (_, input, _) = SynonymSuggestionGroup.For("sofa, red", "settee");

        Assert.That(SynonymExpansion.SplitTerms(input), Is.EqualTo(new[] { "sofa red", "settee" }).AsCollection);
    }

    [Test]
    public void TheEvidenceColumn_CountsTheReformulations()
    {
        Expect.Multiple(() =>
        {
            Assert.That(SynonymSuggestionListing.Evidence(4), Is.EqualTo("4 reformulations"));
            Assert.That(SynonymSuggestionListing.Evidence(1), Is.EqualTo("1 reformulation"));
        });
    }
}
