using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>
/// The type discriminators the consequence model carries (ADR-0022 addendum). They are the stored
/// contract of the Admin package's <c>RuleConsequences</c> column, so a consequence added without one
/// - or given a name that is already taken - would silently reinterpret rules that are already saved.
/// </summary>
[TestFixture]
internal sealed class RuleConsequenceDiscriminatorTests
{
    private static readonly JsonDerivedTypeAttribute[] Declared =
        [.. typeof(RuleConsequence).GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false).Cast<JsonDerivedTypeAttribute>()];

    [Test]
    public void EveryConsequenceHasOneAndTheDiscriminatorsAreUnique()
    {
        var nested = typeof(RuleConsequence).GetNestedTypes();

        Expect.Multiple(() =>
        {
            Assert.That(Declared.Select(attribute => attribute.DerivedType), Is.EquivalentTo(nested));
            Assert.That(
                Declared.Select(attribute => attribute.TypeDiscriminator).Distinct(),
                Has.Exactly(nested.Length).Items,
                "two consequences sharing a discriminator would read back as the wrong one");
        });
    }

    /// <summary>
    /// The names are spelled out rather than derived, so this is the list a stored rule depends on.
    /// Changing one is a storage migration, not a rename.
    /// </summary>
    [Test]
    public void TheDiscriminatorsAreTheDocumentedNames() =>
        Assert.That(
            Declared.Select(attribute => attribute.TypeDiscriminator),
            Is.EquivalentTo(new object[]
            {
                "pin", "hide", "boost", "bury", "filterResults", "removeWord", "replaceWord", "replaceQuery", "redirect", "customData"
            }));

    /// <summary>The derived member's own values have to survive alongside the discriminator.</summary>
    [Test]
    public void ARoundTripThroughTheDiscriminatorKeepsTheValues()
    {
        RuleConsequence pinned = new RuleConsequence.Pin("doc-1:en", 4);

        string written = JsonSerializer.Serialize(pinned, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Expect.Multiple(() =>
        {
            Assert.That(written, Does.StartWith("{\"type\":\"pin\""));
            Assert.That(JsonSerializer.Deserialize<RuleConsequence>(written, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Is.EqualTo(pinned));
        });
    }
}

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
