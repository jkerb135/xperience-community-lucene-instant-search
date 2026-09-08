using CMS.ContentEngine;
using CMS.Websites;

using Kentico.Xperience.Lucene.Core.Indexing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// IX-2: editing a reusable item whose fields are flattened onto a page must reindex that page.
/// These tests pin what <see cref="XpSearchIndexingStrategy.FindItemsToReindex(IndexEventReusableItemModel)"/>
/// returns for the changed item, and the startup warning about a linked type the index does not watch -
/// without it, Kentico raises no event at all and nothing here ever runs.
/// </summary>
[TestFixture]
internal sealed class LinkedItemReindexTests
{
    private const string ProductPage = "DancingGoat.ProductPage";
    private const string StorePage = "DancingGoat.StorePage";
    private const string ProductCoffee = "DancingGoat.ProductCoffee";
    private const string LinkedField = "ProductPageProduct";
    private const string IndexName = "products";
    private const string Channel = "DancingGoatPages";

    private static readonly Guid PageGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Test]
    public async Task FindItemsToReindex_ReturnsThePagesLinkingTheChangedItem()
    {
        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);
        var strategy = Strategy(options, Definition(["en"]), Page());

        var items = await strategy.FindItemsToReindex(Changed(ProductCoffee));

        var page = items.OfType<IndexEventWebPageItemModel>().Single();

        Expect.Multiple(() =>
        {
            Assert.That(page.ItemID, Is.EqualTo(7));
            Assert.That(page.ItemGuid, Is.EqualTo(PageGuid));
            Assert.That(page.ContentTypeName, Is.EqualTo(ProductPage), "the document that goes stale is the page's, not the product's");
            Assert.That(page.LanguageName, Is.EqualTo("en"));
            Assert.That(page.WebsiteChannelName, Is.EqualTo(Channel));
            Assert.That(page.WebPageItemTreePath, Is.EqualTo("/Store/Cortado"));
            Assert.That(page.Name, Is.EqualTo("Cortado"));
        });
    }

    /// <summary>Every language variant is its own document, so every one of them is reindexed.</summary>
    [Test]
    public async Task FindItemsToReindex_ReturnsOneItemPerIndexedLanguage()
    {
        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);
        var strategy = Strategy(options, Definition(["en", "es"]), Page());

        var items = await strategy.FindItemsToReindex(Changed(ProductCoffee));

        Assert.That(
            items.Select(item => item.LanguageName),
            Is.EquivalentTo(new[] { "en", "es" }));
    }

    [Test]
    public async Task FindItemsToReindex_UnionsTheRegistrationsAndReturnsEachPageOnce()
    {
        var options = new XpSearchIndexingOptions()
            .FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee])
            .FlattenLinkedItems(StorePage, LinkedField, [ProductCoffee, "DancingGoat.ProductBrewer"]);

        // The same page row comes back for both registrations; one page in one language is one document.
        var strategy = Strategy(options, Definition(["en"]), Page());

        var items = await strategy.FindItemsToReindex(Changed(ProductCoffee));

        Assert.That(items.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task FindItemsToReindex_FallsBackToTheBaseBehaviourForAnUnregisteredType()
    {
        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);
        var strategy = Strategy(options, Definition(["en"]), Page());

        var changed = Changed("DancingGoat.Article");
        var items = await strategy.FindItemsToReindex(changed);

        Assert.That(items.Single(), Is.SameAs(changed), "the base strategy reindexes the changed item itself");
    }

    [Test]
    public async Task RegistrationCheck_WarnsOncePerLinkedTypeTheIndexDoesNotWatch()
    {
        var options = new XpSearchIndexingOptions()
            .FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee, "DancingGoat.ProductBrewer"])
            .FlattenLinkedItems(StorePage, LinkedField, [ProductCoffee]);

        var logger = new RecordingLogger<FlattenedLinkRegistrationCheck>();
        var definition = new IndexDefinition(IndexName, [Channel], ["en"], [ProductPage, StorePage], ["DancingGoat.ProductBrewer"]);

        await new FlattenedLinkRegistrationCheck(Accessor(definition), options, logger).RunAsync(CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(logger.Warnings, Has.Exactly(1).Contains(ProductCoffee), "one warning, however many registrations name the type");
            Assert.That(logger.Warnings, Has.None.Contains("ProductBrewer"), "a type the index already watches is fine");
            Assert.That(logger.Warnings.Single(), Does.Contain(IndexName).And.Contain(LinkedField));
        });
    }

    [Test]
    public async Task RegistrationCheck_IgnoresAnIndexThatDoesNotCoverTheFlatteningPage()
    {
        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);

        var logger = new RecordingLogger<FlattenedLinkRegistrationCheck>();
        var definition = new IndexDefinition(IndexName, [Channel], ["en"], ["DancingGoat.ArticlePage"], []);

        await new FlattenedLinkRegistrationCheck(Accessor(definition), options, logger).RunAsync(CancellationToken.None);

        Assert.That(logger.Warnings, Is.Empty);
    }

    private static IndexEventReusableItemModel Changed(string contentTypeName) => new(
        itemID: 42,
        itemGuid: Guid.Parse("55555555-5555-5555-5555-555555555555"),
        languageName: "en",
        contentTypeName: contentTypeName,
        name: "Cortado",
        isSecured: false,
        contentTypeID: 1,
        contentLanguageID: 1);

    private static IndexDefinition Definition(IReadOnlyList<string> languages) =>
        new(IndexName, [Channel], languages, [ProductPage, StorePage], [ProductCoffee]);

    /// <summary>One page row, as the content query returns it: every value read by column name.</summary>
    private static IContentQueryDataContainer Page()
    {
        var page = Substitute.For<IContentQueryDataContainer>();

        page.ContentTypeName.Returns(ProductPage);
        page.GetValue<int>("WebPageItemID").Returns(7);
        page.GetValue<Guid>("WebPageItemGUID").Returns(PageGuid);
        page.GetValue<string>("WebPageItemName").Returns("Cortado");
        page.GetValue<string>("WebPageItemTreePath").Returns("/Store/Cortado");
        page.GetValue<int>("WebPageItemOrder").Returns(3);
        page.GetValue<int>("WebPageItemParentID").Returns(2);
        page.GetValue<int>("ContentItemContentTypeID").Returns(11);
        page.GetValue<int>("ContentItemCommonDataContentLanguageID").Returns(1);

        return page;
    }

    private static ILuceneIndexAccessor Accessor(IndexDefinition definition)
    {
        var accessor = Substitute.For<ILuceneIndexAccessor>();

        accessor.IndexNames().Returns([definition.IndexName]);
        accessor.IndexNamesForStrategy(Arg.Any<Type>()).Returns([definition.IndexName]);
        accessor.GetDefinitionAsync(definition.IndexName, Arg.Any<CancellationToken>()).Returns(Task.FromResult(definition));

        return accessor;
    }

    private static XpSearchIndexingStrategy Strategy(
        XpSearchIndexingOptions options,
        IndexDefinition definition,
        IContentQueryDataContainer page)
    {
        var executor = Substitute.For<IContentQueryExecutor>();

        // GetWebPageResult is an extension over GetResult that wraps each row in a web page container,
        // so the stub hands the query's own selector the row above.
        executor
            .GetResult(
                Arg.Any<ContentItemQueryBuilder>(),
                Arg.Any<Func<IContentQueryDataContainer, IWebPageContentQueryDataContainer>>(),
                Arg.Any<ContentQueryExecutionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IEnumerable<IWebPageContentQueryDataContainer>>(
                [call.Arg<Func<IContentQueryDataContainer, IWebPageContentQueryDataContainer>>()(page)]));

        return new XpSearchIndexingStrategy(
            executor,
            Substitute.For<IWebPageUrlRetriever>(),
            Substitute.For<ITaxonomyRetriever>(),
            Substitute.For<ITagAncestrySource>(),
            Substitute.For<IContentTypeFieldSource>(),
            Accessor(definition),
            Substitute.For<IIndexSchemaProvider>(),
            options,
            NullLogger<XpSearchIndexingStrategy>.Instance);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
