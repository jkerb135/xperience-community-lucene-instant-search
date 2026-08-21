using NUnit.Framework;

using NSubstitute;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Options;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Sorting;
using XpSearch.Widgets.Templates;

[assembly: RegisterSearchResultTemplate(
    "XpSearch.Tests.ProductCard",
    "Product card",
    "~/Components/Search/_ProductCard.cshtml",
    contentTypes: ["MyCompany.Product"])]
[assembly: RegisterSearchResultTemplate(
    "XpSearch.Tests.Compact",
    "Compact row",
    "~/Components/Search/_Compact.cshtml")]

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The drop-downs an editor sees: index list, facet attributes from the real schema (§7.4), result
/// templates (§5.8), and the sort option grammar (§7.3).
/// </summary>
[TestFixture]
internal sealed class EditorOptionsTests
{
    private static IIndexSchemaProvider SchemaProvider(string indexName, params SchemaField[] fields)
    {
        var provider = Substitute.For<IIndexSchemaProvider>();
        provider.GetSchemaAsync(indexName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IndexSchema(indexName, fields)));
        provider.GetSchemaAsync(Arg.Is<string>(name => name != indexName), Arg.Any<CancellationToken>())
            .Returns<Task<IndexSchema>>(_ => throw new IndexNotFoundException("nope"));

        return provider;
    }

    private static SchemaField Field(string name, bool facetable) =>
        new(name, facetable ? SearchFieldKind.Taxonomy : SearchFieldKind.Text, true, facetable, false, true);

    [Test]
    public async Task The_attribute_dropdown_offers_the_facetable_fields_only()
    {
        var provider = SchemaProvider("site-content", Field("title", false), Field("tags", true), Field("contentType", true));

        string? options = await FacetAttributeOptions.BuildOptionsAsync(provider, "site-content", CancellationToken.None);

        Assert.That(options, Is.EqualTo("tags;tags\r\ncontentType;contentType"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task The_attribute_dropdown_is_hidden_while_no_index_is_selected(string? indexName)
    {
        var provider = SchemaProvider("site-content", Field("tags", true));

        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(provider, indexName, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task The_attribute_dropdown_is_hidden_for_an_unknown_index_or_one_without_facets()
    {
        var unknown = SchemaProvider("site-content", Field("tags", true));
        var noFacets = SchemaProvider("site-content", Field("title", false));

        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(unknown, "gone", CancellationToken.None), Is.Null);
        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(noFacets, "site-content", CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task The_index_dropdown_lists_every_registered_index()
    {
        var provider = new XpSearchIndexOptionsProvider(new FakeIndexCatalog("products", "site-content"));

        var items = (await provider.GetOptionItems()).ToList();

        Expect.Multiple(() =>
        {
            Assert.That(items.Select(item => item.Value), Is.EqualTo(new[] { "products", "site-content" }));
            Assert.That(items.Select(item => item.Text), Is.EqualTo(new[] { "products", "site-content" }));
        });
    }

    [Test]
    public void The_template_registry_discovers_the_assembly_attributes()
    {
        var registry = new SearchResultTemplateRegistry(() => [typeof(EditorOptionsTests).Assembly]);

        var templates = registry.GetTemplates();
        var card = registry.Find("XpSearch.Tests.ProductCard");

        Expect.Multiple(() =>
        {
            Assert.That(templates.Select(template => template.Identifier),
                Is.EqualTo(new[] { "XpSearch.Tests.Compact", "XpSearch.Tests.ProductCard" }));
            Assert.That(card, Is.Not.Null);
            Assert.That(card!.ViewName, Is.EqualTo("~/Components/Search/_ProductCard.cshtml"));
            Assert.That(card.ContentTypes, Is.EqualTo(new[] { "MyCompany.Product" }));
            Assert.That(registry.Find("nothing.registered"), Is.Null);
        });
    }

    [Test]
    public async Task The_template_dropdown_is_filled_from_the_registry()
    {
        var provider = new ResultTemplateOptionsProvider(
            new SearchResultTemplateRegistry(() => [typeof(EditorOptionsTests).Assembly]));

        var items = (await provider.GetOptionItems()).ToList();

        Assert.That(items.Select(item => item.Text), Is.EqualTo(new[] { "Compact row", "Product card" }));
    }

    [Test]
    public void Sort_options_parse_one_per_line_and_fall_back_to_the_key_as_the_label()
    {
        var options = SortOptionsValidation.Parse("relevance;Most relevant\r\n\r\nprice_asc\n  newest ; Newest first ");

        Assert.That(
            options,
            Is.EqualTo(new[]
            {
                new SortOption("relevance", "Most relevant"),
                new SortOption("price_asc", "price_asc"),
                new SortOption("newest", "Newest first")
            }));
    }

    [Test]
    public void Sort_keys_are_validated_against_the_index_options_and_the_suffix_convention()
    {
        var index = new XpSearchIndexOptions();
        index.SortKeys["newest"] = new SortKey("PublishedAt", Descending: true);

        Expect.Multiple(() =>
        {
            Assert.That(SortOptionsValidation.IsValidKey("relevance", null), Is.True);
            Assert.That(SortOptionsValidation.IsValidKey("NEWEST", index), Is.True);
            Assert.That(SortOptionsValidation.IsValidKey("newest", null), Is.False);
            Assert.That(SortOptionsValidation.IsValidKey("price_asc", null), Is.True);
            Assert.That(SortOptionsValidation.IsValidKey("price_desc", null), Is.True);
            Assert.That(SortOptionsValidation.IsValidKey("_asc", null), Is.False);
            Assert.That(SortOptionsValidation.IsValidKey("price", null), Is.False);
            Assert.That(SortOptionsValidation.IsValidKey("", null), Is.False);
        });
    }

    [Test]
    public void Invalid_sort_keys_are_reported_and_dropped()
    {
        const string Text = "relevance;Most relevant\r\nprice;Cheapest\r\ntitle_asc;A to Z";

        Expect.Multiple(() =>
        {
            Assert.That(SortOptionsValidation.InvalidKeys(Text, null), Is.EqualTo(new[] { "price" }));
            Assert.That(
                SortOptionsValidation.ParseValid(Text, null).Select(option => option.Value),
                Is.EqualTo(new[] { "relevance", "title_asc" }));
        });
    }
}
