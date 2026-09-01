using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Facets;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// The option lines behind the facet attribute drop-down of the Page Builder widgets (spec §7.4).
/// Lives in Core because both <c>XpSearch.Widgets</c> (which declares the drop-down) and
/// <c>XpSearch.Admin</c> (which hosts the configurator) reach it from here.
/// </summary>
[TestFixture]
internal sealed class FacetAttributeOptionsTests
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
    public async Task Only_facetable_fields_are_offered()
    {
        var provider = SchemaProvider("site-content", Field("title", false), Field("tags", true), Field("contentType", true));

        string? options = await FacetAttributeOptions.BuildOptionsAsync(provider, "site-content", CancellationToken.None);

        Assert.That(options, Is.EqualTo("contentType;contentType\r\ntags;tags"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task No_index_means_no_options_so_the_field_is_hidden(string? indexName)
    {
        var provider = SchemaProvider("site-content", Field("tags", true));

        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(provider, indexName, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task An_unknown_index_or_one_without_facets_means_no_options()
    {
        var unknown = SchemaProvider("site-content", Field("tags", true));
        var noFacets = SchemaProvider("site-content", Field("title", false));

        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(unknown, "gone", CancellationToken.None), Is.Null);
        Assert.That(await FacetAttributeOptions.BuildOptionsAsync(noFacets, "site-content", CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_predicate_narrows_the_options_to_the_range_filterable_fields()
    {
        var provider = SchemaProvider(
            "site-content",
            new SchemaField("title", SearchFieldKind.Text, true, false, false, true),
            new SchemaField("tags", SearchFieldKind.Taxonomy, true, true, false, true),
            new SchemaField("price", SearchFieldKind.Number, false, true, true, true),
            new SchemaField("publishedAt", SearchFieldKind.Date, false, false, true, true));

        string? numeric = await FacetAttributeOptions.BuildOptionsAsync(
            provider, "site-content", CancellationToken.None, FacetAttributeOptions.IsRangeFilterable);

        Expect.Multiple(() =>
        {
            Assert.That(numeric, Is.EqualTo("price;price\r\npublishedAt;publishedAt"));
            // The default is unchanged: the facet drop-down still offers the facetable fields.
            Assert.That(
                FacetAttributeOptions.BuildOptionsAsync(provider, "site-content", CancellationToken.None).Result,
                Is.EqualTo("price;price\r\ntags;tags"));
        });
    }

    /// <summary>
    /// A field weight multiplies the per-field boost <c>BuildQueryStage.Boosts</c> builds, and that
    /// loop only visits searchable fields - so a weight on anything else would never be applied.
    /// </summary>
    [Test]
    public async Task The_weight_predicate_narrows_the_options_to_the_searchable_fields()
    {
        var provider = SchemaProvider(
            "site-content",
            new SchemaField("title", SearchFieldKind.Text, true, false, false, true),
            new SchemaField("tags", SearchFieldKind.Taxonomy, false, true, false, true),
            new SchemaField("price", SearchFieldKind.Number, false, true, true, true),
            new SchemaField("summary", SearchFieldKind.Text, true, false, false, true));

        string? weightable = await FacetAttributeOptions.BuildOptionsAsync(
            provider, "site-content", CancellationToken.None, FacetAttributeOptions.IsWeightable);

        Assert.That(weightable, Is.EqualTo("summary;summary\r\ntitle;title"));
    }

    /// <summary>
    /// Discovery order is meaningless to an editor hunting for a field name, so every drop-down fed
    /// from here is alphabetical (AD-9, HW-11 #17).
    /// </summary>
    [Test]
    public async Task Options_are_alphabetical_regardless_of_case_or_discovery_order()
    {
        var provider = SchemaProvider("site-content", Field("Zebra", true), Field("apple", true), Field("Banana", true));

        Assert.That(
            await FacetAttributeOptions.BuildOptionsAsync(provider, "site-content", CancellationToken.None),
            Is.EqualTo("apple;apple\r\nBanana;Banana\r\nZebra;Zebra"));
    }

    [Test]
    public async Task An_index_with_no_numeric_or_date_field_offers_nothing_so_the_field_is_hidden()
    {
        var provider = SchemaProvider("site-content", Field("tags", true), Field("title", false));

        Assert.That(
            await FacetAttributeOptions.BuildOptionsAsync(
                provider, "site-content", CancellationToken.None, FacetAttributeOptions.IsRangeFilterable),
            Is.Null);
    }

    [Test]
    public void An_index_name_is_trimmed_before_the_schema_is_looked_up()
    {
        var provider = SchemaProvider("site-content", Field("tags", true));

        Expect.Multiple(() =>
            Assert.That(
                FacetAttributeOptions.BuildOptionsAsync(provider, "  site-content  ", CancellationToken.None).Result,
                Is.EqualTo("tags;tags")));
    }
}
