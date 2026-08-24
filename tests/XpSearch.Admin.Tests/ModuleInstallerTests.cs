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
                nameof(XpSearchRuleInfo.RuleConsequences),
                nameof(XpSearchRuleInfo.RuleMigrated),
                nameof(XpSearchRuleInfo.RuleValidFrom),
                nameof(XpSearchRuleInfo.RuleValidTo),
                nameof(XpSearchRuleInfo.RulePriority),
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
            ]);

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
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleConsequences)].DataType, Is.EqualTo(FieldDataType.LongText));
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
