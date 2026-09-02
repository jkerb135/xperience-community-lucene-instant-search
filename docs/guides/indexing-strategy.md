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
        ITagAncestrySource tagAncestry,
        IContentTypeFieldSource fieldSource,
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        XpSearchIndexingOptions indexingOptions,
        ILogger<XpSearchIndexingStrategy> logger)
        : base(executor, urlRetriever, taxonomyRetriever, tagAncestry, fieldSource, accessor, schemaProvider, indexingOptions, logger)
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

| Attribute | Lucene field | Kind | Flags | Where it comes from |
|---|---|---|---|---|
| — | `ID` | keyword | — | `{itemGuid}:{languageName}`, surfaced as the result's `id` |
| `title` | `Title` | text | searchable, sortable, retrievable | the content item's name; boosted ×2 |
| `contentType` | `ContentTypeName` | keyword | facetable, retrievable | the Lucene integration |
| `language` | `LanguageName` | keyword | facetable, retrievable | the Lucene integration |
| `url` | `Url` | keyword | retrievable | `IWebPageUrlRetriever`, converted from `~/x` to `/x` |
| `_source` | `_source` | keyword | facetable, retrievable | `xperience`, or the pushed document's source |

These four are the only fields whose **attribute name** (what a request and a result call them) differs
from the Lucene field the documents carry: the wire names are this library's, so one default result
template works for every project, while the documents keep the names the Lucene integration writes. A
field detected from a content type is the same name on both sides — `ProductFieldName` is
`ProductFieldName` in `fields`, in `attributes` and in `highlights`.

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
through `ITaxonomyRetriever.RetrieveTags(identifiers, languageName)` and writes four things per tag:

- a `FacetField(fieldName, tag.Name)` — the facet dimension, named after your field, registered
  multi-valued on the index's `FacetsConfig`;
- a stored `StringField(fieldName, tag.Name)` — so the tag comes back as a hit attribute;
- a `TextField(fieldName_text, tag.Title)` — so a visitor searching for the tag's display title matches;
- a `StringField(fieldName_label, …)` pairing the code name with the title and the tag's ancestry, which
  is where a facet value's `label` and `path` come from.

#### A tag's ancestors are indexed with it

Xperience taxonomies are hierarchies. For each tag, the strategy also writes **every ancestor of that
tag** as a value of the same, still-flat dimension — root first, each with its own shorter path — so a
document tagged *Espresso* carries *Coffee* and *Machines* too. Two things follow for free:
counts roll up (the count on *Coffee* includes every *Espresso* document), and a filter on a parent
matches its descendants with no hierarchy logic in the query pipeline. The wire shape is
[`FacetValue.path`](search-api.md#hierarchical-taxonomies); the decision is [ADR-0018](../adr/0018-hierarchical-facets.md).

Ancestry comes from `ITagAncestrySource`. The default implementation reads the tag table once through
`IInfoProvider<TagInfo>` and caches it in Xperience's data cache with a dependency on `cms.tag|all`,
because `ITaxonomyRetriever` gives a tag's `ParentID` but no way to resolve that identifier to a tag.
Replace the registration if your project resolves ancestry differently:

```csharp
builder.Services.AddSingleton<ITagAncestrySource, MyTagAncestrySource>();
```

Because ancestry is baked into the document, **moving a tag in the Taxonomies application needs an
index rebuild** before the new shape reaches the wire.

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
written to, so renaming it breaks the read. Project a different name in your own code, or declare a second
field with `AddField` and write it from the contribution hook below.

### Adding fields of your own

A field the detector cannot know about — an asset path, a computed summary, anything you assemble
yourself — takes **two steps**, and both are required:

1. **Declare it once**, at startup, with `indexing.AddField`. The declaration is what puts the field in
   the index schema, and the schema is what result attributes, the ingestion schema endpoint and the
   admin attribute selectors are projected from.
2. **Write its value per document**, in an override of `ContributeAsync`.

Declaring without writing gives you an attribute that is never populated. Writing without declaring is
worse: the value is in the Lucene index, so it is searchable, but no result ever carries it — which is
why the library logs a warning naming the missing `AddField` call the first time it happens.

```csharp
// Program.cs — step 1: declare.
builder.Services.AddXpSearch(
    options => { },
    indexing => indexing
        .AddField(
            "DancingGoat.ArticlePage",
            new SchemaField("Summary", SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true))
        .AddField(
            "DancingGoat.ProductPage",
            new SchemaField("ProductImage", SearchFieldKind.Keyword, Searchable: false, Facetable: false, Sortable: false, Retrievable: true)));
```

```csharp
// Step 2: write. ContributeAsync runs once per document, after the item's own fields and after any
// flattening, with everything the mapping used.
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

    // Or straight into the document, when the encoding is yours. Nothing added this way is in the
    // schema, so it is searchable but never returned — deliberately, for fields only your own
    // queries look at.
    document.Add(new StringField("Source", "cms", Field.Store.YES));
}
```

`Summary` now comes back in `result.attributes` alongside the detected fields, and `curl`ing
`/api/xpsearch/query` shows it.

`context.AddFieldAsync(field, value)` takes the raw value as a content query data container returns it —
including the raw JSON of a taxonomy column, which it converts through the field's registered data type.
`context.AddTaxonomyAsync(field, tags)` takes `TagReference`s you already hold. Neither duplicates any of
the mapping code: both are the same calls the base mapping makes.

Two rules about `AddField`:

- **A detected field of the same name wins.** The schema keeps the first definition it sees, and
  contributed fields are appended after the content type's own and flattened fields, so a contributed
  field can never shadow a real one — it is silently dropped. Give it a name of its own.
- **`Configure` and `Exclude` do not apply to it.** They rewrite *detected* fields. You wrote the
  definition you passed to `AddField`, so edit that call instead. Registering the same content type and
  field name twice throws at startup.

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

### Worked example: a computed relevance field

**Want click-based ranking and no code? You do not need any of this.** The library ships that exact
signal as a per-index toggle — see [Popularity boosts](popularity-boosts.md). This example is here for
the *pattern*: any signal you can compute — stock level, editorial score, a rating from another
system — can be written onto the document at index time and fed back into ranking at query time. It
uses click counts only because every site already has them.

Two moving parts, both of them extension points you have already met: the indexing strategy writes the
field, and a pipeline stage of your own boosts on it. The code below is the sample project's
(`src/Search/` in the Dancing Goat host), with its comments and its `explain` line elided.

**1. Compute the signal, once per mapping scope.** The strategy reads the aggregate query log
(`IQueryLogStore`, see [Analytics](analytics.md)) through a `Lazy<Task<…>>`: the same strategy instance
maps every item of a rebuild, so the log is scanned once, not once per document.

```csharp
private readonly Lazy<Task<IReadOnlyDictionary<string, int>>> clickCounts;

public DancingGoatSearchIndexingStrategy(/* … */ IQueryLogStore queryLog)
    : base(/* … */)
{
    clickCounts = new Lazy<Task<IReadOnlyDictionary<string, int>>>(() => LoadClickCountsAsync(queryLog));
}

private static async Task<IReadOnlyDictionary<string, int>> LoadClickCountsAsync(IQueryLogStore queryLog)
{
    var now = DateTime.UtcNow;
    var rows = await queryLog.ReadAsync(INDEX_NAME, now.AddDays(-30), now, CancellationToken.None);

    return rows
        .Where(row => !string.IsNullOrEmpty(row.ClickedResultId))
        .GroupBy(row => row.ClickedResultId!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
```

A query log row's `ClickedResultId` is the result `id` of the clicked document — for Xperience content
`{WebPageItemGUID}:{language}`, which `XpSearchIndexingStrategy.ComposeResultId` builds. That is the
key the count is looked up under.

**2. Write it per document**, in `ContributeAsync` — the step-2 half of *Adding fields of your own*
above. Every indexed page gets the field, including the ones nobody clicked (a `0`), so the sort below
has a value for every document:

```csharp
var clicks = await clickCounts.Value;

await context.AddFieldAsync(
    ClicksField,
    clicks.GetValueOrDefault(ComposeResultId(page.ItemGuid.ToString(), page.LanguageName)),
    cancellationToken);
```

**3. Declare it** — step 1 — and, one line more, publish it as a sort key:

```csharp
internal static readonly SchemaField ClicksField =
    new("clicks", SearchFieldKind.Number, Searchable: false, Facetable: false, Sortable: true, Retrievable: true);
```

```csharp
builder.Services.AddXpSearch(
    options => options.Indexes["DancingGoatSample"].SortKeys["popular"] = new SortKey("clicks", Descending: true),
    indexing => indexing
        .AddField(ProductPage.CONTENT_TYPE_NAME, DancingGoatSearchIndexingStrategy.ClicksField)
        .AddField(ArticlePage.CONTENT_TYPE_NAME, DancingGoatSearchIndexingStrategy.ClicksField));
```

The flags are the whole design of the field: **retrievable** so `result.attributes.clicks` shows it,
**sortable** so `"sort": "popular"` works, **not searchable** (nobody types a click count) and **not
facetable** (a facet per distinct number is noise). After a rebuild a raw hit carries it:

```jsonc
{ "id": "bc9493ac-…:en", "attributes": { "ProductFieldName": "Clever Dripper", "clicks": 5 } }
```

**4. Boost on it at query time**, with a stage of your own — `ISearchStage`, registered with
`AddXpSearchStage`, exactly like the library's own stages:

```csharp
public sealed class ClicksBoostStage : ISearchStage
{
    private static readonly (int MinClicks, float Factor)[] tiers = [(2, 1.5f), (5, 2.5f)];

    public int Order => SearchStageOrder.PopularityBoost + 1;

    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Index != "DancingGoatSample" || context.SortField is not null)
        {
            return Task.CompletedTask;
        }

        var boosted = new BooleanQuery { { context.BaseQuery, Occur.MUST } };

        foreach ((int minClicks, float factor) in tiers)
        {
            var tier = NumericRangeQuery.NewDoubleRange("clicks", minClicks, null, true, true);
            tier.Boost = factor;
            boosted.Add(tier, Occur.SHOULD);
        }

        context.BaseQuery = boosted;

        return Task.CompletedTask;
    }
}
```

```csharp
builder.Services.AddXpSearchStage<ClicksBoostStage>();
```

Four things in there are the parts worth copying:

- **`Order`** decides where the stage runs. `SearchStageOrder` names every shipped slot; anything that
  rewrites the query has to land after `BuildQuery` (400) and before `Execute` (800). This one sits
  just after the built-in popularity boost.
- **SHOULD, next to the query everything else built**, never MUST — a boost must not turn into a
  filter. A document with clicks that does not match the text stays out of the results.
- **It is bounded.** The tiers cap what any amount of clicking can buy. Tune the factors against your
  own scores: these are constant-score clauses, so on an index whose text scores are small a factor of
  2.5 is a big move, and on one with large scores it is a nudge. Turn on `"explain": true` and read
  `ranking.baseScore` before you pick numbers.
- **It gets out of the way.** A request sorted by a field ignores scores altogether, so the stage does
  nothing for one — and it does nothing for other indexes, which have no `clicks` field.

Running this *and* the built-in popularity boost stacks two bounded boosts on the same evidence. Pick
one.

**The ceiling: the value is as fresh as the document.** A computed field is written when the document
is indexed, so it only changes when that document is re-indexed or the index is rebuilt — clicks that
arrived since then are not in it. A scheduled rebuild (or a nightly task that re-indexes the documents
whose signal moved) is the operational answer; if you need a signal that is live at query time, read it
in the stage instead of writing it on the document.

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
