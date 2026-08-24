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
/// every consequence type survives a round trip, and that a column a hand edit broke cannot take the
/// index's tuning down with it.
/// </summary>
[TestFixture]
internal sealed class RuleJsonTests
{
    private static readonly RuleConsequence[] EveryType =
    [
        new RuleConsequence.Pin("doc-1:en", 3),
        new RuleConsequence.Hide("doc-2:en"),
        new RuleConsequence.Boost("doc-3:en", "Category:coffee", 2.5),
        new RuleConsequence.Bury("doc-4:en", "Category:tea"),
        new RuleConsequence.FilterResults("Category:coffee, Tags:brewing"),
        new RuleConsequence.RemoveWord("cheap"),
        new RuleConsequence.ReplaceWord("mill", "grinder"),
        new RuleConsequence.ReplaceQuery("hand grinder"),
        new RuleConsequence.Redirect("/campaigns/grinder-week"),
        new RuleConsequence.CustomData("{\"banner\":\"Grinder week\"}")
    ];

    /// <summary>
    /// Every consequence the model has must survive storage. A type added to
    /// <see cref="RuleConsequence"/> without a <c>JsonDerivedType</c> throws on write, which this
    /// catches; one added without a case here would go untested, so the count is asserted too.
    /// </summary>
    [Test]
    public void EveryConsequenceTypeRoundTrips()
    {
        int declared = typeof(RuleConsequence).GetNestedTypes().Length;

        var read = RuleJson.ReadConsequences(RuleJson.Write(EveryType));

        Expect.Multiple(() =>
        {
            Assert.That(EveryType, Has.Length.EqualTo(declared), "a new consequence type needs a case in this test");
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

        string consequences = RuleJson.Write([new RuleConsequence.Pin("doc-1:en", 1)]);

        Expect.Multiple(() =>
        {
            Assert.That(
                conditions,
                Is.EqualTo("{\"query\":{\"operator\":\"contains\",\"pattern\":\"grinder\",\"matchAnalyzed\":true},\"filters\":[{\"attribute\":\"Category\",\"value\":\"Grinders\"}],\"contactGroup\":\"CoffeeGrinders\",\"language\":\"en\"}"));
            Assert.That(consequences, Is.EqualTo("[{\"type\":\"pin\",\"targetId\":\"doc-1:en\",\"position\":1}]"));
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
            Assert.That(RuleJson.ReadConsequences("[{\"type\":\"nope\"}]"), Is.Empty);
            Assert.That(RuleJson.ReadConsequences("not json"), Is.Empty);
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

        var read = RuleJson.ReadConsequences(RuleJson.Write([new RuleConsequence.CustomData(authored)]));

        Assert.That(((RuleConsequence.CustomData)read[0]).Json, Is.EqualTo(authored), "the author's formatting is theirs");
    }
}

/// <summary>
/// The one-time conversion of the flat rule columns into the JSON ones (unit CR-4b): every legacy
/// condition and consequence has to arrive unchanged, the pass has to be repeatable, and it has to
/// be safe to interrupt.
/// </summary>
[TestFixture]
internal sealed class RuleStorageMigrationTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static TuningRule Flat(
        LegacyCondition condition = LegacyCondition.Contains,
        string pattern = "espresso",
        LegacyConsequence consequence = LegacyConsequence.Boost,
        string targetId = "doc-1",
        int position = 3,
        double boost = 2.5,
        string filter = "Category:coffee",
        string redirect = "/promo",
        string group = "vips") =>
        RuleStorageMigration.FromFlat(
            7, "legacy", true, condition, pattern, consequence, targetId, position, boost, filter, redirect,
            Now.AddDays(-1), Now.AddDays(1), 42, group);

    [Test]
    public void EveryConsequenceTypeIsMapped()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Flat(consequence: LegacyConsequence.Pin).Consequences[0], Is.EqualTo(new RuleConsequence.Pin("doc-1", 3)));
            Assert.That(Flat(consequence: LegacyConsequence.Bury).Consequences[0], Is.EqualTo(new RuleConsequence.Bury("doc-1", string.Empty)));
            Assert.That(Flat(consequence: LegacyConsequence.Boost).Consequences[0], Is.EqualTo(new RuleConsequence.Boost("doc-1", "Category:coffee", 2.5)));
            Assert.That(Flat(consequence: LegacyConsequence.Filter).Consequences[0], Is.EqualTo(new RuleConsequence.FilterResults("Category:coffee")));
            Assert.That(Flat(consequence: LegacyConsequence.Redirect).Consequences[0], Is.EqualTo(new RuleConsequence.Redirect("/promo")));
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
            Assert.That(rule.Consequences, Has.Count.EqualTo(1));
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
        foreach (var consequence in Enum.GetValues<LegacyConsequence>())
        {
            foreach (var condition in Enum.GetValues<LegacyCondition>())
            {
                var expected = Flat(condition, consequence: consequence);
                string because = $"{condition} / {consequence}";

                SameConditions.Assert(RuleJson.ReadConditions(RuleJson.Write(expected.Conditions)), expected.Conditions, because);
                Assert.That(
                    RuleJson.ReadConsequences(RuleJson.Write(expected.Consequences)),
                    Is.EqualTo(expected.Consequences).AsCollection,
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

    /// <summary>Every flat column has to be named for retirement, or the class keeps a NOT NULL trap.</summary>
    [Test]
    public void TheRetiredColumnsAreTheOnesTheOldFormDefined()
    {
        var current = XpSearchTuningModuleInstaller.RuleForm().GetFields(true, true).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(RuleStorageMigration.LegacyColumns, Has.Count.EqualTo(9));
            Assert.That(
                RuleStorageMigration.LegacyColumns.Where(current.Contains),
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
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleConsequences)));
            Assert.That(names, Does.Contain(nameof(XpSearchRuleInfo.RuleMigrated)));
            Assert.That(names, Does.Contain("RulePattern"), "CombineWithForm only ever adds - the removal is explicit");
        });
    }

    /// <summary>The rule class as it shipped before the if/then storage: the flat columns of ADR-0014.</summary>
    private static FormInfo OldRuleForm()
    {
        var form = XpSearchTuningModuleInstaller.RuleForm();

        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleConditions));
        form.RemoveFormField(nameof(XpSearchRuleInfo.RuleConsequences));
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

    private static IReadOnlyList<string> Fields(string? name, RuleConditions? conditions, params RuleConsequence[] consequences) =>
        [.. RuleValidation.Validate(name, conditions, consequences).Select(error => error.Field)];

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

            Assert.That(Fields("r", Something, new RuleConsequence.Pin(string.Empty, 1)), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.Pin("doc-1", 0)), Does.Contain("consequence:0"), "position counts from 1");
            Assert.That(Fields("r", Something, new RuleConsequence.Pin("doc-1", 1)), Is.Empty);

            Assert.That(Fields("r", Something, new RuleConsequence.Hide(" ")), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.Bury(" ", string.Empty)), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.Boost(string.Empty, string.Empty, 2)), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.Boost("doc-1", string.Empty, 0)), Does.Contain("consequence:0"), "a multiplier of 0 switches the rule off");
            Assert.That(Fields("r", Something, new RuleConsequence.Boost(string.Empty, "Category:coffee", 2)), Is.Empty, "an expression is a target too");
            Assert.That(Fields("r", Something, new RuleConsequence.FilterResults(" ")), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.RemoveWord(" ")), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.ReplaceWord("mill", " ")), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.ReplaceQuery(" ")), Does.Contain("consequence:0"));
            Assert.That(Fields("r", Something, new RuleConsequence.Redirect(" ")), Does.Contain("consequence:0"));

            Assert.That(Fields("r", Something, new RuleConsequence.CustomData("{ \"banner\": ")), Does.Contain("consequence:0"), "invalid JSON blocks save");
            Assert.That(Fields("r", Something, new RuleConsequence.CustomData("[1]")), Does.Contain("consequence:0"), "an array is not an object");
            Assert.That(Fields("r", Something, new RuleConsequence.CustomData("{\"banner\":\"x\"}")), Is.Empty);
        });
    }

    /// <summary>Errors are addressed to the card that has to change, so the second card is not blamed.</summary>
    [Test]
    public void ErrorsPointAtTheCardTheyBelongTo()
    {
        var errors = RuleValidation.Validate(
            "r",
            Something,
            [new RuleConsequence.Pin("doc-1", 1), new RuleConsequence.Redirect(string.Empty)]);

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
    public void EveryConsequenceReadsAsASentence()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleSummary.Describe(new RuleConsequence.Pin("doc-1", 1)), Is.EqualTo("Pin doc-1 to position 1"));
            Assert.That(RuleSummary.Describe(new RuleConsequence.Hide("doc-1")), Is.EqualTo("Hide doc-1"));
            Assert.That(RuleSummary.Describe(new RuleConsequence.Boost("doc-1", string.Empty, 2)), Is.EqualTo("Boost doc-1 ×2"));
            Assert.That(RuleSummary.Describe(new RuleConsequence.Boost(string.Empty, "Category:coffee", 1.5)), Is.EqualTo("Boost Category:coffee ×1.5"));
            Assert.That(RuleSummary.Describe(new RuleConsequence.RemoveWord("cheap")), Is.EqualTo("Remove the word “cheap”"));
            Assert.That(RuleSummary.Describe(new RuleConsequence.CustomData("{}")), Is.EqualTo("Return custom data"));
            Assert.That(RuleSummary.Describe((IReadOnlyList<RuleConsequence>)[]), Is.EqualTo("Nothing"));
        });
    }
}

/// <summary>What the builder sends and receives, and the rule a zero-result row seeds it with.</summary>
[TestFixture]
internal sealed class RuleBuilderModelTests
{
    /// <summary>
    /// The flat consequence shape the client edits has to carry every type both ways, or a card would
    /// silently save as something else.
    /// </summary>
    [Test]
    public void EveryConsequenceTypeSurvivesTheWireShape()
    {
        RuleConsequence[] every =
        [
            new RuleConsequence.Pin("doc-1", 2),
            new RuleConsequence.Hide("doc-2"),
            new RuleConsequence.Boost("doc-3", "Category:coffee", 3),
            new RuleConsequence.Bury("doc-4", "Category:tea"),
            new RuleConsequence.FilterResults("Category:coffee"),
            new RuleConsequence.RemoveWord("cheap"),
            new RuleConsequence.ReplaceWord("mill", "grinder"),
            new RuleConsequence.ReplaceQuery("hand grinder"),
            new RuleConsequence.Redirect("/promo"),
            new RuleConsequence.CustomData("{\"a\":1}")
        ];

        Expect.Multiple(() =>
        {
            Assert.That(RuleConsequenceDto.Types, Is.EquivalentTo(every.Select(c => RuleConsequenceDto.From(c).Type)));
            Assert.That(every.Select(c => RuleConsequenceDto.From(c).ToModel()).ToList(), Is.EqualTo(every).AsCollection);
            Assert.That(new RuleConsequenceDto { Type = "invented" }.ToModel(), Is.Null, "an unknown type is dropped, not guessed");
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
            [new RuleConsequence.Pin("doc-1", 1)]);

        var dto = RuleDto.From(rule);
        (var conditions, var consequences) = dto.ToModel();

        Expect.Multiple(() =>
        {
            Assert.That(dto.ValidFrom, Is.EqualTo("2026-01-02"));
            Assert.That(dto.ValidTo, Is.Empty);
            Assert.That(RuleDto.Moment(dto.ValidFrom), Is.EqualTo(rule.ValidFrom));
            Assert.That(RuleDto.Moment("not a date"), Is.Null);
            SameConditions.Assert(conditions, rule.Conditions);
            Assert.That(consequences, Is.EqualTo(rule.Consequences).AsCollection);
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
    /// only has to choose the consequence (spec §9.3).
    /// </summary>
    [Test]
    public void TheSeededRuleMatchesTheQueryAndDoesNothingYet()
    {
        var seeded = ZeroResultRuleCreatePage.SeedFor("yirgacheffe");
        (var conditions, var consequences) = seeded.ToModel();

        Expect.Multiple(() =>
        {
            Assert.That(seeded.Name, Is.EqualTo("Rule for 'yirgacheffe'"));
            Assert.That(conditions.Query, Is.EqualTo(new QueryCondition(QueryOperator.Contains, "yirgacheffe", false)));
            Assert.That(consequences, Is.Empty);
            Assert.That(seeded.Enabled, Is.True);
            Assert.That(RuleValidation.Validate(seeded.Name, conditions, consequences), Is.Empty, "the seeded rule is savable as it stands");
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
    /// The builder and the storage speak the same consequence vocabulary; a discriminator renamed on
    /// one side only would store a rule the pipeline cannot read.
    /// </summary>
    [Test]
    public void TheWireDiscriminatorsAreTheStoredOnes()
    {
        foreach (string type in RuleConsequenceDto.Types)
        {
            var model = new RuleConsequenceDto { Type = type, TargetId = "doc-1", Word = "w", Replacement = "r", Query = "q", Url = "/u", FilterExpression = "a:b", Json = "{}" }.ToModel();

            using var stored = JsonDocument.Parse(RuleJson.Write([model!]));

            Assert.That(stored.RootElement[0].GetProperty("type").GetString(), Is.EqualTo(type));
        }
    }
}
