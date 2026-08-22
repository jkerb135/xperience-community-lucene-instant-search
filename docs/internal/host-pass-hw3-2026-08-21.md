# Host pass HW-3 — 2026-08-21

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, Xperience by Kentico 31.8.0, net10, DB
`comm_projects`, http://localhost:27340). Library at `libraries/xperience-search`, `main` d6c297f,
unchanged by this pass (nothing was committed to it; this note is untracked).

Verified after the CA-3, AD-1, AN-1 and AD-2 waves.

---

## 1. What changed in the host

| File | Change |
|---|---|
| `F:\Personal\CommunityProjects\src\Search\XpSearchIngestionObjectTypes.cs` | **Deleted.** The three ingestion Info types now carry their own `[assembly: RegisterObjectType]`; the app starts and passes DI validation without the host declaring them. |
| `F:\Personal\CommunityProjects\src\Search\DancingGoatSearchIndexingStrategy.cs` | Removed the `FacetsConfigFactory()` override and the `taxonomyFieldNames` array it needed (facet dimensions are derived from the schema now). Constructor takes the two new services `ILuceneIndexAccessor accessor` and `IIndexSchemaProvider schemaProvider` and passes them to `base(...)`. Usings adjusted: `+Kentico.Xperience.Lucene.Core`, `+XpSearch.Core.Abstractions`, `-Lucene.Net.Facet`. |
| `F:\Personal\CommunityProjects\src\Search\SearchStartupExtensions.cs` | Removed the temporary `"Lucene client in use: {ClientType}"` diagnostic log (it existed to prove `AddXpSearch` decorated `ILuceneClient`; the decoration is proven end-to-end now by §4.3). Everything else in `SearchIndexSeeder` is permanent sample behaviour and was kept. |
| `F:\Personal\CommunityProjects\src\Program.cs` | Added `builder.Services.AddXpSearchAdmin();` after `AddXpSearchIngestion()`, per the call order documented on the method. |
| `F:\Personal\CommunityProjects\src\Search\README.md` | Rewrote the document-shape section for the CA-3 wire names, documented `AddXpSearchAdmin` + Embedded client module, replaced the five stale blockers with the one that survives (`_source`, §5.1) and a list of what is fixed. |

**Task B (wire-name renames) turned out to be a no-op in host source.** The host never referenced
`Title` / `Url` / `ContentTypeName` / `LanguageName` as attribute names: the only host-side search
markup is `<xps-search-assets />` in `_DancingGoatLayout.cshtml` / `_LandingPageLayout.cshtml`, and the
sort keys in `Program.cs` (`ArticlePagePublishDate`, `ProductFieldPrice`) are content-type-detected
fields, whose names CA-3 did not change. The `/search` page's widget properties live in the Page Builder
JSON in the database, not in source — see §6 for what the owner should set there.

`src\Components\Widgets\CardWidget\` was not touched.

---

## 2. Pre-build

```
cd libraries/xperience-search/src/XpSearch.Client && npm ci && npm run build
  → created dist/xpsearch.umd.js; packaged themes/shell.css, themes/default.css, mock/server.mjs
cd libraries/xperience-search/src/XpSearch.Admin/Client && npm ci && npm run build
  → webpack 5.109.2 compiled successfully in 809 ms
  → dist/entry.kxh.9bb6481dc725ee682f06.js
```

## 3. Build (task D)

```
$ dotnet build CommProjects.sln -t:Rebuild
    0 Error(s)
    193 Warning(s)
```

No warning names any `XpSearch.*` project (they are warnings-as-errors, so 0 errors proves it). The 193
are the host's pre-existing ones, counted across a full rebuild of every project in the umbrella
solution; untouched.

First attempt failed with `MSB3027 / MSB3021 … file is locked by "CommunityProjects (9560)"` — a host
instance from a previous session was still running. Stopping it made the build clean; not a code defect.

---

## 4. Runtime verification

Host started with `dotnet run --project src --no-build`. Startup log contains **no** error, warning or
exception other than the expected `DevIngestionKeySeeder` Warning-level key line, and in particular no
DI-validation failure and nothing about `yourco/xperience-search-admin`.

### 4.1 Query with the new attribute names, facets and highlights (task E1)

```
$ curl -s -X POST http://localhost:27340/api/xpsearch/query -H 'content-type: application/json' \
  -d '{"index":"DancingGoatSample","query":"coffee","pageSize":2,
       "facets":["ProductFieldCategory","ProductFieldTags","contentType"],
       "highlight":{"fields":["ProductFieldName","ProductFieldDescription"]}}'
```
```json
{
  "facets": {
    "ProductFieldCategory": [ {"count":8,"label":"Coffees","value":"Coffees"},
                              {"count":7,"label":"Brewers","value":"Brewers"},
                              {"count":6,"label":"Accessories","value":"Accessories"},
                              {"count":3,"label":"Grinders","value":"Grinders"} ],
    "ProductFieldTags":     [ {"count":2,"label":"Bestsellers","value":"Bestsellers"},
                              {"count":2,"label":"Hot tips","value":"HotTips"} ],
    "contentType":          [ {"count":24,"label":"DancingGoat.ProductPage","value":"DancingGoat.ProductPage"} ]
  },
  "results": [
    { "attributes": { "title": "CoffeePlunger-p2e57tss",
                      "contentType": "DancingGoat.ProductPage",
                      "language": "en",
                      "url": "/products/coffee-plunger",
                      "_source": "xperience",
                      "ProductFieldName": "Coffee Plunger",
                      "ProductFieldPrice": 29.9,
                      "ProductFieldCategory": "Brewers",
                      "ProductSKUCode": "ACC-PLU-STAND" },
      "highlights": { "ProductFieldName": "<mark>Coffee</mark> Plunger",
                      "ProductFieldDescription": " Eight cups of <mark>coffee</mark> in a single plunger. …" },
      "id": "d09cafef-980f-4eee-83f2-fa1ae34f94ca:en", "score": 0.4801144301891327 },
    …
  ],
  "total": 24, "totalPages": 12, "tookMs": 27
}
```

- CA-3 wire names confirmed: `title`, `url`, `contentType`, `language`; content-type fields keep their
  Xperience names; the id is the result's own `id`.
- CA-3 facet fix confirmed on a **fresh** index (`App_Data/LuceneSearch/DancingGoatSample` was deleted
  before the run, so the seeder rebuilt from nothing): 32 documents indexed with no
  *dimension "X" is not multiValued* batch failure, with the host's `FacetsConfigFactory` override gone.
  This is the exact failure the removed override existed for.

Filter through a facet also works:
```
$ … "filters":{"facets":[{"attribute":"contentType","values":["DancingGoat.ProductPage"]}]}
total 32
```

### 4.2 The `/search` page and the widgets (task E2)

```
$ curl -s -o /dev/null -w '%{http_code}\n' http://localhost:27340/search
200
$ curl -s http://localhost:27340/search | grep -o 'xps[^"<> ]*' | sort | uniq -c
      7 xps-config=
      7 xps-instance-config=
      7 xps-instance=
      7 xps-mount
      7 xps-widget=
      1 xpsearch/default.css
      1 xpsearch/shell.css
      1 xpsearch/xpsearch.umd.js
```

All seven Page Builder widgets now render their mount markup and the assets tag helper emits both
stylesheets and the UMD bundle — the previous blocker (`CMS.AssemblyDiscoverableAttribute` missing) is
gone. Decoded mounts:

```
searchBox     {"placeholder":"Search products","showReset":true,"autofocus":true}
resultStats   {"textTemplate":"{total} products in {tookMs} ms", …}
facetList     {"attribute":"ProductFieldCategory","label":"Category","operator":"or","limit":10,"showMore":true}
sortSelect    {"items":[{"value":"relevance",…},{"value":"price_asc",…},{"value":"price_desc",…}], …}
results       {}   instance: {"index":"DancingGoatSample","initialState":{"pageSize":6},
                              "fields":["ProductFieldName","ProductFieldDescription","ProductFieldPrice"]}
pagination    {}
suggestions   {"mode":"documents","limit":5}
```

The `results` widget uses the defaults `titleAttribute:"title"`, `urlAttribute:"url"`, which the CA-3
projection now satisfies (see the `attributes` in §4.1), so the empty `<a href="#">` blocker is gone.
**Not verifiable headlessly:** the widgets render client-side, so this pass proves the markup, the
assets and the API payload the JS reads, not the painted DOM. See §6.

### 4.3 Ingestion round trip (task E3)

The dev key's plaintext is only logged at creation, so the `dev-sample` row was deleted
(`DELETE FROM XpSearch_ApiKey WHERE KeyName='dev-sample'`) and the host restarted to reissue it.

```
$ curl -s -H "Authorization: Bearer $KEY" …/indexes/DancingGoatSample/status
{"documents":{"bySource":{"xperience":32},"total":32},"health":"healthy","index":"DancingGoatSample"}
```
**CA-3 double-count fix confirmed:** 32, not the 64 the previous pass saw on 32 documents.

```
$ curl -s -X POST …/indexes/DancingGoatSample/documents -H "Authorization: Bearer $KEY" \
   -d '{"waitForIndex":true,"documents":[{"id":"pim-sku-88213","_source":"pim",
        "Title":"Ethiopian Yirgacheffe (PIM)","ProductFieldName":"Ethiopian Yirgacheffe",
        "ProductFieldPrice":18.50,"ProductFieldCategory":["Coffees"]}]}'
{"errors":[],"failed":0,"indexed":1,"tookMs":299}

$ curl -s -X POST …/query -d '{"index":"DancingGoatSample","query":"Yirgacheffe","pageSize":3}'
total 2
  { "id":"pim-sku-88213",
    "attributes":{"title":"Ethiopian Yirgacheffe (PIM)","_source":"pim",
                  "ProductFieldName":"Ethiopian Yirgacheffe","ProductFieldPrice":18.5,
                  "ProductFieldCategory":"Coffees"}, "score":1.3175636529922485 }
  { "id":"7db69c12-…:en", … "_source":"xperience" }
```
**CA-3 searcher-invalidation fix confirmed:** the pushed document is found in the same process, with no
restart, immediately after `waitForIndex:true` returned. Its `Title` is projected as the wire `title`.

```
$ curl -s -H "Authorization: Bearer $KEY" …/status
{"documents":{"bySource":{"pim":1,"xperience":32},"total":33},"health":"healthy",
 "lastWrite":"2026-08-22T03:07:18+00:00"}

$ curl -s -X POST -H "Authorization: Bearer $KEY" …/rebuild
{"errors":[],"failed":0,"indexed":0,"tookMs":131}
# host log:
#  ExternalDocumentReplayLuceneClient: Index DancingGoatSample was rebuilt; queueing a replay …
#  ExternalDocumentWriter: Replaying 1 external documents into rebuilt index DancingGoatSample.

$ curl -s -H "Authorization: Bearer $KEY" …/status
{"documents":{"bySource":{"pim":1,"xperience":32},"total":33},"health":"healthy", …}
$ curl -s -X POST …/query -d '{…,"query":"Yirgacheffe"}'
total 2  → ['pim-sku-88213', '7db69c12-…:en']
```
Survives the rebuild. Counts are sane and add up (`1 + 32 = 33`).

Scoped clear, run to leave the index as found:
```
$ curl -s -X POST -H "Authorization: Bearer $KEY" '…/clear?source=pim'
{"deleted":1,"taskId":"ff530d7085584c4d93c6147e7c55bb1f","tookMs":20}
$ curl … /status      # immediately
{"documents":{"bySource":{"pim":1,"xperience":32},"total":33},"health":"degraded", …}
$ curl … /status      # ~10 s later
{"documents":{"bySource":{"xperience":32},"total":32},"health":"healthy", …}
```
(See defect §5.2 about that transient `degraded`.)

### 4.4 `/suggest` and `/events` (task E4)

```
$ curl -s -w '\nHTTP %{http_code}\n' -X POST …/suggest -d '{"index":"DancingGoatSample","query":"esp","limit":3}'
{"suggestions":[{"result":{"attributes":{"title":"EsproPress-ifroai4d"},
  "id":"5c3db363-…:en","score":1},"text":"EsproPress-ifroai4d","url":"/products/espro-press"}]}
HTTP 200

$ curl -s -w '\nHTTP %{http_code}\n' -X POST …/events \
  -d '{"type":"click","queryId":"11111111-1111-1111-1111-111111111111","resultId":"x","position":1}'
HTTP 202
```

### 4.5 AN-1 analytics query log

```
$ sqlcmd -S localhost -d comm_projects -E -C -Q "SELECT TOP 6 * FROM XpSearch_QueryLog ORDER BY 1 DESC"
LogID|LogQueryID                            |LogIndexName      |LogQueryText |LogResultCount|LogTimestamp       |LogChannelName   |LogProcessingTimeMs
10   |ab7c9daf-d6cb-42d3-b7fb-b883d6c63eff |DancingGoatSample |             |32            |2026-08-22 03:08:33|DancingGoatPages |6
7    |b5edf935-9535-4f3a-b016-0230fff12707 |DancingGoatSample |             |33            |2026-08-22 03:08:03|DancingGoatPages |10
6    |73252505-c3a9-4ca5-8f11-5d7bb96554e8 |DancingGoatSample |yirgacheffe  |2             |2026-08-22 03:07:55|DancingGoatPages |6
5    |729283c9-562b-49f1-81aa-54e40f81f79b |DancingGoatSample |yirgacheffe  |0             |2026-08-22 03:07:41|DancingGoatPages |18
```
Every curl search above landed a row, with the result count, the channel and the duration. Row 5 (a
zero-result `yirgacheffe`, run before the push) is exactly the kind of row the AD-2 dashboard's
*Create rule* button should offer.

All eight module tables exist: `XpSearch_ApiKey`, `XpSearch_ExternalDocument`, `XpSearch_FieldWeight`,
`XpSearch_IngestionLog`, `XpSearch_QueryLog`, `XpSearch_Rule`, `XpSearch_StopwordList`,
`XpSearch_Synonym`.

### 4.6 Admin module and the AD-2 bundle (task E5)

- The application starts with `XpSearch.Admin` loaded and `AddXpSearchAdmin()` registered; `GET /admin`
  → `200`; the whole startup log holds no error mentioning the module or `yourco/xperience-search-admin`.
- `dotnet msbuild …/XpSearch.Admin.csproj -getItem:EmbeddedResource` returns **an empty list** — the
  Kentico admin targets add the items during the build, not at evaluation, so that command is not a
  usable check. The built assembly is, and it proves Embedded mode:

```
$ powershell -c "[Reflection.Assembly]::LoadFrom('…\XpSearch.Admin\bin\Debug\net8.0\XpSearch.Admin.dll').GetManifestResourceNames()"
XpSearch.Admin.AdminResources.yourco.xperience.search.admin.entry.kxh.9bb6481dc725ee682f06.js
XpSearch.Admin.AdminResources.yourco.xperience.search.admin.entry.kxh.9bb6481dc725ee682f06.js.LICENSE.txt
Microsoft.Extensions.FileProviders.Embedded.Manifest.xml
```
  The hash matches the webpack output from §2, i.e. the bundle in the dll is the one just built.
- The **no-`appsettings`-change claim in `docs/guides/admin-client-development.md` is correct.** Kentico
  docs (*Prepare your environment for admin development*): "Embedded — Client scripts are stored in an
  assembly as embedded resources. **This is the default method when no mode is explicitly configured for
  the module.**" The host's `appsettings.json` has no `CMSAdminClientModuleSettings` section at all and
  the module loads.
- **Not verified:** that the browser actually mounts the two React templates. No admin login is
  available to this pass, and the client-module bundle is only requested by the authenticated admin SPA,
  so there is no anonymous URL to curl.

---

## 5. Defects found in the library

### 5.1 `_source` is declared facetable but is neither filterable nor countable — for *any* document

```
$ curl … '{"index":"DancingGoatSample","query":"","pageSize":1,"facets":["_source"]}'
total 33   facets: {"_source": []}

$ curl … 'filters":{"facets":[{"attribute":"_source","values":["xperience"]}]}'   → total 0
$ curl … 'filters":{"facets":[{"attribute":"_source","values":["pim"]}]}'         → total 0
$ curl … 'filters":{"facets":[{"attribute":"contentType","values":["DancingGoat.ProductPage"]}]}' → total 32
```

Root cause:

- `src/XpSearch.Core/Indexing/IndexSchemaProvider.cs:81` declares
  `new SchemaField(LuceneFieldNames.SourceField, SearchFieldKind.Keyword, …, Facetable: true, …)`.
- `src/XpSearch.Core/Indexing/XpSearchIndexingStrategy.cs:300` writes it as
  `new StringField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource, Field.Store.YES)` —
  a plain field, **not** a `FacetField`, unlike `ContentTypeName` and `LanguageName` on lines 294-295.

Facet counting and facet filtering both go through the Lucene taxonomy index, so `_source` matches
nothing. The documents *are* tagged (the attribute comes back on every hit and `bySource` counts are
right — that path reads the sidecar, not the facet index).

Three things in the docs are inconsistent with this and with each other:

- `docs/guides/indexing-strategy.md:59` lists `_source` as **"facetable, retrievable"**.
- `docs/guides/search-api.md:292` says the facetable attributes are "taxonomy fields plus `contentType`
  and `language`" — correct, and it silently contradicts the table above.
- `IndexSchemaProvider.cs:79-80`'s own comment says "facet *counts* are only collected for taxonomy
  dimensions" — contradicted by `contentType`, which returns counts (§4.1) because it *is* written as a
  `FacetField`.

Proposed fix (owner's call, not applied here): add
`new FacetField(LuceneFieldNames.SourceField, <source>)` next to the `StringField`, in both
`XpSearchIndexingStrategy.Map` and the ingestion document writer that stamps `_source` on pushed
documents, register the dimension in `FacetsConfigFactory`, and fix
`docs/guides/indexing-strategy.md:59`'s row. If that is not wanted, the cheaper fix is
`Facetable: false` at `IndexSchemaProvider.cs:81` plus a term-query fallback for the filter, and to
drop `_source` from the ingestion guide's implied filter story.

The previous host pass recorded this as "`_source` is not usable as a facet filter **for pushed
documents**". That understates it: it does not work for Xperience content either.

### 5.2 `GET …/status` immediately after `clear` reports the pre-clear counts and `health: "degraded"`

`clear` returned `{"deleted":1}` and the very next `status` still read `{"pim":1,…,"total":33}` with
`health` flipped from `healthy` to `"degraded"`; ten seconds later it was `{"total":32,"health":"healthy"}`
(§4.3). The delete is queued (the response carries a `taskId`), so the count lag is expected, but
surfacing it as `degraded` will read as an incident to anyone polling status in a sync pipeline. Suggest
either not deriving `health` from the transient mismatch, or documenting in
`docs/guides/ingestion.md` §"Source isolation" that `clear` is asynchronous and `status` is eventually
consistent — the guide currently only promises this for `rebuild`.

### 5.3 Note, not a defect: `title` is the web page item *name*

`title` comes back as `CoffeePlunger-p2e57tss`, `EsproPress-ifroai4d`, and `/suggest`'s `text` is the
same string. `docs/guides/indexing-strategy.md` does say `Title` is "the content item's name", so this
is as designed, but out of the box every Dancing Goat result heading and every document suggestion is a
slug with a random suffix. The CA-3 `titleAttribute` widget param is the intended escape hatch;
`/suggest` has no equivalent param that this pass could find, so document suggestions are stuck with the
item name. Worth a line in `widget-reference.md` / `search-api.md` telling an integrator to expect this.

---

## 6. Owner's manual checklist (browser required)

Nothing below could be exercised by this pass — everything needs an admin login or a rendering browser.

**Page Builder / live site**
1. `/search` — confirm the seven widgets paint, results show a heading and a working link, facets and
   sorting drive the list, pagination and suggestions work.
2. On the **Results** widget, set `titleAttribute` = `ProductFieldName`, `urlAttribute` = `url` (default
   is already right) and `snippetAttributes` = `ProductFieldDescription`. With the defaults the heading
   is the item name (§5.3) and the snippet is empty, because the documents carry no
   `summary`/`content`/`excerpt`.
3. Flip **Allow search indexing** on a few Dancing Goat articles and confirm they enter the index (the
   sample data has it off for every article, so today the index is the 32 product pages only).

**AD-1 — Search tuning application (under *Development*)**
4. Rules, Synonyms, Field weights, Stopwords listings and their edit pages; the dependent **index**
   dropdown on each form; the application icons; the rule editor block.
5. API keys listing + create page (plaintext shown exactly once), Index status page, Ingestion log
   listing filtered by index and newest first.

**AD-2 — Query tester (order 500)**
6. Two columns side by side, per result: position, final score, base score, and the boost/rule lines;
   the ▲ / ▼ / + / − marks on results that differ between the tuned and untuned sides; the per-side
   query-rewrite list. Confirm neither side writes to `XpSearch_QueryLog` (row count must not move).

**AD-2 — Analytics dashboard (order 600)**
7. All six reports render; 7/30/90-day presets and a custom `yyyy-mm-dd` range; the volume bar chart and
   its table fallback.
8. On a zero-result row, **Create rule** must open the rule form with the index and the query
   pre-filled, and saving must land the rule in the Rules listing. `XpSearch_QueryLog` already holds a
   real zero-result row for `yirgacheffe` (LogID 5) to test with.

**Both AD-2 pages**
9. That the React templates actually mount from the embedded module (§4.6) — a blank page or a
   *template not found* console error is the failure mode this pass cannot see.

---

## 7. Not verified, and why

- The painted DOM of the seven front-end widgets and of the two AD-2 admin pages — no browser.
- Any admin UI behaviour at all — no login credentials.
- Relevance tuning end to end (a rule changing a ranking) — the four tuning tables are empty and
  creating a rule needs the admin UI.
- `dotnet msbuild -getItem:EmbeddedResource` as an Embedded-mode check: it returns an empty list because
  the Kentico targets inject the items during the build. Use the assembly's manifest resource names
  instead (§4.6) — worth correcting wherever that command is suggested.

The host was stopped at the end of the pass.
