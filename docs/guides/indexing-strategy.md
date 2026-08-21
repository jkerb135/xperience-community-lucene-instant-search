## Indexing strategy

`XpSearchIndexingStrategy` is the base class an Xperience Search project derives its Lucene indexing
strategy from. It indexes every content type an index covers without a line of per-type mapping, and it
turns every Xperience **Taxonomy** field into a facet dimension automatically — that automatic binding is
the point of the class.

```csharp
using CMS.ContentEngine;
using CMS.Websites;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Indexing;

public sealed class MySearchIndexingStrategy : XpSearchIndexingStrategy
{
    public MySearchIndexingStrategy(
        IContentQueryExecutor executor,
        IWebPageUrlRetriever urlRetriever,
        ITaxonomyRetriever taxonomyRetriever,
        IContentTypeFieldSource fieldSource,
        ILogger<XpSearchIndexingStrategy> logger)
        : base(executor, urlRetriever, taxonomyRetriever, fieldSource, logger)
    {
    }
}
```

```csharp
// Program.cs
builder.Services.AddKenticoLucene(lucene => lucene
    .RegisterStrategy<MySearchIndexingStrategy>(nameof(MySearchIndexingStrategy)));

builder.Services.AddXpSearch(
    options => options.CacheTtl = TimeSpan.FromSeconds(60),
    indexing => indexing
        .Exclude("DancingGoat.ArticlePage", "ArticlePageSummary")
        .Configure("DancingGoat.ArticlePage", "ArticleTitle", field => field with { Boost = 3f }));
```

Pick the registered strategy name in the **Search** application when you create the index, then rebuild.

### What it indexes

Every document gets these fields whatever its content type:

| Field | Kind | Flags | Where it comes from |
|---|---|---|---|
| `ID` | keyword | — | `{itemGuid}:{languageName}`, surfaced as the result's `id` |
| `Title` | text | searchable, sortable, retrievable | the content item's name; boosted ×2 |
| `ContentTypeName` | keyword | facetable, retrievable | the Lucene integration |
| `LanguageName` | keyword | facetable, retrievable | the Lucene integration |
| `Url` | keyword | retrievable | `IWebPageUrlRetriever`, converted from `~/x` to `/x` |

On top of that, each field of each indexed content type is detected from the content type definition
(`DataClassInfo.ClassFormDefinition`) and mapped by its
[field data type](https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types):

| Field data type | Schema kind | Searchable | Facetable | Sortable | Retrievable |
|---|---|---|---|---|---|
| Taxonomy | `Taxonomy` | yes | **yes** | no | yes |
| Text | `Text` | yes | no | yes | yes |
| Long text, Rich text | `Text` | yes | no | no | yes |
| Integer, Long integer, Decimal, Double | `Number` | no | no | yes | yes |
| Date, Date and time | `Date` | no | no | yes | yes |
| everything else | — | not indexed | | | |

Fields the platform marks as system fields, primary keys or dummy fields are skipped. Data types with no
search meaning — assets, content item references, booleans, GUIDs, XML — are skipped rather than indexed
as text; use an override if you want one of them anyway.

Rich text is stripped with `CMS.Helpers.HTMLHelper.StripTags` before it is indexed, so markup and HTML
entities never reach the analyzer or a highlighted snippet.

### Reusable field schema fields

Fields a content type gets from a
[reusable field schema](https://docs.kentico.com/documentation/developers-and-admins/development/content-types/reusable-field-schemas)
are detected and mapped exactly like the type's own — which matters, because on Dancing Goat the
`ProductFieldTags` and `ProductFieldCategory` taxonomies reach `DancingGoat.ProductCoffee` and
`DancingGoat.ProductBrewer` only that way.

They are not in the content type's own `ClassFormDefinition`: it holds one `<schema guid="…"/>`
reference per schema, and the schema's fields live on the `CMS.ContentItemCommonData` class, each
carrying a `kxp_schema_identifier` property naming the schema it belongs to. So `IContentTypeFieldSource`
reads both class definitions, takes the referenced schema GUIDs from the content type
(`FormInfo.GetFields<FormSchemaInfo>()`) and merges in every `CMS.ContentItemCommonData` field whose
`kxp_schema_identifier` matches one of them.

A name defined by both the content type and one of its schemas is a configuration error — the Kentico
docs tell you to prefix schema fields with the schema name to avoid exactly that. When it happens the
content type's own field is the one indexed and the schema field is dropped, with an `ILogger` warning
naming the field and the content type.

Nothing about this is specific to web pages: a reusable content item is mapped through the same
`IContentTypeFieldSource.GetFields(item.ContentTypeName)` call, so its schema fields are indexed the
same way.

### How taxonomies become facets

An Xperience taxonomy field holds tag references (GUIDs). For each one the strategy resolves the tags
through `ITaxonomyRetriever.RetrieveTags(identifiers, languageName)` and writes three things per tag:

- a `FacetField(fieldName, tag.Name)` — the facet dimension, named after your field, registered
  multi-valued on the index's `FacetsConfig`;
- a stored `StringField(fieldName, tag.Name)` — so the tag comes back as a hit attribute;
- a `TextField(fieldName_text, tag.Title)` — so a visitor searching for the tag's display title matches.

That is all a request needs to use it:

```jsonc
{
  "index": "MySiteIndex",
  "facets": ["ProductFieldTags"],
  "filters": { "facets": [{ "attribute": "ProductFieldTags", "values": ["espresso-machines"] }] }
}
```

Facet values are tag **code names**, because they are stable across language variants and renames; the
tag **title** comes back as the facet value's `label`, so a widget never has to display a code name. The
strategy writes both, as a `<dimension>_label` term per tag, and the query side reads the map straight
out of the term dictionary.

An `or` entry on a single dimension is executed as a Lucene drill-down through `DrillSideways`, so the
counts you get back for that dimension still include the values the visitor could switch to — which is
what a facet list needs. An `and` entry, or a second entry on an already-drilled dimension, falls back to
an ordinary boolean filter.

### Overriding a field

Auto-detection is deliberately opinionated; `AddXpSearch`'s second argument is the escape hatch:

```csharp
indexing
    // Drop a field entirely: not indexed, not returned.
    .Exclude("DancingGoat.ArticlePage", "ArticlePageSummary")

    // Rewrite the detected definition. Returning null drops it, like Exclude.
    .Configure("DancingGoat.ArticlePage", "ArticleTitle", field => field with { Boost = 3f })
    .Configure("DancingGoat.ProductPage", "ProductFieldPrice", field => field with { Retrievable = false })
    .Configure("DancingGoat.ProductPage", "ProductFieldSummary", field => field with { Kind = SearchFieldKind.Keyword });
```

`SchemaField` is a record, so `with` is the whole API: `Kind`, `Searchable`, `Facetable`, `Sortable`,
`Retrievable`, `Boost`. Overrides are matched on content type name and field name, case-insensitively,
and applied in registration order.

Do not change `Name`: it is both the content type field the value is read from and the Lucene field it is
written to, so renaming it breaks the read. Project a different name in your own code, or add a second
field in an override of `MapToLuceneDocumentOrNull`.

To add fields the detector cannot know about, override `MapToLuceneDocumentOrNull`, call `base`, and add
to the document it returns:

```csharp
public override async Task<Document?> MapToLuceneDocumentOrNull(IIndexEventItemModel item)
{
    var document = await base.MapToLuceneDocumentOrNull(item);

    document?.Add(new StringField("Source", "cms", Field.Store.YES));

    return document;
}
```

### The schema it exposes

Everything above is described by an `IndexSchema`, which the query pipeline uses to validate filters and
sort keys, and which the admin attribute picker and the ingestion schema endpoint read:

```csharp
public sealed class SchemaEndpoint(IIndexSchemaProvider schemas)
{
    public async Task<IResult> Get(string indexName, CancellationToken cancellationToken)
    {
        IndexSchema schema = await schemas.GetSchemaAsync(indexName, cancellationToken);

        return Results.Ok(schema.Fields.Select(field => new
        {
            field.Name,
            Kind = field.Kind.ToString(),
            field.Searchable,
            field.Facetable,
            field.Sortable,
            field.Retrievable
        }));
    }
}
```

`GetSchemaAsync` throws `IndexNotFoundException` for an index the Search application does not know.
`schema.Find(name)` looks a single field up case-insensitively and returns `null` when there is none.

### Secured content

Items marked `IsSecured` are never indexed, exactly as `DefaultLuceneIndexingStrategy` does it. If you
override `MapToLuceneDocumentOrNull` and do not call `base`, that guard is yours to reinstate.

### Related

- [Quick start](quick-start.md)
- [Search API](search-api.md)
