using CMS.Websites.Routing;

using Lucene.Net.Analysis.Standard;
using Lucene.Net.Facet;
using Lucene.Net.Util;

using Microsoft.Extensions.Logging;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Covers the half of the query tester that cannot be substituted: that the "without rules" side
/// really runs with no tuning at all (spec §8.4).
/// </summary>
[TestFixture]
internal sealed class QueryTesterSearchTests
{
    private static readonly TuningRule Rule = TuningRuleCompat.FromFlat(
        1,
        "Espresso first",
        enabled: true,
        FlatCondition.Always,
        "espresso",
        FlatConsequence.Pin,
        "doc-1",
        1,
        1,
        string.Empty,
        string.Empty,
        null,
        null,
        100,
        string.Empty);

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public async Task ExecuteAsync_AppliesTheIndexTuningOnlyOnTheWithRulesSide(bool applyTuning, int expectedRules)
    {
        var recorder = new RecordingStage();
        var search = Build(recorder);

        await search.ExecuteAsync(Request(), applyTuning, string.Empty, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(recorder.Tuning!.Rules, Has.Count.EqualTo(expectedRules));
            Assert.That(recorder.Tuning!.Synonyms, Has.Count.EqualTo(expectedRules));
            Assert.That(recorder.Tuning!.Stopwords, Has.Count.EqualTo(expectedRules));
            Assert.That(recorder.Tuning!.FieldWeights, Has.Count.EqualTo(expectedRules));
        });
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheQueryLevelExplanations()
    {
        var recorder = new RecordingStage();
        recorder.OnExecute = context => context.QueryExplanations.Add("field weight: title x2");

        var result = await Build(recorder).ExecuteAsync(Request(), applyTuning: true, string.Empty, CancellationToken.None);

        Assert.That(
            result.QueryExplanations,
            Is.EqualTo(new[] { "synonym:coffee", "field weight: title x2" }),
            "the tuning stage's own explanation and everything a later stage added");
    }

    /// <summary>
    /// A tester run must not skew the analytics dashboard (spec §9.2). It cannot: the search activity
    /// and the query log row are written by <see cref="ISearchRequestJournal"/> from the caching
    /// decorator, and the tester assembles its own <see cref="SearchPipeline"/> instead of resolving
    /// the registered <see cref="ISearchPipeline"/>. This pins that construction down; the companion
    /// guard that no stage journals either lives in Core's stage ordering tests.
    /// </summary>
    [Test]
    public void QueryTesterSearch_TakesNoPipelineAndNoAnalyticsDependency()
    {
        var parameters = typeof(QueryTesterSearch).GetConstructors().SelectMany(c => c.GetParameters());

        Assert.That(
            parameters.Select(parameter => parameter.ParameterType),
            Has.None.AnyOf(typeof(ISearchPipeline), typeof(ISearchRequestJournal), typeof(IQueryLogQueue)));
    }

    private static IndexSchema Schema() =>
        new("articles", [new SchemaField("title", SearchFieldKind.Text, true, false, false, true)]);

    private static SearchRequest Request() =>
        new() { Index = "articles", Query = "espresso", Explain = true, Page = 1, PageSize = 10 };

    private static QueryTesterSearch Build(params ISearchStage[] extraStages)
    {
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        accessor.Exists("articles").Returns(true);
        accessor.GetAnalyzer("articles").Returns(new StandardAnalyzer(LuceneVersion.LUCENE_48));
        accessor.GetFacetsConfig("articles").Returns((FacetsConfig?)null);

        var schemaProvider = Substitute.For<IIndexSchemaProvider>();
        schemaProvider.GetSchemaAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult(Schema()));

        var source = Substitute.For<IRelevanceTuningSource>();
        source.GetRulesAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<TuningRule>>([Rule]));
        source.GetSynonymsAsync("articles", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TuningSynonym>>([new TuningSynonym(SynonymDirection.TwoWay, ["espresso", "coffee"], [])]));
        source.GetStopwordsAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["the"]));
        source.GetFieldWeightsAsync("articles", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FieldWeight>>([new FieldWeight("title", 2)]));

        ISearchStage[] stages =
        [
            new QueryRewriteStage(source, TimeProvider.System),
            new SynonymExpansionStage(source),
            .. extraStages
        ];

        return new QueryTesterSearch(accessor, schemaProvider, stages, TimeProvider.System);
    }

    /// <summary>Stands in for the projection stage and records what the tuning stage produced.</summary>
    private sealed class RecordingStage : ISearchStage
    {
        public TuningSet? Tuning { get; private set; }

        public Action<SearchContext>? OnExecute { get; set; }

        public int Order => SearchStageOrder.Project;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            Tuning = context.Tuning;
            OnExecute?.Invoke(context);
            context.Response = new SearchResponse { Results = [], Total = 0, Page = 1, PageSize = 10, TotalPages = 0 };

            return Task.CompletedTask;
        }
    }

}
