using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>A tuning source that serves whatever a test hands it (spec §8.3).</summary>
internal sealed class FakeTuningSource : IRelevanceTuningSource
{
    internal IReadOnlyList<TuningRule> Rules { get; set; } = [];

    internal IReadOnlyList<TuningSynonym> Synonyms { get; set; } = [];

    internal IReadOnlyList<string> Stopwords { get; set; } = [];

    internal IReadOnlyList<FieldWeight> Weights { get; set; } = [];

    public Task<IReadOnlyList<TuningRule>> GetRulesAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(Rules);

    public Task<IReadOnlyList<TuningSynonym>> GetSynonymsAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(Synonyms);

    public Task<IReadOnlyList<string>> GetStopwordsAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(Stopwords);

    public Task<IReadOnlyList<FieldWeight>> GetFieldWeightsAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(Weights);
}

/// <summary>
/// Rule matching, scheduling and precedence, and synonym expansion - the pure parts of spec §8.3.
/// </summary>
[TestFixture]
internal sealed class RuleSelectionTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    internal static TuningRule Rule(
        int id = 1,
        string name = "rule",
        bool enabled = true,
        RuleCondition condition = RuleCondition.Contains,
        string pattern = "espresso",
        RuleConsequence consequence = RuleConsequence.Boost,
        string targetId = "",
        int targetPosition = 0,
        double boost = 2,
        string filter = "",
        string redirectUrl = "",
        DateTime? from = null,
        DateTime? to = null,
        int priority = 100) =>
        new(id, name, enabled, condition, pattern, consequence, targetId, targetPosition, boost, filter, redirectUrl, from, to, priority);

    [Test]
    public void Matching_HonoursEveryConditionType()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.Contains, pattern: "press"), "espresso machine"), Is.True);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.Exact, pattern: "espresso"), "espresso machine"), Is.False);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.Exact, pattern: "Espresso"), "espresso"), Is.True);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.StartsWith, pattern: "espresso"), "espresso machine"), Is.True);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.StartsWith, pattern: "machine"), "espresso machine"), Is.False);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.Always, pattern: ""), "anything"), Is.True);
            Assert.That(RuleSelection.Matches(Rule(condition: RuleCondition.Contains, pattern: "  "), "anything"), Is.False);
        });
    }

    [Test]
    public void Scheduling_ExcludesRulesOutsideTheirWindow()
    {
        var future = Rule(id: 1, from: Now.AddDays(1));
        var past = Rule(id: 2, to: Now.AddDays(-1));
        var live = Rule(id: 3, from: Now.AddDays(-1), to: Now.AddDays(1));

        var active = RuleSelection.Active([future, past, live], "espresso", Now);

        Assert.That(active.Select(rule => rule.Id), Is.EqualTo(new[] { 3 }).AsCollection);
    }

    [Test]
    public void Precedence_IsPriorityThenId()
    {
        var a = Rule(id: 9, priority: 10);
        var b = Rule(id: 2, priority: 50);
        var c = Rule(id: 1, priority: 50);

        var active = RuleSelection.Active([b, a, c], "espresso", Now);

        Assert.That(active.Select(rule => rule.Id), Is.EqualTo(new[] { 9, 1, 2 }).AsCollection);
    }

    [Test]
    public void DisabledRules_NeverApply() =>
        Assert.That(RuleSelection.Active([Rule(enabled: false)], "espresso", Now), Is.Empty);

    [Test]
    public void TwoWaySynonyms_ExpandEveryTermIntoEveryOther()
    {
        var slots = SynonymExpansion.Expand(
            "red sofa",
            [new TuningSynonym(SynonymDirection.TwoWay, ["sofa", "couch", "settee"], [])]);

        Expect.Multiple(() =>
        {
            Assert.That(slots, Has.Count.EqualTo(2));
            Assert.That(slots[0], Is.EqualTo(new[] { "red" }).AsCollection);
            Assert.That(slots[1], Is.EqualTo(new[] { "sofa", "couch", "settee" }).AsCollection);
        });
    }

    [Test]
    public void OneWaySynonyms_ExpandOnlyFromInputToOutput()
    {
        var synonyms = new[] { new TuningSynonym(SynonymDirection.OneWay, ["laptop"], ["notebook"]) };

        Expect.Multiple(() =>
        {
            Assert.That(SynonymExpansion.Expand("laptop", synonyms)[0], Is.EqualTo(new[] { "laptop", "notebook" }).AsCollection);
            Assert.That(SynonymExpansion.Expand("notebook", synonyms), Is.Empty);
        });
    }

    [Test]
    public void MultiWordSynonyms_MatchTheLongestPhraseFirst()
    {
        var slots = SynonymExpansion.Expand(
            "cheap sofa bed",
            [
                new TuningSynonym(SynonymDirection.TwoWay, ["sofa", "couch"], []),
                new TuningSynonym(SynonymDirection.TwoWay, ["sofa bed", "futon"], [])
            ]);

        Expect.Multiple(() =>
        {
            Assert.That(slots, Has.Count.EqualTo(2));
            Assert.That(slots[1], Is.EqualTo(new[] { "sofa bed", "futon" }).AsCollection);
        });
    }

    [Test]
    public void NoApplicableSynonym_LeavesTheQueryUnexpanded() =>
        Assert.That(
            SynonymExpansion.Expand("espresso", [new TuningSynonym(SynonymDirection.TwoWay, ["sofa", "couch"], [])]),
            Is.Empty);

    [Test]
    public void SplitTerms_TrimsLowercasesAndDeduplicates() =>
        Assert.That(
            SynonymExpansion.SplitTerms(" Sofa , couch,, SOFA "),
            Is.EqualTo(new[] { "sofa", "couch" }).AsCollection);

    [Test]
    public void FilterExpression_ParsesFieldValuePairsAndDropsRubbish() =>
        Assert.That(
            RuleFilterExpression.Parse("Category:coffee, Tags:brewing, nonsense, :empty, trailing:"),
            Is.EqualTo(new[] { ("Category", "coffee"), ("Tags", "brewing") }).AsCollection);
}

/// <summary>
/// The tuning stages over the real Lucene fixture: boost, filter, pin, bury and synonym expansion
/// as they actually affect ranking (spec §8.3).
/// </summary>
[TestFixture]
internal sealed class TuningPipelineTests
{
    private static TestHarness Harness(FakeTuningSource source) => new(tuning: source);

    private static SearchRequest Request(string query, bool explain = false) =>
        new() { Index = TestCorpus.IndexName, Query = query, PageSize = 5, Explain = explain };

    [Test]
    public async Task Pin_MovesADocumentToItsPosition()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Pin, targetId: "doc-5:en", targetPosition: 1)]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Assert.That(response.Results[0].Id, Is.EqualTo("doc-5:en"));
    }

    [Test]
    public async Task Pin_InjectsADocumentTheQueryDidNotMatch()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Pin, targetId: "doc-4:en", targetPosition: 1)]
        };

        using var harness = Harness(source);

        var withoutRule = await new TestHarness().Search(Request("espresso"));
        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(withoutRule.Results.Select(result => result.Id), Does.Not.Contain("doc-4:en"));
            Assert.That(response.Results[0].Id, Is.EqualTo("doc-4:en"));
            Assert.That(response.Total, Is.EqualTo(withoutRule.Total + 1));
        });
    }

    [Test]
    public async Task Pin_DoesNotInjectADocumentThatFailsTheActiveFilters()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Pin, targetId: "doc-4:en", targetPosition: 1)]
        };

        using var harness = Harness(source);

        // doc-4 is an "equipment" product; the request refines to "coffee", so the pin must not fire.
        var response = await harness.Search(new SearchRequest
        {
            Index = TestCorpus.IndexName,
            Query = "espresso",
            PageSize = 5,
            Filters = new Filters
            {
                Facets = [new FacetFilter { Attribute = TestCorpus.CategoryField, Values = ["coffee"] }]
            }
        });

        Assert.That(response.Results.Select(result => result.Id), Does.Not.Contain("doc-4:en"));
    }

    [Test]
    public async Task Pin_LeavesPagesThatDoNotContainTheTargetPositionAlone()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Pin, targetId: "doc-5:en", targetPosition: 1)]
        };

        using var harness = Harness(source);

        var response = await harness.Search(new SearchRequest
        {
            Index = TestCorpus.IndexName,
            Query = "espresso",
            Page = 2,
            PageSize = 1
        });

        Assert.That(response.Results[0].Id, Is.Not.EqualTo("doc-5:en"));
    }

    [Test]
    public async Task Bury_RemovesADocumentAndDecrementsTheTotal()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Bury, targetId: "doc-1:en")]
        };

        using var harness = Harness(source);

        var withoutRule = await new TestHarness().Search(Request("espresso"));
        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(withoutRule.Results.Select(result => result.Id), Does.Contain("doc-1:en"));
            Assert.That(response.Results.Select(result => result.Id), Does.Not.Contain("doc-1:en"));
            Assert.That(response.Total, Is.EqualTo(withoutRule.Total - 1));
        });
    }

    [Test]
    public async Task Boost_RaisesItsTargetAboveTheRest()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(consequence: RuleConsequence.Boost, targetId: "doc-3:en", boost: 100)]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Assert.That(response.Results[0].Id, Is.EqualTo("doc-3:en"));
    }

    [Test]
    public async Task Filter_RestrictsTheResultSet()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(
                    consequence: RuleConsequence.Filter,
                    filter: $"{TestCorpus.CategoryField}:equipment")
            ]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Results, Is.Not.Empty);
            Assert.That(response.Results.Select(result => result.Id), Is.All.EqualTo("doc-3:en"));
        });
    }

    /// <summary>
    /// A rule is written against the attribute names a request uses, which for the base fields are
    /// not the Lucene field names the documents carry.
    /// </summary>
    [Test]
    public async Task Filter_ResolvesABaseAttributeToTheFieldTheDocumentsCarry()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(
                    consequence: RuleConsequence.Filter,
                    filter: $"{TestCorpus.ContentTypeField}:Product")
            ]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Results, Is.Not.Empty, "'contentType' must reach the ContentTypeName field");
            Assert.That(response.Results.Select(result => result.Id), Is.All.EqualTo("doc-3:en"));
        });
    }

    [Test]
    public async Task Redirect_IsSurfacedOnTheResponseNextToTheResults()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(
                    name: "Espresso landing page",
                    consequence: RuleConsequence.Redirect,
                    redirectUrl: "  /promotions/espresso  ")
            ]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Redirect, Is.Not.Null);
            Assert.That(response.Redirect!.Url, Is.EqualTo("/promotions/espresso"));
            Assert.That(response.Redirect.Rule, Is.EqualTo("Espresso landing page"));
            Assert.That(response.Results, Is.Not.Empty, "a redirect does not replace the results; the client decides");
        });
    }

    [Test]
    public async Task Redirect_IsNullWhenNoRuleApplies()
    {
        var never = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(id: 1, enabled: false, consequence: RuleConsequence.Redirect, redirectUrl: "/disabled"),
                RuleSelectionTests.Rule(id: 2, consequence: RuleConsequence.Redirect, redirectUrl: "/expired", to: never),
                RuleSelectionTests.Rule(id: 3, pattern: "decaf", consequence: RuleConsequence.Redirect, redirectUrl: "/other-query"),
                RuleSelectionTests.Rule(id: 4, consequence: RuleConsequence.Redirect, redirectUrl: "   ")
            ]
        };

        using var harness = Harness(source);
        using var untuned = new TestHarness();

        var response = await harness.Search(Request("espresso"));
        var plain = await untuned.Search(Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Redirect, Is.Null);
            Assert.That(plain.Redirect, Is.Null);
        });
    }

    [Test]
    public async Task Redirect_TakesTheFirstRuleInPrecedenceOrder()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(id: 7, name: "Late", consequence: RuleConsequence.Redirect, redirectUrl: "/late", priority: 200),
                RuleSelectionTests.Rule(id: 9, name: "Early", consequence: RuleConsequence.Redirect, redirectUrl: "/early", priority: 100),
                RuleSelectionTests.Rule(id: 2, name: "Same priority, lower id", consequence: RuleConsequence.Redirect, redirectUrl: "/tie-break", priority: 100)
            ]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso"));

        Assert.That(response.Redirect!.Url, Is.EqualTo("/tie-break"));
    }

    [Test]
    public async Task Redirect_IsExplainedLikeAnyOtherRule()
    {
        var source = new FakeTuningSource
        {
            Rules =
            [
                RuleSelectionTests.Rule(name: "Support redirect", consequence: RuleConsequence.Redirect, redirectUrl: "/support")
            ]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso", explain: true));

        Assert.That(response.Results[0].Ranking!.Boosts!, Does.Contain("rule:Support redirect"));
    }

    [Test]
    public async Task Synonyms_ExpandTheQueryIntoTheOtherTerm()
    {
        var source = new FakeTuningSource
        {
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["grinder", "mill"], [])]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("mill"));

        Assert.That(response.Results.Select(result => result.Id), Does.Contain("doc-4:en"));
    }

    [Test]
    public async Task Stopwords_AreDroppedFromTheQuery()
    {
        var source = new FakeTuningSource { Stopwords = ["the", "a"] };

        using var harness = Harness(source);

        var withStopword = await harness.Search(Request("the espresso"));
        var without = await harness.Search(Request("espresso"));

        Assert.That(withStopword.Total, Is.EqualTo(without.Total));
    }

    [Test]
    public async Task Explain_ListsWeightsSynonymsAndRules()
    {
        var source = new FakeTuningSource
        {
            Rules = [RuleSelectionTests.Rule(name: "Promote machines", consequence: RuleConsequence.Boost, targetId: "doc-3:en", boost: 5)],
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["espresso", "coffee"], [])],
            Weights = [new FieldWeight(TestCorpus.BodyField, 2.5)]
        };

        using var harness = Harness(source);

        var response = await harness.Search(Request("espresso", explain: true));
        string[] boosts = response.Results[0].Ranking!.Boosts!;

        Expect.Multiple(() =>
        {
            Assert.That(boosts, Does.Contain("synonym:coffee"));
            Assert.That(boosts, Does.Contain("weight:Body×2.5"));
            Assert.That(boosts, Does.Contain("rule:Promote machines"));
        });
    }

    [Test]
    public async Task NoTuning_LeavesRankingExactlyAsItWas()
    {
        using var tuned = Harness(new FakeTuningSource());
        using var plain = new TestHarness();

        var a = await tuned.Search(Request("espresso"));
        var b = await plain.Search(Request("espresso"));

        Assert.That(
            a.Results.Select(result => result.Id),
            Is.EqualTo(b.Results.Select(result => result.Id)).AsCollection);
    }
}
