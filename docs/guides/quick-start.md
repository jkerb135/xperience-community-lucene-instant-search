## Quick start

Zero to a working JSON search endpoint on an Xperience by Kentico site, in under fifteen minutes. At the
end you will `curl` `/api/xpsearch/query` and get facets, filters and highlighted snippets back from a
real Lucene index — with no per-content-type mapping code.

```csharp
// Program.cs
using Kentico.Xperience.Lucene.Core.Indexing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKenticoLucene(lucene => lucene
    .RegisterStrategy<MySearchIndexingStrategy>(nameof(MySearchIndexingStrategy)));

builder.Services.AddXpSearch(options =>
{
    options.CacheTtl = TimeSpan.FromSeconds(60);
    options.Indexes["MySiteIndex"].SuggestField = "title";
});

var app = builder.Build();

app.UseKentico();
app.UseXpSearch();          // maps /api/xpsearch/query, /suggest and /events

app.Run();
```

```csharp
// Search/MySearchIndexingStrategy.cs — this is the whole strategy.
using XpSearch.Core.Indexing;

public sealed class MySearchIndexingStrategy : XpSearchIndexingStrategy
{
    public MySearchIndexingStrategy(
        CMS.ContentEngine.IContentQueryExecutor executor,
        CMS.Websites.IWebPageUrlRetriever urlRetriever,
        CMS.ContentEngine.ITaxonomyRetriever taxonomyRetriever,
        IContentTypeFieldSource fieldSource,
        XpSearch.Core.Abstractions.ILuceneIndexAccessor accessor,
        XpSearch.Core.Abstractions.IIndexSchemaProvider schemaProvider,
        XpSearchIndexingOptions indexingOptions,
        Microsoft.Extensions.Logging.ILogger<XpSearchIndexingStrategy> logger)
        : base(executor, urlRetriever, taxonomyRetriever, fieldSource, accessor, schemaProvider, indexingOptions, logger)
    {
    }
}
```

### 1. Install the packages

```bash
dotnet add package YourCo.Xperience.Search.Core
dotnet add package Kentico.Xperience.Lucene.Core
dotnet add package Kentico.Xperience.Lucene.Admin
```

`YourCo.Xperience.Search.Core` targets .NET 8 and builds on `Kentico.Xperience.Lucene` 15.0.5, which in
turn needs `Kentico.Xperience.Core` 31.0.0 or later. The admin package is what puts the **Search**
application in the administration interface, where indexes are defined.

#### Installing from a private feed

Building from source, or consuming the packages from an internal Azure Artifacts / GitHub Packages
feed? Add a `nuget.config` next to your solution. Adding the `<packageSource>` alone is **not enough**
on any machine that enables [package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)
globally — restore fails with `NU1101: Unable to find package YourCo.Xperience.Search.Core …
PackageSourceMapping is enabled, the following source(s) were not considered: xps-local`, because a
mapped configuration only searches the sources a pattern names:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="xps-local" value="..\.feed" />        <!-- a folder, a UNC path or a feed URL -->
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="xps-local">
      <package pattern="YourCo.Xperience.Search.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The `<clear />` elements drop whatever the machine-wide config contributed, so the file describes the
whole resolution and behaves the same on a developer machine and a build agent. A relative source path
is relative to the `nuget.config` that declares it. The package version never changes while you are
iterating on a local build, so the machine-wide package cache will happily serve the previous copy —
`dotnet restore --packages <a throwaway folder>` (or `dotnet nuget locals global-packages --clear`)
when a rebuilt package appears not to take effect. `samples/CustomWidget.Dropdown` in this repository
is set up exactly this way; `samples/pack-and-build.mjs` runs it.

The npm client is not on a public registry yet (see
[KNOWN-LIMITATIONS](../internal/KNOWN-LIMITATIONS.md)). Install the tarball by path:
`npm install ./yourco-xperience-search-0.1.0.tgz`. Every documented entry point — the root, the
`/behaviors` subpath, `/themes/*.css` and `/mock/server.mjs` — resolves the same way it will from a
registry.

### 2. Register the services

Add the two calls from the sample above to `Program.cs`. Order matters:

- `AddXpSearch(...)` must come **after** `AddKenticoLucene(...)`. It decorates the Lucene integration's
  `ILuceneClient` so that rebuilding an index drops this library's cached responses, and it can only
  decorate a registration that already exists.
- `UseXpSearch()` must come **after** `UseKentico()`, and needs a builder that can map endpoints — the
  `WebApplication` returned by `builder.Build()` is one. If your host maps endpoints itself, call
  `endpoints.MapXpSearch()` instead; it does the same thing.

`AddXpSearch()` also has a no-argument overload if the defaults suit you: a 60 second cache, 20 results
per page, a server ceiling of 100, and document suggestions for autocomplete.

### 3. Add the indexing strategy

Derive from `XpSearchIndexingStrategy` and register it with `RegisterStrategy`, as in the sample. You do
not write a mapping: the base class reads each indexed content type's field definitions, indexes text,
numbers and dates with sensible Lucene types, and turns every Xperience **Taxonomy** field into a facet
dimension named after the field. See [Indexing strategy](indexing-strategy.md) for what it indexes and
how to exclude, rename or boost a field.

### 4. Create the index in the admin UI

1. Open the administration interface and go to the **Search** application.
2. **New index**: give it a code name (`MySiteIndex` in the sample), pick the website channel, the
   languages, the included paths and their content types.
3. Set **Indexing strategy** to `MySearchIndexingStrategy` and pick an analyzer.
4. Save, then use **Rebuild** on the index listing. Rebuilding is what populates both the index and its
   taxonomy sidecar, which the facets are read from.

### 5. Query it

```bash
curl -sS -X POST http://localhost:5000/api/xpsearch/query \
  -H 'Content-Type: application/json' \
  -d '{
        "index": "MySiteIndex",
        "query": "espresso",
        "pageSize": 5,
        "facets": ["contentType"],
        "highlight": { "fields": ["Body"] }
      }'
```

```jsonc
{
  "results": [
    {
      "id": "6f1a…:en",
      "score": 1.42,
      "attributes": {
        "title": "Espresso Basics",
        "url": "/articles/espresso-basics",
        "contentType": "DancingGoat.ArticlePage",
        "language": "en"
      },
      "highlights": { "Body": "Brewing <mark>espresso</mark> requires pressure" }
    }
  ],
  "facets": {
    "contentType": [
      { "value": "Article", "label": "Article", "count": 4 },
      { "value": "Product", "label": "Product", "count": 3 }
    ]
  },
  "page": 1,
  "pageSize": 5,
  "total": 5,
  "totalPages": 1,
  "tookMs": 3,
  "queryId": "0f2f…",
  "redirect": null
}
```

Every response also carries `X-XpSearch-Api-Version: 1`. The keys inside `attributes` are the names of
your content type's fields, unchanged, plus the four attributes every document carries whatever its
content type: `title`, `url`, `contentType` and `language`. The document id is the result's own `id`,
never an attribute. To find out what an index exposes without guessing, `IIndexSchemaProvider.GetSchemaAsync` returns
the field list with its `Searchable` / `Facetable` / `Sortable` / `Retrievable` flags.

### 6. Filter

```bash
curl -sS -X POST http://localhost:5000/api/xpsearch/query \
  -H 'Content-Type: application/json' \
  -d '{
        "index": "MySiteIndex",
        "query": "",
        "filters": {
          "facets": [
            { "attribute": "Category", "values": ["coffee", "equipment"] },
            { "attribute": "Tags", "values": ["brewing"] }
          ],
          "numeric": [{ "attribute": "Price", "operator": "lte", "value": 200 }]
        },
        "sort": "Price_desc"
      }'
```

Entries in `filters.facets` are ANDed and the values inside one entry are ORed by default, so that reads
"(coffee OR equipment) AND brewing". `sort` is `relevance`, a sort key configured for the index, or a
sortable attribute suffixed `_asc` or `_desc`. Both are covered in [Search API](search-api.md).

### 7. Autocomplete

```bash
curl -sS -X POST http://localhost:5000/api/xpsearch/suggest \
  -H 'Content-Type: application/json' \
  -d '{ "index": "MySiteIndex", "query": "espr", "limit": 5 }'
```

This prefix-matches the index's suggest field (`Title` by default) and returns the matching documents, so
a dropdown can show real results. The other mode, suggesting previously typed queries, needs the search
analytics store and is not available yet.

### 8. Tune it

Everything that shapes results for an index lives inside the index:

**Lucene Search → indexes → click the index → the *Tuning* sidebar** — Settings, Rules, Synonyms,
Stopwords, Field weights, Query tester, Analytics and Status
(`/admin/lucene/indexes/tuning/{id}/rules` and friends). Clicking an index row in the listing opens
the sidebar directly.

These pages are governed by the **Lucene Search** application in *Role management*: *View* to read
them, *Create*/*Update*/*Delete* to change them, *Rebuild* to rebuild an index.

The two pages that are not about one index — **API keys** and **Ingestion log** — are in the
**Search ingestion** application under *Development*.

See [Relevance tuning](relevance-tuning.md) and [Analytics](analytics.md).

### 9. The UI

The JavaScript widgets that bind to these endpoints ship separately — see the JS client guide. Nothing on
this page depends on them: the endpoints are a plain JSON API and are equally usable from your own
front end.
