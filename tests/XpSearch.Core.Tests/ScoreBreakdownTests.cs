using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>
/// The per-stage score breakdown of QT-2: the checkpoints the scoring stages leave, the steps
/// <c>ScoreBreakdownStage</c> explains out of them, and the rules recorded against a document.
/// </summary>
[TestFixture]
internal sealed class ScoreBreakdownTests
{
    private const string Boosted = "doc-3:en";

    /// <summary>Keeps the finished context, which carries what the response does not expose yet.</summary>
    private sealed class CaptureStage : ISearchStage
    {
        internal SearchContext? Context { get; private set; }

        public int Order => SearchStageOrder.LogActivity;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            Context = context;

            return Task.CompletedTask;
        }
    }

    private static SearchRequest Request(bool explain = true) =>
        new() { Index = TestCorpus.IndexName, Query = "espresso", PageSize = 5, Explain = explain };

    private static TuningRule BoostRule() =>
        RuleSelectionTests.Rule(action: FlatConsequence.Boost, targetId: Boosted, boost: 5, name: "Espresso grinders");

    [Test]
    public async Task Boost_LeavesTheTargetAStepAndTheRestOnlyTheLuceneScore()
    {
        var rule = BoostRule();
        var capture = new CaptureStage();
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] }, extraStages: capture);

        var response = await harness.Search(Request());

        var boosted = response.Results.Single(result => result.Id == Boosted).Ranking!;
        var untouched = response.Results.First(result => result.Id != Boosted).Ranking!;

        Expect.Multiple(() =>
        {
            Assert.That(
                boosted.Steps!.Select(step => step.Stage),
                Is.EqualTo(new[] { "Lucene score", RuleSelection.Explain(rule) }).AsCollection);
            Assert.That(boosted.Steps![^1].Score, Is.GreaterThan(boosted.Steps[0].Score), "the boost raised it");
            Assert.That(boosted.BaseScore, Is.EqualTo(boosted.Steps[0].Score), "baseScore is the score before any boost, not the final one");
            Assert.That(
                untouched.Steps![^1].Score,
                Is.LessThan(untouched.Steps[0].Score),
                "the boost's SHOULD clause costs every document that does not match it Lucene's coordination factor");
            Assert.That(
                capture.Context!.AppliedRules[Boosted].Select(applied => (applied.RuleId, applied.Effect)),
                Is.EqualTo(new[] { (rule.Id, "boost") }).AsCollection);
            Assert.That(
                capture.Context.AppliedRules.Keys,
                Is.EqualTo(new[] { Boosted }).AsCollection,
                "only the document the boost raised was touched by the rule");
        });
    }

    [Test]
    public async Task TheLastStep_IsTheScoreTheResultCameBackWith()
    {
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [BoostRule()] });

        var response = await harness.Search(Request());

        Expect.Multiple(() =>
        {
            foreach (var result in response.Results)
            {
                Assert.That(result.Ranking!.Steps![^1].Score, Is.EqualTo(result.Score!.Value).Within(0.001), result.Id);
            }
        });
    }

    /// <summary>
    /// A drill-down runs the query through <c>DrillSideways</c>, which wraps it in clauses of its own;
    /// they are scoreless by construction, so the breakdown still ends where the search did.
    /// </summary>
    [Test]
    public async Task TheLastStep_HoldsUnderAFacetDrillDown()
    {
        using var harness = new TestHarness();
        var request = Request();
        request.Filters = new Filters { Facets = [new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["coffee"] }] };

        var response = await harness.Search(request);

        Assert.That(response.Results, Is.Not.Empty);
        Expect.Multiple(() =>
        {
            foreach (var result in response.Results)
            {
                Assert.That(result.Ranking!.Steps![^1].Score, Is.EqualTo(result.Score!.Value).Within(0.001), result.Id);
            }
        });
    }

    [Test]
    public async Task WithoutExplain_TheStageDoesNothing()
    {
        var capture = new CaptureStage();
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [BoostRule()] }, extraStages: capture);

        var response = await harness.Search(Request(explain: false));

        Expect.Multiple(() =>
        {
            Assert.That(capture.Context!.ScoreSteps, Is.Empty);
            Assert.That(capture.Context.AppliedRules, Is.Empty);
            Assert.That(response.Results[0].Ranking, Is.Null);
        });
    }

    [Test]
    public async Task EveryDocumentOnThePage_CarriesItsLuceneDocumentId()
    {
        var capture = new CaptureStage();
        using var harness = new TestHarness(extraStages: capture);

        await harness.Search(Request());

        Assert.That(capture.Context!.Documents.Select(document => document.DocId), Has.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AnInjectedPin_GetsItsOwnScoreAndThePinAsTheLastStep()
    {
        var rule = RuleSelectionTests.Rule(action: FlatConsequence.Pin, targetId: "doc-4:en", targetPosition: 1, name: "Espresso pick");
        var capture = new CaptureStage();
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] }, extraStages: capture);

        var response = await harness.Search(Request());

        var pinned = response.Results[0];

        Expect.Multiple(() =>
        {
            Assert.That(pinned.Id, Is.EqualTo("doc-4:en"));
            Assert.That(
                pinned.Ranking!.Steps!.Select(step => step.Stage),
                Is.EqualTo(new[] { "Lucene score", $"{RuleSelection.Explain(rule)} → #1" }).AsCollection);
            Assert.That(pinned.Ranking.Steps![^1].Score, Is.EqualTo(pinned.Ranking.Steps[0].Score), "a pin moves a document, it does not rescore it");
            Assert.That(
                capture.Context!.AppliedRules["doc-4:en"].Select(applied => applied.Effect),
                Is.EqualTo(new[] { "pin" }).AsCollection);
        });
    }

    /// <summary>
    /// A pin injects a document the query never matched; the id lookup that loaded it used to lend it
    /// its own score (QT-3). Lucene explains a non-match as 0, and that is what the whole breakdown
    /// reads.
    /// </summary>
    [Test]
    public async Task AnInjectedPin_ThatDoesNotMatchTheQuery_ScoresZeroThroughout()
    {
        var rule = RuleSelectionTests.Rule(action: FlatConsequence.Pin, targetId: "doc-4:en", targetPosition: 3, name: "Demo: Espresso accessories");
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] });

        var response = await harness.Search(Request());

        var pinned = response.Results.Single(result => result.Id == "doc-4:en");

        Expect.Multiple(() =>
        {
            Assert.That(pinned.Score, Is.EqualTo(0));
            Assert.That(pinned.Ranking!.BaseScore, Is.EqualTo(0));
            Assert.That(
                pinned.Ranking.Steps!.Select(step => (step.Stage, step.Score)),
                Is.EqualTo(new[] { ("Lucene score", 0d), ($"{RuleSelection.Explain(rule)} → #3", 0d) }).AsCollection);
        });
    }

    /// <summary>
    /// A pin can also inject a document that matched the query but fell off the page. It keeps the
    /// score the query gives it, and the checkpoints that moved it - here the boost's coordination
    /// factor - are steps of its story like any other document's.
    /// </summary>
    [Test]
    public async Task AnInjectedPin_ThatMatchesTheQuery_KeepsItsQueryScoreAndCheckpoints()
    {
        var boost = BoostRule();
        var pin = RuleSelectionTests.Rule(action: FlatConsequence.Pin, targetId: "doc-2:en", targetPosition: 1, name: "Demo: Latte art");
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [boost, pin] });
        var request = Request();
        request.PageSize = 1;

        var response = await harness.Search(request);

        var pinned = response.Results[0];
        var steps = pinned.Ranking!.Steps!;

        Expect.Multiple(() =>
        {
            Assert.That(pinned.Id, Is.EqualTo("doc-2:en"), "it was off the page, so the pin injected it");
            Assert.That(pinned.Score, Is.GreaterThan(0));
            Assert.That(
                steps.Select(step => step.Stage),
                Is.EqualTo(new[] { "Lucene score", RuleSelection.Explain(boost), $"{RuleSelection.Explain(pin)} → #1" }).AsCollection);
            Assert.That(steps[^1].Score, Is.EqualTo(pinned.Score!.Value).Within(0.001), "the score is the last step, as for any document");
            Assert.That(steps[^1].Score, Is.EqualTo(steps[^2].Score), "a pin moves a document, it does not rescore it");
            Assert.That(pinned.Ranking.BaseScore, Is.EqualTo(steps[0].Score));
        });
    }

    [Test]
    public async Task APinnedDocumentThatWasOnThePage_KeepsItsStepsAndGainsTheMove()
    {
        var rule = RuleSelectionTests.Rule(action: FlatConsequence.Pin, targetId: "doc-5:en", targetPosition: 1, name: "Espresso pick");
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] });

        var response = await harness.Search(Request());

        Assert.That(
            response.Results[0].Ranking!.Steps!.Select(step => step.Stage),
            Is.EqualTo(new[] { "Lucene score", $"{RuleSelection.Explain(rule)} → #1" }).AsCollection);
    }

    [Test]
    public async Task Bury_RecordsItsRuleAgainstTheDocumentItTookOffThePage()
    {
        var rule = RuleSelectionTests.Rule(action: FlatConsequence.Bury, targetId: "doc-1:en", name: "Out of stock");
        var capture = new CaptureStage();
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] }, extraStages: capture);

        var response = await harness.Search(Request());

        Expect.Multiple(() =>
        {
            Assert.That(response.Results.Select(result => result.Id), Has.No.Member("doc-1:en"));
            Assert.That(
                capture.Context!.AppliedRules["doc-1:en"].Select(applied => applied.Effect),
                Is.EqualTo(new[] { "bury" }).AsCollection);
        });
    }

    [Test]
    public async Task Hide_RecordsItsRuleToo()
    {
        var rule = RuleSelectionTests.Rule(name: "Retired") with { Actions = [new RuleAction.Hide("doc-1:en")] };
        var capture = new CaptureStage();
        using var harness = new TestHarness(tuning: new FakeTuningSource { Rules = [rule] }, extraStages: capture);

        await harness.Search(Request());

        Assert.That(
            capture.Context!.AppliedRules["doc-1:en"].Select(applied => applied.Effect),
            Is.EqualTo(new[] { "hide" }).AsCollection);
    }

    [Test]
    public async Task FieldWeights_AddTheirOwnCheckpointOnlyWhenAWeightMoves()
    {
        var weighted = new CaptureStage();
        var neutral = new CaptureStage();
        var none = new CaptureStage();

        using (var harness = new TestHarness(
            tuning: new FakeTuningSource { Weights = [new FieldWeight(TestCorpus.BodyField, 3)] },
            extraStages: weighted))
        {
            await harness.Search(Request());
        }

        using (var harness = new TestHarness(
            tuning: new FakeTuningSource { Weights = [new FieldWeight(TestCorpus.BodyField, 1)] },
            extraStages: neutral))
        {
            await harness.Search(Request());
        }

        using (var harness = new TestHarness(extraStages: none))
        {
            await harness.Search(Request());
        }

        Expect.Multiple(() =>
        {
            Assert.That(
                weighted.Context!.ScoreCheckpoints.Select(checkpoint => checkpoint.Stage),
                Is.EqualTo(new[] { "Lucene score", "Field weights" }).AsCollection);
            Assert.That(
                neutral.Context!.ScoreCheckpoints.Select(checkpoint => checkpoint.Stage),
                Is.EqualTo(new[] { "Lucene score" }).AsCollection,
                "a weight of 1.0 builds the same query, so there is nothing to compare");
            Assert.That(
                none.Context!.ScoreCheckpoints.Select(checkpoint => checkpoint.Stage),
                Is.EqualTo(new[] { "Lucene score" }).AsCollection);
        });
    }

    [Test]
    public async Task FieldWeights_ChangeTheScoreBetweenTheTwoCheckpoints()
    {
        using var harness = new TestHarness(
            tuning: new FakeTuningSource { Weights = [new FieldWeight(TestCorpus.BodyField, 4)] });

        var response = await harness.Search(Request());

        var steps = response.Results[0].Ranking!.Steps!;

        Expect.Multiple(() =>
        {
            Assert.That(steps.Select(step => step.Stage), Is.EqualTo(new[] { "Lucene score", "Field weights" }).AsCollection);
            Assert.That(steps[1].Score, Is.GreaterThan(steps[0].Score));
            Assert.That(steps[^1].Score, Is.EqualTo(response.Results[0].Score!.Value).Within(0.001));
        });
    }
}
