using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// How a variant's tuning rows are told apart from the live ones (XP-1), and the state machine that
/// guards an experiment. Both are the parts that can be checked without a database; the cloning and
/// promotion themselves need one - see KNOWN-LIMITATIONS.
/// </summary>
[TestFixture]
internal sealed class ExperimentStorageTests
{
    /// <summary>
    /// The one rule the whole feature rests on: a live read must ask for rows with no experiment, so
    /// an experiment's draft can never reach a visitor who is not in it.
    /// </summary>
    [Test]
    public void ALiveReadAsksForRowsWithNoExperimentAndAVariantReadForItsOwn()
    {
        var live = VariantScope.Condition("RuleExperimentID", TuningVariant.Live);
        var variant = VariantScope.Condition("RuleExperimentID", new TuningVariant(7));

        Expect.Multiple(() =>
        {
            Assert.That(live.WhereCondition, Does.Contain("RuleExperimentID").And.Contain("IS NULL"));
            Assert.That(variant.WhereCondition, Does.Contain("RuleExperimentID"));
            Assert.That(variant.WhereCondition, Does.Not.Contain("IS NULL"));
            Assert.That(
                variant.Parameters.Select(parameter => parameter.Value),
                Does.Contain(7),
                "the identifier is a parameter, not concatenated SQL");
        });
    }

    /// <summary>
    /// Every tuning object type has to carry the reference, or its rows could not be cloned into a
    /// variant at all - and would silently stay live in variant B.
    /// </summary>
    [Test]
    public void EveryTuningObjectTypeHasAnExperimentColumn() =>
        Assert.That(
            VariantScope.ExperimentColumns,
            Is.EquivalentTo(new Dictionary<string, string>
            {
                [XpSearchRuleInfo.OBJECT_TYPE] = nameof(XpSearchRuleInfo.RuleExperimentID),
                [XpSearchSynonymInfo.OBJECT_TYPE] = nameof(XpSearchSynonymInfo.SynonymExperimentID),
                [XpSearchStopwordListInfo.OBJECT_TYPE] = nameof(XpSearchStopwordListInfo.StopwordListExperimentID),
                [XpSearchFieldWeightInfo.OBJECT_TYPE] = nameof(XpSearchFieldWeightInfo.WeightExperimentID)
            }));

    /// <summary>
    /// The cached tuning of variant B must not be served to variant A, and the other way round: the
    /// entries are separate or the swap would depend on which variant searched first.
    /// </summary>
    [Test]
    public void EachVariantCachesItsTuningSeparately()
    {
        string[] live = InfoRelevanceTuningSource.CacheKeyParts("products", "rules");
        string[] variant = InfoRelevanceTuningSource.CacheKeyParts("products", "rules", new TuningVariant(7));

        Expect.Multiple(() =>
        {
            Assert.That(variant, Is.Not.EqualTo(live).AsCollection);
            Assert.That(
                InfoRelevanceTuningSource.CacheKeyParts("products", "rules", TuningVariant.Live),
                Is.EqualTo(live).AsCollection,
                "the default is the live variant");
            Assert.That(
                InfoRunningExperimentSource.CacheKeyParts("products"),
                Is.Not.EqualTo(InfoRunningExperimentSource.CacheKeyParts("articles")).AsCollection);
        });
    }

    [Test]
    public void AnExperimentOnlyMovesForwardsThroughItsStates() =>
        Expect.Multiple(() =>
        {
            Assert.That(ExperimentRules.CanStart(ExperimentState.Draft), Is.True);
            Assert.That(ExperimentRules.CanStart(ExperimentState.Running), Is.False);
            Assert.That(ExperimentRules.CanStart(ExperimentState.Concluded), Is.False);

            Assert.That(ExperimentRules.CanConclude(ExperimentState.Running), Is.True);
            Assert.That(ExperimentRules.CanConclude(ExperimentState.Draft), Is.False);
            Assert.That(ExperimentRules.CanConclude(ExperimentState.Concluded), Is.False, "concluding twice would delete or promote rows again");
        });

    [Test]
    public void TheSplitIsFixedOnceTrafficIsBeingDividedByIt() =>
        Expect.Multiple(() =>
        {
            Assert.That(ExperimentRules.CanChangeSplit(ExperimentState.Draft), Is.True);
            Assert.That(ExperimentRules.CanChangeSplit(ExperimentState.Running), Is.False);
            Assert.That(ExperimentRules.CanChangeSplit(ExperimentState.Concluded), Is.False);
        });

    [Test]
    public void BothVariantsMustGetTraffic() =>
        Expect.Multiple(() =>
        {
            Assert.That(ExperimentRules.IsValidSplit(0), Is.False);
            Assert.That(ExperimentRules.IsValidSplit(100), Is.False);
            Assert.That(ExperimentRules.IsValidSplit(-5), Is.False);
            Assert.That(ExperimentRules.IsValidSplit(1), Is.True);
            Assert.That(ExperimentRules.IsValidSplit(50), Is.True);
            Assert.That(ExperimentRules.IsValidSplit(99), Is.True);
        });

    /// <summary>Only a concluded experiment lets the index have another one (amendment: one running per index).</summary>
    [Test]
    public void AnIndexCanOnlyHaveOneUnfinishedExperiment() =>
        Expect.Multiple(() =>
        {
            Assert.That(ExperimentRules.BlocksNewExperiment(ExperimentState.Draft), Is.True);
            Assert.That(ExperimentRules.BlocksNewExperiment(ExperimentState.Running), Is.True);
            Assert.That(ExperimentRules.BlocksNewExperiment(ExperimentState.Concluded), Is.False);
        });
}
