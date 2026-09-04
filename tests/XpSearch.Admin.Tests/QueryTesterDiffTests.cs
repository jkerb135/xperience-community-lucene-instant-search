using System.Text.Json;

using NUnit.Framework;

using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;

namespace XpSearch.Admin.Tests;

/// <summary>Covers the pure marking the query tester renders (spec §8.4).</summary>
[TestFixture]
internal sealed class QueryTesterDiffTests
{
    [Test]
    public void Compare_MarksAHitTheRulesLifted()
    {
        var result = QueryTesterDiff.Compare(
            Side(["b", "a"], []),
            Side(["a", "b"], []));

        Expect.Multiple(() =>
        {
            Assert.That(result.WithRules.Hits[0].Change, Is.EqualTo(ResultChange.MovedUp), "b is first with the rules");
            Assert.That(result.WithRules.Hits[1].Change, Is.EqualTo(ResultChange.MovedDown));
            Assert.That(result.WithoutRules.Hits[0].Change, Is.EqualTo(ResultChange.MovedDown), "a is first without them");
            Assert.That(result.WithoutRules.Hits[1].Change, Is.EqualTo(ResultChange.MovedUp));
        });
    }

    [Test]
    public void Compare_MarksInjectedAndRemovedHits()
    {
        var result = QueryTesterDiff.Compare(
            Side(["pinned", "a"], []),
            Side(["a", "buried"], []));

        Expect.Multiple(() =>
        {
            Assert.That(result.WithRules.Hits[0].Change, Is.EqualTo(ResultChange.Injected));
            Assert.That(result.WithRules.Hits[1].Change, Is.EqualTo(ResultChange.MovedDown));
            Assert.That(result.WithoutRules.Hits[1].Change, Is.EqualTo(ResultChange.Removed));
        });
    }

    [Test]
    public void Compare_MarksAnUnchangedRankingAsUnchanged()
    {
        var result = QueryTesterDiff.Compare(Side(["a", "b"], []), Side(["a", "b"], []));

        Assert.That(
            result.WithRules.Hits.Select(hit => hit.Change),
            Is.All.EqualTo(ResultChange.Unchanged));
    }

    [Test]
    public void Compare_SplitsTheQueryLevelExplanationsOffEveryHit()
    {
        var side = Side(
            ["a"],
            ["synonym: tea", "field weight: title x2"],
            ["pinned to position 1 by rule 'Espresso'"]);

        var result = QueryTesterDiff.Compare(side, Side(["a"], []));

        Expect.Multiple(() =>
        {
            Assert.That(result.WithRules.QueryExplanations, Has.Count.EqualTo(2));
            Assert.That(result.WithRules.Hits[0].Boosts, Is.EqualTo(new[] { "pinned to position 1 by rule 'Espresso'" }));
        });
    }

    [Test]
    public void Compare_CarriesTheTitleUrlScoreAndTotals()
    {
        var result = QueryTesterDiff.Compare(Side(["a"], []), Side(["a"], []));
        var hit = result.WithRules.Hits[0];

        Expect.Multiple(() =>
        {
            Assert.That(hit.Title, Is.EqualTo("Title of a"));
            Assert.That(hit.Url, Is.EqualTo("/a"));
            Assert.That(hit.Score, Is.EqualTo(2.5).Within(0.001));
            Assert.That(hit.BaseScore, Is.EqualTo(1.5).Within(0.001));
            Assert.That(hit.Position, Is.EqualTo(1));
            Assert.That(result.WithRules.Total, Is.EqualTo(1));
            Assert.That(result.WithRules.TookMs, Is.EqualTo(7));
            Assert.That(result.Error, Is.Empty);
        });
    }

    /// <summary>The per-stage score breakdown of QT-2 travels to the client on every hit.</summary>
    [Test]
    public void Compare_CarriesTheScoreStepsAndTheRulesThatTouchedTheHit()
    {
        var side = Side(
            ["a"],
            [],
            steps: [new RankingStep { Stage = "Lucene score", Score = 1.5 }, new RankingStep { Stage = "rule:Espresso", Score = 2.5 }],
            appliedRules: new Dictionary<string, IReadOnlyList<AppliedRule>>(StringComparer.Ordinal)
            {
                ["a"] = [new AppliedRule(7, "Espresso", "boost")]
            });

        var hit = QueryTesterDiff.Compare(side, Side(["a"], [])).WithRules.Hits[0];

        Expect.Multiple(() =>
        {
            Assert.That(hit.Steps.Select(step => step.Stage), Is.EqualTo(new[] { "Lucene score", "rule:Espresso" }).AsCollection);
            Assert.That(hit.Steps[^1].Score, Is.EqualTo(2.5).Within(0.001));
            Assert.That(hit.Rules, Is.EqualTo(new[] { new HitRule(7, "Espresso", "boost") }).AsCollection);
            Assert.That(hit.BaseScore, Is.EqualTo(1.5).Within(0.001), "the base score is the ranking's own, which is the first step since QT-2");
        });
    }

    [Test]
    public void Compare_LeavesAHitNoRuleTouchedWithoutStepsOrRules()
    {
        var hit = QueryTesterDiff.Compare(Side(["a"], []), Side(["a"], [])).WithRules.Hits[0];

        Expect.Multiple(() =>
        {
            Assert.That(hit.Steps, Is.Empty);
            Assert.That(hit.Rules, Is.Empty);
        });
    }

    [Test]
    public void Attribute_ReturnsAnEmptyStringWhenTheIndexDoesNotProjectIt()
    {
        var result = new Result { Id = "a", Attributes = [] };

        Assert.That(QueryTesterDiff.Attribute(result, QueryTesterDiff.TitleAttribute), Is.Empty);
    }

    private static QueryTesterSideResult Side(
        string[] ids,
        string[] queryExplanations,
        string[]? hitExplanations = null,
        RankingStep[]? steps = null,
        IReadOnlyDictionary<string, IReadOnlyList<AppliedRule>>? appliedRules = null)
    {
        var response = new SearchResponse
        {
            Total = ids.Length,
            TookMs = 7,
            Page = 1,
            PageSize = 10,
            TotalPages = 1,
            Results =
            [
                .. ids.Select((id, index) => new Result
                {
                    Id = id,
                    Score = 2.5,
                    Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        [QueryTesterDiff.TitleAttribute] = JsonSerializer.SerializeToElement($"Title of {id}"),
                        [QueryTesterDiff.UrlAttribute] = JsonSerializer.SerializeToElement($"/{id}")
                    },
                    Ranking = new RankingInfo
                    {
                        BaseScore = 1.5,
                        Position = index + 1,
                        Boosts = [.. queryExplanations, .. hitExplanations ?? []],
                        Steps = steps
                    }
                })
            ]
        };

        return appliedRules is null
            ? new QueryTesterSideResult(response, queryExplanations)
            : new QueryTesterSideResult(response, queryExplanations, appliedRules);
    }
}
