using System.Reflection;

using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Core.Analytics;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Covers the experiment detail page (XP-1, amendment 2026-08-25): the per-variant report and the two
/// irreversible transitions behind it.
/// </summary>
[TestFixture]
internal sealed class ExperimentDetailPageTests
{
    private const int IndexIdentifier = 7;
    private const int ExperimentIdentifier = 11;
    private const string IndexName = "articles";

    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Started = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ended = new(2026, 8, 27, 17, 0, 0, DateTimeKind.Utc);

    private IExperimentCatalog catalog = null!;
    private IExperimentService experiments = null!;
    private ISearchAnalyticsService analytics = null!;
    private ExperimentDetailPage page = null!;

    [SetUp]
    public void SetUp()
    {
        catalog = Substitute.For<IExperimentCatalog>();
        catalog.Get(ExperimentIdentifier).Returns(Summary(ExperimentState.Running));

        experiments = Substitute.For<IExperimentService>();

        analytics = Substitute.For<ISearchAnalyticsService>();
        analytics
            .GetReportAsync(Arg.Any<SearchAnalyticsQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Report(((SearchAnalyticsQuery)call[0]).Variant == "A" ? 1204 : 1187)));

        page = new ExperimentDetailPage(
            Storage.Holding(IndexIdentifier, IndexName),
            catalog,
            experiments,
            analytics,
            new FakeClock(Now))
        {
            IndexIdentifier = IndexIdentifier,
            ExperimentIdentifier = ExperimentIdentifier
        };
    }

    /// <summary>
    /// The two sides are the same metrics over the same range, split by the variant stamped on the
    /// query log rows - which is the only thing that makes the comparison honest.
    /// </summary>
    [Test]
    public async Task Load_ReportsBothVariantsOverTheExperimentsOwnRange()
    {
        var report = (await page.Load(CancellationToken.None)).Result;

        var queries = analytics.ReceivedCalls().Select(call => (SearchAnalyticsQuery)call.GetArguments()[0]!).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(report.Error, Is.Empty);
            Assert.That(queries.Select(query => query.Variant), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(queries.Select(query => query.ExperimentId), Is.All.EqualTo(ExperimentIdentifier));
            Assert.That(queries.Select(query => query.IndexName), Is.All.EqualTo(IndexName));
            Assert.That(queries.Select(query => query.FromUtc), Is.All.EqualTo(Started));
            Assert.That(queries.Select(query => query.ToUtc), Is.All.EqualTo(Now), "a running experiment is reported up to now");
            Assert.That(report.A.Searches, Is.EqualTo(1204));
            Assert.That(report.B.Searches, Is.EqualTo(1187), "the sample sizes are shown as they are, never rounded away");
            Assert.That(report.A.ZeroResultSearches, Is.EqualTo(120));
            Assert.That(report.A.Clicks, Is.EqualTo(300));
            Assert.That(report.A.AverageClickedPosition, Is.EqualTo(2.5).Within(0.001));
            Assert.That(report.State, Is.EqualTo("Running"));
            Assert.That(report.SplitPercent, Is.EqualTo(40));
        });
    }

    /// <summary>A concluded experiment's report is the snapshot of the window it ran in, not of today.</summary>
    [Test]
    public async Task Load_BoundsAConcludedExperimentByItsOwnStartAndEnd()
    {
        catalog.Get(ExperimentIdentifier).Returns(Summary(ExperimentState.Concluded, ExperimentOutcome.Promoted));

        var report = (await page.Load(CancellationToken.None)).Result;

        var queries = analytics.ReceivedCalls().Select(call => (SearchAnalyticsQuery)call.GetArguments()[0]!).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(queries.Select(query => query.ToUtc), Is.All.EqualTo(Ended));
            Assert.That(report.Outcome, Is.EqualTo("Promoted"));
        });
    }

    [Test]
    public async Task Load_ADraftHasAnsweredNothingSoItReportsNothing()
    {
        catalog.Get(ExperimentIdentifier).Returns(Summary(ExperimentState.Draft));

        var report = (await page.Load(CancellationToken.None)).Result;

        Expect.Multiple(() =>
        {
            Assert.That(analytics.ReceivedCalls(), Is.Empty);
            Assert.That(report.A.Searches, Is.Zero);
            Assert.That(report.B.Searches, Is.Zero);
            Assert.That(report.State, Is.EqualTo("Draft"));
        });
    }

    /// <summary>An experiment reached through another index's URL is not this page's to read or conclude.</summary>
    [Test]
    public async Task Commands_RefuseAnExperimentThatBelongsToAnotherIndex()
    {
        catalog.Get(ExperimentIdentifier).Returns(Summary(ExperimentState.Running) with { IndexName = "products" });

        var loaded = (await page.Load(CancellationToken.None)).Result;
        var concluded = (await page.Conclude(new ConcludeRequest { Promote = true }, CancellationToken.None)).Result;

        Expect.Multiple(() =>
        {
            Assert.That(loaded.Error, Is.Not.Empty);
            Assert.That(concluded.Error, Is.Not.Empty);
            Assert.That(experiments.ReceivedCalls(), Is.Empty, "nothing is promoted through the wrong index's URL");
            Assert.That(analytics.ReceivedCalls(), Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Conclude_PassesTheChoiceThroughAndSaysWhatHappened(bool promote)
    {
        var response = await page.Conclude(new ConcludeRequest { Promote = promote }, CancellationToken.None);

        await experiments.Received(1).ConcludeAsync(ExperimentIdentifier, promote, Arg.Any<CancellationToken>());

        Assert.That(
            response.Messages.Single().Message,
            promote ? Does.Contain("live tuning") : Does.Contain("deleted"));
    }

    [Test]
    public async Task Start_StartsTheExperimentInTheUrl()
    {
        await page.Start(CancellationToken.None);

        await experiments.Received(1).StartAsync(ExperimentIdentifier, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetSplit_PassesTheSubmittedSplit()
    {
        await page.SetSplit(new SplitRequest { SplitPercent = 25 }, CancellationToken.None);

        await experiments.Received(1).SetSplitAsync(ExperimentIdentifier, 25, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The service owns the state machine; a transition it refuses has to reach the editor as a message
    /// rather than as a failed request.
    /// </summary>
    [Test]
    public async Task Start_TurnsARefusedTransitionIntoAMessage()
    {
        experiments
            .StartAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Only a draft experiment can be started."));

        var response = await page.Start(CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Messages.Single().Message, Does.Contain("Only a draft experiment"));
            Assert.That(response.Result.Error, Is.Empty, "the report is still shown next to the refusal");
        });
    }

    /// <summary>
    /// Every command is a plain method on the final page class: inherited or re-annotated ones have
    /// failed discovery on the host (docs/internal/agent-primer.md).
    /// </summary>
    [TestCase(nameof(ExperimentDetailPage.Load), SystemPermissions.VIEW)]
    [TestCase(nameof(ExperimentDetailPage.SetSplit), SystemPermissions.UPDATE)]
    [TestCase(nameof(ExperimentDetailPage.Start), SystemPermissions.UPDATE)]
    [TestCase(nameof(ExperimentDetailPage.Conclude), SystemPermissions.UPDATE)]
    public void Commands_AreDeclaredOnThePageItselfBehindItsPermission(string method, string permission)
    {
        var declared = typeof(ExperimentDetailPage).GetMethod(
            method,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.That(declared, Is.Not.Null);

        Assert.That(
            declared!.GetCustomAttributes<PageCommandAttribute>(inherit: false).Single().Permission,
            Is.EqualTo(permission));
    }

    [Test]
    public async Task ConfigureTemplateProperties_TellsTheClientTheSplitsItMayOffer()
    {
        var properties = await page.ConfigureTemplateProperties(new ExperimentDetailClientProperties());

        Expect.Multiple(() =>
        {
            Assert.That(properties.IndexName, Is.EqualTo(IndexName));
            Assert.That(properties.MinSplit, Is.EqualTo(ExperimentRules.MinSplit));
            Assert.That(properties.MaxSplit, Is.EqualTo(ExperimentRules.MaxSplit));
        });
    }

    private static ExperimentSummary Summary(ExperimentState state, ExperimentOutcome outcome = ExperimentOutcome.None) =>
        new(
            ExperimentIdentifier,
            IndexName,
            "Boost recent",
            40,
            state,
            outcome,
            state == ExperimentState.Draft ? null : Started,
            state == ExperimentState.Concluded ? Ended : null);

    private static SearchAnalyticsReport Report(int searches) =>
        new([], [], [], 2.5, [], [], searches, 120, 300);

    private sealed class FakeClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
