using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Endpoints;
using XpSearch.Core.Options;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the aggregate query log of spec §9.2: what the worker writes, what a click updates, what
/// retention deletes, and that a failing sink still leaves <c>/events</c> answering 202.
/// </summary>
[TestFixture]
internal sealed class QueryLogTests
{
    [Test]
    public async Task QueueWorker_PersistsTheQueuedRow()
    {
        var store = new InMemoryQueryLogStore();
        var entry = Entry("mugs", DateTime.UtcNow, results: 4);

        await XpSearchQueryLogQueueWorker.ProcessAsync(store, QueryLogWorkItem.Append(entry), CancellationToken.None);

        Assert.That(store.Rows, Is.EqualTo(new[] { entry }).AsCollection);
    }

    [Test]
    public async Task QueuedClick_RecordsThePositionOnTheRowOfItsQuery()
    {
        var store = new InMemoryQueryLogStore();

        await store.AppendAsync(Entry("mugs", DateTime.UtcNow, results: 4, queryId: "q-1"), CancellationToken.None);
        await XpSearchQueryLogQueueWorker.ProcessAsync(store, QueryLogWorkItem.Click("q-1", 3), CancellationToken.None);

        Assert.That(store.Rows.Single().ClickedPosition, Is.EqualTo(3));
    }

    [Test]
    public async Task QueuedClick_ForAnUnknownQuery_ChangesNothing()
    {
        var store = new InMemoryQueryLogStore();

        await store.AppendAsync(Entry("mugs", DateTime.UtcNow, results: 4, queryId: "q-1"), CancellationToken.None);
        await XpSearchQueryLogQueueWorker.ProcessAsync(store, QueryLogWorkItem.Click("q-2", 3), CancellationToken.None);

        Assert.That(store.Rows.Single().ClickedPosition, Is.Null);
    }

    [Test]
    public async Task RetentionTask_DeletesOnlyRowsOlderThanTheRetentionWindow()
    {
        var store = new InMemoryQueryLogStore();
        var options = new XpSearchOptions();
        options.Analytics.RetentionDays = 30;
        options.Analytics.RetentionBatchSize = 2;

        await store.AppendAsync(Entry("old", DateTime.UtcNow.AddDays(-400)), CancellationToken.None);
        await store.AppendAsync(Entry("older", DateTime.UtcNow.AddDays(-60)), CancellationToken.None);
        await store.AppendAsync(Entry("oldest", DateTime.UtcNow.AddDays(-31)), CancellationToken.None);
        await store.AppendAsync(Entry("recent", DateTime.UtcNow.AddDays(-29)), CancellationToken.None);
        await store.AppendAsync(Entry("newest", DateTime.UtcNow.AddDays(-10)), CancellationToken.None);

        await Retention(store, options).Execute(null!, CancellationToken.None);

        Assert.That(store.Rows.Select(row => row.QueryText), Is.EqualTo(new[] { "recent", "newest" }).AsCollection);
    }

    [Test]
    public async Task RetentionTask_WithNoConfiguredWindow_KeepsTheLastYear()
    {
        var store = new InMemoryQueryLogStore();

        await store.AppendAsync(Entry("ancient", DateTime.UtcNow.AddDays(-400)), CancellationToken.None);
        await store.AppendAsync(Entry("old", DateTime.UtcNow.AddDays(-300)), CancellationToken.None);

        // Nothing configured anything: the option's own default, 365, is the window.
        await Retention(store, new XpSearchOptions()).Execute(null!, CancellationToken.None);

        Assert.That(store.Rows.Select(row => row.QueryText), Is.EqualTo(new[] { "old" }).AsCollection);
    }

    [Test]
    public async Task RetentionTask_PrunesAnsweredSuggestionsWithTheSameCutoff()
    {
        var store = new InMemoryQueryLogStore();
        var popularity = new FakePopularitySignalStore { Answered = { [TestCorpus.IndexName] = 3 } };
        var synonyms = new FakeSynonymSuggestionStore { Answered = { [TestCorpus.IndexName] = 1 } };
        var options = new XpSearchOptions();
        options.Analytics.RetentionDays = 30;
        options.Analytics.RetentionBatchSize = 2;

        var logger = new RecordingRetentionLogger();

        await Retention(store, options, popularity, synonyms, logger).Execute(null!, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(popularity.Pruned.Select(call => call.BatchSize), Is.EqualTo(new[] { 2, 2 }).AsCollection);
            Assert.That(popularity.Pruned.Select(call => call.IndexName), Is.All.EqualTo(TestCorpus.IndexName));
            Assert.That(synonyms.Pruned.Select(call => call.BatchSize), Is.EqualTo(new[] { 2 }).AsCollection);
            Assert.That(popularity.Pruned[0].CutoffUtc, Is.EqualTo(synonyms.Pruned[0].CutoffUtc));
            Assert.That(popularity.Pruned[0].CutoffUtc, Is.EqualTo(DateTime.UtcNow.AddDays(-30)).Within(TimeSpan.FromMinutes(1)));

            // The same string the task returns as its result, which is the Last result column.
            Assert.That(
                logger.Messages.Last(),
                Does.StartWith($"{TestCorpus.IndexName}: 0 query log rows, 3 popularity suggestions, 1 synonym suggestion (older than "));
        });
    }

    /// <summary>
    /// AR-2: each index is pruned with its own window, and rows of an index nobody registers any more
    /// are pruned with the code-configured defaults.
    /// </summary>
    [Test]
    public async Task RetentionTask_PrunesEachIndexWithItsOwnWindow_AndOrphansWithTheDefaults()
    {
        var store = new InMemoryQueryLogStore();

        await store.AppendAsync(Entry("registered", DateTime.UtcNow.AddDays(-10)), CancellationToken.None);
        await store.AppendAsync(Entry("orphaned", DateTime.UtcNow.AddDays(-400), index: "Retired"), CancellationToken.None);

        var settings = new PerIndexSettings(new XpSearchOptions());

        // The registered index keeps a day of analytics; everything else keeps the shipped 365.
        settings[TestCorpus.IndexName] = new XpSearchIndexSettings { RetentionDays = 1 };

        var logger = new RecordingRetentionLogger();

        await new XpSearchQueryLogRetentionTask(
            store,
            new FakePopularitySignalStore(),
            new FakeSynonymSuggestionStore(),
            Registry(TestCorpus.IndexName),
            settings,
            logger)
            .Execute(null!, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(store.Rows, Is.Empty, "both the one-day index and the orphan's year-old row are gone");
            Assert.That(
                logger.Messages.Last(),
                Does.StartWith($"{TestCorpus.IndexName}: 1 query log row, 0 popularity suggestions, 0 synonym suggestions (older than "));
            Assert.That(logger.Messages.Last(), Does.Contain("; Retired: 1 query log row"));
            Assert.That(
                logger.Messages,
                Has.Some.Contains("Retired").And.Some.Contains("not registered any more"));
        });
    }

    /// <summary>An index with a long window keeps rows a short-windowed one would lose.</summary>
    [Test]
    public async Task RetentionTask_KeepsTheRowsOfAnIndexWithALongerWindow()
    {
        var store = new InMemoryQueryLogStore();

        await store.AppendAsync(Entry("kept", DateTime.UtcNow.AddDays(-10)), CancellationToken.None);

        var settings = new PerIndexSettings(new XpSearchOptions());
        settings[TestCorpus.IndexName] = new XpSearchIndexSettings { RetentionDays = 365 };

        await new XpSearchQueryLogRetentionTask(
            store,
            new FakePopularitySignalStore(),
            new FakeSynonymSuggestionStore(),
            Registry(TestCorpus.IndexName),
            settings,
            NullLogger<XpSearchQueryLogRetentionTask>.Instance)
            .Execute(null!, CancellationToken.None);

        Assert.That(store.Rows.Select(row => row.QueryText), Is.EqualTo(new[] { "kept" }).AsCollection);
    }

    [TestCase(0, 10, false, Description = "a pending row is never pruned, however old")]
    [TestCase(1, 10, true, Description = "an accepted row older than the cutoff is pruned")]
    [TestCase(2, 1, false, Description = "a dismissed row inside the window is kept")]
    public void PrunableSuggestion_IsAnsweredAndOlderThanTheCutoff(int state, int ageDays, bool expected)
    {
        var cutoff = DateTime.UtcNow.AddDays(-5);

        Assert.That(SuggestionRetention.IsPrunable(state, DateTime.UtcNow.AddDays(-ageDays), cutoff), Is.EqualTo(expected));
    }

    private static XpSearchQueryLogRetentionTask Retention(
        IQueryLogStore store,
        XpSearchOptions options,
        FakePopularitySignalStore? popularity = null,
        FakeSynonymSuggestionStore? synonyms = null,
        ILogger<XpSearchQueryLogRetentionTask>? logger = null) =>
        new(
            store,
            popularity ?? new FakePopularitySignalStore(),
            synonyms ?? new FakeSynonymSuggestionStore(),
            Registry(TestCorpus.IndexName),
            new PerIndexSettings(options),
            logger ?? NullLogger<XpSearchQueryLogRetentionTask>.Instance);

    private static ILuceneIndexAccessor Registry(params string[] indexNames) => TestIndexRegistry.Of(indexNames);

    /// <summary>Captures what the task reported, which is also what it returns as its result message.</summary>
    private sealed class RecordingRetentionLogger : ILogger<XpSearchQueryLogRetentionTask>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Messages.Add(formatter(state, exception));
        }
    }

    [Test]
    public async Task Events_AnswerTwoHundredAndTwoWhenLoggingThrows()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ISearchEventSink, ThrowingSearchEventSink>();

        var app = builder.Build();
        app.MapXpSearch();

        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var response = await client.PostAsJsonAsync(
            ContractConstants.EventsRoute,
            new EventRequest { Type = EventType.Click, QueryId = "q-1", ResultId = "doc-1", Position = 1 });

        await app.StopAsync();
        await app.DisposeAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    private static QueryLogEntry Entry(string query, DateTime timestamp, int results = 0, string queryId = "q-1", string? index = null) =>
        new(queryId, index ?? TestCorpus.IndexName, query, results, timestamp, "Store", "en", 12);

    /// <summary>A sink that fails the way a database outage would.</summary>
    private sealed class ThrowingSearchEventSink : ISearchEventSink
    {
        public Task HandleAsync(EventRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The activity could not be logged.");
    }
}
