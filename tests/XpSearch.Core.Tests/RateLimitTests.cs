using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using XpSearch.Core;
using XpSearch.Core.Contract;
using XpSearch.Core.Endpoints;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the public endpoints' rate limit (SC-1): <c>AddXpSearch()</c> registers the policy,
/// <c>MapXpSearch()</c> applies it to all three routes unless it is switched off, and a caller past
/// the limit is answered 429 with <c>Retry-After</c> without ever reaching the endpoint.
/// </summary>
[TestFixture]
internal sealed class RateLimitTests
{
    [Test]
    public void MapXpSearch_RateLimitsAllThreeRoutes()
    {
        using var app = Map(_ => { });

        Assert.That(
            PolicyNames(app),
            Is.EqualTo(new[]
            {
                XpSearchConstants.PublicRateLimitPolicy,
                XpSearchConstants.PublicRateLimitPolicy,
                XpSearchConstants.PublicRateLimitPolicy,
            }));
    }

    [Test]
    public void MapXpSearch_WithTheLimitSwitchedOff_LeavesTheRoutesUnlimited()
    {
        using var app = Map(options => options.PublicRateLimitEnabled = false);

        Assert.That(PolicyNames(app), Is.EqualTo(new string?[] { null, null, null }));
    }

    [Test]
    public async Task PastTheLimit_TheCallerIsAnswered429WithRetryAfter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddXpSearch(options =>
        {
            options.PublicRateLimitPermitsPerWindow = 2;
            options.PublicRateLimitWindow = TimeSpan.FromMinutes(5);
        });

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapXpSearch();

        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            var statuses = new List<HttpStatusCode>();
            HttpResponseMessage? rejected = null;

            for (int i = 0; i < 3; i++)
            {
                var response = await client.PostAsync(ContractConstants.EventsRoute, JsonContent());
                statuses.Add(response.StatusCode);
                rejected = response;
            }

            Expect.Multiple(() =>
            {
                Assert.That(statuses.Take(2), Has.None.EqualTo(HttpStatusCode.TooManyRequests), "the permitted requests reach the endpoint");
                Assert.That(statuses[2], Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That(rejected!.Headers.RetryAfter, Is.Not.Null, "a rejected caller is told when to come back");
            });

            rejected!.Dispose();
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public void AddXpSearch_TwiceOrBesideTheHostsOwnPolicies_StillBuildsTheLimiter()
    {
        var services = new ServiceCollection();
        services.AddRateLimiter(limiter => limiter.AddFixedWindowLimiter("host-own", options =>
        {
            options.PermitLimit = 1;
            options.Window = TimeSpan.FromMinutes(1);
        }));
        services.AddXpSearch();
        services.AddXpSearch();

        using var provider = services.BuildServiceProvider();

        // Building them is the assertion: a duplicate policy name throws here, and so would a host
        // policy this package had overwritten.
        Assert.That(provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value, Is.Not.Null);
    }

    /// <summary>An event body the endpoint accepts, so only the limiter can turn a request away.</summary>
    private static StringContent JsonContent() =>
        new("""{"queryId":"q-1","resultId":"doc-1","type":"click","position":1}""", System.Text.Encoding.UTF8, "application/json");

    private static WebApplication Map(Action<Options.XpSearchOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddXpSearch(configure);

        var app = builder.Build();
        app.MapXpSearch();

        return app;
    }

    private static IEnumerable<string?> PolicyNames(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
}
