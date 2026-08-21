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

    private static FacetFilter Facet(string attribute, params string[] values) =>
        new() { Attribute = attribute, Values = values };

    private static NumericFilter Numeric(string attribute, NumericOperator op, double value) =>
        new() { Attribute = attribute, Operator = op, Value = value };

    [Test]
    public async Task EmptyQuery_MatchesEveryDocument()
    {
        var response = await harness.Search(TestHarness.Request());

        Assert.That(response.Total, Is.EqualTo(TestCorpus.Documents.Count));
    }

    [Test]
    public async Task FreeTextQuery_MatchesTitleAndBody()
    {
        var response = await harness.Search(TestHarness.Request("espresso"));

        Assert.That(response.Total, Is.EqualTo(5), "four English documents plus the German one mention espresso");
        Assert.That(response.Results.Select(result => result.Id), Does.Contain("doc-1:en"));
    }

    [Test]
    public async Task Facets_ContainOnlyRequestedDimensionsAndNonZeroCounts()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.ContentTypeField];

        var response = await harness.Search(request);
        var values = response.Facets![TestCorpus.ContentTypeField];

        Expect.Multiple(() =>
        {
            Assert.That(response.Facets.Keys, Is.EquivalentTo(new[] { TestCorpus.ContentTypeField }));
            Assert.That(values.Single(value => value.Value == "Article").Count, Is.EqualTo(4));
            Assert.That(values.Single(value => value.Value == "Product").Count, Is.EqualTo(3));
            Assert.That(values.Select(value => value.Count), Has.None.EqualTo(0L));
        });
    }

    [Test]
    public async Task Facets_AreOrderedByCountThenValueAndCarryTheTaxonomyTitleAsTheLabel()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.CategoryField, TestCorpus.ContentTypeField];

        var response = await harness.Search(request);
        var categories = response.Facets![TestCorpus.CategoryField];

        Expect.Multiple(() =>
        {
            Assert.That(categories.Select(value => value.Count), Is.Ordered.Descending);
            Assert.That(categories[0].Value, Is.EqualTo("coffee"), "the tag code name is what a filter sends back");
            Assert.That(categories[0].Label, Is.EqualTo("Coffee beans"), "the tag title is what a widget displays");
            // Two categories carry two documents each; the tie is broken by the value, ordinally.
            Assert.That(
                categories.Where(value => value.Count == 1).Select(value => value.Value),
                Is.Ordered.Ascending);
            // A non-taxonomy dimension has no titles to look up, so its label is its value.
            Assert.That(
                response.Facets[TestCorpus.ContentTypeField].All(value => value.Label == value.Value),
                Is.True);
        });
    }

    [Test]
    public async Task FacetFilters_OrWithinAnEntryAndAndAcrossEntries()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters
        {
            Facets =
            [
                Facet(TestCorpus.CategoryField, "coffee", "equipment"),
                Facet(TestCorpus.TagsField, "brewing")
            ]
        };

        var response = await harness.Search(request);

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-1:en", "doc-3:en", "doc-6:de" }),
            "(coffee OR equipment) AND brewing");
    }

    [Test]
    public async Task FacetFilters_AndOperatorRequiresEveryValue()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters
        {
            Facets = [new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["brewing", "coffee"], Operator = FacetOperator.And }]
        };

        var response = await harness.Search(request);

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-1:en" }),
            "only the document carrying both tags");
    }

    [Test]
    public async Task FacetFilters_DrillSidewaysKeepsTheDrilledDimensionsAlternatives()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.CategoryField];
        request.Filters = new Filters { Facets = [Facet(TestCorpus.CategoryField, "coffee")] };

        var response = await harness.Search(request);

        Assert.That(response.Results, Has.Length.EqualTo(4), "only the coffee documents are returned");
        Assert.That(
            response.Facets![TestCorpus.CategoryField].Select(value => value.Value),
            Is.SupersetOf(new[] { "coffee", "equipment", "accessories" }),
            "drill sideways still counts the values the visitor could switch to");
    }

    [Test]
    public async Task NumericFilters_ApplyRangesAndNotEqual()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters
        {
            Numeric =
            [
                Numeric(TestCorpus.PriceField, NumericOperator.Lte, 200),
                Numeric(TestCorpus.PriceField, NumericOperator.Ne, 5)
            ]
        };

        var response = await harness.Search(request);

        Assert.That(response.Results.Select(result => result.Id), Is.EquivalentTo(new[] { "doc-4:en" }));
    }

    [Test]
    public async Task NumericFilters_OnADateCompareEpochSeconds()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Numeric = [Numeric(TestCorpus.PublishedAtField, NumericOperator.Gte, 1_700_000_000)] };

        var response = await harness.Search(request);

        Assert.That(response.Total, Is.EqualTo(4));
    }

    [Test]
    public async Task Sort_OrdersByASortableAttribute()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Numeric = [Numeric(TestCorpus.PriceField, NumericOperator.Gte, 0)] };
        request.Sort = $"{TestCorpus.PriceField}_desc";

        var response = await harness.Search(request);

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EqualTo(new[] { "doc-3:en", "doc-4:en", "doc-5:en" }).AsCollection);
    }

    [Test]
    public async Task Sort_AcceptsAKeyConfiguredForTheIndex()
    {
        var options = new XpSearchOptions();
        options.Indexes[TestCorpus.IndexName].SortKeys["newest"] = new SortKey(TestCorpus.PublishedAtField, Descending: true);

        using var configured = new TestHarness(options);
        var request = TestHarness.Request();
        request.Sort = "newest";

        var response = await configured.Search(request);

        Assert.That(response.Results[0].Id, Is.EqualTo(TestCorpus.ScriptDocumentId), "the most recently published document");
    }

    [Test]
    public async Task Paging_IsOneBasedAndReportsTheClampedPageSizeAndPageCount()
    {
        var request = TestHarness.Request();
        request.PageSize = 3;
        request.Page = 2;

        var response = await harness.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.PageSize, Is.EqualTo(3));
            Assert.That(response.Page, Is.EqualTo(2));
            Assert.That(response.Total, Is.EqualTo(7));
            Assert.That(response.TotalPages, Is.EqualTo(3));
            Assert.That(response.Results, Has.Length.EqualTo(3));
        });
    }

    [Test]
    public async Task Paging_DefaultsToTheFirstPage()
    {
        var response = await harness.Search(TestHarness.Request());

        Assert.That(response.Page, Is.EqualTo(1));
    }

    [Test]
    public void Paging_RejectsAZeroPage()
    {
        var request = TestHarness.Request();
        request.Page = 0;

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("page"));
    }

    [Test]
    public void Paging_RejectsAWindowDeeperThanTheConfiguredMaximum()
    {
        using var shallow = new TestHarness(new XpSearchOptions { MaxResultWindow = 10 });
        var request = TestHarness.Request();
        request.Page = 3;
        request.PageSize = 5;

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => shallow.Search(request));

        Assert.That(exception.Errors, Contains.Key("page"));
    }

    [Test]
    public async Task Highlighting_EncodesTheStoredValueBeforeInsertingTags()
    {
        var request = TestHarness.Request("espresso");
        request.Highlight = new HighlightOptions { Fields = [TestCorpus.BodyField] };

        var response = await harness.Search(request);
        var result = response.Results.Single(candidate => candidate.Id == TestCorpus.ScriptDocumentId);
        string snippet = result.Highlights![TestCorpus.BodyField];

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

        Assert.That(response.Results.Any(result => result.Highlights?[TestCorpus.BodyField].Contains("<b>espresso</b>", StringComparison.Ordinal) == true));
    }

    [Test]
    public async Task Fields_ProjectOnlyTheRequestedAttributes()
    {
        var request = TestHarness.Request("espresso");
        request.Fields = [IndexSchemaProvider.TitleField, "Url"];

        var response = await harness.Search(request);
        var result = response.Results[0];

        Expect.Multiple(() =>
        {
            Assert.That(result.Attributes.Keys, Is.EquivalentTo(new[] { IndexSchemaProvider.TitleField, "Url" }));
            Assert.That(result.Id, Is.Not.Empty, "the result id is always returned");
        });
    }

    [Test]
    public async Task DefaultProjection_ReturnsEveryRetrievableFieldAndNumbersAsNumbers()
    {
        var request = TestHarness.Request("grinder");

        var response = await harness.Search(request);
        var result = response.Results.Single();

        Expect.Multiple(() =>
        {
            Assert.That(result.Attributes.ContainsKey(TestCorpus.BodyField));
            Assert.That(result.Attributes[TestCorpus.PriceField].ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(result.Attributes[TestCorpus.PriceField].GetDouble(), Is.EqualTo(149.5));
            Assert.That(result.Attributes[TestCorpus.TagsField].GetString(), Is.EqualTo("grinding"));
        });
    }

    [Test]
    public async Task MultiValuedField_ProjectsAsAnArray()
    {
        var request = TestHarness.Request("basics");

        var response = await harness.Search(request);
        var tags = response.Results.Single().Attributes[TestCorpus.TagsField];

        Expect.Multiple(() =>
        {
            Assert.That(tags.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(tags.EnumerateArray().Select(value => value.GetString()), Is.EquivalentTo(new[] { "brewing", "coffee" }));
        });
    }

    [Test]
    public async Task Explain_AddsRankingWithAPositionAcrossPages()
    {
        var request = TestHarness.Request();
        request.Explain = true;
        request.PageSize = 2;
        request.Page = 3;

        var response = await harness.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.Results[0].Ranking!.Position, Is.EqualTo(5));
            Assert.That(response.Results[0].Ranking!.BaseScore, Is.EqualTo(response.Results[0].Score));
            Assert.That(response.Results[0].Ranking!.Boosts, Is.Empty);
        });
    }

    [Test]
    public async Task Explain_IsAbsentByDefault()
    {
        var response = await harness.Search(TestHarness.Request());

        Assert.That(response.Results[0].Ranking, Is.Null);
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

        Assert.That(response.Results.Select(result => result.Id), Is.EquivalentTo(new[] { "doc-6:de" }));
    }

    [Test]
    public void UnknownIndex_Throws()
    {
        var request = new SearchRequest { Index = "NoSuchIndex" };

        Expect.ThrowsAsync<IndexNotFoundException>(() => harness.Pipeline.ExecuteAsync(request, CancellationToken.None));
    }

    [Test]
    public void UnknownFacetAttribute_IsAValidationErrorKeyedByItsPath()
    {
        var request = TestHarness.Request();
        request.Facets = ["NotAnAttribute"];

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("facets[0]"));
    }

    [Test]
    public void UnknownFilterAttribute_IsAValidationErrorKeyedByItsPath()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Facets = [Facet(TestCorpus.CategoryField, "coffee"), Facet("NotAnAttribute", "x")] };

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("filters.facets[1].attribute"));
    }

    [Test]
    public void NonFacetableFilterAttribute_IsAValidationError()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Facets = [Facet(TestCorpus.PriceField, "5")] };

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("filters.facets[0].attribute"));
    }

    [Test]
    public void NonNumericFilterAttribute_IsAValidationErrorKeyedByItsPath()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Numeric = [Numeric(IndexSchemaProvider.TitleField, NumericOperator.Gte, 1)] };

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("filters.numeric[0].attribute"));
    }

    [Test]
    public void UnknownProjectedField_IsAValidationErrorKeyedByItsPath()
    {
        var request = TestHarness.Request();
        request.Fields = [IndexSchemaProvider.TitleField, "NotAField"];

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("fields[1]"));
    }

    [Test]
    public async Task EmptyFacetValues_RefineNothing()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters { Facets = [Facet(TestCorpus.CategoryField)] };

        var response = await harness.Search(request);

        Assert.That(response.Total, Is.EqualTo(TestCorpus.Documents.Count));
    }

    [Test]
    public void PageSizeAboveTheContractCeiling_IsAValidationError()
    {
        var request = TestHarness.Request();
        request.PageSize = 1001;

        var exception = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(exception.Errors, Contains.Key("pageSize"));
    }

    [Test]
    public async Task PageSizeAboveTheConfiguredCeiling_IsClamped()
    {
        using var clamped = new TestHarness(new XpSearchOptions { MaxPageSize = 2 });
        var request = TestHarness.Request();
        request.PageSize = 50;

        var response = await clamped.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.PageSize, Is.EqualTo(2));
            Assert.That(response.Results, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task WithoutATaxonomySidecar_FacetFiltersStillFilterAndNoCountsAreReturned()
    {
        using var flat = new TestHarness(withTaxonomy: false);
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.CategoryField];
        request.Filters = new Filters { Facets = [Facet(TestCorpus.CategoryField, "equipment")] };

        var response = await flat.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(response.Results.Select(result => result.Id), Is.EquivalentTo(new[] { "doc-3:en", "doc-4:en" }));
            Assert.That(response.Facets, Is.Empty, "an index without a taxonomy sidecar reports no counts");
        });
    }
}
