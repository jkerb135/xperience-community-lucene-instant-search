## Indexing strategy

`XpSearchIndexingStrategy` is the base class an Xperience Search project derives its Lucene indexing
strategy from. It indexes every content type an index covers without a line of per-type mapping, and it
turns every Xperience **Taxonomy** field into a facet dimension automatically — that automatic binding is
the point of the class.

```csharp
using CMS.ContentEngine;
using CMS.Websites;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

public sealed class MySearchIndexingStrategy : XpSearchIndexingStrategy
{
    public MySearchIndexingStrategy(
        IContentQueryExecutor executor,
        IWebPageUrlRetriever urlRetriever,
        ITaxonomyRetriever taxonomyRetriever,
        IContentTypeFieldSource fieldSource,
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        XpSearchIndexingOptions indexingOptions,
        ILogger<XpSearchIndexingStrategy> logger)
        : base(executor, urlRetriever, taxonomyRetriever, fieldSource, accessor, schemaProvider, indexingOptions, logger)
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

Both definitions are read through `IDataClassDefinitionSource`, whose default implementation calls
`DataClassInfoProvider.GetDataClassInfo(className)`. The static provider is deliberate: `DataClassInfo`
is one of the system classes that has no `IInfoProvider<T>` registration, so injecting one makes an
application fail to start. Substitute the interface if you need class definitions from somewhere else.

Nothing about this is specific to web pages: a reusable content item is mapped through the same
`IContentTypeFieldSource.GetFields(item.ContentTypeName)` call, so its schema fields are indexed the
same way.

### How taxonomies become facets

An Xperience taxonomy field holds tag references (GUIDs), stored in the column as JSON. The untyped
query result hands the column back as it is stored, so the strategy converts it through the data type
registered for the field data type `taxonomy` (`DataTypeManager.ConvertToSystemType`) rather than casting
it — which works for every content type, including ones with no generated class. For each reference the
strategy resolves the tags
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

`FacetsConfigFactory` is derived from the detected schema, not from what has been mapped so far: the
first time the Lucene client asks for it, every facetable field of every index registered for this
strategy is declared as a multi-valued dimension. A dimension a document turns out to carry that the
schema did not know about is still registered while mapping, as a fallback. There is nothing left for
a host to override: a fresh index, or one whose documents this process has not mapped yet, accepts a
document with several tags in one dimension instead of failing the whole batch with
*dimension "X" is not multiValued*.

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
field from the contribution hook below.

### Adding fields of your own

To add fields the detector cannot know about, override `ContributeAsync`. It runs once per document,
after the item's own fields and after any flattening, with everything the mapping used:

```csharp
protected override async Task ContributeAsync(
    IndexingContext context,
    Document document,
    CancellationToken cancellationToken)
{
    // The item, its loaded content and its detected schema.
    string? summary = context.Data.GetValue<object>("ArticlePageSummary") as string;

    // Written with the same encoding the base mapping uses: analyzed text plus its sort field,
    // a facet dimension plus its stored code names and its `_label` terms, and so on.
    await context.AddFieldAsync(
        new SchemaField("Summary", SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true),
        summary,
        cancellationToken);

    // Or straight into the document, when the encoding is yours.
    document.Add(new StringField("Source", "cms", Field.Store.YES));
}
```

`context.AddFieldAsync(field, value)` takes the raw value as a content query data container returns it —
including the raw JSON of a taxonomy column, which it converts through the field's registered data type.
`context.AddTaxonomyAsync(field, tags)` takes `TagReference`s you already hold. Neither duplicates any of
the mapping code: both are the same calls the base mapping makes.

An item that throws while it is mapped — from your hook or from a field — is logged as an error naming
the item and the field, and skipped. It never escapes `MapToLuceneDocumentOrNull`, because the Lucene
integration maps a whole batch in one task and one bad document would otherwise cost the rebuild.

### Linked items

A page often holds nothing itself: everything lives on a linked reusable content item. Dancing Goat's
`DancingGoat.ProductPage` is exactly that — one field, `ProductPageProduct`, linking a
`ProductCoffee` / `ProductBrewer` / `ProductGrinder` / `ProductAccessory`, all of which take their
`ProductFieldName`, `ProductFieldPrice`, `ProductFieldTags` and `ProductFieldCategory` from the
`ProductFields` reusable field schema. Indexed as-is, the page document has a `Title` and a `Url` and
nothing to search or facet on, while the product documents have the fields but no URL.

`FlattenLinkedItems` folds the linked item into the page's document (spec §10.7):

```csharp
builder.Services.AddXpSearch(
    options => { },
    indexing => indexing.FlattenLinkedItems(
        ProductPage.CONTENT_TYPE_NAME,
        nameof(ProductPage.ProductPageProduct),
        [
            ProductCoffee.CONTENT_TYPE_NAME,
            ProductBrewer.CONTENT_TYPE_NAME,
            ProductGrinder.CONTENT_TYPE_NAME,
            ProductAccessory.CONTENT_TYPE_NAME
        ]));
```

What that does:

- the item is loaded with `WithLinkedItems`, and the linked items are read off the result with
  `TryGetLinkedItems` — the documented way to reach them when a query result is mapped by hand;
- each linked item's **own** `ContentTypeName` decides which fields are detected, so a field that
  accepts four content types needs no per-type code;
- every detected field is written onto the parent's document under its own name, with the same
  encoding it would have on the linked item's own document — a flattened taxonomy is a facet
  dimension like any other;
- the class names you list are what the **schema** reports. The document is mapped from whatever the
  linked item turns out to be, but `facets`, `fields`, sort validation and the admin attribute
  dropdown have to know the flattened fields without loading an item, so the types the field accepts
  are named in the registration. After the call above, `IndexSchema` reports `ProductFieldTags` on
  `DancingGoat.ProductPage`.

A name the parent content type already defines wins over the flattened one, with a warning naming the
field, the parent type and the link. When the field holds several linked items, the first one to define
a name is the one that contributes it.

The `depth` argument (1 by default) is the depth the parent is loaded with, so raise it only when a
`ContributeAsync` override needs the linked item's *own* linked items.

**Reindexing is still yours.** Flattening changes what a page's document contains, not when it is
rebuilt: if the linked product changes, nothing tells the integration that the page has to be reindexed.
That is what `DefaultLuceneIndexingStrategy.FindItemsToReindex` is for, and the flatten option does not
infer it — the mapping from a changed reusable item back to the pages that link it needs a query the
option has no way to guess. Dancing Goat's sample strategy overrides it; do the same for each flattened
relationship.

Once the page carries the product's fields, drop the reusable content types from the index
configuration, or the same product is in the index twice — once with a URL, once without.

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

Items marked `IsSecured` are never indexed, exactly as `DefaultLuceneIndexingStrategy` does it. Use
`ContributeAsync` rather than overriding `MapToLuceneDocumentOrNull`; if you do override it and do not
call `base`, that guard — and the skip-and-log behaviour above — is yours to reinstate.

### Related

- [Quick start](quick-start.md)
- [Search API](search-api.md)
