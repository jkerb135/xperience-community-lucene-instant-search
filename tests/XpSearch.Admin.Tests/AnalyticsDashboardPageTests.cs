using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Core.Analytics;

namespace XpSearch.Admin.Tests;

/// <summary>Covers the analytics dashboard page commands (spec §9.3).</summary>
[TestFixture]
internal sealed class AnalyticsDashboardPageTests
{
    private const int IndexIdentifier = 7;

    private ISearchAnalyticsService analytics = null!;
    private IPageLinkGenerator links = null!;
    private AnalyticsDashboardPage page = null!;

    [SetUp]
    public void SetUp()
    {
        analytics = Substitute.For<ISearchAnalyticsService>();
        analytics.GetReportAsync(Arg.Any<SearchAnalyticsQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Report()));

        links = Substitute.For<IPageLinkGenerator>();
        links.GetPath<ZeroResultRuleCreatePage>(Arg.Any<PageParameterValues>()).Returns("/admin/lucene/indexes/tuning/7/analytics/SEED");

        page = new AnalyticsDashboardPage(
            Storage.Holding(IndexIdentifier, "articles"),
            analytics,
            links,
            new FakeTime(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc)))
        {
            IndexIdentifier = IndexIdentifier
        };
    }

    [Test]
    public async Task Load_MapsEveryReportOntoTheClientDto()
    {
        var response = await page.Load(Request(), CancellationToken.None);
        var report = response.Result;

        Expect.Multiple(() =>
        {
            Assert.That(report.Error, Is.Empty);
            Assert.That(report.TopQueries.Select(row => row.Query), Is.EqualTo(new[] { "espresso" }));
            Assert.That(report.TopQueries[0].P95ProcessingTimeMs, Is.EqualTo(38), "the top queries table shows p95 too");
            Assert.That(report.ZeroResultQueries[0].LastSeen, Is.EqualTo("2026-08-20"));
            Assert.That(report.ClickThrough[0].ClickThroughRate, Is.EqualTo(0.5).Within(0.001));
            Assert.That(report.ClickThrough[0].AverageClickedPosition, Is.EqualTo(2.5).Within(0.001));
            Assert.That(report.AverageClickedPosition, Is.EqualTo(3.5).Within(0.001));
            Assert.That(report.VolumeOverTime.Select(point => point.Day), Is.EqualTo(new[] { "2026-08-19" }));
            Assert.That(report.VolumeOverTime[0].ZeroResultVolume, Is.EqualTo(2), "the chart draws a second series");
            Assert.That(report.SlowestQueries[0].P95ProcessingTimeMs, Is.EqualTo(412));
            Assert.That(report.TotalSearches, Is.EqualTo(9));
            Assert.That(report.ZeroResultSearches, Is.EqualTo(3), "the zero-result rate tile divides these two");
            Assert.That(report.Clicks, Is.EqualTo(4), "the click-through rate tile divides these two");
        });
    }

    [Test]
    public async Task Load_PassesTheWholeLastDayOfTheRangeAndClampsTheLimit()
    {
        await page.Load(
            new AnalyticsRequest { From = "2026-08-01", To = "2026-08-21", Limit = 5000 },
            CancellationToken.None);

        var query = (SearchAnalyticsQuery)analytics.ReceivedCalls().Single().GetArguments()[0]!;

        Expect.Multiple(() =>
        {
            Assert.That(query.IndexName, Is.EqualTo("articles"));
            Assert.That(query.FromUtc, Is.EqualTo(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified)));
            Assert.That(query.ToUtc.Date, Is.EqualTo(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Unspecified)));
            Assert.That(query.ToUtc.Hour, Is.EqualTo(23));
            Assert.That(query.Limit, Is.EqualTo(AnalyticsDashboardPage.MaxLimit));
        });
    }

    [TestCase("nonsense", "2026-08-21")]
    [TestCase("2026-08-01", "")]
    [TestCase("2026-08-21", "2026-08-01")]
    public async Task Load_RejectsARangeItCannotUse(string from, string to)
    {
        var response = await page.Load(new AnalyticsRequest { From = from, To = to }, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Result.Error, Is.Not.Empty);
            Assert.That(analytics.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task CreateRule_DeepLinksToTheCreatePageWithTheQuerySeeded()
    {
        var response = await page.CreateRule(new CreateRuleRequest { Query = "cold brew / iced" });

        var parameters = (PageParameterValues)links.ReceivedCalls().Single().GetArguments()[0]!;
        parameters.TryGetValue(typeof(ZeroResultRuleCreatePage), out object? seed);
        parameters.TryGetValue(typeof(IndexTuningSection), out object? index);

        Expect.Multiple(() =>
        {
            Assert.That(RuleSeed.Decode(seed as string), Is.EqualTo(("articles", "cold brew / iced")));
            Assert.That(index, Is.EqualTo(IndexIdentifier), "the create page also needs the index segment of the URL");
            Assert.That(
                parameters.Select(entry => entry.Key),
                Is.EqualTo(new[] { typeof(IndexTuningSection), typeof(ZeroResultRuleCreatePage) }),
                "two parameterized ancestors, so the values must be added in the order they appear in the URL");
            Assert.That(response, Is.Not.Null);
        });
    }

    [Test]
    public void RuleSeed_RoundTripsAndFallsBackOnGarbage()
    {
        Expect.Multiple(() =>
        {
            Assert.That(RuleSeed.Decode(RuleSeed.Encode("articles", "café ☕")), Is.EqualTo(("articles", "café ☕")));
            Assert.That(RuleSeed.Decode(ZeroResultRuleCreatePage.EmptySeed), Is.EqualTo((string.Empty, string.Empty)));
            Assert.That(RuleSeed.Decode("not base64 at all!"), Is.EqualTo((string.Empty, string.Empty)));
            Assert.That(RuleSeed.Encode("articles", "cold brew / iced"), Does.Not.Contain("/").And.Not.Contain("+").And.Not.Contain("="));
        });
    }

    /// <summary>The dashboard reports on the index in the URL, and says so to the client template.</summary>
    [Test]
    public async Task ConfigureTemplateProperties_LocksTheIndexToTheOneInTheUrl()
    {
        var properties = await page.ConfigureTemplateProperties(new AnalyticsDashboardClientProperties());

        Expect.Multiple(() =>
        {
            Assert.That(properties.SelectedIndexName, Is.EqualTo("articles"));
            Assert.That(properties.Today, Is.EqualTo("2026-08-21"));
        });
    }

    [Test]
    public void PageAndCommands_AreBehindTheApplicationsPermissions()
    {
        var pagePermission = typeof(AnalyticsDashboardPage)
            .GetCustomAttributes(typeof(UIEvaluatePermissionAttribute), inherit: false)
            .Cast<UIEvaluatePermissionAttribute>()
            .Single();

        var createPagePermission = typeof(ZeroResultRuleCreatePage)
            .GetCustomAttributes(typeof(UIEvaluatePermissionAttribute), inherit: false)
            .Cast<UIEvaluatePermissionAttribute>()
            .Single();

        Expect.Multiple(() =>
        {
            Assert.That(pagePermission.Permission, Is.EqualTo(SystemPermissions.VIEW));
            Assert.That(Command(nameof(AnalyticsDashboardPage.Load)).Permission, Is.EqualTo(SystemPermissions.VIEW));
            Assert.That(Command(nameof(AnalyticsDashboardPage.CreateRule)).Permission, Is.EqualTo(SystemPermissions.CREATE));
            Assert.That(createPagePermission.Permission, Is.EqualTo(SystemPermissions.CREATE));
        });
    }

    private static PageCommandAttribute Command(string method) =>
        typeof(AnalyticsDashboardPage)
            .GetMethod(method)!
            .GetCustomAttributes(typeof(PageCommandAttribute), inherit: false)
            .Cast<PageCommandAttribute>()
            .Single();

    private static AnalyticsRequest Request() =>
        new() { From = "2026-08-01", To = "2026-08-21", Limit = 20 };

    private static SearchAnalyticsReport Report() =>
        new(
            [new QueryVolume("espresso", 5, 38)],
            [new ZeroResultQuery("cold brew", 3, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc))],
            [new QueryClickThrough("espresso", 4, 2, 0.5, 2.5)],
            3.5,
            [new SearchVolumePoint(new DateOnly(2026, 8, 19), 9, 2)],
            [new SlowQuery("espresso", 5, 412)],
            9,
            3,
            4);

    private sealed class FakeTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
