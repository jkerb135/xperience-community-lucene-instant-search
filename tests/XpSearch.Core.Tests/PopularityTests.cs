using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using Lucene.Net.Util;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the popularity signal of RK-1: the position damping, what one run aggregates, the bounded
/// boost, the suggestion thresholds and their dismissal, and where the signal version shows up.
/// </summary>
[TestFixture]
internal sealed class PopularityTests
{
    [Test]
    public void AClickFurtherDownTheList_IsWorthMore()
    {
        Expect.Multiple(() =>
        {
            Assert.That(PopularityAggregator.Damp(8), Is.GreaterThan(PopularityAggregator.Damp(1)));
            Assert.That(PopularityAggregator.Damp(1), Is.EqualTo(1.0).Within(1e-9));

            // An unknown position is read as position 1: the most conservative weight there is.
            Assert.That(PopularityAggregator.Damp(null), Is.EqualTo(PopularityAggregator.Damp(1)));
            Assert.That(PopularityAggregator.Damp(0), Is.EqualTo(PopularityAggregator.Damp(1)));
        });
    }

    [Test]
    public void TheSignal_SumsTheDampedMassPerDocument()
    {
        var aggregate = PopularityAggregator.Aggregate(
            [Click("espresso", "doc-1:en", 1), Click("espresso", "doc-1:en", 3), Click("espresso", "doc-2:en", 1)],
            documentLimit: 100,
            suggestionQueries: 10);

        Expect.Multiple(() =>
        {
            Assert.That(
                aggregate.Scores["doc-1:en"],
                Is.EqualTo(PopularityAggregator.Damp(1) + PopularityAggregator.Damp(3)).Within(1e-9));
            Assert.That(aggregate.Scores["doc-2:en"], Is.EqualTo(1.0).Within(1e-9));
        });
    }

    [Test]
    public void TheSignal_KeepsOnlyTheStrongestDocuments()
    {
        var rows = new List<QueryLogEntry>();

        for (int document = 1; document <= 5; document++)
        {
            for (int click = 0; click < document; click++)
            {
                rows.Add(Click("espresso", $"doc-{document}", 1));
            }
        }

        var aggregate = PopularityAggregator.Aggregate(rows, documentLimit: 2, suggestionQueries: 10);

        Assert.That(aggregate.Scores.Keys, Is.EquivalentTo(new[] { "doc-5", "doc-4" }));
    }

    [Test]
    public void AWindowWithNoClicks_ProducesNothing()
    {
        var aggregate = PopularityAggregator.Aggregate(
            [Search("espresso"), Search("espresso")],
            documentLimit: 100,
            suggestionQueries: 10);

        Expect.Multiple(() =>
        {
            Assert.That(aggregate.Scores, Is.Empty);
            Assert.That(aggregate.Suggestions, Is.Empty);
        });
    }

    [Test]
    public void ADocumentThatClearlyWinsAQuery_IsSuggested()
    {
        var rows = Enumerable.Range(0, 6).Select(_ => Click("espresso", "doc-1:en", 1)).ToList();
        rows.Add(Click("espresso", "doc-2:en", 1));

        var suggestion = PopularityAggregator.Aggregate(rows, 100, 10).Suggestions.Single();

        Expect.Multiple(() =>
        {
            Assert.That(suggestion.Query, Is.EqualTo("espresso"));
            Assert.That(suggestion.DocumentId, Is.EqualTo("doc-1:en"));
            Assert.That(suggestion.Clicks, Is.EqualTo(6));
            Assert.That(suggestion.SharePercent, Is.EqualTo(86));
        });
    }

    [Test]
    public void TooFewClicks_IsNotEvidenceEnough()
    {
        var rows = Enumerable.Range(0, PopularityAggregator.MinimumSuggestionClicks - 1)
            .Select(_ => Click("espresso", "doc-1:en", 1));

        Assert.That(PopularityAggregator.Aggregate(rows, 100, 10).Suggestions, Is.Empty);
    }

    [Test]
    public void ADividedQuery_IsNotSuggested()
    {
        var rows = Enumerable.Range(0, 5).Select(_ => Click("espresso", "doc-1:en", 1))
            .Concat(Enumerable.Range(0, 5).Select(_ => Click("espresso", "doc-2:en", 1)))
            .Concat(Enumerable.Range(0, 5).Select(_ => Click("espresso", "doc-3:en", 1)));

        Assert.That(PopularityAggregator.Aggregate(rows, 100, 10).Suggestions, Is.Empty);
    }

    [Test]
    public void OnlyTheMostFrequentQueries_AreExamined()
    {
        var rows = Enumerable.Range(0, 6).Select(_ => Click("espresso", "doc-1:en", 1))
            .Concat(Enumerable.Range(0, 10).Select(_ => Search("grinder")))
            .ToList();

        Assert.That(PopularityAggregator.Aggregate(rows, 100, suggestionQueries: 1).Suggestions, Is.Empty);
    }

    [Test]
    public void AnAnsweredSuggestion_NeverComesBack()
    {
        PopularitySuggestion[] candidates =
        [
            new("espresso", "doc-1:en", 6, 90),
            new("grinder", "doc-4:en", 7, 80)
        ];

        var pending = PopularitySuggestionMerge.Pending(candidates, [("ESPRESSO", "doc-1:en")]);

        Assert.That(pending.Select(entry => entry.Query), Is.EqualTo(new[] { "grinder" }).AsCollection);
    }

    [Test]
    public void TheTopDocument_ReachesTheCapAndTheRestScaleDown()
    {
        var signal = new PopularitySignal(
            TestCorpus.IndexName,
            42,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["doc-1"] = 4, ["doc-2"] = 2, ["doc-3"] = 0 });

        var boosts = signal.Boosts().ToDictionary(entry => entry.DocumentId, entry => entry.Factor, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(boosts["doc-1"], Is.EqualTo(PopularitySignal.MaxFactor).Within(1e-9));
            Assert.That(boosts["doc-2"], Is.EqualTo(1.5).Within(1e-9), "half the mass, half the way to the cap");
            Assert.That(boosts, Does.Not.ContainKey("doc-3"), "no evidence, no boost");
        });
    }

    [Test]
    public void AnEmptySignal_IsANoOp()
    {
        Expect.Multiple(() =>
        {
            Assert.That(PopularitySignal.Empty.Boosts(), Is.Empty);
            Assert.That(
                new PopularitySignal("i", 1, new Dictionary<string, double> { ["doc-1"] = 0 }).Boosts(),
                Is.Empty);
        });
    }

    [Test]
    public async Task TheStage_LeavesTheQueryAloneWhenThereIsNoSignal()
    {
        var context = Context();
        var before = context.BaseQuery;

        await new PopularityBoostStage(new FakePopularitySignalStore()).ExecuteAsync(context, CancellationToken.None);

        Assert.That(context.BaseQuery, Is.SameAs(before));
    }

    [Test]
    public async Task TheStage_AddsOneBoundedClausePerScoredDocument()
    {
        var store = new FakePopularitySignalStore();
        store.Set(TestCorpus.IndexName, 7, ("doc-1:en", 4), ("doc-2:en", 2));

        var context = Context();

        await new PopularityBoostStage(store).ExecuteAsync(context, CancellationToken.None);

        var clauses = ((BooleanQuery)context.BaseQuery).Clauses;

        Expect.Multiple(() =>
        {
            Assert.That(clauses[0].Occur, Is.EqualTo(Occur.MUST), "the query everything else built stays required");
            Assert.That(clauses.Count, Is.EqualTo(3));
            Assert.That(clauses.Skip(1).Select(clause => clause.Occur), Is.All.EqualTo(Occur.SHOULD));
            Assert.That(
                clauses.Skip(1).Select(clause => (double)clause.Query.Boost),
                Is.EqualTo(new[] { PopularitySignal.MaxFactor, 1.5 }).AsCollection);
        });
    }

    [Test]
    public void TheStage_RunsAfterTheRulesAndBeforeTheSearch() =>
        Assert.That(
            new[] { SearchStageOrder.BoostRules, SearchStageOrder.PopularityBoost, SearchStageOrder.Execute },
            Is.Ordered.Ascending);

    [Test]
    public async Task ASearch_KeepsAnsweringWithTheStageInThePipeline()
    {
        var store = new FakePopularitySignalStore();
        store.Set(TestCorpus.IndexName, 7, ("doc-3:en", 10));

        using var plain = new TestHarness();
        using var boosted = new TestHarness(extraStages: new PopularityBoostStage(store));

        var before = await plain.Search(TestHarness.Request("espresso"));
        var after = await boosted.Search(TestHarness.Request("espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(after.Results.Select(result => result.Id), Is.EquivalentTo(before.Results.Select(result => result.Id)));
            Assert.That(
                Rank(after, "doc-3:en"),
                Is.LessThan(Rank(before, "doc-3:en")),
                "the clicked document rises");
        });
    }

    [Test]
    public async Task TheTask_ReplacesAnIndexsSignalPerRunAndKeepsIndexesApart()
    {
        var log = new InMemoryQueryLogStore();
        var store = new FakePopularitySignalStore();

        await log.AppendAsync(Click("espresso", "doc-1:en", 1) with { IndexName = "A" }, CancellationToken.None);
        await log.AppendAsync(Click("espresso", "doc-2:en", 1) with { IndexName = "B" }, CancellationToken.None);

        var task = Task(log, store);

        await task.Execute(null!, CancellationToken.None);
        await task.Execute(null!, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(store.Written.Count, Is.EqualTo(4), "every run writes every index it saw");
            Assert.That(store.Signals["A"].Scores.Keys, Is.EqualTo(new[] { "doc-1:en" }).AsCollection);
            Assert.That(store.Signals["B"].Scores.Keys, Is.EqualTo(new[] { "doc-2:en" }).AsCollection);
            Assert.That(
                store.Written[0].Aggregate.Scores,
                Is.EqualTo(store.Written[2].Aggregate.Scores),
                "a second run over the same window computes the same rows");
        });
    }

    [Test]
    public async Task TheTask_IgnoresClicksOlderThanTheLookbackWindow()
    {
        var log = new InMemoryQueryLogStore();
        var store = new FakePopularitySignalStore();
        var options = new XpSearchOptions();
        options.Analytics.PopularityLookbackDays = 30;

        await log.AppendAsync(Click("espresso", "doc-old:en", 1) with { Timestamp = DateTime.UtcNow.AddDays(-31) }, CancellationToken.None);
        await log.AppendAsync(Click("espresso", "doc-new:en", 1), CancellationToken.None);

        await Task(log, store, options).Execute(null!, CancellationToken.None);

        Assert.That(store.Signals[TestCorpus.IndexName].Scores.Keys, Is.EqualTo(new[] { "doc-new:en" }).AsCollection);
    }

    [Test]
    public void TheSignalVersion_JoinsTheCacheKeyOnlyWhenTheIndexOptedIn()
    {
        var request = TestHarness.Request("espresso");

        string off = SearchCacheKey.Compute(request, "espresso");
        string on = SearchCacheKey.Compute(request, "espresso", popularityVersion: 12);
        string next = SearchCacheKey.Compute(request, "espresso", popularityVersion: 13);

        Expect.Multiple(() =>
        {
            Assert.That(SearchCacheKey.Compute(request, "espresso", popularityVersion: 0), Is.EqualTo(off), "an index that has not opted in keys the same as before RK-1");
            Assert.That(on, Is.Not.EqualTo(off));
            Assert.That(next, Is.Not.EqualTo(on), "a task run invalidates the responses it changed");
        });
    }

    /// <summary>
    /// The signal's storage, checked against the columns the code reads. Installing the classes needs
    /// a database; getting a column name or its nullability wrong does not.
    /// </summary>
    [Test]
    public void TheStorageHasTheColumnsTheSignalIsReadFrom()
    {
        var fields = XpSearchAnalyticsModuleInstaller.PopularityIndexForm()
            .GetFields(true, true)
            .Select(field => field.Name);

        var clickedResult = XpSearchAnalyticsModuleInstaller.QueryLogForm()
            .GetFields(true, true)
            .Single(field => field.Name == nameof(XpSearchQueryLogInfo.LogClickedResultID));

        Expect.Multiple(() =>
        {
            Assert.That(
                fields,
                Is.EquivalentTo(new[]
                {
                    nameof(XpSearchPopularityIndexInfo.PopularityIndexID),
                    nameof(XpSearchPopularityIndexInfo.PopularityIndexGuid),
                    nameof(XpSearchPopularityIndexInfo.PopularityIndexName),
                    nameof(XpSearchPopularityIndexInfo.PopularityIndexEnabled),
                    nameof(XpSearchPopularityIndexInfo.PopularityIndexComputed),
                }));

            Assert.That(
                XpSearchAnalyticsModuleInstaller.PopularityScoreForm().GetFields(true, true).Select(field => field.Name),
                Is.EquivalentTo(new[]
                {
                    nameof(XpSearchPopularityScoreInfo.ScoreID),
                    nameof(XpSearchPopularityScoreInfo.ScoreGuid),
                    nameof(XpSearchPopularityScoreInfo.ScoreIndexName),
                    nameof(XpSearchPopularityScoreInfo.ScoreDocumentID),
                    nameof(XpSearchPopularityScoreInfo.ScoreValue),
                    nameof(XpSearchPopularityScoreInfo.ScoreComputed),
                }));

            Assert.That(
                XpSearchAnalyticsModuleInstaller.PopularitySuggestionForm().GetFields(true, true).Select(field => field.Name),
                Is.EquivalentTo(new[]
                {
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionID),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionGuid),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionIndexName),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionQuery),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionDocumentID),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionClicks),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionSharePercent),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionComputed),
                    nameof(XpSearchPopularitySuggestionInfo.SuggestionState),
                }));

            // An upgraded installation has query log rows that predate the column entirely.
            Assert.That(clickedResult.AllowEmpty, Is.True);
        });
    }

    private static int Rank(SearchResponse response, string id) =>
        response.Results.Select((result, index) => (result.Id, index)).First(entry => entry.Id == id).index;

    private static XpSearchPopularityTask Task(IQueryLogStore log, IPopularitySignalStore store, XpSearchOptions? options = null) =>
        new(
            log,
            store,
            Microsoft.Extensions.Options.Options.Create(options ?? new XpSearchOptions()),
            NullLogger<XpSearchPopularityTask>.Instance);

    private static SearchContext Context() =>
        new(
            TestHarness.Request("espresso"),
            TestCorpus.Schema,
            new StandardAnalyzer(LuceneVersion.LUCENE_48),
            null,
            CancellationToken.None);

    private static QueryLogEntry Search(string query) =>
        new("q", TestCorpus.IndexName, query, 3, DateTime.UtcNow, "Store", "en", 12);

    private static QueryLogEntry Click(string query, string documentId, int position) =>
        Search(query) with { ClickedPosition = position, ClickedResultId = documentId };
}
