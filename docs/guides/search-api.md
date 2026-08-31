## Search API

The JSON contract for Xperience Search: three POST endpoints, one request and one response type each,
and no versioned routes. The names and shapes are this product's own — chosen for Xperience's content
model, not copied from anyone. Arriving from a hosted search service? Read
[Migrating from Algolia](migrating-from-algolia.md), which maps their concepts onto these ones.

> **Status:** the contract types, the constants and the query pipeline that serves them all ship today
> (`XpSearch.Core`, and `@xperience-community/xperience-search`). Every payload on this page is a fixture in the
> round-trip tests, so it is the exact shape the types accept.

### Calling the search endpoint

```js
import { QUERY_ROUTE, API_VERSION_HEADER } from '@xperience-community/xperience-search';

const response = await fetch(QUERY_ROUTE, {                 // '/api/xpsearch/query'
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ index: 'site-content', query: 'espresso', page: 1, pageSize: 20 }),
});

console.log(response.headers.get(API_VERSION_HEADER));      // '1'

const body = await response.json();
for (const result of body.results) {
  console.log(result.id, result.attributes.title, result.score);
}
```

The same constants exist in C# as `XpSearch.Core.Contract.ContractConstants`:
`QueryRoute`, `SuggestRoute`, `EventsRoute`, `ApiVersion`, `ApiVersionHeader`.

### Endpoints

| Route | Request | Response |
|---|---|---|
| `POST /api/xpsearch/query` | `SearchRequest` | `200` `SearchResponse` |
| `POST /api/xpsearch/suggest` | `SuggestRequest` | `200` `SuggestResponse` |
| `POST /api/xpsearch/events` | `EventRequest` | `202 Accepted`, empty body |

All three take and return `application/json`. Every response carries `X-XpSearch-Api-Version: 1`. The
version is the semver major of both packages; routes never carry a `/v1/` segment, so a breaking contract
change is a new major of `XperienceCommunity.Search.Core` and of `@xperience-community/xperience-search` together.

### `POST /api/xpsearch/query`

```json
{
  "index": "site-content",
  "query": "espresso",
  "page": 1,
  "pageSize": 20,
  "facets": ["contentType", "tags"],
  "filters": {
    "facets": [
      { "attribute": "contentType", "values": ["Article", "Product"], "operator": "or" },
      { "attribute": "tags", "values": ["coffee"] }
    ],
    "numeric": [
      { "attribute": "price", "operator": "lte", "value": 50 },
      { "attribute": "publishedAt", "operator": "gte", "value": 1700000000 }
    ]
  },
  "sort": "relevance",
  "highlight": {
    "fields": ["title", "content"],
    "preTag": "<mark>",
    "postTag": "</mark>",
    "snippetLength": 200
  },
  "fields": ["title", "url", "summary", "image"],
  "language": "en",
  "queryId": "generated-guid",
  "explain": false
}
```

`index` is the only required field.

| Field | Default | Notes |
|---|---|---|
| `index` | — | Required. Code name of the Lucene index. |
| `query` | `""` | Empty string matches all documents. |
| `page` | `1` | One-based. `0` is a `400`. |
| `pageSize` | `20` | Contract ceiling 1000; the effective maximum is enforced server-side and may be lower. |
| `facets` | — | Attributes to count. Values come back in `facets`. |
| `filters.facets` | — | One entry per attribute, ANDed. |
| `filters.numeric` | — | ANDed. |
| `sort` | `"relevance"` | `"relevance"` or a sort key the index accepts. |
| `highlight.fields` | — | Fields to snippet. |
| `highlight.preTag` / `postTag` | `<mark>` / `</mark>` | Inserted after HTML-encoding, so snippets are safe to render. |
| `highlight.snippetLength` | `200` | Characters. |
| `fields` | — | Omit for the index's default projection. The result `id` is always returned. |
| `language` | — | Omit to use the current request's language. |
| `queryId` | — | Omit and the server generates one. |
| `explain` | `false` | See [the explain flag](#the-explain-flag). |

And the response:

```json
{
  "results": [
    {
      "id": "web-page-42-en",
      "score": 8.42,
      "attributes": {
        "title": "Espresso Basics",
        "url": "/articles/espresso-basics",
        "summary": "..."
      },
      "highlights": {
        "title": "<mark>Espresso</mark> Basics",
        "content": "...brewing <mark>espresso</mark> requires..."
      },
      "ranking": {
        "baseScore": 6.1,
        "boosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
        "position": 1
      }
    }
  ],
  "facets": {
    "contentType": [
      { "value": "Article", "label": "Article", "count": 34 },
      { "value": "Product", "label": "Product", "count": 12 }
    ],
    "tags": [
      { "value": "coffee", "label": "Coffee", "count": 40 },
      { "value": "brewing", "label": "Brewing", "count": 18, "path": ["coffee"] }
    ]
  },
  "page": 1,
  "pageSize": 20,
  "total": 46,
  "totalPages": 3,
  "tookMs": 14,
  "queryId": "generated-guid",
  "redirect": null
}
```

`total` is the number of matching documents across all pages, `totalPages` the page count, `tookMs` the
server-side time excluding the network.

#### `redirect` is present on every response

`redirect` is `null` unless a relevance rule with the **Redirect** consequence matched the query, in which
case it names the destination and the rule that chose it:

```json
"redirect": { "url": "/promotions/espresso", "rule": "Espresso landing page" }
```

The key is always there, so a client tests `response.redirect !== null` rather than probing for a member
that only sometimes exists. The first matching redirect rule in the precedence order (priority, then id)
wins, and the search still runs: the response carries its results next to the destination, because
navigating away is the client's decision, not the server's. The shipped `searchBox` widget makes that
decision for a query the visitor **submitted** and never as they type — see
[Relevance tuning](relevance-tuning.md#redirect-rules) and
[the widget reference](widget-reference.md#searchbox).

#### A result is a closed object

`id`, `score`, `attributes`, `highlights` and `ranking` are the only members a result ever has. Every
retrieved document field lives inside `attributes`, so a document with a field called `score` or `id`
cannot shadow anything. Which fields appear is decided by `fields` and the index configuration; `url` is a
convention, not a contract member, so an index that projects no link simply has no `url` attribute.

- TypeScript: `Result<TAttributes>` is generic over the attribute bag, so
  `createSearch<{ title: string }>` gives you `result.attributes.title` typed and `result.id` as `string`.
- C#: `Result.Attributes` is a plain `Dictionary<string, JsonElement>` —
  `result.Attributes["url"].GetString()`.

Any attribute holding a link is always root-relative (`/articles/espresso-basics`) or absolute
(`https://example.com/articles/espresso-basics`). It is never the app-relative `~/…` form Xperience's URL
retriever returns: the server resolves that before the result reaches the wire, so a JS client can use the
value as-is. The same rule holds for `Suggestion.url`, which *is* a contract member.

#### Facets are ordered arrays with labels

`facets` is keyed by attribute, and each entry is a list, not a map:

```json
"tags": [{ "value": "coffee", "label": "Coffee", "count": 40 }]
```

- `value` is what you send back in `filters.facets` — for a taxonomy dimension, the tag **code name**.
- `label` is what you display — for a taxonomy dimension, the tag **title**. For any other attribute it
  equals `value`.
- `path` is present only on a taxonomy value that has ancestors — see below.
- The list is ordered by `count` descending, then by `value` ascending, so a facet list is stable between
  searches without client-side sorting.

Only the attributes you asked for appear, and within them only values with a non-zero count in the current
result set — a value that no longer matches disappears rather than coming back as `0`.

#### Hierarchical taxonomies

An Xperience taxonomy is a tree, and a facet value that sits inside one carries **`path`** — the code
names of its ancestors, root first, excluding the value itself:

```jsonc
"tags": [
  { "value": "coffee",    "label": "Coffee",      "count": 9 },
  { "value": "espresso",  "label": "Espresso",    "count": 8, "path": ["coffee"] },
  { "value": "equipment", "label": "Equipment",   "count": 4 },
  { "value": "grinder",   "label": "Grinders",    "count": 4, "path": ["equipment"] },
  { "value": "beans",     "label": "Beans",       "count": 1, "path": ["coffee"] },
  { "value": "brewing",   "label": "Brewing",     "count": 1 },
  { "value": "milk",      "label": "Milk drinks", "count": 1, "path": ["brewing"] }
]
```

(The response of the mock server's `espresso` query in the
[JavaScript client guide](js-client.md#run-it-against-the-mock-server) — the same payload the
`categoryTree` widget is tested against.)

Three rules make a tree buildable from that list alone:

- **`path` is absent, not empty**, for a root-level value and for every non-taxonomy attribute.
- **Every ancestor a `path` names is itself in the same list**, with its own count. Nothing has to
  be looked up, and there is no second request.
- **A count rolls up.** A document tagged *Espresso* counts towards *Espresso* and towards *Coffee*,
  so `coffee` is 9 of which `espresso` is 8. The same is true of filtering:
  `{"attribute":"tags","values":["coffee"]}` matches the documents tagged with any descendant of
  *Coffee*, with no special filter syntax.

The dimension itself stays flat — `value` is one tag code name, never a `"lvl0 > lvl1"` string — so a
client that ignores `path` sees exactly the facet list it saw before (ADR-0018). The shipped
[`categoryTree` widget](widget-reference.md#categorytree) and the `withCategoryTree` behaviour read
`path` and nothing else.

Ancestry is resolved **when a document is indexed**, so moving a tag in the *Taxonomies* application
needs a rebuild of the index before the new shape reaches the wire.

#### Filters

`filters.facets` is one entry per attribute. Entries are ANDed; the values inside one entry combine
according to its `operator`:

```json
{ "facets": [
  { "attribute": "contentType", "values": ["Article", "Product"] },
  { "attribute": "tags", "values": ["coffee"] }
] }
```

means *(Article OR Product) AND coffee* — the usual facet-list behaviour, where picking two values of the
same facet widens the result and picking values of two facets narrows it. `"operator": "and"` inverts the
inner rule: every listed value must be present on the document.

`filters.numeric` is a flat array of comparisons, all ANDed:

```json
{ "numeric": [{ "attribute": "price", "operator": "lte", "value": 50 }] }
```

`operator` is one of `lt`, `lte`, `eq`, `ne`, `gte`, `gt`. Dates compare as Unix epoch seconds. There is no
string grammar and therefore nothing to escape; a bad entry is a `400` keyed by its own JSON path, for
example `filters.numeric[0].attribute`.

#### The explain flag

Send `"explain": true` and every result carries `ranking`:

```json
{ "index": "site-content", "query": "espresso", "explain": true }
```

```json
"ranking": {
  "baseScore": 6.1,
  "boosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
  "position": 1
}
```

`baseScore` is the Lucene score before boosts, `boosts` lists the boosts and rules that changed the score
or the position in application order, and `position` is the one-based rank across all pages. Without
`explain`, `ranking` is absent — not `null`. The admin query tester uses this flag; it is equally useful
from `curl` when a result is ranked in a way nobody can explain.

### `POST /api/xpsearch/suggest`

```json
{ "index": "site-content", "query": "esp", "limit": 5, "language": "en" }
```

```json
{
  "suggestions": [
    { "text": "espresso" },
    {
      "text": "Espresso machine",
      "url": "/products/espresso-machine",
      "result": { "id": "web-page-7-en", "attributes": { "title": "Espresso machine" } }
    }
  ]
}
```

`index` and `query` are required; `limit` defaults to 5. A suggestion always has `text`; `url` and
`result` are present only for indexes configured to suggest documents. Whether an index answers with query
suggestions or with matching documents is per-index server configuration, not a request field, so the same
client code works for both.

For document suggestions, `text` is the result's `title` — which for Xperience content is the content
item's *name*, often a slug with a generated suffix (`CoffeePlunger-p2e57tss`). `/suggest` has no
equivalent of the results widget's `titleAttribute`: read `result.attributes` and render your own
label if the item name is not what you want a user to see.

### `POST /api/xpsearch/events`

```json
{ "type": "click", "queryId": "generated-guid", "resultId": "web-page-42-en", "position": 1 }
```

The endpoint answers `202 Accepted` with an empty body — there is no event response type. `type` is
`"click"` or `"conversion"`; `queryId` and `resultId` are required; `position` is the one-based position in
the result list and is required for `click` and ignored for `conversion`.

The `queryId` from the search response is what correlates a click back to the search that produced it,
which is what makes click-through rate per query meaningful. A `202` means the event was accepted, not that
an activity was written: activity logging is consent-gated and never blocks or throws.

### Errors

Failures are [RFC 9457 Problem Details](https://learn.microsoft.com/aspnet/core/web-api/handle-errors),
`Content-Type: application/problem+json` — ASP.NET Core's native shape, not a custom envelope:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "The request is not valid.",
  "status": 400,
  "errors": {
    "filters.numeric[0].attribute": ["'title' is not a numeric attribute of index 'site-content'."]
  }
}
```

Read `status` and `title`; `errors` is present for validation failures and keyed by the **JSON path** of
the offending field, so a client can point at the exact entry that was wrong. The version header is on
error responses too.

### Filters, sorting and schema

The contract fixes the *shape* of `filters` and `sort`; what an index accepts in them comes from its
schema, which the [indexing strategy](indexing-strategy.md) derives from the content types the index
covers. `IIndexSchemaProvider.GetSchemaAsync(indexName, cancellationToken)` returns that list with a
`Searchable` / `Facetable` / `Sortable` / `Retrievable` flag per field.

**`facets` and `filters.facets`** accept attributes the schema marks facetable — in practice, Xperience
Taxonomy fields plus `contentType`, `language` and `_source`. Values are tag code names; the tag
title comes back as the facet value's `label`. Anything else is a `400` keyed `facets[i]` or
`filters.facets[i].attribute`.

**`filters.numeric`** accepts attributes the schema marks numeric — integer, decimal and date fields. A
date is compared as Unix epoch seconds. A filter on a non-numeric attribute is a `400` keyed
`filters.numeric[i].attribute`.

**`sort`** is either the literal `relevance` (the default, score descending), a sort key configured for the
index, or a sortable attribute with an `_asc` or `_desc` suffix: `Price_desc`, `Title_asc`. Configured keys
are checked first:

```csharp
services.AddXpSearch(options =>
{
    options.Indexes["site-content"].SortKeys["newest"] = new SortKey("PublishedAt", Descending: true);
});
```

lets a request send `"sort": "newest"`. Anything else is a `400` keyed `sort`.

**`pageSize`** above the contract's ceiling of 1000 is a `400`; above the server's configured ceiling
(`XpSearchOptions.MaxPageSize`, 100 by default) it is silently clamped, and the response reports the
clamped value. Deep paging is bounded too: `page * pageSize` must not exceed
`XpSearchOptions.MaxResultWindow` (10000 by default), because Lucene ranks every document up to that
depth.

**`language`** filters on the language field the Lucene integration writes to every document. One index
holds every language; whether a per-language index is the better model is not decided yet, so treat this
as filtering, not as index selection.

**`fields`** accepts attributes the schema marks retrievable. Omit it and every retrievable attribute is
returned. The result `id` is always returned and is never an attribute.

**`highlight.fields`** accepts retrievable attributes. The stored value is HTML-encoded before the
highlight tags are inserted, so a snippet is safe to render as HTML and markup in the source content comes
back escaped.

### One schema, two type sets

Both the C# DTOs (`XpSearch.Core.Contract`) and the TypeScript types (`@xperience-community/xperience-search`) are
generated from `contract/xpsearch-api.schema.json`. That file is the single source of truth: changing a
field there regenerates both sides, and `npm run contract:check` fails the build if a committed type set
has drifted from it. A field change is therefore a coordinated, semver-major event — which is exactly why
the shape is ours to choose rather than a copy of someone else's.
