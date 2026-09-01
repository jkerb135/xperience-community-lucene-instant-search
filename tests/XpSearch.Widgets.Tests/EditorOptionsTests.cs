using System.Reflection;

using Kentico.Xperience.Admin.Base.FormAnnotations;

using NUnit.Framework;

using XpSearch.Core;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Options;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Sorting;
using XpSearch.Core.Rendering;

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
/// The drop-downs an editor sees: the index list, the result templates (§5.8) and the sort option
/// grammar (§7.3). The facet attribute options live in Core; see <c>FacetAttributeOptionsTests</c>.
/// </summary>
[TestFixture]
internal sealed class EditorOptionsTests
{
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
    public async Task The_field_selector_offers_the_stored_fields_of_every_index_once()
    {
        var provider = new IndexFieldSelectorDataProvider(
            new FakeIndexCatalog("products", "site-content"),
            new StubServices(new StubSchemas(
                new IndexSchema("products", [Field("title"), Field("price"), Field("sku", retrievable: false)]),
                new IndexSchema("site-content", [Field("title"), Field("summary")]))));

        var all = await provider.GetItemsAsync(string.Empty, 0, CancellationToken.None);
        var searched = await provider.GetItemsAsync("TIT", 0, CancellationToken.None);
        var selected = await provider.GetSelectedItemsAsync(["retired-field"], CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(
                all.Items.Select(item => item.Value),
                Is.EqualTo(new[] { "title", "price", "summary" }),
                "the union of the retrievable fields, each once");
            Assert.That(all.NextPageAvailable, Is.False);
            Assert.That(searched.Items.Select(item => item.Value), Is.EqualTo(new[] { "title" }));
            // A value an older widget stored has to survive the round trip, schema or no schema.
            Assert.That(selected.Single().Value, Is.EqualTo("retired-field"));
        });
    }

    [Test]
    public async Task The_field_selector_is_empty_without_a_schema_provider()
    {
        var provider = new IndexFieldSelectorDataProvider(new FakeIndexCatalog("products"), new StubServices(null));

        var items = await provider.GetItemsAsync(string.Empty, 0, CancellationToken.None);

        Assert.That(items.Items, Is.Empty);
    }

    private static SchemaField Field(string name, bool retrievable = true) =>
        new(name, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: retrievable);

    private sealed class StubServices : IServiceProvider
    {
        private readonly IIndexSchemaProvider? schemas;

        public StubServices(IIndexSchemaProvider? schemas) => this.schemas = schemas;

        public object? GetService(Type serviceType) => serviceType == typeof(IIndexSchemaProvider) ? schemas : null;
    }

    private sealed class StubSchemas : IIndexSchemaProvider
    {
        private readonly IndexSchema[] schemas;

        public StubSchemas(params IndexSchema[] schemas) => this.schemas = schemas;

        public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken)
        {
            var schema = schemas.FirstOrDefault(candidate =>
                string.Equals(candidate.IndexName, indexName, StringComparison.OrdinalIgnoreCase));

            return schema is null
                ? Task.FromException<IndexSchema>(new IndexNotFoundException(indexName))
                : Task.FromResult(schema);
        }
    }

    [Test]
    public void The_range_filter_attribute_dropdown_asks_for_the_numeric_configurator()
    {
        var attribute = typeof(RangeFilterWidgetProperties).GetProperty(nameof(RangeFilterWidgetProperties.Attribute))!
            .GetCustomAttribute<FormComponentConfigurationAttribute>();

        Expect.Multiple(() =>
        {
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute!.Identifier, Is.EqualTo(XpSearchConstants.NumericAttributeConfiguratorIdentifier));
            // The drop-down must be ordered after the index it depends on.
            Assert.That(
                typeof(RangeFilterWidgetProperties).GetProperty(nameof(RangeFilterWidgetProperties.Attribute))!
                    .GetCustomAttribute<DropDownComponentAttribute>()!.Order,
                Is.GreaterThan(XpSearchMountWidgetProperties.OrderIndex));
        });
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
