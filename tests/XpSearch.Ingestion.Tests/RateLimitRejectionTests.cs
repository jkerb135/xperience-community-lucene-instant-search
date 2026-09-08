using System.Net;

using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Caching;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Endpoints;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Schema;
using XpSearch.Ingestion.Security;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// A caller past the per-key limit is answered <c>429</c> with <c>Retry-After</c> - what
/// <c>ingestion.md</c> promises and what the typed clients back off on - not the rate limiting
/// middleware's default <c>503</c>. Its own host, so the burst never touches the shared fixture's
/// budget.
/// </summary>
[TestFixture]
internal sealed class RateLimitRejectionTests
{
    [Test]
    public async Task PastTheLimit_TheCallerIsAnswered429WithRetryAfter()
    {
        using var index = new TestLuceneIndex(TestHarness.IndexName);
        var strategies = Substitute.For<IIndexStrategySource>();
        strategies.GetIndexNames().Returns([TestHarness.IndexName]);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ILuceneIndexAccessor>(index);
        builder.Services.AddSingleton<ILuceneClient>(new CacheEvictingLuceneClient(index, new NullSearchCache(), index));
        builder.Services.AddSingleton<IExternalDocumentStore>(new InMemoryDocumentStore());
        builder.Services.AddSingleton<IApiKeyStore>(new InMemoryApiKeyStore());
        builder.Services.AddSingleton<IIngestionLog, RecordingIngestionLog>();
        builder.Services.AddSingleton<IIngestionSchemaProvider>(new StaticSchemaProvider(TestSchema.Products()));
        builder.Services.AddSingleton(strategies);
        builder.Services.AddSingleton<IRebuildCompletionWaiter, ImmediateRebuildWaiter>();
        // Nothing is ever queued: the limiter answers before any route runs.
        builder.Services.AddSingleton(Substitute.For<IIngestionQueue>());
        builder.Services.AddXpSearchIngestion(options =>
        {
            options.RateLimitPermitsPerWindow = 1;
            options.RateLimitWindow = TimeSpan.FromMinutes(5);
        });

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapXpSearchIngestion();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            // No key: the limiter partitions by address and runs before the key filter, so the first
            // request reaches the endpoint (401) and the second is turned away by the limiter.
            using var first = await client.GetAsync(IngestionContractConstants.IndexesRoute);
            using var second = await client.GetAsync(IngestionContractConstants.IndexesRoute);

            Expect.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That(second.Headers.RetryAfter?.Delta, Is.Not.Null.And.GreaterThan(TimeSpan.Zero), "Retry-After tells the client how long to back off");
            });
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
