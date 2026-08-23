using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Options;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the reports of spec §9.3 and the query suggestions of §4.3 over a seeded query log.
/// </summary>
[TestFixture]
internal sealed class SearchAnalyticsTests
{
    private static readonly DateTime Day = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private InMemoryQueryLogStore store = null!;

    [SetUp]
    public void SeedTheLog()
    {
        store = new InMemoryQueryLogStore();

        // "mugs" is popular and clicked, "kettle" is popular and never clicked, "teapot" finds nothing,
        // "slow" is rare and slow.
        Add("mugs", Day, results: 5, ms: 10, clicked: 1);
        Add("mugs", Day.AddHours(1), results: 5, ms: 12, clicked: 3);
        Add("mugs", Day.AddHours(2), results: 5, ms: 11);
        Add("mugs", Day.AddHours(3), results: 5, ms: 90);
        Add("kettle", Day.AddDays(1), results: 2, ms: 20);
        Add("kettle", Day.AddDays(1).AddHours(1), results: 2, ms: 22);
        Add("teapot", Day.AddDays(2), results: 0, ms: 5);
        Add("teapot", Day.AddDays(2).AddHours(1), results: 0, ms: 6);
        Add("slow", Day.AddDays(2).AddHours(2), results: 1, ms: 400);
    }

    [Test]
    public async Task Report_RanksQueriesByVolumeAndCountsZeroResultQueriesWithTheirLastSighting()
    {
        var report = await Report();

        Assert.That(report.TotalSearches, Is.EqualTo(9));
        Assert.That(report.TopQueries.Select(entry => entry.Query), Is.EqualTo(new[] { "mugs", "kettle", "teapot", "slow" }).AsCollection);
        Assert.That(report.TopQueries[0].Volume, Is.EqualTo(4));
        Assert.That(report.TopQueries[0].P95ProcessingTimeMs, Is.EqualTo(90), "the dashboard shows p95 on the top queries table too");
        Assert.That(report.ZeroResultSearches, Is.EqualTo(2), "the zero-result rate tile divides this by TotalSearches");

        var zero = report.ZeroResultQueries.Single();

        Assert.That(zero.Query, Is.EqualTo("teapot"));
        Assert.That(zero.Volume, Is.EqualTo(2));
        Assert.That(zero.LastSeen, Is.EqualTo(Day.AddDays(2).AddHours(1)));
    }

    [Test]
    public async Task Report_MeasuresClickThroughRateAndAverageClickedPosition()
    {
        var report = await Report();

        var mugs = report.ClickThrough.Single(entry => entry.Query == "mugs");
        var kettle = report.ClickThrough.Single(entry => entry.Query == "kettle");

        Assert.That(mugs.Clicks, Is.EqualTo(2));
        Assert.That(mugs.ClickThroughRate, Is.EqualTo(0.5));
        Assert.That(mugs.AverageClickedPosition, Is.EqualTo(2));
        Assert.That(kettle.ClickThroughRate, Is.Zero);
        Assert.That(kettle.AverageClickedPosition, Is.Null);
        Assert.That(report.AverageClickedPosition, Is.EqualTo(2));
        Assert.That(report.Clicks, Is.EqualTo(2), "the click-through rate tile divides this by TotalSearches");
    }

    [Test]
    public async Task Report_BucketsVolumeByDayAndKeepsEmptyDays()
    {
        var report = await Report();

        Assert.That(
            report.VolumeOverTime.Select(point => point.Volume),
            Is.EqualTo(new[] { 4, 2, 3, 0 }).AsCollection);

        // The chart's second series: only the two "teapot" searches, on the third day.
        Assert.That(
            report.VolumeOverTime.Select(point => point.ZeroResultVolume),
            Is.EqualTo(new[] { 0, 0, 2, 0 }).AsCollection);
    }

    [Test]
    public async Task Report_RanksTheSlowestQueriesByTheirNinetyFifthPercentile()
    {
        var report = await Report();

        Assert.That(report.SlowestQueries[0].Query, Is.EqualTo("slow"));
        Assert.That(report.SlowestQueries[0].P95ProcessingTimeMs, Is.EqualTo(400));

        // 4 samples: the 95th percentile by nearest rank is the slowest of them.
        Assert.That(report.SlowestQueries.Single(entry => entry.Query == "mugs").P95ProcessingTimeMs, Is.EqualTo(90));
    }

    [Test]
    public async Task Suggestions_ReturnLoggedQueriesByVolume_WithoutTheOnesThatFoundNothing()
    {
        var suggestions = await Suggestions("", limit: 10);

        Assert.That(suggestions, Is.EqualTo(new[] { "mugs", "kettle", "slow" }).AsCollection);
    }

    [Test]
    public async Task Suggestions_PrefixMatchAndHonourTheLimit()
    {
        Assert.That(await Suggestions("k", limit: 10), Is.EqualTo(new[] { "kettle" }).AsCollection);
        Assert.That(await Suggestions("", limit: 1), Is.EqualTo(new[] { "mugs" }).AsCollection);
        Assert.That(await Suggestions("zzz", limit: 10), Is.Empty);
    }

    [Test]
    public async Task Suggestions_AreCachedForTheConfiguredCacheTtl()
    {
        var now = Day.AddDays(3);
        var options = new XpSearchOptions();
        var service = new QuerySuggestionService(store, Microsoft.Extensions.Options.Options.Create(options), () => now);

        var before = await service.SuggestAsync(TestCorpus.IndexName, "k", 10, CancellationToken.None);

        Add("kettle", now, results: 2, ms: 5);
        Add("kite", now, results: 2, ms: 5);

        var cached = await service.SuggestAsync(TestCorpus.IndexName, "k", 10, CancellationToken.None);

        now = now.Add(options.CacheTtl).AddSeconds(1);

        var fresh = await service.SuggestAsync(TestCorpus.IndexName, "k", 10, CancellationToken.None);

        Assert.That(cached, Is.EqualTo(before).AsCollection);
        Assert.That(fresh, Is.EqualTo(new[] { "kettle", "kite" }).AsCollection);
    }

    private Task<SearchAnalyticsReport> Report() =>
        new SearchAnalyticsService(store).GetReportAsync(
            new SearchAnalyticsQuery(TestCorpus.IndexName, Day.Date, Day.Date.AddDays(3), Limit: 10),
            CancellationToken.None);

    private Task<IReadOnlyList<string>> Suggestions(string prefix, int limit) =>
        new QuerySuggestionService(store, Microsoft.Extensions.Options.Options.Create(new XpSearchOptions()), () => Day.AddDays(3))
            .SuggestAsync(TestCorpus.IndexName, prefix, limit, CancellationToken.None);

    private void Add(string query, DateTime timestamp, int results, int ms, int? clicked = null) =>
        store.Rows.Add(new QueryLogEntry(
            $"q-{store.Rows.Count}",
            TestCorpus.IndexName,
            query,
            results,
            timestamp,
            "Store",
            "en",
            ms,
            clicked));
}
