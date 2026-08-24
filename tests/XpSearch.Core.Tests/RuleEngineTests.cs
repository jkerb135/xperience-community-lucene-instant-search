using System.Text.Json;

using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>
/// The if/then rule engine of ADR-0022: which conditions fire a rule, and what its consequences do
/// to the query, the results and the response.
/// </summary>
[TestFixture]
internal sealed class RuleConditionTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A stand-in analyzer: lowercases, splits on spaces and drops a trailing "s".</summary>
    private static IReadOnlyList<string> Stem(string text) =>
    [
        .. text
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.EndsWith('s') ? word[..^1] : word)
    ];

    private static RuleMatchContext Match(
        string query,
        bool analyzed = false,
        (string Attribute, string Value)[]? filters = null,
        string[]? groups = null,
        string language = "")
    {
        var facets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (attribute, value) in filters ?? [])
        {
            facets[attribute] = new HashSet<string>([value], StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyList<IReadOnlySet<string>> positions = analyzed
            ?
            [
                .. query
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => (IReadOnlySet<string>)new HashSet<string>(Stem(word), StringComparer.Ordinal))
            ]
            : [];

        return new RuleMatchContext(
            query,
            positions,
            Stem,
            facets,
            groups is null ? ContactGroupSets.None : ContactGroupSets.Of(groups),
            language);
    }

    private static TuningRule Rule(RuleConditions conditions) =>
        new(1, "rule", true, 100, null, null, conditions, [new RuleConsequence.Redirect("/x")]);

    private static RuleConditions Conditions(
        QueryCondition? query = null,
        AttributeIs[]? filters = null,
        string group = "",
        string language = "") =>
        new(query, filters ?? [], group, language);

    private static bool Fires(RuleConditions conditions, RuleMatchContext match) =>
        RuleSelection.Active([Rule(conditions)], match, Now).Count == 1;

    [Test]
    public void QueryOperators_CompareTheRawQuery()
    {
        var context = Match("espresso machine");

        Expect.Multiple(() =>
        {
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Is, "espresso machine", false)), context), Is.True);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Is, "espresso", false)), context), Is.False);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Contains, "press", false)), context), Is.True);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Contains, "grinder", false)), context), Is.False);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.StartsWith, "ESPRESSO", false)), context), Is.True);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.StartsWith, "machine", false)), context), Is.False);
        });
    }

    /// <summary>
    /// The analyzed comparison is per position, so the pattern's terms have to line up with the
    /// query's - which is what makes a stem match and a substring of a word not match.
    /// </summary>
    [Test]
    public void AnalyzedMatching_ComparesTheAnalyzedTermsPositionByPosition()
    {
        var context = Match("espresso machines", analyzed: true);

        Expect.Multiple(() =>
        {
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Contains, "machine", true)), context), Is.True, "stemmed");
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Contains, "machine", false)), context), Is.True, "raw substring");
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Is, "espresso machine", true)), context), Is.True);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Is, "espresso", true)), context), Is.False);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.StartsWith, "espresso", true)), context), Is.True);
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.StartsWith, "machine", true)), context), Is.False);
            // "press" is inside the word, so the raw comparison finds it and the analyzed one does not:
            // there is no typo tolerance and no partial-term matching in either direction.
            Assert.That(Fires(Conditions(new QueryCondition(QueryOperator.Contains, "press", true)), context), Is.False);
        });
    }

    [Test]
    public void AnalyzedMatching_FallsBackToTheRawComparisonWhenNothingAnalyzedTheQuery() =>
        Assert.That(
            Fires(Conditions(new QueryCondition(QueryOperator.Contains, "press", true)), Match("espresso machine")),
            Is.True);

    [Test]
    public void FilterConditions_HoldWhenEveryPairIsSelectedOnTheRequest()
    {
        var context = Match("espresso", filters: [("category", "coffee"), ("tags", "brewing")]);

        Expect.Multiple(() =>
        {
            Assert.That(Fires(Conditions(filters: [new AttributeIs("category", "coffee")]), context), Is.True);
            Assert.That(
                Fires(Conditions(filters: [new AttributeIs("Category", "COFFEE")]), context),
                Is.True,
                "attribute and value are compared case-insensitively");
            Assert.That(
                Fires(Conditions(filters: [new AttributeIs("category", "coffee"), new AttributeIs("tags", "brewing")]), context),
                Is.True);
            Assert.That(
                Fires(Conditions(filters: [new AttributeIs("category", "coffee"), new AttributeIs("tags", "milk")]), context),
                Is.False,
                "every listed pair must hold");
            Assert.That(Fires(Conditions(filters: [new AttributeIs("price", "5")]), context), Is.False);
            Assert.That(Fires(Conditions(filters: [new AttributeIs("category", "coffee")]), Match("espresso")), Is.False);
        });
    }

    [Test]
    public void ContactGroupAndLanguage_ScopeTheRule()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Fires(Conditions(group: "vips"), Match("q", groups: ["vips"])), Is.True);
            Assert.That(Fires(Conditions(group: "vips"), Match("q", groups: ["others"])), Is.False);
            Assert.That(Fires(Conditions(group: "vips"), Match("q")), Is.False);
            Assert.That(Fires(Conditions(language: "en"), Match("q", language: "en")), Is.True);
            Assert.That(Fires(Conditions(language: "EN"), Match("q", language: "en")), Is.True);
            Assert.That(Fires(Conditions(language: "de"), Match("q", language: "en")), Is.False);
            Assert.That(Fires(Conditions(language: "de"), Match("q")), Is.False);
        });
    }

    [Test]
    public void EveryConditionMustHold()
    {
        var conditions = Conditions(
            new QueryCondition(QueryOperator.Contains, "espresso", false),
            [new AttributeIs("category", "coffee")],
            "vips",
            "en");

        Expect.Multiple(() =>
        {
            Assert.That(
                Fires(conditions, Match("espresso machine", filters: [("category", "coffee")], groups: ["vips"], language: "en")),
                Is.True);
            Assert.That(
                Fires(conditions, Match("grinder", filters: [("category", "coffee")], groups: ["vips"], language: "en")),
                Is.False,
                "the query condition fails");
            Assert.That(
                Fires(conditions, Match("espresso", filters: [("category", "equipment")], groups: ["vips"], language: "en")),
                Is.False,
                "the filter condition fails");
            Assert.That(
                Fires(conditions, Match("espresso", filters: [("category", "coffee")], language: "en")),
                Is.False,
                "the contact group condition fails");
            Assert.That(
                Fires(conditions, Match("espresso", filters: [("category", "coffee")], groups: ["vips"], language: "de")),
                Is.False,
                "the language condition fails");
        });
    }

    /// <summary>A rule with no <c>if</c> would fire on every search, so it is treated as unfinished.</summary>
    [Test]
    public void ConditionsThatSayNothing_NeverFire() =>
        Assert.That(Fires(Conditions(), Match("anything")), Is.False);
}

/// <summary>
/// The flat-storage shim: every legacy condition and consequence has to reach the if/then model
/// unchanged, because the Search tuning application still writes the flat columns until CR-4b.
/// </summary>
[TestFixture]
internal sealed class TuningRuleCompatTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static TuningRule Flat(
        FlatCondition condition = FlatCondition.Contains,
        string pattern = "espresso",
        FlatConsequence consequence = FlatConsequence.Boost,
        string targetId = "doc-1",
        int position = 3,
        double boost = 2.5,
        string filter = "Category:coffee",
        string redirect = "/promo",
        string group = "vips") =>
        TuningRuleCompat.FromFlat(
            7, "legacy", true, condition, pattern, consequence, targetId, position, boost, filter, redirect,
            Now.AddDays(-1), Now.AddDays(1), 42, group);

    [Test]
    public void EveryConsequenceTypeIsMapped()
    {
        Expect.Multiple(() =>
        {
            Assert.That(
                Flat(consequence: FlatConsequence.Pin).Consequences[0],
                Is.EqualTo(new RuleConsequence.Pin("doc-1", 3)));
            Assert.That(
                Flat(consequence: FlatConsequence.Bury).Consequences[0],
                Is.EqualTo(new RuleConsequence.Bury("doc-1", string.Empty)));
            Assert.That(
                Flat(consequence: FlatConsequence.Boost).Consequences[0],
                Is.EqualTo(new RuleConsequence.Boost("doc-1", "Category:coffee", 2.5)));
            Assert.That(
                Flat(consequence: FlatConsequence.Filter).Consequences[0],
                Is.EqualTo(new RuleConsequence.FilterResults("Category:coffee")));
            Assert.That(
                Flat(consequence: FlatConsequence.Redirect).Consequences[0],
                Is.EqualTo(new RuleConsequence.Redirect("/promo")));
        });
    }

    [Test]
    public void EveryConditionTypeIsMapped()
    {
        Expect.Multiple(() =>
        {
            Assert.That(
                Flat(condition: FlatCondition.Contains).Conditions.Query,
                Is.EqualTo(new QueryCondition(QueryOperator.Contains, "espresso", false)));
            Assert.That(
                Flat(condition: FlatCondition.Exact).Conditions.Query,
                Is.EqualTo(new QueryCondition(QueryOperator.Is, "espresso", false)));
            Assert.That(
                Flat(condition: FlatCondition.StartsWith).Conditions.Query,
                Is.EqualTo(new QueryCondition(QueryOperator.StartsWith, "espresso", false)));
            Assert.That(
                Flat(condition: FlatCondition.Always, pattern: string.Empty).Conditions.Query,
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
        var anything = TuningRuleCompat.FromFlat(
            1, "always", true, FlatCondition.Always, string.Empty, FlatConsequence.Boost, "doc-1", 0, 2,
            string.Empty, string.Empty, null, null, 100, string.Empty);

        var blank = TuningRuleCompat.FromFlat(
            2, "blank", true, FlatCondition.Contains, "   ", FlatConsequence.Boost, "doc-1", 0, 2,
            string.Empty, string.Empty, null, null, 100, string.Empty);

        Expect.Multiple(() =>
        {
            Assert.That(RuleSelection.Active([anything], "anything at all", Now), Has.Count.EqualTo(1));
            Assert.That(RuleSelection.Active([anything], string.Empty, Now), Has.Count.EqualTo(1));
            Assert.That(RuleSelection.Active([blank], "anything at all", Now), Is.Empty);
        });
    }
}

/// <summary>
/// The consequences over the real Lucene fixture: query rewrites, hide, several consequences on one
/// rule and the data a rule attaches to the response.
/// </summary>
[TestFixture]
internal sealed class RuleConsequencePipelineTests
{
    private static SearchRequest Request(string query, bool explain = false) =>
        new() { Index = TestCorpus.IndexName, Query = query, PageSize = 10, Explain = explain };

    private static TuningRule Rule(int id, string pattern, params RuleConsequence[] consequences) =>
        new(
            id,
            $"rule-{id}",
            true,
            100,
            null,
            null,
            new RuleConditions(new QueryCondition(QueryOperator.Contains, pattern, false), [], string.Empty, string.Empty),
            consequences);

    /// <summary>Captures the query as the stages that build the Lucene query see it.</summary>
    private sealed class CaptureQueryStage : ISearchStage
    {
        public string QueryText { get; private set; } = string.Empty;

        public IReadOnlyList<IReadOnlyList<string>> Slots { get; private set; } = [];

        public int Order => SearchStageOrder.BuildQuery - 1;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            QueryText = context.QueryText;
            Slots = context.QuerySlots;

            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Rewrites_AreAppliedInRuleOrderAndThenListedOrder()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                Rule(1, "cheap", new RuleConsequence.RemoveWord("cheap"), new RuleConsequence.ReplaceWord("machine", "grinder")),
                Rule(2, "espresso", new RuleConsequence.ReplaceWord("grinder", "mill"))
            ]
        };

        var capture = new CaptureQueryStage();
        using var harness = new TestHarness(tuning: source, extraStages: capture);

        await harness.Search(Request("cheap espresso machine"));

        Assert.That(capture.QueryText, Is.EqualTo("espresso mill"));
    }

    [Test]
    public async Task ReplaceQuery_ReplacesEverythingAndSynonymsExpandTheResult()
    {
        var source = new FakeTuningSource
        {
            Rules = [Rule(1, "beans", new RuleConsequence.ReplaceQuery("grinder"))],
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["grinder", "mill"], [])]
        };

        var capture = new CaptureQueryStage();
        using var harness = new TestHarness(tuning: source, extraStages: capture);

        var response = await harness.Search(Request("coffee beans"));

        Expect.Multiple(() =>
        {
            Assert.That(capture.QueryText, Is.EqualTo("grinder"), "the rewritten text is what the search runs");
            Assert.That(
                capture.Slots.SelectMany(slot => slot),
                Does.Contain("mill"),
                "synonyms are expanded from the rewritten query, not the original");
            Assert.That(response.Results.Select(result => result.Id), Does.Contain("doc-4:en"));
        });
    }

    [Test]
    public async Task Hide_TakesTheDocumentOutOfTheResultsAndOutOfTheTotal()
    {
        var source = new FakeTuningSource { Rules = [Rule(1, "espresso", new RuleConsequence.Hide("doc-1:en"))] };

        using var harness = new TestHarness(tuning: source);
        using var untuned = new TestHarness();

        var response = await harness.Search(Request("espresso"));
        var plain = await untuned.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(plain.Results.Select(result => result.Id), Does.Contain("doc-1:en"));
            Assert.That(response.Results.Select(result => result.Id), Does.Not.Contain("doc-1:en"));
            Assert.That(response.Total, Is.EqualTo(plain.Total - 1));
        });
    }

    [Test]
    public async Task AHiddenDocumentCannotBePinnedBackIn()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                Rule(1, "espresso", new RuleConsequence.Hide("doc-4:en"), new RuleConsequence.Pin("doc-4:en", 1))
            ]
        };

        using var harness = new TestHarness(tuning: source);

        var response = await harness.Search(Request("espresso"));

        Assert.That(response.Results.Select(result => result.Id), Does.Not.Contain("doc-4:en"));
    }

    [Test]
    public async Task OneRuleAppliesEveryConsequenceItLists()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                Rule(
                    1,
                    "espresso",
                    new RuleConsequence.Pin("doc-5:en", 1),
                    new RuleConsequence.Bury("doc-1:en", string.Empty),
                    new RuleConsequence.CustomData("""{"banner":"espresso-week"}"""))
            ]
        };

        using var harness = new TestHarness(tuning: source);

        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Results[0].Id, Is.EqualTo("doc-5:en"));
            Assert.That(response.Results.Select(result => result.Id), Does.Not.Contain("doc-1:en"));
            Assert.That(((JsonElement)response.RuleData!["banner"]).GetString(), Is.EqualTo("espresso-week"));
        });
    }

    [Test]
    public async Task CustomData_IsShallowMergedInRuleOrderAndBadJsonIsSkipped()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                Rule(1, "espresso", new RuleConsequence.CustomData("""{"banner":"first","layout":"grid"}""")),
                Rule(2, "espresso", new RuleConsequence.CustomData("""{"banner":"second"}""")),
                Rule(3, "espresso", new RuleConsequence.CustomData("not json at all")),
                Rule(4, "espresso", new RuleConsequence.CustomData("[1,2,3]"))
            ]
        };

        using var harness = new TestHarness(tuning: source);

        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.RuleData!.Keys, Is.EquivalentTo(new[] { "banner", "layout" }));
            Assert.That(((JsonElement)response.RuleData["banner"]).GetString(), Is.EqualTo("second"), "a later rule wins the key");
            Assert.That(((JsonElement)response.RuleData["layout"]).GetString(), Is.EqualTo("grid"));
        });
    }

    [Test]
    public async Task RuleData_IsAbsentWhenNoRuleReturnsAny()
    {
        using var harness = new TestHarness();

        var response = await harness.Search(Request("espresso"));

        Assert.That(response.RuleData, Is.Null);
    }
}
