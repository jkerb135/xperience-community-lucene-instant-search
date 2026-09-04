using NUnit.Framework;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// FC-1: a facet always carries the values the request refines it by, count 0 when the filtered
/// result set has none, so a UI can always name - and offer to remove - what a visitor arrived with.
/// </summary>
[TestFixture]
internal sealed class SelectedFacetValueTests
{
    private readonly TestHarness harness = new();

    [OneTimeTearDown]
    public void Dispose() => harness.Dispose();

    private static FacetValue? ValueOf(SearchResponse response, string attribute, string value) =>
        response.Facets![attribute].FirstOrDefault(facet => facet.Value == value);

    private Task<SearchResponse> Search(string[] facets, params FacetFilter[] filters)
    {
        var request = TestHarness.Request();
        request.Facets = facets;
        request.Filters = new Filters { Facets = filters };

        return harness.Search(request);
    }

    [Test]
    public async Task ASelectedValueTheFilteredSetHasNoHitFor_ComesBackAtZeroWithItsLabel()
    {
        // Only the two coffee articles are tagged "milk"; the equipment refinement leaves neither.
        var response = await Search(
            [TestCorpus.TagsField],
            new FacetFilter { Attribute = TestCorpus.CategoryField, Values = ["equipment"] },
            new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["milk"] });

        Expect.Multiple(() =>
        {
            Assert.That(response.Total, Is.Zero);
            Assert.That(ValueOf(response, TestCorpus.TagsField, "milk")?.Count, Is.Zero);
            Assert.That(ValueOf(response, TestCorpus.TagsField, "milk")?.Label, Is.EqualTo("Milk"), "named by its tag title, not its code");
            Assert.That(ValueOf(response, TestCorpus.TagsField, "milk")?.Path, Is.Null, "a flat taxonomy has no ancestry");
        });
    }

    [Test]
    public async Task TheCountedValues_AreUnchangedAndComeFirst()
    {
        var response = await Search(
            [TestCorpus.TagsField],
            new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["brewing", "milk"] });

        var values = response.Facets![TestCorpus.TagsField];

        Expect.Multiple(() =>
        {
            // The drill-down keeps the dimension's own counts answering "what if I picked another
            // value", so "brewing" still counts every document that carries it.
            Assert.That(ValueOf(response, TestCorpus.TagsField, "brewing")?.Count, Is.EqualTo(4));
            Assert.That(ValueOf(response, TestCorpus.TagsField, "milk")?.Count, Is.EqualTo(1), "a selected value with hits is untouched");
            Assert.That(values.Select(value => value.Count), Is.Ordered.Descending, "counted values keep their order and nothing zero jumps ahead");
        });
    }

    [Test]
    public async Task AnAndRefinementThatMatchesNothing_StillNamesBothItsValues()
    {
        // No document carries both tags, so the result set - and every count in it - is empty.
        var response = await Search(
            [TestCorpus.TagsField],
            new FacetFilter
            {
                Attribute = TestCorpus.TagsField,
                Operator = FacetOperator.And,
                Values = ["milk", "grinding"]
            });

        Expect.Multiple(() =>
        {
            Assert.That(response.Total, Is.Zero);
            Assert.That(
                response.Facets![TestCorpus.TagsField].Select(value => (value.Value, value.Label, value.Count)),
                Is.EqualTo(new[] { ("milk", "Milk", 0L), ("grinding", "Grinding", 0L) }),
                "in request order, after the (here absent) counted values");
        });
    }

    [Test]
    public async Task AValueThatIsNotInTheTaxonomyAtAll_IsReturnedWithItselfAsItsLabel()
    {
        var response = await Search(
            [TestCorpus.TagsField, TestCorpus.ContentTypeField],
            new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["typo-in-a-deep-link"] },
            new FacetFilter { Attribute = TestCorpus.ContentTypeField, Values = ["Whitepaper"] });

        Expect.Multiple(() =>
        {
            // A taxonomy value nobody ever indexed, and a non-taxonomy attribute, which has no
            // titles at all: both name themselves rather than disappearing.
            Assert.That(ValueOf(response, TestCorpus.TagsField, "typo-in-a-deep-link")?.Label, Is.EqualTo("typo-in-a-deep-link"));
            Assert.That(ValueOf(response, TestCorpus.ContentTypeField, "Whitepaper")?.Label, Is.EqualTo("Whitepaper"));
            Assert.That(ValueOf(response, TestCorpus.ContentTypeField, "Whitepaper")?.Count, Is.Zero);
        });
    }

    [Test]
    public async Task ASelectedNestedValue_CarriesItsPathAndBringsItsUnseenAncestors()
    {
        // "latte" only ever tags articles, so the product refinement empties every Topic count.
        var response = await Search(
            [TestCorpus.TopicField],
            new FacetFilter { Attribute = TestCorpus.ContentTypeField, Values = ["Product"] },
            new FacetFilter { Attribute = TestCorpus.TopicField, Values = ["latte"] });

        var values = response.Facets![TestCorpus.TopicField];

        Expect.Multiple(() =>
        {
            Assert.That(ValueOf(response, TestCorpus.TopicField, "latte")?.Label, Is.EqualTo("Latte"));
            Assert.That(ValueOf(response, TestCorpus.TopicField, "latte")?.Path, Is.EqualTo(new[] { "drinks", "espresso-drinks" }));

            // The contract promises every ancestor a path names is in the same facet's values, so
            // the appended leaf brings the ancestors no product carries along at zero too. They
            // follow the values the sideways count did find ("gear", "grinders").
            Assert.That(
                values.TakeLast(3).Select(value => (value.Value, value.Label, value.Count)),
                Is.EqualTo(new[]
                {
                    ("drinks", "Drinks", 0L),
                    ("espresso-drinks", "Espresso drinks", 0L),
                    ("latte", "Latte", 0L)
                }));
            Assert.That(values.Take(values.Length - 3).Select(value => value.Count), Is.All.Positive);
        });
    }

    [Test]
    public void TheSelectedValues_AreAlreadyPartOfTheResponseCacheKey()
    {
        var request = TestHarness.Request("coffee");
        request.Facets = [TestCorpus.TagsField];
        request.Filters = new Filters { Facets = [new FacetFilter { Attribute = TestCorpus.TagsField, Values = ["milk"] }] };

        string key = SearchCacheKey.Compute(request, "coffee");

        request.Filters.Facets![0].Values = ["brewing"];

        Assert.That(SearchCacheKey.Compute(request, "coffee"), Is.Not.EqualTo(key), "a different refinement is a different cached response");
    }
}
