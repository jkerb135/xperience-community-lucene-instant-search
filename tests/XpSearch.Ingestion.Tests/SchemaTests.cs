using System.Text.Json;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Schema;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// Spec §10.3: pushed documents are validated against the index schema, unknown fields are rejected
/// unless the index opts in, coercion is narrow, and a field whose type changed is called out.
/// </summary>
[TestFixture]
internal sealed class SchemaTests
{
    [Test]
    public async Task UnknownFieldIsRejectedByDefault()
    {
        using var harness = new TestHarness();

        var response = await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: ("colour", "black"))],
            waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Failed, Is.EqualTo(1));
            Assert.That(response.Errors[0].Field, Is.EqualTo("colour"));
            Assert.That(response.Errors[0].Message, Does.Contain("allowDynamicFields"));
            Assert.That(harness.Index.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task UnknownFieldIsAcceptedWithAllowDynamicFields()
    {
        using var harness = new TestHarness(TestSchema.Products(allowDynamicFields: true));

        var response = await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: ("colour", "black"))],
            waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Failed, Is.Zero);
            Assert.That(harness.Index.Matching("colour", "black"), Is.EqualTo(1));
        });
    }

    [Test]
    public void CoercionIsNarrowAndExplicit()
    {
        var schema = TestSchema.Products();

        Expect.Multiple(() =>
        {
            Assert.That(Validate(schema, "price", "18.50").Errors, Is.Empty, "an unambiguous string becomes a number");
            Assert.That(Validate(schema, "price", "18.50 EUR").Errors, Has.Count.EqualTo(1), "an ambiguous string is an error, not a guess");
            Assert.That(Validate(schema, "inStock", 1).Errors, Has.Count.EqualTo(1), "1 is not a boolean");
            Assert.That(Validate(schema, "inStock", "true").Errors, Is.Empty);
            Assert.That(Validate(schema, "publishedAt", "2026-01-01T00:00:00Z").Attributes["publishedAt"].GetInt64(), Is.EqualTo(1767225600));
            Assert.That(Validate(schema, "title", 42).Errors, Has.Count.EqualTo(1), "a number is not text");
            Assert.That(Validate(schema, "tags", "coffee").Attributes["tags"].GetArrayLength(), Is.EqualTo(1), "a single value becomes a one-element list");
            Assert.That(Validate(schema, "_source", "anything").Errors, Has.Count.EqualTo(1), "reserved attributes cannot be pushed");
        });
    }

    [Test]
    public void PushingAsXperienceIsRejected()
    {
        var validated = DocumentValidator.Validate(TestSchema.Products(), "pim-1", "xperience", new Dictionary<string, JsonElement>(), "external");

        Assert.That(validated.Errors[0].Message, Does.Contain("reserved"));
    }

    [Test]
    public async Task ChangingAFieldsTypeIsDetectedAndSaysARebuildIsNeeded()
    {
        using var harness = new TestHarness();

        // "sku" is a keyword in the schema the documents were written with.
        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: ("sku", "88213"))],
            waitForIndex: true);

        // The same index, now declaring "sku" as a number: the encoding in the index contradicts it.
        var changed = new IngestionSchema(
            new IndexSchema(TestHarness.IndexName, [new SchemaField("sku", SearchFieldKind.Number, false, false, true, true)]),
            AllowDynamicFields: false);

        var errors = new FieldTypeGuard(harness.Index).Check(TestHarness.IndexName, changed.Fields, ["sku"]);

        Expect.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Field, Is.EqualTo("sku"));
            Assert.That(errors[0].Message, Does.Contain("requires a rebuild"));
        });
    }

    [Test]
    public void DeclaredSchemaIsReadOffTheStrategyClass()
    {
        var (fields, allowDynamic) = IngestionSchemaProvider.Declared(typeof(DeclaredStrategy));

        Expect.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Name), Is.EquivalentTo(["title", "price"]));
            Assert.That(fields.Single(field => field.Name == "price").Kind, Is.EqualTo(SearchFieldKind.Number));
            Assert.That(fields.Single(field => field.Name == "title").Searchable, Is.True);
            Assert.That(allowDynamic, Is.True);
        });
    }

    [Test]
    public async Task DeclaredFieldsWinOverDetectedOnes()
    {
        var detected = Substitute.For<IIndexSchemaProvider>();
        detected.GetSchemaAsync(TestHarness.IndexName, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new IndexSchema(TestHarness.IndexName, [new SchemaField("price", SearchFieldKind.Text, true, false, false, true)])));

        var strategies = Substitute.For<IIndexStrategySource>();
        strategies.GetStrategyType(TestHarness.IndexName).Returns(typeof(DeclaredStrategy));

        var provider = new IngestionSchemaProvider(
            detected,
            strategies,
            Microsoft.Extensions.Options.Options.Create(new XpSearchIngestionOptions()));

        var schema = await provider.GetSchemaAsync(TestHarness.IndexName, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(schema.Fields.Find("price")!.Kind, Is.EqualTo(SearchFieldKind.Number));
            Assert.That(schema.AllowDynamicFields, Is.True);
        });
    }

    private static ValidatedDocument Validate(IngestionSchema schema, string field, object value) =>
        DocumentValidator.Validate(
            schema,
            "pim-1",
            "pim",
            new Dictionary<string, JsonElement> { [field] = TestHarness.Value(value) },
            "external");

    [XpSearchSchema(AllowDynamicFields = true)]
    [XpSearchField("title", SearchFieldKind.Text, Searchable = true, Sortable = true)]
    [XpSearchField("price", SearchFieldKind.Number, Sortable = true)]
    private sealed class DeclaredStrategy
    {
    }
}
