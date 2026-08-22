using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Endpoints;
using XpSearch.Core.Facets;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Search;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the three endpoints over real HTTP, on a minimal host that registers the production services
/// around the test index. No Xperience bootstrap is involved.
/// </summary>
[TestFixture]
internal sealed class EndpointTests
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private TestSearchIndex index = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        index = new TestSearchIndex(TestCorpus.IndexName, TestCorpus.Documents);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var options = Microsoft.Extensions.Options.Options.Create(new XpSearchOptions());
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILuceneIndexAccessor>(index);
        builder.Services.AddSingleton<IIndexSchemaProvider>(new StaticSchemaProvider(TestCorpus.Schema));
        builder.Services.AddSingleton<ISearchStage>(new NormalizeRequestStage(options));
        builder.Services.AddSingleton<ISearchStage, BuildQueryStage>();
        builder.Services.AddSingleton<ISearchStage, FacetFilterStage>();
        builder.Services.AddSingleton<ISearchStage, NumericFilterStage>();
        builder.Services.AddSingleton<ISearchStage>(new ExecuteSearchStage(index));
        builder.Services.AddSingleton<ISearchStage>(new CollectFacetsStage(new TaxonomyFacetProvider(index), options));
        builder.Services.AddSingleton<ISearchStage>(new HighlightStage(new LuceneHighlighter()));
        builder.Services.AddSingleton<ISearchStage, ProjectResponseStage>();
        builder.Services.AddSingleton<ISearchPipeline, SearchPipeline>();
        builder.Services.AddSingleton<IQuerySuggestionSource>(new FakeQuerySuggestionSource());
        builder.Services.AddSingleton<ISuggestService, DocumentSuggestService>();
        builder.Services.AddSingleton<ISearchEventSink, LoggingSearchEventSink>();

        app = builder.Build();
        app.MapXpSearch();

        await app.StartAsync();

        client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
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
    public async Task Query_ReturnsTheContractShapeAndTheVersionHeader()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.QueryRoute,
            new SearchRequest { Index = TestCorpus.IndexName, Query = "espresso" });

        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Version(response), Is.EqualTo(ContractConstants.ApiVersion));
            Assert.That(body!.Results, Is.Not.Empty);
            Assert.That(body.Total, Is.EqualTo(5));
            Assert.That(body.QueryId, Is.Not.Null);
        });
    }

    [Test]
    public async Task Query_AnswersFourHundredWithAFieldKeyedValidationBody()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.QueryRoute,
            new SearchRequest { Index = TestCorpus.IndexName, PageSize = 5000 });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(body.RootElement.GetProperty("errors").TryGetProperty("pageSize", out _), Is.True);
            Assert.That(Version(response), Is.EqualTo(ContractConstants.ApiVersion), "the version header is set on errors too");
        });
    }

    [Test]
    public async Task Query_KeysAFilterValidationErrorByItsJsonPath()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.QueryRoute,
            new SearchRequest
            {
                Index = TestCorpus.IndexName,
                Filters = new Filters
                {
                    Numeric =
                    [
                        new NumericFilter
                        {
                            Attribute = IndexSchemaProvider.TitleField,
                            Operator = NumericOperator.Gte,
                            Value = 1
                        }
                    ]
                }
            });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                body.RootElement.GetProperty("errors").TryGetProperty("filters.numeric[0].attribute", out _),
                Is.True,
                "the error is keyed by the JSON path of the offending entry");
        });
    }

    [Test]
    public async Task Query_AnswersFourHundredForAnOperatorTheContractDoesNotDefine()
    {
        // Raw JSON: the typed DTO cannot express an operator that is not in the enum, and that is
        // exactly the point - a bad one must be a bad request, not a server fault.
        var content = new StringContent(
            $$$"""{"index":"{{{TestCorpus.IndexName}}}","filters":{"numeric":[{"attribute":"Price","operator":"nope","value":1}]}}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(ContractConstants.QueryRoute, content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Query_AnswersFourHundredAndFourForAnUnknownIndex()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.QueryRoute,
            new SearchRequest { Index = "NoSuchIndex" });

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(Version(response), Is.EqualTo(ContractConstants.ApiVersion));
        });
    }

    [Test]
    public async Task Suggest_PrefixMatchesTitlesAndReturnsDocuments()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.SuggestRoute,
            new SuggestRequest { Index = TestCorpus.IndexName, Query = "espr" });

        var body = await response.Content.ReadFromJsonAsync<SuggestResponse>();

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Suggestions, Is.Not.Empty);
            Assert.That(body.Suggestions.All(suggestion => suggestion.Text.StartsWith("Espresso", StringComparison.OrdinalIgnoreCase)));
            Assert.That(body.Suggestions[0].Url, Does.StartWith("/"));
            Assert.That(body.Suggestions[0].Result, Is.Not.Null);
        });
    }

    [Test]
    public async Task Suggest_AnswersFourHundredAndFourForAnUnknownIndex()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.SuggestRoute,
            new SuggestRequest { Index = "NoSuchIndex", Query = "espr" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Events_AnswerTwoHundredAndTwoWithAnEmptyBody()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.EventsRoute,
            new EventRequest
            {
                Type = EventType.Click,
                ResultId = "doc-1:en",
                QueryId = Guid.NewGuid().ToString(),
                Position = 1
            });

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(response.Content.Headers.ContentLength ?? 0, Is.Zero);
            Assert.That(Version(response), Is.EqualTo(ContractConstants.ApiVersion));
        });
    }

    [Test]
    public async Task Events_RejectAClickWithoutAPosition()
    {
        var response = await client.PostAsJsonAsync(
            ContractConstants.EventsRoute,
            new EventRequest
            {
                Type = EventType.Click,
                ResultId = "doc-1:en",
                QueryId = Guid.NewGuid().ToString()
            });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Expect.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("errors").TryGetProperty("position", out _), Is.True);
        });
    }

    private static string? Version(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ContractConstants.ApiVersionHeader, out var values) ? values.First() : null;
}
