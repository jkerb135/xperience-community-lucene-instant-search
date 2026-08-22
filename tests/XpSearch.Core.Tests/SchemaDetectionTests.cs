using CMS.DataEngine;
using CMS.FormEngine;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests that auto-detection turns a content type's class form definition into a schema, and in
/// particular that a Taxonomy field becomes a facetable dimension without any hand-written mapping
/// (spec §4.5).
/// </summary>
/// <remarks>
/// <para>
/// The synthetic definition is produced by <see cref="FormInfo.GetXmlDefinition"/> from
/// <see cref="FormFieldInfo"/> instances, which is the same serializer that writes
/// <c>CMS_Class.ClassFormDefinition</c>. Data types are the ones documented at
/// https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types
/// - the Tag selector component is listed there as field data type Taxonomy / C# type
/// IEnumerable&lt;TagReference&gt;.
/// </para>
/// <para>
/// The reusable field schema tests run against the real <c>ClassFormDefinition</c> of
/// <c>DancingGoat.ProductCoffee</c> and <c>CMS.ContentItemCommonData</c>, copied verbatim out of a
/// Dancing Goat database into <c>Fixtures/ClassFormDefinitions</c>. In that data
/// <c>ProductFieldTags</c> and <c>ProductFieldCategory</c> exist only on
/// <c>CMS.ContentItemCommonData</c>, tagged with the <c>ProductFields</c> schema guid; the content
/// type itself carries nothing but the <c>&lt;schema&gt;</c> reference.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class SchemaDetectionTests
{
    private const string ContentTypeName = "DancingGoat.ProductPage";
    private const string ProductCoffee = "DancingGoat.ProductCoffee";
    private const string CommonData = FormInfoContentTypeFieldSource.ReusableFieldSchemaClassName;

    /// <summary>Reads a class form definition copied verbatim from a Dancing Goat database.</summary>
    private static string RealDefinition(string className) => File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ClassFormDefinitions", className + ".xml"));

    private static string ClassFormDefinition()
    {
        var form = new FormInfo();

        form.AddFormItem(Field("ProductFieldName", FieldDataType.Text));
        form.AddFormItem(Field("ProductFieldDescription", FieldDataType.RichTextHTML));
        form.AddFormItem(Field("ProductFieldPrice", FieldDataType.Decimal));
        form.AddFormItem(Field("ProductFieldReleasedOn", FieldDataType.DateTime));
        form.AddFormItem(Field("ProductFieldTags", FieldDataType.Taxonomy));
        form.AddFormItem(Field("ProductFieldImage", FieldDataType.ContentItemAsset));
        form.AddFormItem(Field("ProductFieldSystemFlag", FieldDataType.Boolean));

        return form.GetXmlDefinition();
    }

    private static FormFieldInfo Field(string name, string dataType) => new()
    {
        Name = name,
        DataType = dataType,
        Caption = name,
        Visible = true,
        AllowEmpty = true
    };

    [Test]
    public void Detect_MakesATaxonomyFieldAFacetableDimension()
    {
        var fields = FormInfoContentTypeFieldSource.Detect(ClassFormDefinition(), ContentTypeName, new XpSearchIndexingOptions());
        var tags = fields.Single(field => field.Name == "ProductFieldTags");

        Expect.Multiple(() =>
        {
            Assert.That(tags.Kind, Is.EqualTo(SearchFieldKind.Taxonomy));
            Assert.That(tags.Facetable, Is.True, "a taxonomy field must be facetable without any hand-written mapping");
            Assert.That(tags.Searchable, Is.True);
            Assert.That(tags.Retrievable, Is.True);
        });
    }

    [Test]
    public void Detect_MapsTheOtherDataTypesAndSkipsTheOnesWithoutSearchMeaning()
    {
        var fields = FormInfoContentTypeFieldSource
            .Detect(ClassFormDefinition(), ContentTypeName, new XpSearchIndexingOptions())
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields["ProductFieldName"].Kind, Is.EqualTo(SearchFieldKind.Text));
            Assert.That(fields["ProductFieldName"].Sortable, Is.True);
            Assert.That(fields["ProductFieldDescription"].Kind, Is.EqualTo(SearchFieldKind.Text));
            Assert.That(fields["ProductFieldDescription"].Sortable, Is.False, "long text is not a sensible sort key");
            Assert.That(fields["ProductFieldPrice"].Kind, Is.EqualTo(SearchFieldKind.Number));
            Assert.That(fields["ProductFieldReleasedOn"].Kind, Is.EqualTo(SearchFieldKind.Date));
            Assert.That(fields.ContainsKey("ProductFieldImage"), Is.False, "an asset field is not searchable text");
            Assert.That(fields.ContainsKey("ProductFieldSystemFlag"), Is.False, "a boolean is not searchable text");
        });
    }

    [Test]
    public void Detect_HonoursTheDeveloperOverrides()
    {
        var options = new XpSearchIndexingOptions()
            .Exclude(ContentTypeName, "ProductFieldDescription")
            .Configure(ContentTypeName, "ProductFieldName", field => field with { Boost = 5f });

        var fields = FormInfoContentTypeFieldSource
            .Detect(ClassFormDefinition(), ContentTypeName, options)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields.ContainsKey("ProductFieldDescription"), Is.False);
            Assert.That(fields["ProductFieldName"].Boost, Is.EqualTo(5f));
        });
    }

    [Test]
    public void Detect_LeavesOtherContentTypesAlone()
    {
        var options = new XpSearchIndexingOptions().Exclude("Some.OtherType", "ProductFieldName");

        var fields = FormInfoContentTypeFieldSource.Detect(ClassFormDefinition(), ContentTypeName, options);

        Assert.That(fields.Any(field => field.Name == "ProductFieldName"), Is.True);
    }

    [Test]
    public void Detect_BindsTaxonomyFieldsThatComeFromAReusableFieldSchema()
    {
        var fields = FormInfoContentTypeFieldSource
            .Detect(RealDefinition(ProductCoffee), ProductCoffee, new XpSearchIndexingOptions(), RealDefinition(CommonData))
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields["ProductFieldTags"].Kind, Is.EqualTo(SearchFieldKind.Taxonomy));
            Assert.That(fields["ProductFieldTags"].Facetable, Is.True, "taxonomy ProductTags reaches the type only through the ProductFields schema");
            Assert.That(fields["ProductFieldCategory"].Kind, Is.EqualTo(SearchFieldKind.Taxonomy));
            Assert.That(fields["ProductFieldCategory"].Facetable, Is.True);
            Assert.That(fields["ProductSKUCode"].Kind, Is.EqualTo(SearchFieldKind.Text), "the second referenced schema contributes too");
            Assert.That(fields["CoffeeTastes"].Facetable, Is.True, "the content type's own fields are still detected");
            Assert.That(fields.ContainsKey("SEOFieldsTitle"), Is.False, "a schema the content type does not reference contributes nothing");
            Assert.That(fields.ContainsKey("ProductFieldImage"), Is.False, "a schema field is mapped by data type like any other");
        });
    }

    [Test]
    public void Detect_PrefersTheContentTypeFieldWhenASchemaDefinesTheSameName()
    {
        var form = new FormInfo(RealDefinition(ProductCoffee));
        form.AddFormItem(Field("ProductFieldTags", FieldDataType.Text));

        var logger = new RecordingLogger();

        var fields = FormInfoContentTypeFieldSource
            .Detect(form.GetXmlDefinition(), ProductCoffee, new XpSearchIndexingOptions(), RealDefinition(CommonData), logger)
            .ToLookup(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields["ProductFieldTags"].Single().Kind, Is.EqualTo(SearchFieldKind.Text), "the content type's own field wins");
            Assert.That(logger.Warnings, Has.Exactly(1).Contains("ProductFieldTags"));
        });
    }

    [Test]
    public async Task IndexSchemaProvider_MergesTheBaseFieldsWithEveryContentType()
    {
        var provider = new IndexSchemaProvider(
            new TestSearchIndex(TestCorpus.IndexName, []),
            new StaticContentTypeSource([ContentTypeName]),
            new StaticFieldSource(FormInfoContentTypeFieldSource.Detect(ClassFormDefinition(), ContentTypeName, new XpSearchIndexingOptions())),
            new XpSearchIndexingOptions());

        var schema = await provider.GetSchemaAsync(TestCorpus.IndexName, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(schema.Find(IndexSchemaProvider.TitleAttribute), Is.Not.Null);
            Assert.That(schema.Find("ProductFieldTags")!.Facetable, Is.True);
            Assert.That(schema.Find(IndexSchemaProvider.ContentTypeAttribute)!.Facetable, Is.True);

            // The base fields are the only ones whose attribute name and Lucene field differ.
            Assert.That(schema.Find(IndexSchemaProvider.TitleAttribute)!.LuceneName, Is.EqualTo(IndexSchemaProvider.TitleField));
            Assert.That(schema.Find("ProductFieldTags")!.LuceneName, Is.EqualTo("ProductFieldTags"));
        });
    }

    [Test]
    public void IndexSchemaProvider_ThrowsForAnUnknownIndex()
    {
        using var index = new TestSearchIndex(TestCorpus.IndexName, []);
        var provider = new IndexSchemaProvider(index, new StaticContentTypeSource([]), new StaticFieldSource([]), new XpSearchIndexingOptions());

        Expect.ThrowsAsync<IndexNotFoundException>(() => provider.GetSchemaAsync("Nope", CancellationToken.None));
    }

    private sealed class RecordingLogger : ILogger
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

    private sealed class StaticContentTypeSource(IReadOnlyList<string> names) : IIndexContentTypeSource
    {
        public Task<IReadOnlyList<string>> GetContentTypesAsync(string indexName, CancellationToken cancellationToken) =>
            Task.FromResult(names);
    }

    private sealed class StaticFieldSource(IReadOnlyList<SchemaField> fields) : IContentTypeFieldSource
    {
        public IReadOnlyList<SchemaField> GetFields(string contentTypeName) => fields;
    }
}
