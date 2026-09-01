using System.Text.Json;

using CMS.FormEngine;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Compares conditions member by member. <see cref="RuleConditions"/> is a record whose
/// <c>Filters</c> is a list, and a record compares a list member by reference, so two round-tripped
/// values are never <c>Equals</c> however identical their contents.
/// </summary>
internal static class SameConditions
{
    internal static void Assert(RuleConditions actual, RuleConditions expected, string because = "")
    {
        NUnit.Framework.Assert.That(actual.Query, Is.EqualTo(expected.Query), because);
        NUnit.Framework.Assert.That(actual.Filters, Is.EqualTo(expected.Filters).AsCollection, because);
        NUnit.Framework.Assert.That(actual.ContactGroup, Is.EqualTo(expected.ContactGroup), because);
        NUnit.Framework.Assert.That(actual.Language, Is.EqualTo(expected.Language), because);
    }
}

/// <summary>
/// The two JSON columns a rule is stored in (ADR-0022 addendum): what they look like on disk, that
/// every action type survives a round trip, and that a column a hand edit broke cannot take the
/// index's tuning down with it.
/// </summary>
[TestFixture]
internal sealed class RuleJsonTests
{
    private static readonly RuleAction[] EveryType =
    [
        new RuleAction.Pin("doc-1:en", 3),
        new RuleAction.Hide("doc-2:en"),
        new RuleAction.Boost("doc-3:en", "Category:coffee", 2.5),
        new RuleAction.Bury("doc-4:en", "Category:tea"),
        new RuleAction.FilterResults("Category:coffee, Tags:brewing"),
        new RuleAction.RemoveWord("cheap"),
        new RuleAction.ReplaceWord("mill", "grinder"),
        new RuleAction.ReplaceQuery("hand grinder"),
        new RuleAction.Redirect("/campaigns/grinder-week"),
        new RuleAction.CustomData("{\"banner\":\"Grinder week\"}")
    ];

    /// <summary>
    /// Every action the model has must survive storage. A type added to
    /// <see cref="RuleAction"/> without a <c>JsonDerivedType</c> throws on write, which this
    /// catches; one added without a case here would go untested, so the count is asserted too.
    /// </summary>
    [Test]
    public void EveryActionTypeRoundTrips()
    {
        int declared = typeof(RuleAction).GetNestedTypes().Length;

        var read = RuleJson.ReadActions(RuleJson.Write(EveryType));

        Expect.Multiple(() =>
        {
            Assert.That(EveryType, Has.Length.EqualTo(declared), "a new action type needs a case in this test");
            Assert.That(read, Is.EqualTo(EveryType).AsCollection);
        });
    }

    [Test]
    public void ConditionsRoundTripIncludingTheEmptyOne()
    {
        var full = new RuleConditions(
            new QueryCondition(QueryOperator.StartsWith, "grinder", true),
            [new AttributeIs("ProductFieldCategory", "Grinders")],
            "CoffeeGrinders",
            "en");

        Expect.Multiple(() =>
        {
            SameConditions.Assert(RuleJson.ReadConditions(RuleJson.Write(full)), full);
            SameConditions.Assert(RuleJson.ReadConditions(RuleJson.Write(RuleJson.NoConditions)), RuleJson.NoConditions);
        });
    }

    /// <summary>
    /// The stored shape is part of the contract - the ADR documents it and a support engineer reads
    /// it in a database client - so the discriminator and the member names are asserted, not just
    /// the round trip.
    /// </summary>
    [Test]
    public void TheStoredShapeIsTheDocumentedOne()
    {
        string conditions = RuleJson.Write(new RuleConditions(
            new QueryCondition(QueryOperator.Contains, "grinder", true),
            [new AttributeIs("Category", "Grinders")],
            "CoffeeGrinders",
            "en"));

        string actions = RuleJson.Write([new RuleAction.Pin("doc-1:en", 1)]);

        Expect.Multiple(() =>
        {
            Assert.That(
                conditions,
                Is.EqualTo("{\"query\":{\"operator\":\"contains\",\"pattern\":\"grinder\",\"matchAnalyzed\":true},\"filters\":[{\"attribute\":\"Category\",\"value\":\"Grinders\"}],\"contactGroup\":\"CoffeeGrinders\",\"language\":\"en\"}"));
            Assert.That(actions, Is.EqualTo("[{\"type\":\"pin\",\"targetId\":\"doc-1:en\",\"position\":1}]"));
            Assert.That(
                RuleJson.Write(RuleJson.NoConditions),
                Is.EqualTo("{\"filters\":[],\"contactGroup\":\"\",\"language\":\"\"}"),
                "'any query' is the absence of the member, and IsEmpty is derived so it is never stored");
        });
    }

    /// <summary>A rule whose columns were hand-edited into nonsense goes inert, not fatal.</summary>
    [Test]
    public void UnreadableColumnsBecomeAnInertRule()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleJson.ReadConditions("not json").IsEmpty, Is.True);
            Assert.That(RuleJson.ReadConditions("{\"query\":").IsEmpty, Is.True);
            Assert.That(RuleJson.ReadActions("[{\"type\":\"nope\"}]"), Is.Empty);
            Assert.That(RuleJson.ReadActions("not json"), Is.Empty);
            Assert.That(RuleJson.ReadConditions(string.Empty).IsEmpty, Is.True);
        });
    }

    [Test]
    public void OnlyAJsonObjectCountsAsCustomData()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleJson.IsJsonObject("{\"a\":1}"), Is.True);
            Assert.That(RuleJson.IsJsonObject("[1,2]"), Is.False, "the response member is an object");
            Assert.That(RuleJson.IsJsonObject("\"text\""), Is.False);
            Assert.That(RuleJson.IsJsonObject("{ \"banner\": \"Grinder week…"), Is.False);
            Assert.That(RuleJson.IsJsonObject("   "), Is.False);
        });
    }

    /// <summary>The reader must not choke on a document nested deeper than it expects, either.</summary>
    [Test]
    public void CustomDataKeepsItsTextExactly()
    {
        string authored = "{\n  \"banner\": \"Grinder week\",\n  \"cta\": \"/campaigns\"\n}";

        var read = RuleJson.ReadActions(RuleJson.Write([new RuleAction.CustomData(authored)]));

        Assert.That(((RuleAction.CustomData)read[0]).Json, Is.EqualTo(authored), "the author's formatting is theirs");
    }
}

/// <summary>
/// The one-time conversion of the flat rule columns into the JSON ones (unit CR-4b): every legacy
/// condition and action has to arrive unchanged, the pass has to be repeatable, and it has to
/// be safe to interrupt.
/// </summary>
[TestFixture]
internal sealed class RuleStorageMigrationTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static TuningRule Flat(
        LegacyCondition condition = LegacyCondition.Contains,
        string pattern = "espresso",
        LegacyConsequence action = LegacyConsequence.Boost,
        string targetId = "doc-1",
        int position = 3,
        double boost = 2.5,
        string filter = "Category:coffee",
        string redirect = "/promo",
        string group = "vips") =>
        RuleStorageMigration.FromFlat(
            7, "legacy", true, condition, pattern, action, targetId, position, boost, filter, redirect,
            Now.AddDays(-1), Now.AddDays(1), 42, group);

    [Test]
    public void EveryActionTypeIsMapped()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Flat(action: LegacyConsequence.Pin).Actions[0], Is.EqualTo(new RuleAction.Pin("doc-1", 3)));
            Assert.That(Flat(action: LegacyConsequence.Bury).Actions[0], Is.EqualTo(new RuleAction.Bury("doc-1", string.Empty)));
            Assert.That(Flat(action: LegacyConsequence.Boost).Actions[0], Is.EqualTo(new RuleAction.Boost("doc-1", "Category:coffee", 2.5)));
            Assert.That(Flat(action: LegacyConsequence.Filter).Actions[0], Is.EqualTo(new RuleAction.FilterResults("Category:coffee")));
            Assert.That(Flat(action: LegacyConsequence.Redirect).Actions[0], Is.EqualTo(new RuleAction.Redirect("/promo")));
        });
    }

    [Test]
    public void EveryConditionTypeIsMapped()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Flat(condition: LegacyCondition.Contains).Conditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.Contains, "espresso", false)));
            Assert.That(Flat(condition: LegacyCondition.Exact).Conditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.Is, "espresso", false)));
            Assert.That(Flat(condition: LegacyCondition.StartsWith).Conditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.StartsWith, "espresso", false)));
            Assert.That(
                Flat(condition: LegacyCondition.Always, pattern: string.Empty).Conditions.Query,
                Is.EqualTo(new QueryCondition(QueryOperator.Contains, string.Empty, false)),
                "'is anything at all' becomes a pattern every query contains");
        });
    }

    [Test]
    public void TheRestOfTheRowSurvives()
    {
        var rule = Flat();

        Expect.Multiple(() =>
        {
            Assert.That(rule.Id, Is.EqualTo(7));
            Assert.That(rule.Name, Is.EqualTo("legacy"));
            Assert.That(rule.Priority, Is.EqualTo(42));
            Assert.That(rule.ValidFrom, Is.EqualTo(Now.AddDays(-1)));
            Assert.That(rule.ValidTo, Is.EqualTo(Now.AddDays(1)));
            Assert.That(rule.Conditions.ContactGroup, Is.EqualTo("vips"));
            Assert.That(rule.Conditions.Filters, Is.Empty);
            Assert.That(rule.Conditions.Language, Is.Empty);
            Assert.That(rule.Actions, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// The two ends of the flat behaviour: "is anything at all" still fires on every query, and a
    /// blank pattern under any other operator still fires on none.
    /// </summary>
    [Test]
    public void TheOldEdgeCasesKeepTheirBehaviour()
    {
        var anything = RuleStorageMigration.FromFlat(
            1, "always", true, LegacyCondition.Always, string.Empty, LegacyConsequence.Boost, "doc-1", 0, 2,
            string.Empty, string.Empty, null, null, 100, string.Empty);

        var blank = RuleStorageMigration.FromFlat(
            2, "blank", true, LegacyCondition.Contains, "   ", LegacyConsequence.Boost, "doc-1", 0, 2,
            string.Empty, string.Empty, null, null, 100, string.Empty);

        Expect.Multiple(() =>
        {
            Assert.That(RuleSelection.Active([anything], "anything at all", Now), Has.Count.EqualTo(1));
            Assert.That(RuleSelection.Active([anything], string.Empty, Now), Has.Count.EqualTo(1));
            Assert.That(RuleSelection.Active([blank], "anything at all", Now), Is.Empty);
        });
    }

    /// <summary>
    /// Every flat row converts to JSON that reads back as the same rule. This is the whole promise of
    /// the migration: automatic and lossless.
    /// </summary>
    [Test]
    public void ConvertingAndReadingBackGivesTheSameRule()
    {
        foreach (var action in Enum.GetValues<LegacyConsequence>())
        {
            foreach (var condition in Enum.GetValues<LegacyCondition>())
            {
                var expected = Flat(condition, action: action);
                string because = $"{condition} / {action}";

                SameConditions.Assert(RuleJson.ReadConditions(RuleJson.Write(expected.Conditions)), expected.Conditions, because);
                Assert.That(
                    RuleJson.ReadActions(RuleJson.Write(expected.Actions)),
                    Is.EqualTo(expected.Actions).AsCollection,
                    because);
            }
        }
    }

    /// <summary>
    /// The marker is the row: a filled <c>if</c> column means converted. That is what makes the pass
    /// idempotent, and what makes it safe to kill the process halfway through the table - a row that
    /// was written stays written, and one that was not is picked up next start.
    /// </summary>
    /// <remarks>
    /// Asserted on the marker rather than on a stored row: constructing an <c>XpSearchRuleInfo</c>
    /// needs Kentico's IoC container. See docs/internal/KNOWN-LIMITATIONS.md.
    /// </remarks>
    [Test]
    public void ARowIsConvertedExactlyOnce()
    {
        var converted = Flat();
        string written = RuleJson.Write(converted.Conditions);

        Expect.Multiple(() =>
        {
            Assert.That(RuleStorageMigration.NeedsConversion((string?)null), Is.True);
            Assert.That(RuleStorageMigration.NeedsConversion(string.Empty), Is.True);
            Assert.That(RuleStorageMigration.NeedsConversion("   "), Is.True);

            Assert.That(RuleStorageMigration.NeedsConversion(written), Is.False, "a converted row is never converted again");

            // Even a rule that matches everything writes an object, so no live rule can look unconverted.
            Assert.That(RuleStorageMigration.NeedsConversion(RuleJson.Write(RuleJson.NoConditions)), Is.False);
        });
    }

    /// <summary>
    /// The same "the marker is the row" trick for the interim <c>RuleConsequences</c> column: an
    /// empty <c>RuleActions</c> next to a legacy column that still holds something means the row has
    /// not been carried forward, and the array is copied verbatim because it is the same contract.
    /// </summary>
    /// <remarks>
    /// Asserted on the marker and the copy rather than on a stored row: constructing an
    /// <c>XpSearchRuleInfo</c> needs Kentico's IoC container. See docs/internal/KNOWN-LIMITATIONS.md.
    /// </remarks>
    [Test]
    public void ARowIsCarriedForwardFromTheOldActionsColumnExactlyOnce()
    {
        string legacy = RuleJson.Write([new RuleAction.Pin("doc-1:en", 1), new RuleAction.CustomData("{\"banner\":\"Grinder week\"}")]);

        string carried = RuleStorageMigration.StoredActions(string.Empty, legacy);

        Expect.Multiple(() =>
        {
            Assert.That(RuleStorageMigration.NeedsActionCopy(string.Empty, legacy), Is.True);
            Assert.That(RuleStorageMigration.NeedsActionCopy(null, legacy), Is.True);
            Assert.That(carried, Is.EqualTo(legacy), "the array is copied verbatim, not re-serialized");

            // Second pass over the same row, with the legacy column still present: nothing to do.
            Assert.That(RuleStorageMigration.NeedsActionCopy(carried, legacy), Is.False);
            Assert.That(RuleStorageMigration.StoredActions(carried, legacy), Is.EqualTo(legacy));

            // And once the legacy column has been retired.
            Assert.That(RuleStorageMigration.NeedsActionCopy(carried, string.Empty), Is.False);

            // A rule that genuinely does nothing stores an empty array, so it is not carried forward.
            Assert.That(RuleStorageMigration.NeedsActionCopy("[]", legacy), Is.False);
            Assert.That(RuleStorageMigration.NeedsActionCopy(string.Empty, string.Empty), Is.False);
        });
    }

    /// <summary>Every flat column has to be named for retirement, or the class keeps a NOT NULL trap.</summary>
    [Test]
    public void TheRetiredColumnsAreTheOnesTheOldFormDefined()
    {
        var current = XpSearchTuningModuleInstaller.RuleForm().GetFields(true, true).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(RuleStorageMigration.LegacyColumns, Has.Count.EqualTo(9));
            Assert.That(
                RuleStorageMigration.LegacyColumns.Append(RuleStorageMigration.LegacyActionsColumn).Where(current.Contains),
                Is.Empty,
                "the installed form must not offer a column the migration is about to drop");
        });
    }

    /// <summary>
    /// Removing the flat fields from an installed definition is what retires them, so the operation
    /// the migration relies on is checked here rather than in the database.
    /// </summary>
    [Test]
    public void TheFlatColumnsCanBeRemovedFromAnInstalledDefinition()
    {
        var installed = OldRuleForm();

        foreach (string column in RuleStorageMigration.LegacyColumns)
        {
            installed.RemoveFormField(column);
        }

        var names = installed.GetFields(true, true).Select(field => field.Name).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(names, Has.No.Member(nameof(XpSearchRuleInfo.RuleConditions)).And.No.Member("RulePattern"));
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleName)), "the columns that stay, stay");
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleValidFrom)));
        });
    }

    /// <summary>
    /// The upgrade path: CombineWithForm adds the two JSON columns to a class that has the flat ones,
    /// which is why the conversion can read both shapes in the same start-up.
    /// </summary>
    [Test]
    public void CombiningTheOldFormWithTheNewOneAddsTheJsonColumns()
    {
        var installed = new FormInfo(OldRuleForm().GetXmlDefinition());

        // Twice: the installer runs on every application start.
        installed.CombineWithForm(XpSearchTuningModuleInstaller.RuleForm(), new CombineWithFormSettings());
        installed.CombineWithForm(XpSearchTuningModuleInstaller.RuleForm(), new CombineWithFormSettings());

        var names = installed.GetFields(true, true).Select(field => field.Name).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(names.Count(name => name == nameof(XpSearchRuleInfo.RuleConditions)), Is.EqualTo(1));
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleActions)));
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleMigrated)));
            Assert.That(names, Does.Contain("RulePattern"), "CombineWithForm only ever adds - the removal is explicit");
        });
    }

    /// <summary>The rule class as it shipped before the if/then storage: the flat columns of ADR-0014.</summary>
    private static FormInfo OldRuleForm()
    {
        var form = XpSearchTuningModuleInstaller.RuleForm();

        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleConditions));
        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleActions));
        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleMigrated));

        foreach (string column in RuleStorageMigration.LegacyColumns)
        {
            form.AddFormItem(
                new FormFieldInfo { Name = column, AllowEmpty = false, Visible = true, Enabled = true, Size = 450, DataType = "text" },
                -1);
        }

        return form;
    }
}

/// <summary>What the rule builder refuses to save (design canvas 5d).</summary>
[TestFixture]
internal sealed class RuleValidationTests
{
    private static readonly RuleConditions Something = new(new QueryCondition(QueryOperator.Contains, "grinder", false), [], string.Empty, string.Empty);

    private static IReadOnlyList<string> Fields(string? name, RuleConditions? conditions, params RuleAction[] actions) =>
        [.. RuleValidation.Validate(name, conditions, actions).Select(error => error.Field)];

    [Test]
    public void ARuleThatSaysNothingIsRefused()
    {
        var errors = RuleValidation.Validate("Promote grinders", RuleJson.NoConditions, []);

        Expect.Multiple(() =>
        {
            Assert.That(errors.Select(error => error.Field), Is.EqualTo(new[] { RuleValidation.ConditionsField }).AsCollection);
            Assert.That(errors[0].Message, Is.EqualTo(RuleValidation.NoConditions));
        });
    }

    [Test]
    public void AnyOneConditionIsEnough()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Fields("r", Something), Is.Empty, "a query");
            Assert.That(Fields("r", new RuleConditions(null, [new AttributeIs("Category", "Grinders")], string.Empty, string.Empty)), Is.Empty, "a filter");
            Assert.That(Fields("r", new RuleConditions(null, [], "vips", string.Empty)), Is.Empty, "a contact group");
            Assert.That(Fields("r", new RuleConditions(null, [], string.Empty, "en")), Is.Empty, "a language");
        });
    }

    [Test]
    public void TheFieldLevelMatrix()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Fields(" ", Something), Does.Contain("name"));
            Assert.That(
                Fields("r", new RuleConditions(new QueryCondition(QueryOperator.Contains, "  ", false), [], "vips", string.Empty)),
                Does.Contain("query"),
                "the Query toggle is on, so a text is required");
            Assert.That(Fields("r", new RuleConditions(null, [new AttributeIs("Category", " ")], string.Empty, string.Empty)), Does.Contain("filters"));

            Assert.That(Fields("r", Something, new RuleAction.Pin(string.Empty, 1)), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.Pin("doc-1", 0)), Does.Contain("action:0"), "position counts from 1");
            Assert.That(Fields("r", Something, new RuleAction.Pin("doc-1", 1)), Is.Empty);

            Assert.That(Fields("r", Something, new RuleAction.Hide(" ")), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.Bury(" ", string.Empty)), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.Boost(string.Empty, string.Empty, 2)), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.Boost("doc-1", string.Empty, 0)), Does.Contain("action:0"), "a multiplier of 0 switches the rule off");
            Assert.That(Fields("r", Something, new RuleAction.Boost(string.Empty, "Category:coffee", 2)), Is.Empty, "an expression is a target too");
            Assert.That(Fields("r", Something, new RuleAction.FilterResults(" ")), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.RemoveWord(" ")), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.ReplaceWord("mill", " ")), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.ReplaceQuery(" ")), Does.Contain("action:0"));
            Assert.That(Fields("r", Something, new RuleAction.Redirect(" ")), Does.Contain("action:0"));

            Assert.That(Fields("r", Something, new RuleAction.CustomData("{ \"banner\": ")), Does.Contain("action:0"), "invalid JSON blocks save");
            Assert.That(Fields("r", Something, new RuleAction.CustomData("[1]")), Does.Contain("action:0"), "an array is not an object");
            Assert.That(Fields("r", Something, new RuleAction.CustomData("{\"banner\":\"x\"}")), Is.Empty);
        });
    }

    /// <summary>Errors are addressed to the card that has to change, so the second card is not blamed.</summary>
    [Test]
    public void ErrorsPointAtTheCardTheyBelongTo()
    {
        var errors = RuleValidation.Validate(
            "r",
            Something,
            [new RuleAction.Pin("doc-1", 1), new RuleAction.Redirect(string.Empty)]);

        Assert.That(errors.Select(error => error.Field), Is.EqualTo(new[] { RuleValidation.Field(1) }).AsCollection);
    }
}

/// <summary>The one-line reading of a rule that the listing and the builder's summary rows share.</summary>
[TestFixture]
internal sealed class RuleSummaryTests
{
    [Test]
    public void OnlyTheConditionsThatAreSetAppear()
    {
        var full = new RuleConditions(
            new QueryCondition(QueryOperator.Contains, "grinder", true),
            [new AttributeIs("ProductFieldCategory", "Grinders")],
            "CoffeeGrinders",
            "en");

        Expect.Multiple(() =>
        {
            Assert.That(
                RuleSummary.Describe(full),
                Is.EqualTo("Query contains “grinder” (plurals & synonyms) · Filter ProductFieldCategory is Grinders · Language en"),
                "the listing has a contact group column of its own, so the summary leaves it out");
            Assert.That(
                RuleSummary.Describe(full, code => code == "CoffeeGrinders" ? "Grinder shoppers" : code),
                Does.Contain("Contact group Grinder shoppers"));
            Assert.That(RuleSummary.Describe(RuleJson.NoConditions), Is.EqualTo(RuleSummary.Anything));
            Assert.That(
                RuleSummary.Describe(new RuleConditions(new QueryCondition(QueryOperator.Is, "espresso", false), [], string.Empty, string.Empty)),
                Is.EqualTo("Query is “espresso”"));
        });
    }

    [Test]
    public void EveryActionReadsAsASentence()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleSummary.Describe(new RuleAction.Pin("doc-1", 1)), Is.EqualTo("Pin doc-1 to position 1"));
            Assert.That(RuleSummary.Describe(new RuleAction.Hide("doc-1")), Is.EqualTo("Hide doc-1"));
            Assert.That(RuleSummary.Describe(new RuleAction.Boost("doc-1", string.Empty, 2)), Is.EqualTo("Boost doc-1 ×2"));
            Assert.That(RuleSummary.Describe(new RuleAction.Boost(string.Empty, "Category:coffee", 1.5)), Is.EqualTo("Boost Category:coffee ×1.5"));
            Assert.That(RuleSummary.Describe(new RuleAction.RemoveWord("cheap")), Is.EqualTo("Remove the word “cheap”"));
            Assert.That(RuleSummary.Describe(new RuleAction.CustomData("{}")), Is.EqualTo("Return custom data"));
            Assert.That(RuleSummary.Describe((IReadOnlyList<RuleAction>)[]), Is.EqualTo("Nothing"));
        });
    }
}

/// <summary>What the builder sends and receives, and the rule a zero-result row seeds it with.</summary>
[TestFixture]
internal sealed class RuleBuilderModelTests
{
    /// <summary>
    /// The flat action shape the client edits has to carry every type both ways, or a card would
    /// silently save as something else.
    /// </summary>
    [Test]
    public void EveryActionTypeSurvivesTheWireShape()
    {
        RuleAction[] every =
        [
            new RuleAction.Pin("doc-1", 2),
            new RuleAction.Hide("doc-2"),
            new RuleAction.Boost("doc-3", "Category:coffee", 3),
            new RuleAction.Bury("doc-4", "Category:tea"),
            new RuleAction.FilterResults("Category:coffee"),
            new RuleAction.RemoveWord("cheap"),
            new RuleAction.ReplaceWord("mill", "grinder"),
            new RuleAction.ReplaceQuery("hand grinder"),
            new RuleAction.Redirect("/promo"),
            new RuleAction.CustomData("{\"a\":1}")
        ];

        Expect.Multiple(() =>
        {
            Assert.That(RuleActionDto.Types, Is.EquivalentTo(every.Select(c => RuleActionDto.From(c).Type)));
            Assert.That(every.Select(c => RuleActionDto.From(c).ToModel()).ToList(), Is.EqualTo(every).AsCollection);
            Assert.That(new RuleActionDto { Type = "invented" }.ToModel(), Is.Null, "an unknown type is dropped, not guessed");
        });
    }

    [Test]
    public void TheWholeRuleSurvivesTheWireShape()
    {
        var rule = new TuningRule(
            5,
            "Promote grinders",
            true,
            10,
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            null,
            new RuleConditions(new QueryCondition(QueryOperator.StartsWith, "grind", true), [new AttributeIs("Category", "Grinders")], "vips", "en"),
            [new RuleAction.Pin("doc-1", 1)]);

        var dto = RuleDto.From(rule);
        (var conditions, var actions) = dto.ToModel();

        Expect.Multiple(() =>
        {
            Assert.That(dto.ValidFrom, Is.EqualTo("2026-01-02"));
            Assert.That(dto.ValidTo, Is.Empty);
            Assert.That(RuleDto.Moment(dto.ValidFrom), Is.EqualTo(rule.ValidFrom));
            Assert.That(RuleDto.Moment("not a date"), Is.Null);
            SameConditions.Assert(conditions, rule.Conditions);
            Assert.That(actions, Is.EqualTo(rule.Actions).AsCollection);
        });
    }

    /// <summary>A blank filter row the marketer never filled in is dropped rather than refused.</summary>
    [Test]
    public void EmptyFilterRowsAreDropped()
    {
        var edited = new RuleConditionsDto
        {
            Filters = [new RuleFilterDto(), new RuleFilterDto { Attribute = " Category ", Value = " Grinders " }],
        };

        Assert.That(edited.ToModel().Filters, Is.EqualTo(new[] { new AttributeIs("Category", "Grinders") }).AsCollection);
    }

    /// <summary>
    /// A zero-result row seeds a rule that fires on that query and does nothing yet, so the marketer
    /// only has to choose the action (spec §9.3).
    /// </summary>
    [Test]
    public void TheSeededRuleMatchesTheQueryAndDoesNothingYet()
    {
        var seeded = ZeroResultRuleCreatePage.SeedFor("yirgacheffe");
        (var conditions, var actions) = seeded.ToModel();

        Expect.Multiple(() =>
        {
            Assert.That(seeded.Name, Is.EqualTo("Rule for 'yirgacheffe'"));
            Assert.That(conditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.Contains, "yirgacheffe", false)));
            Assert.That(actions, Is.Empty);
            Assert.That(seeded.Enabled, Is.True);
            Assert.That(RuleValidation.Validate(seeded.Name, conditions, actions), Is.Empty, "the seeded rule is savable as it stands");
        });
    }

    [Test]
    public void AnEmptySeedLeavesTheBuilderBlank()
    {
        var seeded = ZeroResultRuleCreatePage.SeedFor(RuleSeed.Decode(ZeroResultRuleCreatePage.EmptySeed).Query);

        Expect.Multiple(() =>
        {
            Assert.That(seeded.Name, Is.Empty);
            Assert.That(seeded.Conditions.QueryEnabled, Is.False);
            Assert.That(seeded.ToModel().Conditions.IsEmpty, Is.True, "and so Save is refused until a condition is added");
        });
    }

    /// <summary>
    /// The order of the actions is behaviour - rewrites chain and custom data merges in order - so
    /// the up/down buttons of design canvas 5g have to survive the save.
    /// </summary>
    [Test]
    public void ActionsAreStoredInTheOrderTheBuilderListsThem()
    {
        var reordered = new RuleDto
        {
            Actions =
            [
                new RuleActionDto { Type = "replaceWord", Word = "mill", Replacement = "grinder" },
                new RuleActionDto { Type = "pin", TargetId = "doc-1", Position = 1 },
                new RuleActionDto { Type = "removeWord", Word = "cheap" }
            ],
        };

        (_, var actions) = reordered.ToModel();

        Assert.That(
            actions,
            Is.EqualTo(new RuleAction[]
            {
                new RuleAction.ReplaceWord("mill", "grinder"),
                new RuleAction.Pin("doc-1", 1),
                new RuleAction.RemoveWord("cheap")
            }).AsCollection);
    }

    /// <summary>
    /// The Load DTO carries what each targeted id points at, so a summary row reads as a title. An id
    /// the index no longer holds keeps a null title and is never dropped (design canvas 5h).
    /// </summary>
    [Test]
    public void ResolvedItemsAreWrittenOntoTheActionsThatNameThem()
    {
        var rule = new RuleDto
        {
            Actions =
            [
                new RuleActionDto { Type = "pin", TargetId = "doc-1:en", Position = 1 },
                new RuleActionDto { Type = "hide", TargetId = "doc-gone:en" },
                new RuleActionDto { Type = "bury", TargetId = "doc-1:en" },
                new RuleActionDto { Type = "removeWord", Word = "cheap" }
            ],
        };

        Assert.That(rule.TargetIds(), Is.EqualTo(new[] { "doc-1:en", "doc-gone:en" }).AsCollection, "each id is asked about once");

        rule.ApplyResolvedItems(
        [
            new PickedItemDto { Id = "doc-1:en", Title = "Espresso Basics", Url = "/articles/espresso-basics" },
            new PickedItemDto { Id = "doc-gone:en" }
        ]);

        Expect.Multiple(() =>
        {
            Assert.That(rule.Actions[0].TargetTitle, Is.EqualTo("Espresso Basics"));
            Assert.That(rule.Actions[0].TargetUrl, Is.EqualTo("/articles/espresso-basics"));
            Assert.That(rule.Actions[1].TargetTitle, Is.Null, "the id is kept and the builder warns; the action is not dropped");
            Assert.That(rule.Actions[1].TargetId, Is.EqualTo("doc-gone:en"));
            Assert.That(rule.Actions[2].TargetTitle, Is.EqualTo("Espresso Basics"), "two actions on the same item both read as it");
            Assert.That(rule.Actions[3].TargetTitle, Is.Null);
        });
    }

    /// <summary>
    /// The attribute rows and the "Edit as text" box are the same string seen two ways, so whichever
    /// the marketer last touched is stored in one canonical form.
    /// </summary>
    [Test]
    public void AFilterExpressionIsStoredInItsCanonicalForm()
    {
        var stored = new RuleActionDto { Type = "filterResults", FilterExpression = "  Category :  Grinders , rubbish ,Tags:brewing" }.ToModel();

        Assert.That(stored, Is.EqualTo(new RuleAction.FilterResults("Category:Grinders, Tags:brewing")));
    }

    /// <summary>
    /// The builder and the storage speak the same action vocabulary; a discriminator renamed on
    /// one side only would store a rule the pipeline cannot read.
    /// </summary>
    [Test]
    public void TheWireDiscriminatorsAreTheStoredOnes()
    {
        foreach (string type in RuleActionDto.Types)
        {
            var model = new RuleActionDto { Type = type, TargetId = "doc-1", Word = "w", Replacement = "r", Query = "q", Url = "/u", FilterExpression = "a:b", Json = "{}" }.ToModel();

            using var stored = JsonDocument.Parse(RuleJson.Write([model!]));

            Assert.That(stored.RootElement[0].GetProperty("type").GetString(), Is.EqualTo(type));
        }
    }
}
