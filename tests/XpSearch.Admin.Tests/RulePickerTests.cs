using System.Text.Json;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Search;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The two index reads behind the rule builder's pickers (design canvas 5h): what they ask the index
/// for, and that a marketer using them never lands in the analytics.
/// </summary>
[TestFixture]
internal sealed class RulePickerTests
{
    private const string IndexName = "articles";

    private IQueryTesterSearch search = null!;
    private IIndexDocumentLookup lookup = null!;
    private IIndexSchemaProvider schemaProvider = null!;
    private RulePicker picker = null!;

    [SetUp]
    public void SetUp()
    {
        search = Substitute.For<IQueryTesterSearch>();
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new QueryTesterSideResult(Empty(), [])));

        lookup = Substitute.For<IIndexDocumentLookup>();
        lookup
            .ResolveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IndexedDocument>>([]));

        schemaProvider = Substitute.For<IIndexSchemaProvider>();
        schemaProvider
            .GetSchemaAsync(IndexName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IndexSchema(
                IndexName,
                [
                    new SchemaField("title", SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true),
                    new SchemaField("Category", SearchFieldKind.Taxonomy, Searchable: false, Facetable: true, Sortable: false, Retrievable: true),
                    new SchemaField("contentType", SearchFieldKind.Keyword, Searchable: false, Facetable: true, Sortable: false, Retrievable: true)
                ])));

        picker = new RulePicker(search, lookup, schemaProvider);
    }

    [Test]
    public async Task SearchAsync_RunsOnePageOfUntunedResultsAndReadsTheTitleAndUrl()
    {
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new QueryTesterSideResult(
                new SearchResponse
                {
                    Results = [Result("doc-4:en", "Coffee Grinder", "/products/coffee-grinder")],
                    Total = 1,
                    Page = 1,
                    PageSize = RulePicker.MaxItems,
                    TotalPages = 1,
                },
                [])));

        var items = await picker.SearchAsync(IndexName, "grin", CancellationToken.None);

        var arguments = search.ReceivedCalls().Single().GetArguments();
        var request = (SearchRequest)arguments[0]!;

        Expect.Multiple(() =>
        {
            Assert.That(request.Index, Is.EqualTo(IndexName), "the index is the page's, never the client's");
            Assert.That(request.Query, Is.EqualTo("grin"));
            Assert.That(request.PageSize, Is.EqualTo(RulePicker.MaxItems), "the picker offers one short list, not a whole index");
            Assert.That((bool)arguments[1]!, Is.False, "the picker shows the index as it is, not as the rule being written rewrites it");
            Assert.That(items.Select(item => item.Id), Is.EqualTo(new[] { "doc-4:en" }).AsCollection);
            Assert.That(items[0].Title, Is.EqualTo("Coffee Grinder"));
            Assert.That(items[0].Url, Is.EqualTo("/products/coffee-grinder"));
        });
    }

    [Test]
    public async Task ValuesAsync_AsksForOneFacetAndNoResults()
    {
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var response = Empty();
                response.Facets = new Dictionary<string, FacetValue[]>
                {
                    ["Category"] = [new FacetValue { Value = "grinders", Label = "Grinders", Count = 6 }],
                };

                return Task.FromResult(new QueryTesterSideResult(response, []));
            });

        var values = await picker.ValuesAsync(IndexName, " Category ", CancellationToken.None);

        var request = (SearchRequest)search.ReceivedCalls().Single().GetArguments()[0]!;

        Expect.Multiple(() =>
        {
            Assert.That(request.Facets, Is.EqualTo(new[] { "Category" }).AsCollection);
            Assert.That(request.PageSize, Is.EqualTo(1), "a facet-only query: the documents are not wanted");
            Assert.That(request.Query, Is.Empty);
            Assert.That(values.Select(value => (value.Value, value.Label, value.Count)), Is.EqualTo(new[] { ("grinders", "Grinders", 6L) }).AsCollection);
        });
    }

    [Test]
    public async Task ValuesAsync_IsEmptyForAnAttributeTheIndexDoesNotFacet()
    {
        Assert.That(await picker.ValuesAsync(IndexName, "Category", CancellationToken.None), Is.Empty);
        Assert.That(await picker.ValuesAsync(IndexName, "  ", CancellationToken.None), Is.Empty);
        Assert.That(search.ReceivedCalls().Count(), Is.EqualTo(1), "a blank attribute is not worth a search");
    }

    /// <summary>
    /// A rule that names an item the index no longer holds keeps its place with no title, so the
    /// builder can warn about it rather than silently forgetting what the rule points at.
    /// </summary>
    [Test]
    public async Task ResolveAsync_KeepsAnIdTheIndexNoLongerHoldsWithNoTitle()
    {
        lookup
            .ResolveAsync(IndexName, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IndexedDocument>>([new IndexedDocument("doc-1:en", "Espresso Basics", "/articles/espresso-basics")]));

        var items = await picker.ResolveAsync(IndexName, ["doc-1:en", "doc-gone:en", "", "doc-1:en"], CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(items.Select(item => item.Id), Is.EqualTo(new[] { "doc-1:en", "doc-gone:en" }).AsCollection);
            Assert.That(items[0].Title, Is.EqualTo("Espresso Basics"));
            Assert.That(items[1].Title, Is.Null, "null title, not an empty one: the id was not found at all");
            Assert.That(items[1].Url, Is.Null);
        });
    }

    /// <summary>The attribute drop-down offers the facetable fields, and only those (spec §7.4).</summary>
    [Test]
    public async Task AttributesAsync_OffersTheIndexesFacetableFields() =>
        Assert.That(
            await picker.AttributesAsync(IndexName, CancellationToken.None),
            Is.EqualTo(new[] { "Category", "contentType" }).AsCollection);

    /// <summary>
    /// A marketer picking an item must not skew the analytics dashboard: the search activity and the
    /// query log row are written by <see cref="ISearchRequestJournal"/> from the caching decorator,
    /// and the picker searches through <see cref="IQueryTesterSearch"/>, which assembles its own
    /// pipeline instead. This pins that construction down.
    /// </summary>
    [Test]
    public void RulePicker_TakesNoPipelineAndNoAnalyticsDependency()
    {
        var parameters = typeof(RulePicker).GetConstructors().SelectMany(constructor => constructor.GetParameters());

        Expect.Multiple(() =>
        {
            Assert.That(
                parameters.Select(parameter => parameter.ParameterType),
                Has.None.AnyOf(typeof(ISearchPipeline), typeof(ISearchRequestJournal), typeof(IQueryLogQueue), typeof(ISearchActivityLogger)));
            Assert.That(parameters.Select(parameter => parameter.ParameterType), Does.Contain(typeof(IQueryTesterSearch)));
        });
    }

    private static SearchResponse Empty() =>
        new() { Results = [], Total = 0, TookMs = 1, Page = 1, PageSize = 10, TotalPages = 0 };

    private static Result Result(string id, string title, string url) =>
        new()
        {
            Id = id,
            Attributes = new Dictionary<string, JsonElement>
            {
                ["title"] = JsonSerializer.SerializeToElement(title),
                ["url"] = JsonSerializer.SerializeToElement(url),
            },
        };
}

/// <summary>
/// The picker page commands and the resolved items the builder loads with (CR-5). The commands are
/// exercised on the create page, which is the one <see cref="RuleBuilderPage"/> that needs no Info
/// object - see the KNOWN-LIMITATIONS note on Info objects outside a running instance.
/// </summary>
[TestFixture]
internal sealed class RuleBuilderPickerCommandTests
{
    private const int IndexIdentifier = 7;

    private IRulePicker picker = null!;
    private IContactGroupCatalog contactGroups = null!;
    private RuleCreate page = null!;

    [SetUp]
    public void SetUp()
    {
        picker = Substitute.For<IRulePicker>();
        picker.SearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PickedItemDto>>([new PickedItemDto { Id = "doc-1:en", Title = "Espresso Basics" }]));
        picker.ValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttributeValueDto>>([new AttributeValueDto { Value = "grinders", Label = "Grinders", Count = 6 }]));
        picker.AttributesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Category"]));

        contactGroups = Substitute.For<IContactGroupCatalog>();
        contactGroups.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ContactGroupOption>>([]));

        page = Page(IndexIdentifier);
    }

    private RuleCreate Page(int identifier) =>
        new(
            Storage.Holding(IndexIdentifier, "articles", "en"),
            Substitute.For<IInfoProvider<XpSearchRuleInfo>>(),
            contactGroups,
            Substitute.For<IPageLinkGenerator>(),
            picker)
        {
            IndexIdentifier = identifier
        };

    [Test]
    public async Task SearchItems_SearchesTheIndexInTheUrl()
    {
        var response = await page.SearchItems(new ItemSearchRequest { Query = "espresso" }, CancellationToken.None);

        await picker.Received(1).SearchAsync("articles", "espresso", Arg.Any<CancellationToken>());

        Expect.Multiple(() =>
        {
            Assert.That(response.Result.Error, Is.Empty);
            Assert.That(response.Result.Items.Select(item => item.Id), Is.EqualTo(new[] { "doc-1:en" }).AsCollection);
        });
    }

    [Test]
    public async Task GetAttributeValues_ReturnsTheValuesWithTheirCounts()
    {
        var response = await page.GetAttributeValues(new AttributeValuesRequest { Attribute = "Category" }, CancellationToken.None);

        await picker.Received(1).ValuesAsync("articles", "Category", Arg.Any<CancellationToken>());

        Assert.That(response.Result.Values.Select(value => value.Count), Is.EqualTo(new[] { 6L }).AsCollection);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BothCommands_ReportAnUnregisteredIndexWithoutTouchingTheIndex(bool items)
    {
        var unregistered = Page(999);

        string error = items
            ? (await unregistered.SearchItems(new ItemSearchRequest(), CancellationToken.None)).Result.Error
            : (await unregistered.GetAttributeValues(new AttributeValuesRequest { Attribute = "Category" }, CancellationToken.None)).Result.Error;

        Expect.Multiple(() =>
        {
            Assert.That(error, Is.Not.Empty);
            Assert.That(picker.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task ConfigureTemplateProperties_OffersTheIndexesFacetableAttributes()
    {
        var properties = await page.ConfigureTemplateProperties(new RuleBuilderClientProperties());

        Assert.That(properties.Attributes, Is.EqualTo(new[] { "Category" }).AsCollection);
    }

    /// <summary>Both picker commands are reads, so both sit behind the applications read permission.</summary>
    [TestCase(nameof(RuleBuilderPage.SearchItems))]
    [TestCase(nameof(RuleBuilderPage.GetAttributeValues))]
    public void BothCommands_AreBehindTheApplicationsReadPermission(string command) =>
        Assert.That(
            typeof(RuleBuilderPage)
                .GetMethod(command)!
                .GetCustomAttributes(typeof(PageCommandAttribute), inherit: false)
                .Cast<PageCommandAttribute>()
                .Single()
                .Permission,
            Is.EqualTo(SystemPermissions.VIEW));
}
