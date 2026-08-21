using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Filters;
using XpSearch.Core.Indexing;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>Unit tests of the request grammars and normalization rules.</summary>
[TestFixture]
internal sealed class ParsingTests
{
    private static IndexSchema Schema => TestCorpus.Schema;

    [TestCase("price<=50", "price", NumericOperator.LessThanOrEqual, 50d)]
    [TestCase("price >= 12.5", "price", NumericOperator.GreaterThanOrEqual, 12.5d)]
    [TestCase("publishedAt>1700000000", "publishedAt", NumericOperator.GreaterThan, 1700000000d)]
    [TestCase("stock!=0", "stock", NumericOperator.NotEqual, 0d)]
    [TestCase("_rank = -3", "_rank", NumericOperator.Equal, -3d)]
    [TestCase("a.b<1", "a.b", NumericOperator.LessThan, 1d)]
    public void NumericFilter_AcceptsTheContractGrammar(string expression, string attribute, NumericOperator op, double value)
    {
        bool parsed = NumericFilterParser.TryParse(expression, out var filter);

        Expect.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(filter!.Attribute, Is.EqualTo(attribute));
            Assert.That(filter.Operator, Is.EqualTo(op));
            Assert.That(filter.Value, Is.EqualTo(value));
        });
    }

    [TestCase("")]
    [TestCase("price")]
    [TestCase("price<")]
    [TestCase("<=50")]
    [TestCase("1price<=50")]
    [TestCase("price=<50")]
    [TestCase("price<=abc")]
    [TestCase("price <= 50 or stock > 1")]
    [TestCase("price<=1.2.3")]
    public void NumericFilter_RejectsAnythingElse(string expression) =>
        Assert.That(NumericFilterParser.TryParse(expression, out _), Is.False);

    [Test]
    public void NumericFilter_RejectsANonNumericAttribute() =>
        Expect.Throws<SearchValidationException>(() => NumericFilterParser.ParseAll(["Title>=1"], Schema));

    [Test]
    public void NumericFilter_ResolvesTheAttributeToItsSchemaCasing()
    {
        var parsed = NumericFilterParser.ParseAll(["price<=50"], Schema);

        Assert.That(parsed[0].Attribute, Is.EqualTo(TestCorpus.PriceField));
    }

    [Test]
    public void FacetFilter_ParsesGroupsAndKeepsColonsInValues()
    {
        var parsed = FacetFilterParser.ParseAll(
            [[$"{TestCorpus.TagsField}:a:b"], [$"{TestCorpus.CategoryField}:coffee"]],
            Schema);

        Expect.Multiple(() =>
        {
            Assert.That(parsed, Has.Count.EqualTo(2));
            Assert.That(parsed[0][0].Value, Is.EqualTo("a:b"));
            Assert.That(parsed[1][0].Attribute, Is.EqualTo(TestCorpus.CategoryField));
        });
    }

    [TestCase("noattribute")]
    [TestCase(":value")]
    [TestCase("Tags:")]
    [TestCase("Price:5")]
    public void FacetFilter_RejectsMalformedOrNonFacetableEntries(string entry) =>
        Expect.Throws<SearchValidationException>(() => FacetFilterParser.ParseAll([[entry]], Schema));

    [Test]
    public void SortKey_AcceptsRelevanceAndSuffixedSortableAttributes()
    {
        var relevance = SortKeyParser.Parse("relevance", Schema, out bool relevanceDescending);
        var descending = SortKeyParser.Parse($"{TestCorpus.PriceField}_desc", Schema, out bool priceDescending);
        var ascending = SortKeyParser.Parse($"{TestCorpus.PriceField}_asc", Schema, out bool priceAscending);

        Expect.Multiple(() =>
        {
            Assert.That(relevance, Is.Null);
            Assert.That(relevanceDescending, Is.False);
            Assert.That(descending!.Name, Is.EqualTo(TestCorpus.PriceField));
            Assert.That(priceDescending, Is.True);
            Assert.That(ascending!.Name, Is.EqualTo(TestCorpus.PriceField));
            Assert.That(priceAscending, Is.False);
        });
    }

    [TestCase("Price")]
    [TestCase("Body_asc")]
    [TestCase("nosuchfield_desc")]
    [TestCase("Price_up")]
    public void SortKey_RejectsAnythingElse(string sort) =>
        Expect.Throws<SearchValidationException>(() => SortKeyParser.Parse(sort, Schema, out _));

    [TestCase(null, "")]
    [TestCase("   ", "")]
    [TestCase("  Espresso  ", "espresso")]
    [TestCase("ESPRESSO Basics", "espresso basics")]
    public void Normalize_TrimsAndLowercases(string? input, string expected) =>
        Assert.That(NormalizeRequestStage.Normalize(input, 256), Is.EqualTo(expected));

    [Test]
    public void Normalize_CapsTheLength() =>
        Assert.That(NormalizeRequestStage.Normalize(new string('a', 300), 256), Has.Length.EqualTo(256));

    [TestCase("~/products/x", "/products/x")]
    [TestCase("~", "/")]
    [TestCase("/products/x", "/products/x")]
    [TestCase("https://example.com/x", "https://example.com/x")]
    [TestCase(null, "")]
    [TestCase("  ~/a  ", "/a")]
    public void WebUrl_ConvertsAppRelativeUrls(string? input, string expected) =>
        Assert.That(WebUrl.ToRootRelative(input), Is.EqualTo(expected));

    [Test]
    public void LuceneFieldNames_FollowTheConventionsTheStrategyWritesWith()
    {
        var text = new SchemaField("Title", SearchFieldKind.Text, true, false, true, true);
        var number = new SchemaField("Price", SearchFieldKind.Number, false, false, true, true);
        var taxonomy = new SchemaField("Tags", SearchFieldKind.Taxonomy, true, true, false, true);

        Expect.Multiple(() =>
        {
            Assert.That(LuceneFieldNames.SortFieldName(text), Is.EqualTo("Title_sort"));
            Assert.That(LuceneFieldNames.SortFieldName(number), Is.EqualTo("Price"));
            Assert.That(LuceneFieldNames.SearchFieldName(taxonomy), Is.EqualTo("Tags_text"));
            Assert.That(LuceneFieldNames.SearchFieldName(text), Is.EqualTo("Title"));
        });
    }
}
