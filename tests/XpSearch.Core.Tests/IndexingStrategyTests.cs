using CMS.ContentEngine;
using CMS.Websites;

using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;
using Lucene.Net.Facet;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the document <see cref="XpSearchIndexingStrategy"/> produces: that a taxonomy column is read
/// through its registered data type rather than cast (which is what broke every product document on
/// the Dancing Goat host), that an item that cannot be mapped is skipped instead of killing the batch,
/// and the two §10.7 extension points - the contribution hook and linked-item flattening.
/// </summary>
[TestFixture]
internal sealed class IndexingStrategyTests
{
    private const string ProductPage = "DancingGoat.ProductPage";
    private const string ProductCoffee = "DancingGoat.ProductCoffee";
    private const string LinkedField = "ProductPageProduct";

    private static readonly Guid Espresso = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Filter = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly SchemaField Tags =
        new("ProductFieldTags", SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true);

    private static readonly SchemaField Name =
        new("ProductFieldName", SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: true, Retrievable: true);

    [OneTimeSetUp]
    public void RegisterTaxonomyDataType() => TaxonomyDataType.Ensure();

    [Test]
    public async Task Map_ReadsATaxonomyColumnThroughItsRegisteredDataType()
    {
        var data = Container(ProductCoffee, values: new() { [Tags.Name] = TaxonomyDataType.ColumnValue(Espresso, Filter) });
        var strategy = Strategy(data, new XpSearchIndexingOptions(), Fields(ProductCoffee, Tags));

        var document = await strategy.MapToLuceneDocumentOrNull(Item(ProductCoffee));

        Expect.Multiple(() =>
        {
            Assert.That(document, Is.Not.Null, "a JSON taxonomy column must not fail the mapping");
            Assert.That(FacetValues(document!, Tags.Name), Is.EquivalentTo(new[] { "espresso", "filter" }));
            Assert.That(Values(document!, LuceneFieldNames.LabelFieldName(Tags)), Is.EquivalentTo(new[]
            {
                LuceneFieldNames.ComposeLabel("espresso", "Espresso"),
                LuceneFieldNames.ComposeLabel("filter", "Filter")
            }));
            Assert.That(Values(document!, Tags.Name), Is.EquivalentTo(new[] { "espresso", "filter" }), "the tag code names come back as a hit attribute");
        });
    }

    [Test]
    public async Task Map_SkipsAndLogsAnItemWhoseFieldCannotBeRead()
    {
        var data = Container(ProductCoffee, values: []);
        data.GetValue<object>(Tags.Name).Returns(_ => throw new InvalidCastException("Unable to cast object of type 'System.String'."));

        var logger = new RecordingLogger();
        var strategy = Strategy(data, new XpSearchIndexingOptions(), Fields(ProductCoffee, Tags, Name), logger);

        var document = await strategy.MapToLuceneDocumentOrNull(Item(ProductCoffee));

        Expect.Multiple(() =>
        {
            Assert.That(document, Is.Null, "one unmappable document must be skipped, not thrown out of the batch");
            Assert.That(logger.Errors, Has.Exactly(1).Contains(Tags.Name).And.Exactly(1).Contains(ProductCoffee));
        });
    }

    [Test]
    public async Task ContributeAsync_WritesFieldsWithTheSameEncodingAsTheBaseMapping()
    {
        var expectedData = Container(ProductCoffee, values: new()
        {
            [Name.Name] = "Cortado",
            [Tags.Name] = TaxonomyDataType.ColumnValue(Espresso)
        });

        var expected = await Strategy(expectedData, new XpSearchIndexingOptions(), Fields(ProductCoffee, Name, Tags))
            .MapToLuceneDocumentOrNull(Item(ProductCoffee));

        // The same two values, added by a subclass through the context helpers instead of being detected.
        var contributed = await new ContributingStrategy(
                Executor(Container(ProductCoffee, values: [])),
                Substitute.For<IWebPageUrlRetriever>(),
                TaxonomyRetriever(),
                Fields(ProductCoffee),
                new XpSearchIndexingOptions(),
                NullLogger<XpSearchIndexingStrategy>.Instance)
            .MapToLuceneDocumentOrNull(Item(ProductCoffee));

        Expect.Multiple(() =>
        {
            Assert.That(contributed, Is.Not.Null);
            Assert.That(Describe(contributed!), Is.EqualTo(Describe(expected!)));
        });
    }

    [Test]
    public async Task FlattenLinkedItems_IndexesTheLinkedItemsFieldsOnTheParentDocument()
    {
        var linked = Container(ProductCoffee, values: new()
        {
            [Name.Name] = "Cortado",
            [Tags.Name] = TaxonomyDataType.ColumnValue(Espresso)
        });

        var page = Container(ProductPage, values: [], linkedItems: (LinkedField, [linked]));

        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);
        var fields = new StubFieldSource(new() { [ProductPage] = [], [ProductCoffee] = [Name, Tags] });

        var document = await Strategy(page, options, fields).MapToLuceneDocumentOrNull(Item(ProductPage));

        Expect.Multiple(() =>
        {
            Assert.That(Values(document!, Name.Name), Is.EqualTo(new[] { "Cortado" }));
            Assert.That(FacetValues(document!, Tags.Name), Is.EqualTo(new[] { "espresso" }), "a flattened taxonomy is a facet dimension like any other");
        });
    }

    [Test]
    public async Task FlattenLinkedItems_KeepsTheParentsOwnValueWhenTheNamesCollide()
    {
        var linked = Container(ProductCoffee, values: new() { [Name.Name] = "the linked value" });
        var page = Container(ProductPage, values: new() { [Name.Name] = "the parent value" }, linkedItems: (LinkedField, [linked]));

        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);
        var fields = new StubFieldSource(new() { [ProductPage] = [Name], [ProductCoffee] = [Name] });
        var logger = new RecordingLogger();

        var document = await Strategy(page, options, fields, logger).MapToLuceneDocumentOrNull(Item(ProductPage));

        Expect.Multiple(() =>
        {
            Assert.That(Values(document!, Name.Name), Is.EqualTo(new[] { "the parent value" }));
            Assert.That(logger.Warnings, Has.Exactly(1).Contains(Name.Name));
        });
    }

    [Test]
    public async Task IndexSchemaProvider_ReportsFlattenedFieldsOnTheParentContentType()
    {
        var options = new XpSearchIndexingOptions().FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee]);

        using var index = new TestSearchIndex(TestCorpus.IndexName, []);

        var provider = new IndexSchemaProvider(
            index,
            new StubContentTypeSource([ProductPage]),
            new StubFieldSource(new() { [ProductPage] = [], [ProductCoffee] = [Name, Tags] }),
            options);

        var schema = await provider.GetSchemaAsync(TestCorpus.IndexName, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(schema.Find(Tags.Name)?.Facetable, Is.True, "the §7.4 dropdown must see a flattened taxonomy on the page type");
            Assert.That(schema.Find(Name.Name)?.Sortable, Is.True);
        });
    }

    [Test]
    public void FlattenLinkedItems_RaisesTheDepthTheItemIsLoadedWith()
    {
        var options = new XpSearchIndexingOptions()
            .FlattenLinkedItems(ProductPage, LinkedField, [ProductCoffee])
            .FlattenLinkedItems("Some.Other", "OtherField", [ProductCoffee], depth: 3);

        Assert.That(options.LinkedItemsDepth, Is.EqualTo(3));
    }

    private static XpSearchIndexingStrategy Strategy(
        IContentQueryDataContainer data,
        XpSearchIndexingOptions options,
        IContentTypeFieldSource fields,
        ILogger<XpSearchIndexingStrategy>? logger = null) =>
        new(
            Executor(data),
            Substitute.For<IWebPageUrlRetriever>(),
            TaxonomyRetriever(),
            fields,
            options,
            logger ?? NullLogger<XpSearchIndexingStrategy>.Instance);

    private static IContentQueryExecutor Executor(IContentQueryDataContainer data)
    {
        var executor = Substitute.For<IContentQueryExecutor>();

        executor
            .GetResult(
                Arg.Any<ContentItemQueryBuilder>(),
                Arg.Any<Func<IContentQueryDataContainer, IContentQueryDataContainer>>(),
                Arg.Any<ContentQueryExecutionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IContentQueryDataContainer>>([data]));

        return executor;
    }

    private static ITaxonomyRetriever TaxonomyRetriever()
    {
        var retriever = Substitute.For<ITaxonomyRetriever>();

        retriever
            .RetrieveTags(Arg.Any<IEnumerable<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IEnumerable<Tag>>(
                [.. call.Arg<IEnumerable<Guid>>().Select(identifier => identifier == Espresso
                    ? new Tag { Identifier = Espresso, Name = "espresso", Title = "Espresso" }
                    : new Tag { Identifier = Filter, Name = "filter", Title = "Filter" })]));

        return retriever;
    }

    private static IContentQueryDataContainer Container(
        string contentTypeName,
        Dictionary<string, object?> values,
        (string Field, IEnumerable<IContentQueryDataContainer> Items)? linkedItems = null)
    {
        var container = Substitute.For<IContentQueryDataContainer>();

        container.ContentTypeName.Returns(contentTypeName);

        foreach (var (name, value) in values)
        {
            container.GetValue<object>(name).Returns(value);
        }

        if (linkedItems is { } linked)
        {
            container
                .TryGetLinkedItems(linked.Field, out Arg.Any<IEnumerable<IContentQueryDataContainer>>())
                .Returns(call =>
                {
                    call[1] = linked.Items;
                    return true;
                });
        }

        return container;
    }

    private static IIndexEventItemModel Item(string contentTypeName) => new IndexEventReusableItemModel(
        itemID: 1,
        itemGuid: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        languageName: "en",
        contentTypeName: contentTypeName,
        name: "Cortado",
        isSecured: false,
        contentTypeID: 1,
        contentLanguageID: 1);

    private static StubFieldSource Fields(string contentTypeName, params SchemaField[] fields) =>
        new(new() { [contentTypeName] = fields });

    private static IEnumerable<string> Values(Document document, string field) =>
        document.GetFields(field).Where(value => value is not FacetField).Select(value => value.GetStringValue());

    // A FacetField is a placeholder Lucene names "dummy" until FacetsConfig.Build rewrites it, so it is
    // found by its dimension rather than by Document.GetFields.
    private static IEnumerable<string> FacetValues(Document document, string dimension) =>
        document.Fields.OfType<FacetField>().Where(facet => facet.Dim == dimension).Select(facet => facet.Path[0]);

    /// <summary>A comparable rendering of a document: every field, its Lucene type and its value.</summary>
    private static string Describe(Document document) => string.Join(
        "\n",
        document.Fields
            .Select(field => field is FacetField facet
                ? $"{facet.Dim}|FacetField|{facet.Path[0]}"
                : $"{field.Name}|{field.GetType().Name}|{field.GetStringValue()}")
            .OrderBy(line => line, StringComparer.Ordinal));

    /// <summary>Adds the two fields through the hook's helpers instead of letting detection find them.</summary>
    private sealed class ContributingStrategy(
        IContentQueryExecutor executor,
        IWebPageUrlRetriever urlRetriever,
        ITaxonomyRetriever taxonomyRetriever,
        IContentTypeFieldSource fieldSource,
        XpSearchIndexingOptions options,
        ILogger<XpSearchIndexingStrategy> logger)
        : XpSearchIndexingStrategy(executor, urlRetriever, taxonomyRetriever, fieldSource, options, logger)
    {
        protected override async Task ContributeAsync(IndexingContext context, Document document, CancellationToken cancellationToken)
        {
            await context.AddFieldAsync(Name, "Cortado", cancellationToken);
            await context.AddTaxonomyAsync(Tags, [new TagReference { Identifier = Espresso }], cancellationToken);
        }
    }

    private sealed class StubFieldSource(Dictionary<string, IReadOnlyList<SchemaField>> byContentType) : IContentTypeFieldSource
    {
        public IReadOnlyList<SchemaField> GetFields(string contentTypeName) =>
            byContentType.TryGetValue(contentTypeName, out var fields) ? fields : [];
    }

    private sealed class StubContentTypeSource(IReadOnlyList<string> names) : IIndexContentTypeSource
    {
        public Task<IReadOnlyList<string>> GetContentTypesAsync(string indexName, CancellationToken cancellationToken) =>
            Task.FromResult(names);
    }

    private sealed class RecordingLogger : ILogger<XpSearchIndexingStrategy>
    {
        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
            else if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
