using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Endpoints;
using XpSearch.Core.Options;
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

        await store.AppendAsync(Entry("old", DateTime.UtcNow.AddDays(-90)), CancellationToken.None);
        await store.AppendAsync(Entry("older", DateTime.UtcNow.AddDays(-60)), CancellationToken.None);
        await store.AppendAsync(Entry("oldest", DateTime.UtcNow.AddDays(-31)), CancellationToken.None);
        await store.AppendAsync(Entry("recent", DateTime.UtcNow.AddDays(-29)), CancellationToken.None);

        var task = new XpSearchQueryLogRetentionTask(
            store,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<XpSearchQueryLogRetentionTask>.Instance);

        await task.Execute(null!, CancellationToken.None);

        Assert.That(store.Rows.Select(row => row.QueryText), Is.EqualTo(new[] { "recent" }).AsCollection);
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

    private static QueryLogEntry Entry(string query, DateTime timestamp, int results = 0, string queryId = "q-1") =>
        new(queryId, TestCorpus.IndexName, query, results, timestamp, "Store", "en", 12);

    /// <summary>A sink that fails the way a database outage would.</summary>
    private sealed class ThrowingSearchEventSink : ISearchEventSink
    {
        public Task HandleAsync(EventRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The activity could not be logged.");
    }
}
