using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Endpoints;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Schema;
using XpSearch.Ingestion.Security;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// The routes of spec §10.1 over real HTTP, on a minimal host with the Xperience-bound services
/// substituted: the Lucene client and index accessor are the test index, and persistence is in
/// memory. Everything between the route and those seams is the shipped code.
/// </summary>
[TestFixture]
internal sealed class EndpointTests
{
    private const string Index = TestHarness.IndexName;

    private WebApplication app = null!;
    private HttpClient client = null!;
    private TestLuceneIndex index = null!;
    private InMemoryDocumentStore store = null!;
    private string writeKey = null!;
    private string readKey = null!;
    private string adminKey = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        index = new TestLuceneIndex(Index);
        store = new InMemoryDocumentStore();

        var keyStore = new InMemoryApiKeyStore();
        var strategies = Substitute.For<IIndexStrategySource>();
        strategies.GetIndexNames().Returns([Index]);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSingleton<ILuceneIndexAccessor>(index);
        builder.Services.AddSingleton<ILuceneClient>(index);
        builder.Services.AddSingleton<IExternalDocumentStore>(store);
        builder.Services.AddSingleton<IApiKeyStore>(keyStore);
        builder.Services.AddSingleton<IIngestionLog, RecordingIngestionLog>();
        builder.Services.AddSingleton<IIngestionSchemaProvider>(new StaticSchemaProvider(TestSchema.Products()));
        builder.Services.AddSingleton(strategies);
        builder.Services.AddSingleton<IRebuildCompletionWaiter, ImmediateRebuildWaiter>();
        builder.Services.AddSingleton<IIngestionQueue, ImmediateQueue>();

        // The production registrations for everything else, including the rate limiting policy and
        // the rebuild replay decorator over the client registered above.
        builder.Services.AddXpSearchIngestion(options => options.MaxRequestBytes = 10L * 1024 * 1024);

        app = builder.Build();
        app.UseRateLimiter();
        app.MapXpSearchIngestion();

        await app.StartAsync();

        client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var keys = app.Services.GetRequiredService<IApiKeyService>();
        writeKey = (await keys.CreateAsync("write", new ApiKeyScopes { Indexes = [Index], Ops = ["write", "delete", "rebuild", "read"] }, null, default)).Key;
        readKey = (await keys.CreateAsync("read", new ApiKeyScopes { Indexes = [Index], Ops = ["read"] }, null, default)).Key;
        adminKey = (await keys.CreateAsync("admin", new ApiKeyScopes { Indexes = ["*"], Ops = ["*"] }, null, default)).Key;
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        index.Dispose();
    }

    [Test]
    public async Task Upsert_WithoutAKeyIs401()
    {
        var response = await client.PostAsJsonAsync($"/api/xpsearch/admin/indexes/{Index}/documents", Body("no-key"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Upsert_WithAReadOnlyKeyIs403()
    {
        var response = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents", readKey, Body("read-key"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Upsert_AnswersWithTheContractShape()
    {
        var response = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents", writeKey, Body("http-1"));
        var body = await response.Content.ReadFromJsonAsync<UpsertResponse>();

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("X-XpSearch-Api-Version").Single(), Is.EqualTo("1"));
            Assert.That(body!.Indexed, Is.EqualTo(1));
            Assert.That(body.Failed, Is.Zero);
            Assert.That(body.Errors, Is.Empty);
            Assert.That(body.TookMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(index.Stored("http-1", "title"), Is.EqualTo("Pushed over HTTP"));
        });
    }

    [Test]
    public async Task MoreThanAThousandDocumentsIs413()
    {
        var documents = Enumerable.Range(0, 1001).Select(number => new { id = $"bulk-{number}", title = "Too many" });
        var response = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents", writeKey, new { documents });

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(response.Content.ReadAsStringAsync().Result, Does.Contain("1000"));
        });
    }

    [Test]
    public async Task ABodyOverTenMegabytesIs413()
    {
        // One document whose body alone exceeds the cap; the limit is on the request, not the batch.
        string json = $$"""{"documents":[{"id":"huge","title":"{{new string('a', 11 * 1024 * 1024)}}"}]}""";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writeKey);

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [Test]
    public async Task StatusAndIndexListAnswerTheContractShapes()
    {
        await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents", writeKey, Body("status-1"));

        var status = await (await Send(HttpMethod.Get, $"/api/xpsearch/admin/indexes/{Index}/status", readKey)).Content.ReadFromJsonAsync<IndexStatus>();
        var list = await (await Send(HttpMethod.Get, "/api/xpsearch/admin/indexes", readKey)).Content.ReadFromJsonAsync<IndexListResponse>();

        Expect.Multiple(() =>
        {
            Assert.That(status!.Index, Is.EqualTo(Index));
            Assert.That(status.Documents.BySource["pim"], Is.GreaterThanOrEqualTo(1));
            Assert.That(status.Health, Is.EqualTo(Health.Healthy));
            Assert.That(list!.Indexes.Single().Name, Is.EqualTo(Index));
            Assert.That(list.Indexes.Single().AllowDynamicFields, Is.False);
            Assert.That(list.Indexes.Single().Schema.Select(field => field.Name), Does.Contain("price"));
            Assert.That(list.Indexes.Single().Schema.Single(field => field.Name == "tags").Type, Is.EqualTo(TypeEnum.TypeString));
        });
    }

    [Test]
    public async Task PatchDeleteClearAndRebuildAnswerTheDocumentedStatusCodes()
    {
        await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents", writeKey, Body("lifecycle-1"));

        var patch = await Send(HttpMethod.Patch, $"/api/xpsearch/admin/indexes/{Index}/documents/lifecycle-1", writeKey, new { price = 4.5 });
        var missing = await Send(HttpMethod.Patch, $"/api/xpsearch/admin/indexes/{Index}/documents/nope", writeKey, new { price = 4.5 });
        var delete = await Send(HttpMethod.Delete, $"/api/xpsearch/admin/indexes/{Index}/documents/lifecycle-1", writeKey);
        var clear = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/clear?source=pim", writeKey);
        var rebuild = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/rebuild", writeKey);
        var unknownIndex = await Send(HttpMethod.Get, "/api/xpsearch/admin/indexes/nope/status", adminKey);
        var outOfScopeIndex = await Send(HttpMethod.Get, "/api/xpsearch/admin/indexes/nope/status", readKey);

        Expect.Multiple(() =>
        {
            Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(rebuild.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(unknownIndex.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            // Scope is checked before existence, so a key that is not scoped to an index never learns
            // whether it exists.
            Assert.That(outOfScopeIndex.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task BatchDeleteRequiresExactlyOneOfIdsAndFilter()
    {
        var neither = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents/delete", writeKey, new { });
        var both = await Send(HttpMethod.Post, $"/api/xpsearch/admin/indexes/{Index}/documents/delete", writeKey, new { ids = new[] { "a" }, filter = new { source = "pim" } });

        Expect.Multiple(() =>
        {
            Assert.That(neither.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(both.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    private static object Body(string id) => new { documents = new[] { new { id, title = "Pushed over HTTP", _source = "pim" } } };

    private async Task<HttpResponseMessage> Send(HttpMethod method, string route, string key, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Runs queued work as soon as it is queued, so an HTTP test can assert on the index.</summary>
    private sealed class ImmediateQueue(IServiceProvider services) : IIngestionQueue
    {
        public int PendingCount => 0;

        public void Enqueue(IngestionWorkItem item) =>
            services.GetRequiredService<IIngestionWorkProcessor>().ProcessAsync(item, CancellationToken.None).GetAwaiter().GetResult();
    }
}
