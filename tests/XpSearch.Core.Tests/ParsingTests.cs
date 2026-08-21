using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>Unit tests of the sort keys, the normalization rules and the field conventions.</summary>
[TestFixture]
internal sealed class ParsingTests
{
    private static IndexSchema Schema => TestCorpus.Schema;

    private static readonly Dictionary<string, SortKey> NoSortKeys = new(StringComparer.OrdinalIgnoreCase);

    [Test]
    public void SortKey_AcceptsRelevanceAndSuffixedSortableAttributes()
    {
        var relevance = SortKeyParser.Parse("relevance", Schema, NoSortKeys, out bool relevanceDescending);
        var descending = SortKeyParser.Parse($"{TestCorpus.PriceField}_desc", Schema, NoSortKeys, out bool priceDescending);
        var ascending = SortKeyParser.Parse($"{TestCorpus.PriceField}_asc", Schema, NoSortKeys, out bool priceAscending);

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

    [Test]
    public void SortKey_ResolvesAKeyConfiguredForTheIndex()
    {
        var keys = new Dictionary<string, SortKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["newest"] = new SortKey(TestCorpus.PublishedAtField, Descending: true)
        };

        var field = SortKeyParser.Parse("newest", Schema, keys, out bool descending);

        Expect.Multiple(() =>
        {
            Assert.That(field!.Name, Is.EqualTo(TestCorpus.PublishedAtField));
            Assert.That(descending, Is.True);
        });
    }

    [Test]
    public void SortKey_RejectsAConfiguredKeyPointingAtANonSortableAttribute()
    {
        var keys = new Dictionary<string, SortKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["by-body"] = new SortKey(TestCorpus.BodyField)
        };

        Expect.Throws<SearchValidationException>(() => SortKeyParser.Parse("by-body", Schema, keys, out _));
    }

    [TestCase("Price")]
    [TestCase("Body_asc")]
    [TestCase("nosuchfield_desc")]
    [TestCase("Price_up")]
    public void SortKey_RejectsAnythingElse(string sort) =>
        Expect.Throws<SearchValidationException>(() => SortKeyParser.Parse(sort, Schema, NoSortKeys, out _));

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
            Assert.That(LuceneFieldNames.LabelFieldName(taxonomy), Is.EqualTo("Tags_label"));
        });
    }

    [Test]
    public void LuceneFieldNames_RoundTripALabelTerm()
    {
        (string? value, string? title) = LuceneFieldNames.SplitLabel(LuceneFieldNames.ComposeLabel("coffee", "Coffee beans"));

        Expect.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo("coffee"));
            Assert.That(title, Is.EqualTo("Coffee beans"));
            Assert.That(LuceneFieldNames.SplitLabel("coffee").Value, Is.Null, "a term without a separator is not a label");
        });
    }
}
