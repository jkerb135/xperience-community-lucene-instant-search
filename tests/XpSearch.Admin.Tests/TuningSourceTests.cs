using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The database-backed tuning source's cache contract (spec §8.5) and the option values the edit
/// pages store. Anything that touches an Info object needs Kentico's IoC container, so the model to
/// row mapping is verified on a running instance, not here - see KNOWN-LIMITATIONS.
/// </summary>
[TestFixture]
internal sealed class TuningSourceTests
{
    /// <summary>
    /// Every kind of tuning data an index can carry must be able to evict the cached entry. If a
    /// fifth object type is added and not listed here, a saved change would not be picked up.
    /// </summary>
    [Test]
    public void TheCacheDependsOnEveryTuningObjectType() =>
        Assert.That(
            InfoRelevanceTuningSource.DependencyObjectTypes,
            Is.EquivalentTo(new[]
            {
                XpSearchRuleInfo.OBJECT_TYPE,
                XpSearchSynonymInfo.OBJECT_TYPE,
                XpSearchStopwordListInfo.OBJECT_TYPE,
                XpSearchFieldWeightInfo.OBJECT_TYPE
            }));

    [Test]
    public void CacheKeysAreScopedToTheIndexAndTheKindOfData()
    {
        string[] products = InfoRelevanceTuningSource.CacheKeyParts("products", "rules");
        string[] articles = InfoRelevanceTuningSource.CacheKeyParts("articles", "rules");
        string[] synonyms = InfoRelevanceTuningSource.CacheKeyParts("products", "synonyms");

        Expect.Multiple(() =>
        {
            Assert.That(products, Is.Not.EqualTo(articles).AsCollection);
            Assert.That(products, Is.Not.EqualTo(synonyms).AsCollection);
            Assert.That(products, Does.Contain("products"));
        });
    }

    [Test]
    public void StopwordsAreOnePerLineTrimmedAndLowercased() =>
        Assert.That(
            InfoRelevanceTuningSource.SplitStopwords("The\r\n  a \n\nAN\nthe"),
            Is.EqualTo(new[] { "the", "a", "an" }).AsCollection);

    /// <summary>
    /// The drop-downs store the numeric value of the Core enums, so an option added to a form without
    /// a matching enum member would silently become the first one.
    /// </summary>
    [Test]
    public void DropDownOptionsMatchTheCoreEnumValues()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleModel.ParseOption("3"), Is.EqualTo((int)RuleCondition.Always));
            Assert.That(RuleModel.ParseOption("4"), Is.EqualTo((int)RuleConsequence.Redirect));
            Assert.That(RuleModel.ParseOption("1"), Is.EqualTo((int)SynonymDirection.OneWay));
            Assert.That(RuleModel.ParseOption("not a number"), Is.Zero);
        });
    }

}
