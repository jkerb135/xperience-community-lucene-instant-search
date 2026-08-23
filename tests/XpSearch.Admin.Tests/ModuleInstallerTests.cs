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
                nameof(XpSearchRuleInfo.RuleContactGroup),
                nameof(XpSearchRuleInfo.RuleConditionType),
                nameof(XpSearchRuleInfo.RulePattern),
                nameof(XpSearchRuleInfo.RuleConsequenceType),
                nameof(XpSearchRuleInfo.RuleTargetObjectID),
                nameof(XpSearchRuleInfo.RuleTargetPosition),
                nameof(XpSearchRuleInfo.RuleBoostValue),
                nameof(XpSearchRuleInfo.RuleFilterExpression),
                nameof(XpSearchRuleInfo.RuleRedirectUrl),
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
            Assert.That(fields[nameof(XpSearchRuleInfo.RuleBoostValue)].DataType, Is.EqualTo(FieldDataType.Decimal));
        });
    }

    /// <summary>
    /// The contact group column was added after the first release (ADR-0021), so an existing
    /// installation has to gain it without losing the columns already there and without gaining it
    /// twice - which is what <c>InstallClass</c> asks <c>CombineWithForm</c> to do.
    /// </summary>
    [Test]
    public void CombiningTheRuleFormWithAnOlderOneAddsTheContactGroupColumnExactlyOnce()
    {
        var installed = new FormInfo(WithoutContactGroup().GetXmlDefinition());

        // Twice: the installer runs on every application start.
        installed.CombineWithForm(XpSearchTuningModuleInstaller.RuleForm(), new CombineWithFormSettings());
        installed.CombineWithForm(XpSearchTuningModuleInstaller.RuleForm(), new CombineWithFormSettings());

        var names = installed.GetFields(true, true).Select(field => field.Name).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(names.Count(name => string.Equals(name, nameof(XpSearchRuleInfo.RuleContactGroup), StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(names, Is.EquivalentTo(XpSearchTuningModuleInstaller.RuleForm().GetFields(true, true).Select(field => field.Name)));
            Assert.That(
                installed.GetFields(true, true).First(field => field.Name == nameof(XpSearchRuleInfo.RuleContactGroup)).AllowEmpty,
                Is.True,
                "existing rows have no contact group, and an empty one means everyone");
        });
    }

    /// <summary>The rule class as it shipped before the contact group column existed.</summary>
    private static FormInfo WithoutContactGroup()
    {
        var form = XpSearchTuningModuleInstaller.RuleForm();

        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleContactGroup));

        return form;
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
