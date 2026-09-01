using CMS.DataEngine;
using CMS.FormEngine;

using NUnit.Framework;

using XpSearch.Admin.Persistence;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The four custom module classes the Search tuning application stores its data in, checked against
/// the columns spec §8.2 names. A pure test over the form definitions: installing them needs a
/// database, but getting a column name wrong does not.
/// </summary>
[TestFixture]
internal sealed class ModuleInstallerTests
{
    [Test]
    public void RuleClassHasTheColumnsOfTheSpecTable() =>
        AssertColumns(
            XpSearchTuningModuleInstaller.RuleForm(),
            [
                nameof(XpSearchRuleInfo.RuleID),
                nameof(XpSearchRuleInfo.RuleGuid),
                nameof(XpSearchRuleInfo.RuleIndexName),
                nameof(XpSearchRuleInfo.RuleName),
                nameof(XpSearchRuleInfo.RuleEnabled),
                nameof(XpSearchRuleInfo.RuleConditions),
                nameof(XpSearchRuleInfo.RuleActions),
                nameof(XpSearchRuleInfo.RuleMigrated),
                nameof(XpSearchRuleInfo.RuleValidFrom),
                nameof(XpSearchRuleInfo.RuleValidTo),
                nameof(XpSearchRuleInfo.RulePriority),
                nameof(XpSearchRuleInfo.RuleExperimentID),
            ]);

    [Test]
    public void SynonymClassHasTheColumnsOfTheSpecTable() =>
        AssertColumns(
            XpSearchTuningModuleInstaller.SynonymForm(),
            [
                nameof(XpSearchSynonymInfo.SynonymID),
                nameof(XpSearchSynonymInfo.SynonymGuid),
                nameof(XpSearchSynonymInfo.SynonymIndexName),
                nameof(XpSearchSynonymInfo.SynonymType),
                nameof(XpSearchSynonymInfo.SynonymInput),
                nameof(XpSearchSynonymInfo.SynonymOutput),
                nameof(XpSearchSynonymInfo.SynonymEnabled),
                nameof(XpSearchSynonymInfo.SynonymExperimentID),
            ]);

    [Test]
    public void FieldWeightClassHasTheColumnsOfTheSpecTable() =>
        AssertColumns(
            XpSearchTuningModuleInstaller.FieldWeightForm(),
            [
                nameof(XpSearchFieldWeightInfo.WeightID),
                nameof(XpSearchFieldWeightInfo.WeightGuid),
                nameof(XpSearchFieldWeightInfo.WeightIndexName),
                nameof(XpSearchFieldWeightInfo.WeightFieldName),
                nameof(XpSearchFieldWeightInfo.WeightValue),
                nameof(XpSearchFieldWeightInfo.WeightExperimentID),
            ]);

    [Test]
    public void StopwordListClassIsOneRowPerIndex() =>
        AssertColumns(
            XpSearchTuningModuleInstaller.StopwordListForm(),
            [
                nameof(XpSearchStopwordListInfo.StopwordListID),
                nameof(XpSearchStopwordListInfo.StopwordListGuid),
                nameof(XpSearchStopwordListInfo.StopwordListIndexName),
                nameof(XpSearchStopwordListInfo.StopwordListWords),
                nameof(XpSearchStopwordListInfo.StopwordListExperimentID),
            ]);

    [Test]
    public void ExperimentClassHasTheColumnsTheAmendmentNames() =>
        AssertColumns(
            XpSearchTuningModuleInstaller.ExperimentForm(),
            [
                nameof(XpSearchExperimentInfo.ExperimentID),
                nameof(XpSearchExperimentInfo.ExperimentGuid),
                nameof(XpSearchExperimentInfo.ExperimentIndexName),
                nameof(XpSearchExperimentInfo.ExperimentDisplayName),
                nameof(XpSearchExperimentInfo.ExperimentSplitPercent),
                nameof(XpSearchExperimentInfo.ExperimentState),
                nameof(XpSearchExperimentInfo.ExperimentStarted),
                nameof(XpSearchExperimentInfo.ExperimentEnded),
                nameof(XpSearchExperimentInfo.ExperimentConcludedOutcome),
            ]);

    /// <summary>
    /// The experiment reference of a tuning row must be optional on every one of the four classes: a
    /// live row has none, and an upgraded installation has rows that predate the column entirely.
    /// </summary>
    [Test]
    public void TheExperimentColumnsAreNullableOnEveryTuningClass()
    {
        (FormInfo Form, string Column)[] classes =
        [
            (XpSearchTuningModuleInstaller.RuleForm(), nameof(XpSearchRuleInfo.RuleExperimentID)),
            (XpSearchTuningModuleInstaller.SynonymForm(), nameof(XpSearchSynonymInfo.SynonymExperimentID)),
            (XpSearchTuningModuleInstaller.FieldWeightForm(), nameof(XpSearchFieldWeightInfo.WeightExperimentID)),
            (XpSearchTuningModuleInstaller.StopwordListForm(), nameof(XpSearchStopwordListInfo.StopwordListExperimentID)),
        ];

        Expect.Multiple(() =>
        {
            foreach (var (form, column) in classes)
            {
                var field = form.GetFields(true, true).Single(field => field.Name == column);

                Assert.That(field.AllowEmpty, Is.True, column);
                Assert.That(field.DataType, Is.EqualTo(FieldDataType.Integer), column);
            }
        });
    }

    [Test]
    public void TheScheduleColumnsAreOptionalAndTheRequiredOnesAreNot()
    {
        var fields = XpSearchTuningModuleInstaller.RuleForm().GetFields(true, true).ToDictionary(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleValidFrom)].AllowEmpty, Is.True);
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleValidTo)].AllowEmpty, Is.True);
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleIndexName)].AllowEmpty, Is.False);

            // A rule is written before the builder can have filled its if/then, and an empty "if" is
            // the marker RuleStorageMigration keys on.
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleConditions)].AllowEmpty, Is.True);
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleConditions)].DataType, Is.EqualTo(FieldDataType.LongText));
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleActions)].DataType, Is.EqualTo(FieldDataType.LongText));
        });
    }

    /// <summary>
    /// The tuning classes must not land in the ingestion module: two installers writing the same
    /// <c>ResourceInfo</c> would race each other on the first application start.
    /// </summary>
    [Test]
    public void TheTuningModuleIsSeparateFromTheIngestionModule() =>
        Assert.That(XpSearchTuningModuleInstaller.ResourceName, Is.EqualTo("CMS.Integration.XpSearchTuning"));

    private static void AssertColumns(FormInfo form, string[] expected) =>
        Assert.That(form.GetFields(true, true).Select(field => field.Name), Is.EquivalentTo(expected));
}
