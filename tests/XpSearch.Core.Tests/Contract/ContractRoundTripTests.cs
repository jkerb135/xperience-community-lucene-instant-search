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
/// The one edit to the samples is the spec's <c>"baseScore": 6.10</c>, written here in canonical
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

    /// <summary>A hit keeps its non-reserved attributes (title, url, summary) through the open-object extension data.</summary>
    [Test]
    public void Hit_Keeps_Non_Reserved_Attributes()
    {
        var response = JsonSerializer.Deserialize<SearchResponse>(ReadFixture("search-response.json"))!;
        Hit hit = response.Hits[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit.ObjectId, Is.EqualTo("web-page-42-en"));
            Assert.That(hit.Score, Is.EqualTo(8.42d));
            Assert.That(hit.Highlights!["title"], Is.EqualTo("<mark>Espresso</mark> Basics"));
            Assert.That(hit.RankingInfo!.Position, Is.EqualTo(1));
            // objectID and the underscore members are the only reserved ones: url is as much a
            // retrieved attribute as title and summary, so all three arrive as extension data.
            Assert.That(hit.Attributes.Keys, Is.EquivalentTo(new[] { "title", "url", "summary" }));
            Assert.That(hit.Attributes["title"].GetString(), Is.EqualTo("Espresso Basics"));
            Assert.That(hit.Attributes["url"].GetString(), Is.EqualTo("/articles/espresso-basics"));
            Assert.That(hit.Attributes["summary"].GetString(), Is.EqualTo("..."));
        }
    }

    /// <summary>Without explain=true a hit has no _rankingInfo, and it is not written back out as null.</summary>
    [Test]
    public void Hit_Without_Explain_Has_No_RankingInfo()
    {
        const string json = """{"objectID":"web-page-42-en","title":"Espresso Basics"}""";

        var hit = JsonSerializer.Deserialize<Hit>(json)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit.RankingInfo, Is.Null);
            Assert.That(hit.Score, Is.Null);
            Assert.That(JsonSerializer.Serialize(hit), Does.Not.Contain("_rankingInfo"));
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
            Assert.That(request.EventType, Is.EqualTo(EventType.Click));
            Assert.That(request.Position, Is.EqualTo(1));
            Assert.That(JsonSerializer.Serialize(request), Does.Contain("\"eventType\":\"click\""));
            Assert.That(
                JsonSerializer.Serialize(new EventRequest { EventType = EventType.Conversion, QueryId = "q", ObjectId = "o" }),
                Does.Contain("\"eventType\":\"conversion\""));
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
            "ContractConstants", "EventRequest", "EventType", "HighlightOptions", "Hit",
            "RankingInfo", "SearchRequest", "SearchResponse", "SuggestRequest", "SuggestResponse", "Suggestion",
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
