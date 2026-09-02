using System.Net;
using System.Text.Json;

using NUnit.Framework;

using XpSearch.Client.Contract;

namespace XpSearch.Client.Tests;

[TestFixture]
internal sealed class XpSearchIngestionClientTests
{
    private const string Ok = """{"indexed":1,"failed":0,"errors":[],"taskId":"t","tookMs":5}""";
    private const string Deleted = """{"deleted":1,"taskId":"t","tookMs":3}""";

    private FakeHandler handler = null!;
    private List<TimeSpan> delays = null!;

    [SetUp]
    public void SetUp()
    {
        handler = new FakeHandler();
        delays = [];
    }

    [TearDown]
    public void TearDown() => handler.Dispose();

    private XpSearchIngestionClient Client(Action<XpSearchIngestionClientOptions>? configure = null)
    {
        var options = new XpSearchIngestionClientOptions
        {
            // The schedule is asserted, so the jitter factor is pinned and nothing actually sleeps.
            NextDouble = () => 1.0,
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);

                return Task.CompletedTask;
            },
        };

        configure?.Invoke(options);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        return new XpSearchIngestionClient(http, "xps_secret", options);
    }

    private static PushDocument[] Documents(int count) =>
        [.. Enumerable.Range(1, count).Select(i => XpSearchIngestionClient.Document($"id-{i}", new { title = $"Doc {i}" }, "pim"))];

    private static int DocumentCountOf(CapturedRequest request) =>
        JsonDocument.Parse(request.Body!).RootElement.GetProperty("documents").GetArrayLength();

    [Test]
    public async Task UpsertAsync_SendsBearerKeyAndTheDocumentsRoute()
    {
        handler.RespondOk(Ok);

        await Client().Index("products").UpsertAsync(Documents(1));

        var request = handler.Requests.Single();

        Expect.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo("Bearer xps_secret"));
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri, Is.EqualTo("https://example.com/api/xpsearch/admin/indexes/products/documents"));
            Assert.That(request.Body, Does.Contain("\"_source\":\"pim\"").And.Contain("\"title\":\"Doc 1\""));
        });
    }

    [Test]
    public async Task UpsertAsync_SplitsOnTheDocumentCountCap()
    {
        handler.Fallback = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ok) };

        var result = await Client(o => o.MaxDocumentsPerRequest = 2).Index("products").UpsertAsync(Documents(5));

        Expect.Multiple(() =>
        {
            Assert.That(handler.Requests.Select(DocumentCountOf), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(result.Batches, Is.EqualTo(3));
            Assert.That(result.Indexed, Is.EqualTo(3));
            Assert.That(result.TaskIds, Is.EqualTo(new[] { "t", "t", "t" }));
        });
    }

    [Test]
    public async Task UpsertAsync_SplitsOnTheBodySizeCapBeforeTheCountCap()
    {
        handler.Fallback = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ok) };

        // One serialized document is ~45 bytes, so 150 bytes of body leaves room for two of them.
        var result = await Client(o => o.MaxRequestBytes = 150).Index("products").UpsertAsync(Documents(5));

        Expect.Multiple(() =>
        {
            Assert.That(result.Batches, Is.GreaterThan(1));
            Assert.That(handler.Requests.Select(DocumentCountOf).Sum(), Is.EqualTo(5));
            Assert.That(
                handler.Requests.Select(r => System.Text.Encoding.UTF8.GetByteCount(r.Body!)),
                Is.All.LessThanOrEqualTo(150));
        });
    }

    [Test]
    public async Task UpsertAsync_AggregatesTotalsAndEveryPerDocumentError()
    {
        handler
            .RespondOk("""{"indexed":1,"failed":1,"errors":[{"id":"id-2","field":"price","message":"not a number"}],"taskId":"a","tookMs":5}""")
            .RespondOk("""{"indexed":2,"failed":0,"errors":[],"tookMs":5}""");

        var result = await Client(o => o.MaxDocumentsPerRequest = 2).Index("products").UpsertAsync(Documents(4));

        Expect.Multiple(() =>
        {
            Assert.That(result.Indexed, Is.EqualTo(3));
            Assert.That(result.Failed, Is.EqualTo(1));
            Assert.That(result.Errors.Single().Id, Is.EqualTo("id-2"));
            Assert.That(result.Errors.Single().Field, Is.EqualTo("price"));
            // The second batch was awaited, so it carries no task id: one entry, not two.
            Assert.That(result.TaskIds, Is.EqualTo(new[] { "a" }));
        });
    }

    [Test]
    public void UpsertAsync_ReportsWhatWasAlreadyWrittenWhenALaterBatchFails()
    {
        handler
            .RespondOk("""{"indexed":2,"failed":0,"errors":[],"taskId":"a","tookMs":5}""")
            .Throw(new HttpRequestException("connection reset"));

        var exception = Expect.ThrowsAsync<XpSearchIngestionException>(
            () => Client(o =>
            {
                o.MaxDocumentsPerRequest = 2;
                o.MaxAttempts = 1;
            }).Index("products").UpsertAsync(Documents(4)));

        Expect.Multiple(() =>
        {
            Assert.That(exception.PartialUpsert, Is.Not.Null);
            Assert.That(exception.PartialUpsert!.Indexed, Is.EqualTo(2));
            Assert.That(exception.PartialUpsert.Batches, Is.EqualTo(1));
            Assert.That(exception.InnerException, Is.TypeOf<HttpRequestException>());
        });
    }

    [Test]
    public async Task SendAsync_BacksOffExponentiallyOnRetryableStatusesAndTransportFailures()
    {
        handler
            .Respond(HttpStatusCode.RequestTimeout, "{}")
            .Throw(new HttpRequestException("reset"))
            .Respond(HttpStatusCode.InternalServerError, "{}")
            .RespondOk(Ok);

        await Client().Index("products").UpsertAsync(Documents(1));

        Expect.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
        });
    }

    [Test]
    public async Task SendAsync_HonoursRetryAfterInsteadOfTheBackoff()
    {
        handler
            .Respond(HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "7"))
            .RespondOk(Ok);

        await Client().Index("products").UpsertAsync(Documents(1));

        Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(7) }));
    }

    [Test]
    public async Task SendAsync_CapsRetryAfterAtMaxRetryDelay()
    {
        handler
            .Respond(HttpStatusCode.ServiceUnavailable, "{}", ("Retry-After", "600"))
            .RespondOk(Ok);

        await Client(o => o.MaxRetryDelay = TimeSpan.FromSeconds(30)).Index("products").UpsertAsync(Documents(1));

        Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(30) }));
    }

    [Test]
    public void SendAsync_NeverRetriesAValidationFailureAndSurfacesTheProblemDetails()
    {
        handler.Respond(
            HttpStatusCode.BadRequest,
            """{"title":"The request is not valid.","status":400,"errors":{"documents":["At least one document is required."]}}""");

        var exception = Expect.ThrowsAsync<XpSearchIngestionException>(
            () => Client().Index("products").UpsertAsync(Documents(1)));

        Expect.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(delays, Is.Empty);
            Assert.That(exception.StatusCode, Is.EqualTo(400));
            Assert.That(exception.Problem!.Title, Is.EqualTo("The request is not valid."));
            Assert.That(exception.Problem.Errors!["documents"], Is.EqualTo(new[] { "At least one document is required." }));
        });
    }

    [Test]
    public void SendAsync_GivesUpAfterMaxAttempts()
    {
        handler.Fallback = () => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>502</html>") };

        var exception = Expect.ThrowsAsync<XpSearchIngestionException>(
            () => Client(o => o.MaxAttempts = 3).Index("products").UpsertAsync(Documents(1)));

        Expect.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(exception.StatusCode, Is.EqualTo(502));
            // Not Problem Details: the raw body is kept instead.
            Assert.That(exception.Problem, Is.Null);
            Assert.That(exception.ResponseBody, Is.EqualTo("<html>502</html>"));
        });
    }

    [Test]
    public async Task Verbs_MapOneToOneOntoTheFrozenRoutes()
    {
        handler.Fallback = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"indexed":0,"failed":0,"errors":[],"tookMs":1,"deleted":1,"indexes":[],"index":"products","health":"healthy","documents":{"total":0,"bySource":{}}}"""),
        };

        var client = Client();
        var index = client.Index("products");

        await client.ListIndexesAsync();
        await index.GetStatusAsync();
        await index.PatchAsync("a b", new Dictionary<string, object?> { ["price"] = 9.5 });
        await index.DeleteAsync("a b");
        await index.DeleteManyAsync(["x", "y"]);
        await index.ClearAsync("pim");
        await index.ClearAsync();
        await index.RebuildAsync();

        Assert.That(
            handler.Requests.Select(r => $"{r.Method} {r.Uri["https://example.com/api/xpsearch/admin/".Length..]}"),
            Is.EqualTo(new[]
            {
                "GET indexes",
                "GET indexes/products/status",
                "PATCH indexes/products/documents/a%20b",
                "DELETE indexes/products/documents/a%20b",
                "POST indexes/products/documents/delete",
                "POST indexes/products/clear?source=pim",
                "POST indexes/products/clear",
                "POST indexes/products/rebuild",
            }));
    }

    [Test]
    public async Task PatchAsync_SendsTheAttributesAsTheBody()
    {
        handler.RespondOk(Ok);

        await Client().Index("products").PatchAsync("id-1", new Dictionary<string, object?> { ["price"] = 9.5, ["gone"] = null }, waitForIndex: true);

        var request = handler.Requests.Single();

        Expect.Multiple(() =>
        {
            Assert.That(request.Body, Is.EqualTo("""{"price":9.5,"gone":null}"""));
            Assert.That(request.Uri, Does.EndWith("/documents/id-1?waitForIndex=true"));
        });
    }

    [Test]
    public async Task DeleteManyAsync_SplitsOnTheCountCapAndSumsTheDeletes()
    {
        handler.Fallback = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Deleted) };

        var result = await Client(o => o.MaxDocumentsPerRequest = 2).Index("products").DeleteManyAsync(["a", "b", "c"]);

        Expect.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Body, Is.EqualTo("""{"ids":["a","b"]}"""));
            Assert.That(handler.Requests[1].Body, Is.EqualTo("""{"ids":["c"]}"""));
            Assert.That(result.Deleted, Is.EqualTo(2));
            Assert.That(result.Batches, Is.EqualTo(2));
        });
    }

    [Test]
    public void Document_TurnsAnObjectIntoAnOpenAttributeBag()
    {
        var document = XpSearchIngestionClient.Document("sku-1", new { title = "Yirgacheffe", price = 18.5, tags = new[] { "coffee" } }, "pim");

        Expect.Multiple(() =>
        {
            Assert.That(document.Id, Is.EqualTo("sku-1"));
            Assert.That(document.Source, Is.EqualTo("pim"));
            Assert.That(document.Attributes["title"].GetString(), Is.EqualTo("Yirgacheffe"));
            Assert.That(document.Attributes["tags"].EnumerateArray().Single().GetString(), Is.EqualTo("coffee"));
        });
    }

    [Test]
    public void Constructor_RefusesAnHttpClientWithoutABaseAddress() =>
        Expect.Throws<ArgumentException>(() => new XpSearchIngestionClient(new HttpClient(), "xps_secret"));
}
