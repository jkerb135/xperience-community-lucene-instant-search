using System.Text.Json;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// End-to-end tests of the whole pipeline against a real Lucene index with a taxonomy sidecar.
/// </summary>
[TestFixture]
internal sealed class SearchPipelineTests
{
    private TestHarness harness = null!;

    [SetUp]
    public void SetUp() => harness = new TestHarness();

    [TearDown]
    public void TearDown() => harness.Dispose();

    [Test]
    public async Task EmptyQuery_MatchesEveryDocument()
    {
        var response = await harness.Search(TestHarness.Request());

        Assert.That(response.NbHits, Is.EqualTo(TestCorpus.Documents.Count));
    }

    [Test]
    public async Task FreeTextQuery_MatchesTitleAndBody()
    {
        var response = await harness.Search(TestHarness.Request("espresso"));

        Assert.That(response.NbHits, Is.EqualTo(5), "four English documents plus the German one mention espresso");
        Assert.That(response.Hits.Select(hit => hit.ObjectId), Does.Contain("doc-1:en"));
    }

    [Test]
    public async Task Facets_ContainOnlyRequestedDimensionsAndNonZeroCounts()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.ContentTypeField];

        var response = await harness.Search(request);

        Assert.That(response.Facets, Is.Not.Null);
        Assert.That(response.Facets!.Keys, Is.EquivalentTo(new[] { TestCorpus.ContentTypeField }));
        Assert.That(response.Facets[TestCorpus.ContentTypeField]["Article"], Is.EqualTo(4));
        Assert.That(response.Facets[TestCorpus.ContentTypeField]["Product"], Is.EqualTo(3));
        Assert.That(response.Facets[TestCorpus.ContentTypeField].Values, Has.None.EqualTo(0L));
    }

    [Test]
    public async Task FacetFilters_OrWithinGroupAndAndAcrossGroups()
    {
        var request = TestHarness.Request();
        request.FacetFilters =
        [
            [$"{TestCorpus.CategoryField}:coffee", $"{TestCorpus.CategoryField}:equipment"],
            [$"{TestCorpus.TagsField}:brewing"]
        ];

        var response = await harness.Search(request);

        Assert.That(
            response.Hits.Select(hit => hit.ObjectId),
            Is.EquivalentTo(new[] { "doc-1:en", "doc-3:en", "doc-6:de" }),
            "(coffee OR equipment) AND brewing");
    }

    [Test]
    public async Task FacetFilters_DrillSidewaysKeepsTheDrilledDimensionsAlternatives()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.CategoryField];
        request.FacetFilters = [[$"{TestCorpus.CategoryField}:coffee"]];

        var response = await harness.Search(request);

        Assert.That(response.Hits, Has.Length.EqualTo(4), "only the coffee documents are returned");
        Assert.That(
            response.Facets![TestCorpus.CategoryField].Keys,
            Is.SupersetOf(new[] { "coffee", "equipment", "accessories" }),
            "drill sideways still counts the values the visitor could switch to");
    }

    [Test]
    public async Task NumericFilters_ApplyRangesAndNotEqual()
    {
        var request = TestHarness.Request();
        request.NumericFilters = [$"{TestCorpus.PriceField}<=200", $"{TestCorpus.PriceField}!=5"];

        var response = await harness.Search(request);

        Assert.That(response.Hits.Select(hit => hit.ObjectId), Is.EquivalentTo(new[] { "doc-4:en" }));
    }

    [Test]
    public async Task NumericFilters_OnADateCompareEpochSeconds()
    {
        var request = TestHarness.Request();
        request.NumericFilters = [$"{TestCorpus.PublishedAtField}>=1700000000"];

        var response = await harness.Search(request);

        Assert.That(response.NbHits, Is.EqualTo(4));
    }

    [Test]
    public async Task Sort_OrdersByASortableAttribute()
    {
        var request = TestHarness.Request();
        request.NumericFilters = [$"{TestCorpus.PriceField}>=0"];
        request.Sort = $"{TestCorpus.PriceField}_desc";

        var response = await harness.Search(request);

        Assert.That(
            response.Hits.Select(hit => hit.ObjectId),
            Is.EqualTo(new[] { "doc-3:en", "doc-4:en", "doc-5:en" }).AsCollection);
    }

    [Test]
    public async Task Paging_ReportsClampedPageSizeAndPageCount()
    {
        var request = TestHarness.Request();
        request.HitsPerPage = 3;
        request.Page = 1;

        var response = await harness.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.HitsPerPage, Is.EqualTo(3));
            Assert.That(response.Page, Is.EqualTo(1));
            Assert.That(response.NbHits, Is.EqualTo(7));
            Assert.That(response.NbPages, Is.EqualTo(3));
            Assert.That(response.Hits, Has.Length.EqualTo(3));
        });
    }

    [Test]
    public async Task Highlighting_EncodesTheStoredValueBeforeInsertingTags()
    {
        var request = TestHarness.Request("espresso");
        request.Highlight = new HighlightOptions { Fields = [TestCorpus.BodyField] };

        var response = await harness.Search(request);
        var hit = response.Hits.Single(candidate => candidate.ObjectId == TestCorpus.ScriptDocumentId);
        string snippet = hit.Highlights![TestCorpus.BodyField];

        Expect.Multiple(() =>
        {
            Assert.That(snippet, Does.Not.Contain("<script>"), "the stored markup must arrive encoded");
            Assert.That(snippet, Does.Contain("&lt;script&gt;"));
            Assert.That(snippet, Does.Contain("<mark>espresso</mark>"), "only the highlight tags are unencoded");
        });
    }

    [Test]
    public async Task Highlighting_HonoursCustomTags()
    {
        var request = TestHarness.Request("espresso");
        request.Highlight = new HighlightOptions
        {
            Fields = [TestCorpus.BodyField],
            PreTag = "<b>",
            PostTag = "</b>"
        };

        var response = await harness.Search(request);

        Assert.That(response.Hits.Any(hit => hit.Highlights?[TestCorpus.BodyField].Contains("<b>espresso</b>", StringComparison.Ordinal) == true));
    }

    [Test]
    public async Task AttributesToRetrieve_ProjectsOnlyTheRequestedAttributes()
    {
        var request = TestHarness.Request("espresso");
        request.AttributesToRetrieve = [IndexSchemaProvider.TitleField, "Url"];

        var response = await harness.Search(request);
        var hit = response.Hits[0];

        Expect.Multiple(() =>
        {
            Assert.That(hit.Attributes.Keys, Is.EquivalentTo(new[] { IndexSchemaProvider.TitleField, "Url" }));
            Assert.That(hit.ObjectId, Is.Not.Empty, "objectID is always returned");
        });
    }

    [Test]
    public async Task DefaultProjection_ReturnsEveryRetrievableAttributeAndNumbersAsNumbers()
    {
        var request = TestHarness.Request("grinder");

        var response = await harness.Search(request);
        var hit = response.Hits.Single();

        Expect.Multiple(() =>
        {
            Assert.That(hit.Attributes.ContainsKey(TestCorpus.BodyField));
            Assert.That(hit.Attributes[TestCorpus.PriceField].ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(hit.Attributes[TestCorpus.PriceField].GetDouble(), Is.EqualTo(149.5));
            Assert.That(hit.Attributes[TestCorpus.TagsField].GetString(), Is.EqualTo("grinding"));
        });
    }

    [Test]
    public async Task MultiValuedAttribute_ProjectsAsAnArray()
    {
        var request = TestHarness.Request("basics");

        var response = await harness.Search(request);
        var tags = response.Hits.Single().Attributes[TestCorpus.TagsField];

        Expect.Multiple(() =>
        {
            Assert.That(tags.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(tags.EnumerateArray().Select(value => value.GetString()), Is.EquivalentTo(new[] { "brewing", "coffee" }));
        });
    }

    [Test]
    public async Task Explain_AddsRankingInfoWithAPositionAcrossPages()
    {
        var request = TestHarness.Request();
        request.Explain = true;
        request.HitsPerPage = 2;
        request.Page = 2;

        var response = await harness.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.Hits[0].RankingInfo!.Position, Is.EqualTo(5));
            Assert.That(response.Hits[0].RankingInfo!.BaseScore, Is.EqualTo(response.Hits[0].Score));
            Assert.That(response.Hits[0].RankingInfo!.AppliedBoosts, Is.Empty);
        });
    }

    [Test]
    public async Task Explain_IsAbsentByDefault()
    {
        var response = await harness.Search(TestHarness.Request());

        Assert.That(response.Hits[0].RankingInfo, Is.Null);
    }

    [Test]
    public async Task QueryId_IsEchoedWhenSuppliedAndGeneratedOtherwise()
    {
        var supplied = TestHarness.Request();
        supplied.QueryId = "caller-supplied";

        var echoed = await harness.Search(supplied);
        var generated = await harness.Search(TestHarness.Request());

        Expect.Multiple(() =>
        {
            Assert.That(echoed.QueryId, Is.EqualTo("caller-supplied"));
            Assert.That(Guid.TryParse(generated.QueryId, out _), Is.True);
        });
    }

    [Test]
    public async Task Language_FiltersOnTheIntegrationsLanguageField()
    {
        var request = TestHarness.Request();
        request.Language = "de";

        var response = await harness.Search(request);

        Assert.That(response.Hits.Select(hit => hit.ObjectId), Is.EquivalentTo(new[] { "doc-6:de" }));
    }

    [Test]
    public void UnknownIndex_Throws()
    {
        var request = new SearchRequest { Index = "NoSuchIndex" };

        Expect.ThrowsAsync<IndexNotFoundException>(() => harness.Pipeline.ExecuteAsync(request, CancellationToken.None));
    }

    [Test]
    public void UnknownFacetAttribute_IsAValidationError()
    {
        var request = TestHarness.Request();
        request.Facets = ["NotAnAttribute"];

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("facets"));
    }

    [Test]
    public void HitsPerPageAboveTheContractCeiling_IsAValidationError()
    {
        var request = TestHarness.Request();
        request.HitsPerPage = 1001;

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("hitsPerPage"));
    }

    [Test]
    public async Task HitsPerPageAboveTheConfiguredCeiling_IsClamped()
    {
        using var clamped = new TestHarness(new XpSearchOptions { MaxHitsPerPage = 2 });
        var request = TestHarness.Request();
        request.HitsPerPage = 50;

        var response = await clamped.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.HitsPerPage, Is.EqualTo(2));
            Assert.That(response.Hits, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task WithoutATaxonomySidecar_FacetFiltersStillFilterAndNoCountsAreReturned()
    {
        using var flat = new TestHarness(withTaxonomy: false);
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.CategoryField];
        request.FacetFilters = [[$"{TestCorpus.CategoryField}:equipment"]];

        var response = await flat.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.Hits.Select(hit => hit.ObjectId), Is.EquivalentTo(new[] { "doc-3:en", "doc-4:en" }));
            Assert.That(response.Facets, Is.Empty, "an index without a taxonomy sidecar reports no counts");
        });
    }
}
