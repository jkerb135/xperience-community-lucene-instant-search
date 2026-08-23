using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using XpSearch.Core.Contract;

namespace XpSearch.Core.Tests.Contract;

/// <summary>
/// Proves the generated contract types (Contract/Generated/XpSearchContract.g.cs) match the wire
/// shape frozen in spec §4.2. The fixtures are the spec's own samples with the JSONC comments
/// stripped; the TypeScript side checks the same two payloads in
/// <c>XpSearch.Client/src/contract/__fixtures__/spec-samples.ts</c>.
/// The one edit to the samples is the amendment's <c>"baseScore": 6.10</c>, written here in canonical
/// form as <c>6.1</c> - the same number, but <see cref="JsonNode.DeepEquals"/> compares number
/// literals, not values, so the trailing zero would fail the round trip for no contract reason.
/// </summary>
[TestFixture]
public class ContractRoundTripTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Contract", "Fixtures", name));

    private static void AssertRoundTrips<T>(string fixtureName)
    {
        string json = ReadFixture(fixtureName);
        var reSerialized = JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(json));

        Assert.That(
            JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(reSerialized)),
            Is.True,
            $"round trip changed the payload.\noriginal:\n{json}\nround-tripped:\n{reSerialized}");
    }

    /// <summary>The spec §4.2 request sample survives deserialize/serialize unchanged.</summary>
    [Test]
    public void SearchRequest_Spec_Sample_Round_Trips() => AssertRoundTrips<SearchRequest>("search-request.json");

    /// <summary>The spec §4.2 response sample survives deserialize/serialize unchanged.</summary>
    [Test]
    public void SearchResponse_Spec_Sample_Round_Trips() => AssertRoundTrips<SearchResponse>("search-response.json");

    /// <summary>A result's document fields live in the closed <c>attributes</c> bag, beside the contract members.</summary>
    [Test]
    public void Result_Keeps_Its_Attributes_Beside_The_Contract_Members()
    {
        var response = JsonSerializer.Deserialize<SearchResponse>(ReadFixture("search-response.json"))!;
        Result result = response.Results[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo("web-page-42-en"));
            Assert.That(result.Score, Is.EqualTo(8.42d));
            Assert.That(result.Highlights!["title"], Is.EqualTo("<mark>Espresso</mark> Basics"));
            Assert.That(result.Ranking!.Position, Is.EqualTo(1));
            Assert.That(result.Ranking.Boosts, Is.EqualTo(new[] { "freshness:+1.2", "rule:pin-espresso-guide" }));
            // Every retrieved field is an attribute; nothing shares a namespace with id or score.
            Assert.That(result.Attributes.Keys, Is.EquivalentTo(new[] { "title", "url", "summary" }));
            Assert.That(result.Attributes["title"].GetString(), Is.EqualTo("Espresso Basics"));
            Assert.That(result.Attributes["url"].GetString(), Is.EqualTo("/articles/espresso-basics"));
            Assert.That(result.Attributes["summary"].GetString(), Is.EqualTo("..."));
        }
    }

    /// <summary>Facets are ordered arrays carrying the label a widget displays, not a value-to-count map.</summary>
    [Test]
    public void Facets_Are_Ordered_Arrays_With_Labels()
    {
        var response = JsonSerializer.Deserialize<SearchResponse>(ReadFixture("search-response.json"))!;
        FacetValue[] tags = response.Facets!["tags"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tags[0].Value, Is.EqualTo("coffee"));
            Assert.That(tags[0].Label, Is.EqualTo("Coffee"));
            Assert.That(tags[0].Count, Is.EqualTo(40));
            Assert.That(tags.Select(value => value.Count), Is.Ordered.Descending);

            // A taxonomy value names its ancestors; a root-level one has no path at all.
            Assert.That(tags[0].Path, Is.Null);
            Assert.That(tags[1].Path, Is.EqualTo(new[] { "coffee" }));
            Assert.That(
                JsonSerializer.Serialize(tags[0]),
                Does.Not.Contain("path"),
                "an absent path is omitted from the wire, not written as null");
        }
    }

    /// <summary>Structured filters deserialize into typed entries: no grammar, no escaping.</summary>
    [Test]
    public void Filters_Are_Structured()
    {
        var request = JsonSerializer.Deserialize<SearchRequest>(ReadFixture("search-request.json"))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Page, Is.EqualTo(1), "page is one-based");
            Assert.That(request.PageSize, Is.EqualTo(20));
            Assert.That(request.Fields, Is.EqualTo(new[] { "title", "url", "summary", "image" }));
            Assert.That(request.Filters!.Facets![0].Attribute, Is.EqualTo("contentType"));
            Assert.That(request.Filters.Facets[0].Values, Is.EqualTo(new[] { "Article", "Product" }));
            Assert.That(request.Filters.Facets[0].Operator, Is.EqualTo(FacetOperator.Or));
            Assert.That(request.Filters.Facets[1].Operator, Is.Null, "operator is optional and defaults to or");
            Assert.That(request.Filters.Numeric![0].Operator, Is.EqualTo(NumericOperator.Lte));
            Assert.That(request.Filters.Numeric[0].Value, Is.EqualTo(50d));
        }
    }

    /// <summary>Without explain=true a result has no ranking, and it is not written back out as null.</summary>
    [Test]
    public void Result_Without_Explain_Has_No_Ranking()
    {
        const string json = """{"id":"web-page-42-en","attributes":{"title":"Espresso Basics"}}""";

        var result = JsonSerializer.Deserialize<Result>(json)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Ranking, Is.Null);
            Assert.That(result.Score, Is.Null);
            Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("ranking"));
        }
    }

    /// <summary>
    /// An event round-trips with its wire strings: <c>"click"</c> and <c>"conversion"</c>, never the
    /// enum member names or their ordinals, with no serializer options needed at the call site.
    /// </summary>
    [Test]
    public void EventRequest_Round_Trips_With_Lower_Case_Event_Type()
    {
        AssertRoundTrips<EventRequest>("event-request.json");

        var request = JsonSerializer.Deserialize<EventRequest>(ReadFixture("event-request.json"))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Type, Is.EqualTo(EventType.Click));
            Assert.That(request.Position, Is.EqualTo(1));
            Assert.That(JsonSerializer.Serialize(request), Does.Contain("\"type\":\"click\""));
            Assert.That(
                JsonSerializer.Serialize(new EventRequest { Type = EventType.Conversion, QueryId = "q", ResultId = "o" }),
                Does.Contain("\"type\":\"conversion\""));
        }
    }

    /// <summary>
    /// The published API surface of the contract namespace is exactly the wire types plus the
    /// constants - no serializer plumbing leaks out of the generated file into the NuGet package.
    /// </summary>
    [Test]
    public void Contract_Namespace_Exports_Only_The_Contract_Types()
    {
        var exported = typeof(ContractConstants).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "XpSearch.Core.Contract")
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.That(exported, Is.EqualTo(new[]
        {
            "ContractConstants", "EventRequest", "EventType", "FacetFilter", "FacetOperator", "FacetValue",
            "Filters", "HighlightOptions", "NumericFilter", "NumericOperator", "RankingInfo", "Result",
            "SearchRedirect", "SearchRequest", "SearchResponse", "SuggestRequest", "SuggestResponse",
            "Suggestion",
        }));
    }

    /// <summary>The routes and the version header are the frozen values from spec §4.2 and §4.3.</summary>
    [Test]
    public void Routes_And_Version_Header_Are_Frozen()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ContractConstants.ApiVersion, Is.EqualTo("1"));
            Assert.That(ContractConstants.ApiVersionHeader, Is.EqualTo("X-XpSearch-Api-Version"));
            Assert.That(ContractConstants.QueryRoute, Is.EqualTo("/api/xpsearch/query"));
            Assert.That(ContractConstants.SuggestRoute, Is.EqualTo("/api/xpsearch/suggest"));
            Assert.That(ContractConstants.EventsRoute, Is.EqualTo("/api/xpsearch/events"));
        }
    }
}
