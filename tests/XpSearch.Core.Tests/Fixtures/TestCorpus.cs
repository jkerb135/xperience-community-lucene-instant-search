using Kentico.Xperience.Lucene.Core;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>One document of the fixture corpus, before it becomes a Lucene document.</summary>
internal sealed record TestDocument(
    string ResultId,
    string Title,
    string Body,
    string ContentType,
    string Language,
    string Url,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Topics,
    double? Price,
    long? PublishedAt);

/// <summary>
/// The fixture corpus and the schema that describes it: two content types, two taxonomy dimensions,
/// a price, a date, a rich-text body and one document whose body carries a script tag.
/// </summary>
internal static class TestCorpus
{
    internal const string IndexName = "TestIndex";
    internal const string BodyField = "Body";
    internal const string CategoryField = "Category";
    internal const string TagsField = "Tags";
    internal const string TopicField = "Topic";
    internal const string PriceField = "Price";
    internal const string PublishedAtField = "PublishedAt";

    /// <summary>The result id of the document whose body contains a script tag.</summary>
    internal const string ScriptDocumentId = "doc-script:en";

    /// <summary>
    /// The title of each taxonomy tag, keyed by its code name - the pair an Xperience taxonomy field
    /// carries, and the reason a facet value has both a value and a label.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> TagTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["coffee"] = "Coffee beans",
        ["equipment"] = "Equipment",
        ["accessories"] = "Accessories",
        ["brewing"] = "Brewing",
        ["milk"] = "Milk",
        ["grinding"] = "Grinding",
        ["drinks"] = "Drinks",
        ["espresso-drinks"] = "Espresso drinks",
        ["latte"] = "Latte",
        ["gear"] = "Gear",
        ["grinders"] = "Grinders"
    };

    /// <summary>
    /// The parent of each tag of the <see cref="TopicField"/> taxonomy, keyed by code name. Three
    /// levels: <c>drinks &gt; espresso-drinks &gt; latte</c> and <c>gear &gt; grinders</c>. The
    /// other two dimensions stay flat, which is what a taxonomy without sub-tags looks like.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> TagParents { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["espresso-drinks"] = "drinks",
        ["latte"] = "espresso-drinks",
        ["grinders"] = "gear"
    };

    /// <summary>Gets the ancestors of a tag, root first, excluding the tag itself.</summary>
    /// <param name="value">The tag code name.</param>
    /// <returns>The ancestor code names.</returns>
    internal static string[] AncestorsOf(string value) =>
        TagParents.TryGetValue(value, out string? parent) ? [.. AncestorsOf(parent), parent] : [];

    /// <summary>Gets the title of a tag, falling back to its code name.</summary>
    /// <param name="value">The tag code name.</param>
    /// <returns>The title.</returns>
    internal static string TitleOf(string value) => TagTitles.TryGetValue(value, out string? title) ? title : value;

    internal static IndexSchema Schema { get; } = new(
        IndexName,
        [
            .. IndexSchemaProvider.BaseFields(),
            new SchemaField(BodyField, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true),
            new SchemaField(CategoryField, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
            new SchemaField(TagsField, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
            new SchemaField(TopicField, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
            new SchemaField(PriceField, SearchFieldKind.Number, Searchable: false, Facetable: false, Sortable: true, Retrievable: true),
            new SchemaField(PublishedAtField, SearchFieldKind.Date, Searchable: false, Facetable: false, Sortable: true, Retrievable: true)
        ]);

    internal static IReadOnlyList<TestDocument> Documents { get; } =
    [
        new("doc-1:en", "Espresso Basics", "Brewing espresso requires pressure and patience.", "Article", "en", "/articles/espresso-basics", ["coffee"], ["brewing", "coffee"], ["espresso-drinks"], null, 1_700_000_000),
        new("doc-2:en", "Latte Art", "Steamed milk poured into espresso.", "Article", "en", "/articles/latte-art", ["coffee"], ["milk"], ["latte"], null, 1_700_000_100),
        new("doc-3:en", "Espresso Machine", "A pump driven espresso machine for the home.", "Product", "en", "/products/espresso-machine", ["equipment"], ["brewing"], [], 499.99, 1_690_000_000),
        new("doc-4:en", "Coffee Grinder", "A burr grinder for consistent coffee grounds.", "Product", "en", "/products/coffee-grinder", ["equipment"], ["grinding"], ["grinders"], 149.5, 1_690_000_100),
        new("doc-5:en", "Filter Papers", "Paper filters for pour over brewing.", "Product", "en", "/products/filter-papers", ["accessories"], ["brewing"], [], 5, 1_680_000_000),
        new("doc-6:de", "Espresso Grundlagen", "Espresso braucht Druck.", "Article", "de", "/de/artikel/espresso", ["coffee"], ["brewing"], ["latte"], null, 1_700_000_200),
        new(ScriptDocumentId, "Script Danger", "<script>alert('xss')</script> espresso injection attempt.", "Article", "en", "/articles/script-danger", ["coffee"], [], [], null, 1_700_000_300)
    ];

    /// <summary>The content type attribute; the documents carry it under the integration's own field name.</summary>
    internal static string ContentTypeField => IndexSchemaProvider.ContentTypeAttribute;

    /// <summary>The language attribute; the documents carry it under the integration's own field name.</summary>
    internal static string LanguageField => IndexSchemaProvider.LanguageAttribute;
}
