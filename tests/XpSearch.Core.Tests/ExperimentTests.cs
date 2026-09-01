using CMS.Helpers;

using Kentico.Web.Mvc;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>A resolver that answers whatever a test hands it (XP-1).</summary>
internal sealed class StubExperimentResolver : IExperimentAssignmentResolver
{
    internal StubExperimentResolver(ExperimentAssignment? assignment = null) =>
        Assignment = assignment ?? ExperimentAssignment.None;

    internal ExperimentAssignment Assignment { get; set; }

    public Task<ExperimentAssignment> GetAssignmentAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(Assignment);
}

/// <summary>A source that reports one running experiment, or none.</summary>
internal sealed class StubRunningExperimentSource : IRunningExperimentSource
{
    internal StubRunningExperimentSource(RunningExperiment? running = null) => Running = running;

    internal RunningExperiment? Running { get; set; }

    public Task<RunningExperiment?> GetRunningExperimentAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult(Running);
}

/// <summary>A response whose body has already started streaming, as DX-2's server render leaves it.</summary>
internal sealed class StartedResponseFeature : HttpResponseFeature
{
    public override bool HasStarted => true;
}

/// <summary>
/// Bucketing, the variant seam and the response-started guard of an A/B experiment
/// (amendment 2026-08-25).
/// </summary>
[TestFixture]
internal sealed class ExperimentTests
{
    private static readonly Guid Experiment = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Other = new("99999999-8888-7777-6666-555555555555");

    [Test]
    public void BucketingIsStableForTheSameVisitorAndExperiment()
    {
        var first = ExperimentBucketing.Variant("visitor-1", Experiment, 50);

        Expect.Multiple(() =>
        {
            for (int repeat = 0; repeat < 5; repeat++)
            {
                Assert.That(ExperimentBucketing.Variant("visitor-1", Experiment, 50), Is.EqualTo(first));
            }

            Assert.That(
                ExperimentBucketing.Bucket("visitor-1", Experiment),
                Is.InRange(0, 99),
                "the hash must land on the 0-99 line the split percentage is compared against");
        });
    }

    /// <summary>
    /// A visitor in B of one experiment must not automatically be in B of the next one, or the second
    /// experiment would only ever be run on the first one's B half.
    /// </summary>
    [Test]
    public void TheSameVisitorIsBucketedIndependentlyPerExperiment()
    {
        var differing = Enumerable.Range(0, 200)
            .Select(index => $"visitor-{index}")
            .Count(id => ExperimentBucketing.Bucket(id, Experiment) != ExperimentBucketing.Bucket(id, Other));

        Assert.That(differing, Is.GreaterThan(150), "two experiments must bucket the same visitors differently");
    }

    [Test]
    public void TheSplitPercentageIsRespectedAcrossManyVisitors()
    {
        int inB = Enumerable.Range(0, 10_000)
            .Count(index => ExperimentBucketing.Variant($"visitor-{index}", Experiment, 30) == SearchVariant.B);

        Expect.Multiple(() =>
        {
            Assert.That(inB, Is.InRange(2_700, 3_300), "a 30% split must send roughly 30% of visitors to B");

            Assert.That(
                Enumerable.Range(0, 500).Count(index => ExperimentBucketing.Variant($"v{index}", Experiment, 1) == SearchVariant.B),
                Is.LessThan(50),
                "a 1% split keeps nearly everyone on A");

            Assert.That(
                Enumerable.Range(0, 500).Count(index => ExperimentBucketing.Variant($"v{index}", Experiment, 99) == SearchVariant.B),
                Is.GreaterThan(450),
                "a 99% split sends nearly everyone to B");
        });
    }

    [Test]
    public void OnlyVariantBReadsTheExperimentsTuning() =>
        Expect.Multiple(() =>
        {
            Assert.That(new ExperimentAssignment(7, SearchVariant.B).Tuning, Is.EqualTo(new TuningVariant(7)));
            Assert.That(new ExperimentAssignment(7, SearchVariant.A).Tuning, Is.EqualTo(TuningVariant.Live));
            Assert.That(ExperimentAssignment.None.Tuning, Is.EqualTo(TuningVariant.Live));
            Assert.That(ExperimentAssignment.None.IsActive, Is.False);
            Assert.That(TuningVariant.Live.IsLive, Is.True);
            Assert.That(new TuningVariant(7).IsLive, Is.False);
            Assert.That(new TuningVariant(7).CacheKeyPart, Is.Not.EqualTo(TuningVariant.Live.CacheKeyPart));
        });

    [Test]
    public async Task TheStagePutsTheAssignmentOnTheContextBeforeAnyTuningIsRead()
    {
        var assignment = new ExperimentAssignment(7, SearchVariant.B);
        var stage = new ResolveExperimentStage(new StubExperimentResolver(assignment));
        var context = Context();

        await stage.ExecuteAsync(context, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(context.Experiment, Is.EqualTo(assignment));
            Assert.That(stage.Order, Is.LessThan(SearchStageOrder.QueryRewrite));
            Assert.That(stage.Order, Is.LessThan(SearchStageOrder.SynonymExpansion));
            Assert.That(Context().Experiment, Is.EqualTo(ExperimentAssignment.None), "a context nobody resolved is on the live tuning");
        });
    }

    /// <summary>The point of the whole unit: a bucketed visitor is answered from the other tuning.</summary>
    [Test]
    public async Task VariantBReadsTheExperimentsRulesAndVariantATheLiveOnes()
    {
        var live = RuleSelectionTests.Rule(id: 1, name: "live");
        var draft = RuleSelectionTests.Rule(id: 2, name: "draft");
        var source = new FakeTuningSource { Rules = [live], VariantRules = [draft] };
        var stage = new QueryRewriteStage(source, TimeProvider.System);

        var a = Context();
        a.Experiment = new ExperimentAssignment(7, SearchVariant.A);
        await stage.ExecuteAsync(a, CancellationToken.None);
        var readForA = source.LastVariant;

        var b = Context();
        b.Experiment = new ExperimentAssignment(7, SearchVariant.B);
        await stage.ExecuteAsync(b, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(readForA, Is.EqualTo(TuningVariant.Live));
            Assert.That(a.Tuning.Rules.Select(rule => rule.Name), Is.EqualTo(new[] { "live" }).AsCollection);
            Assert.That(source.LastVariant, Is.EqualTo(new TuningVariant(7)));
            Assert.That(b.Tuning.Rules.Select(rule => rule.Name), Is.EqualTo(new[] { "draft" }).AsCollection);
        });
    }

    [Test]
    public void TheCacheKeyDependsOnTheVariantAndOnlyWhileAnExperimentRuns()
    {
        var request = new SearchRequest { Index = "articles", Query = "espresso" };

        string plain = SearchCacheKey.Compute(request, "espresso");
        string a = SearchCacheKey.Compute(request, "espresso", null, new ExperimentAssignment(7, SearchVariant.A));
        string b = SearchCacheKey.Compute(request, "espresso", null, new ExperimentAssignment(7, SearchVariant.B));

        Expect.Multiple(() =>
        {
            Assert.That(b, Is.Not.EqualTo(a), "the two variants are answered from different tuning");
            Assert.That(
                SearchCacheKey.Compute(request, "espresso", null, ExperimentAssignment.None),
                Is.EqualTo(plain),
                "with no experiment running the key is exactly what it was before XP-1");
            Assert.That(
                SearchCacheKey.Compute(request, "espresso", null, new ExperimentAssignment(8, SearchVariant.B)),
                Is.Not.EqualTo(b),
                "another experiment's B is not this one's B");
        });
    }

    [Test]
    public void TheGuardRefusesToAssignACookieOnceTheResponseHasStarted()
    {
        var streaming = new DefaultHttpContext();
        streaming.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        Expect.Multiple(() =>
        {
            Assert.That(ExperimentAssignmentResolver.CanAssignCookie(null), Is.False, "no HTTP context at all");
            Assert.That(ExperimentAssignmentResolver.CanAssignCookie(streaming), Is.False, "DX-2's server-rendered widget");
            Assert.That(ExperimentAssignmentResolver.CanAssignCookie(new DefaultHttpContext()), Is.True);
        });
    }

    [Test]
    public async Task AVisitorWithNoCookieOnAStartedResponseIsBucketedIntoAAndNothingIsWritten()
    {
        var cookies = Substitute.For<ICookieAccessor>();
        cookies.Get(ExperimentBucketing.CookieName).Returns(string.Empty);

        var streaming = new DefaultHttpContext();
        streaming.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var assignment = await Resolver(cookies, streaming).GetAssignmentAsync("articles", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(assignment.Variant, Is.EqualTo(SearchVariant.A));
            Assert.That(assignment.ExperimentId, Is.EqualTo(7), "the request still counts towards the experiment, honestly, as A");
            cookies.DidNotReceiveWithAnyArgs().Set(default!, default!, default!);
        });
    }

    [Test]
    public async Task AVisitorWithNoCookieGetsOneAndIsBucketedByIt()
    {
        var cookies = Substitute.For<ICookieAccessor>();
        cookies.Get(ExperimentBucketing.CookieName).Returns(string.Empty);
        string? written = null;
        cookies.When(accessor => accessor.Set(ExperimentBucketing.CookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(call => written = call.ArgAt<string>(1));

        var assignment = await Resolver(cookies, new DefaultHttpContext()).GetAssignmentAsync("articles", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(written, Is.Not.Null.And.Not.Empty);
            Assert.That(
                assignment.Variant,
                Is.EqualTo(ExperimentBucketing.Variant(written!, Experiment, 30)),
                "the visitor is bucketed by the id they were just given");
        });
    }

    [Test]
    public async Task AnIndexWithNoRunningExperimentIsNeverBucketedAndSetsNoCookie()
    {
        var cookies = Substitute.For<ICookieAccessor>();
        cookies.Get(ExperimentBucketing.CookieName).Returns(string.Empty);

        var resolver = new ExperimentAssignmentResolver(
            new StubRunningExperimentSource(),
            cookies,
            CookieLevel(Kentico.Web.Mvc.CookieLevel.All.Level),
            Accessor(new DefaultHttpContext()),
            NullLogger<ExperimentAssignmentResolver>.Instance);

        var assignment = await resolver.GetAssignmentAsync("articles", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(assignment, Is.EqualTo(ExperimentAssignment.None));
            cookies.DidNotReceiveWithAnyArgs().Set(default!, default!, default!);
        });
    }

    /// <summary>
    /// A visitor below the Essential level cannot store the cookie, so bucketing them on a throwaway
    /// id would flip their variant on every request. They are A, consistently.
    /// </summary>
    [Test]
    public async Task AVisitorWhoRefusedEssentialCookiesIsBucketedIntoA()
    {
        var cookies = Substitute.For<ICookieAccessor>();
        cookies.Get(ExperimentBucketing.CookieName).Returns(string.Empty);

        var resolver = new ExperimentAssignmentResolver(
            new StubRunningExperimentSource(new RunningExperiment(7, Experiment, 30)),
            cookies,
            CookieLevel(Kentico.Web.Mvc.CookieLevel.System.Level),
            Accessor(new DefaultHttpContext()),
            NullLogger<ExperimentAssignmentResolver>.Instance);

        var assignment = await resolver.GetAssignmentAsync("articles", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(assignment.Variant, Is.EqualTo(SearchVariant.A));
            cookies.DidNotReceiveWithAnyArgs().Set(default!, default!, default!);
        });
    }

    [Test]
    public async Task TheAnswerIsResolvedOncePerRequestPerIndex()
    {
        var cookies = Substitute.For<ICookieAccessor>();
        cookies.Get(ExperimentBucketing.CookieName).Returns("visitor-1");

        var context = new DefaultHttpContext();
        var resolver = Resolver(cookies, context);

        await resolver.GetAssignmentAsync("articles", CancellationToken.None);
        await resolver.GetAssignmentAsync("articles", CancellationToken.None);

        cookies.Received(1).Get(ExperimentBucketing.CookieName);
    }

    [Test]
    public void TheBucketCookieIsRegisteredAtTheEssentialLevel()
    {
        var options = new ServiceCollection()
            .AddXpSearchBucketCookie()
            .BuildServiceProvider()
            .GetRequiredService<IOptions<CookieLevelOptions>>()
            .Value;

        Assert.That(
            options.CookieConfigurations.TryGetValue(ExperimentBucketing.CookieName, out var level) ? level : null,
            Is.EqualTo(Kentico.Web.Mvc.CookieLevel.Essential),
            "an unregistered cookie is a Visitor-level one, which would put the experiment behind tracking consent");
    }

    private static ExperimentAssignmentResolver Resolver(ICookieAccessor cookies, HttpContext context) =>
        new(
            new StubRunningExperimentSource(new RunningExperiment(7, Experiment, 30)),
            cookies,
            CookieLevel(Kentico.Web.Mvc.CookieLevel.Essential.Level),
            Accessor(context),
            NullLogger<ExperimentAssignmentResolver>.Instance);

    private static ICurrentCookieLevelProvider CookieLevel(int level)
    {
        var provider = Substitute.For<ICurrentCookieLevelProvider>();
        provider.GetCurrentCookieLevel().Returns(level);

        return provider;
    }

    private static IHttpContextAccessor Accessor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);

        return accessor;
    }

    private static SearchContext Context() =>
        new(
            new SearchRequest { Index = "articles", Query = "espresso" },
            new Abstractions.IndexSchema("articles", [new Abstractions.SchemaField("title", Abstractions.SearchFieldKind.Text, true, false, false, true)]),
            new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48),
            null,
            CancellationToken.None);
}
