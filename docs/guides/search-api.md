## Search API

The JSON contract for Xperience Search: three POST endpoints, one request and one response type each,
and no versioned routes. Field names deliberately mirror Algolia's, so a team migrating off Algolia can
swap the transport and keep most of its UI code — see [Algolia shape](#why-the-payloads-look-like-algolia).

> **Status:** the contract types and constants ship today (`XpSearch.Core.Contract`, and
> `@yourco/xperience-search`); the endpoints that serve them land with the query pipeline. Every payload
> on this page is a fixture in the round-trip tests, so it is the exact shape the types accept.

### Calling the search endpoint

```js
// Today these live at src/contract/constants.ts in @yourco/xperience-search;
// the package entry points that re-export them ship with the client library.
import { QUERY_ROUTE, API_VERSION_HEADER } from './contract/constants.js';

const response = await fetch(QUERY_ROUTE, {                 // '/api/xpsearch/query'
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ index: 'site-content', query: 'espresso', hitsPerPage: 20 }),
});

console.log(response.headers.get(API_VERSION_HEADER));      // '1'

const results = await response.json();
for (const hit of results.hits) {
  console.log(hit.objectID, hit.title, hit._score);         // title is a retrieved attribute, not a reserved member
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
change is a new major of `YourCo.Xperience.Search.Core` and of `@yourco/xperience-search` together.

### `POST /api/xpsearch/query`

```json
{
  "index": "site-content",
  "query": "espresso",
  "page": 0,
  "hitsPerPage": 20,
  "facets": ["contentType", "tags", "language"],
  "facetFilters": [
    ["contentType:Article", "contentType:Product"],
    ["tags:coffee"]
  ],
  "numericFilters": ["price<=50", "publishedAt>=1700000000"],
  "sort": "relevance",
  "highlight": {
    "fields": ["title", "content"],
    "preTag": "<mark>",
    "postTag": "</mark>",
    "snippetLength": 200
  },
  "attributesToRetrieve": ["title", "url", "summary", "image"],
  "language": "en",
  "queryId": "generated-guid"
}
```

`index` is the only required field.

| Field | Default | Notes |
|---|---|---|
| `index` | — | Required. Code name of the Lucene index. |
| `query` | `""` | Empty string matches all documents. |
| `page` | `0` | Zero-based. |
| `hitsPerPage` | `20` | Contract ceiling 1000; the effective maximum is enforced server-side and may be lower. |
| `facets` | — | Attributes to count. Counts come back in `facets`. |
| `facetFilters` | — | Outer array ANDed, inner arrays ORed. |
| `numericFilters` | — | ANDed. |
| `sort` | `"relevance"` | `"relevance"` or a sort key configured for the index. |
| `highlight.fields` | — | Fields to snippet. |
| `highlight.preTag` / `postTag` | `<mark>` / `</mark>` | Inserted after HTML-encoding, so snippets are safe to render. |
| `highlight.snippetLength` | `200` | Characters. |
| `attributesToRetrieve` | — | Omit for the index's default projection. `objectID` is always returned. |
| `language` | — | Omit to use the current request's language. |
| `queryId` | — | Omit and the server generates one. |
| `explain` | `false` | See [the explain flag](#the-explain-flag). |

And the response:

```json
{
  "hits": [
    {
      "objectID": "web-page-42-en",
      "title": "Espresso Basics",
      "url": "/articles/espresso-basics",
      "summary": "...",
      "_score": 8.42,
      "_highlights": {
        "title": "<mark>Espresso</mark> Basics",
        "content": "...brewing <mark>espresso</mark> requires..."
      },
      "_rankingInfo": {
        "baseScore": 6.1,
        "appliedBoosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
        "position": 1
      }
    }
  ],
  "facets": {
    "contentType": { "Article": 34, "Product": 12 },
    "tags": { "coffee": 40, "brewing": 18 }
  },
  "page": 0,
  "hitsPerPage": 20,
  "nbHits": 46,
  "nbPages": 3,
  "processingTimeMs": 14,
  "queryId": "generated-guid"
}
```

`nbHits` is the total across all pages, `nbPages` the page count, `processingTimeMs` the server-side time
excluding the network. `facets` only contains the attributes you asked for, and within them only values
with a non-zero count in the current result set — a value that no longer matches disappears rather than
coming back as `0`.

#### A hit is an open object

Only `objectID` (required) and the underscore-prefixed members `_score`, `_highlights` and `_rankingInfo`
are reserved by the contract. Everything else on a hit — `title`, `url`, `summary`, `image` — is a
retrieved document attribute, decided by `attributesToRetrieve` and the index configuration. `url` is a
convention, not a contract member: an index that projects no link simply has no `url` on its hits.

- TypeScript: `Hit` has an index signature, so `hit.title` and `hit.url` are `unknown` and need narrowing,
  while `hit.objectID` is `string`.
- C#: `Hit` exposes the attributes through `[JsonExtensionData]`, as
  `Dictionary<string, JsonElement> Attributes` — `hit.Attributes["url"].GetString()`.

Any attribute holding a link is always root-relative (`/articles/espresso-basics`) or absolute
(`https://example.com/articles/espresso-basics`). It is never the app-relative `~/…` form Xperience's URL
retriever returns: the server resolves that before the hit reaches the wire, so a JS client can use the
value as-is. The same rule holds for `Suggestion.url`, which *is* a contract member.

#### Filter grammars

`facetFilters` is an array of arrays of `"attribute:value"` strings. The outer array is ANDed, each inner
array is ORed:

```json
[["contentType:Article", "contentType:Product"], ["tags:coffee"]]
```

means *(Article OR Product) AND coffee* — the usual refinement-list behaviour, where picking two values of
the same facet widens the result and picking values of two facets narrows it.

`numericFilters` is a flat array of comparisons, all ANDed:

```
attribute operator number
```

- `attribute` starts with a letter or underscore and may contain word characters and dots (`price`,
  `product.rating`).
- `operator` is one of `<=`, `>=`, `<`, `>`, `=`, `!=`.
- `number` is an optionally negative integer or decimal. Whitespace around the operator is allowed.
- Dates are compared as Unix epoch seconds: `publishedAt>=1700000000`.

#### The explain flag

Send `"explain": true` and every hit carries `_rankingInfo`:

```json
{ "index": "site-content", "query": "espresso", "explain": true }
```

```json
"_rankingInfo": {
  "baseScore": 6.1,
  "appliedBoosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
  "position": 1
}
```

`baseScore` is the Lucene score before boosts, `appliedBoosts` lists the boosts and rules that changed the
score or the position in application order, and `position` is the one-based rank across all pages. Without
`explain`, `_rankingInfo` is absent — not `null`. The admin query tester uses this flag; it is equally
useful from `curl` when a result is ranked in a way nobody can explain.

### `POST /api/xpsearch/suggest`

```json
{ "index": "site-content", "query": "esp", "maxItems": 5, "language": "en" }
```

```json
{
  "suggestions": [
    { "text": "espresso" },
    { "text": "espresso machine", "url": "/products/espresso-machine" }
  ]
}
```

`index` and `query` are required; `maxItems` defaults to 5. A suggestion always has `text`; `hits` (an
array of the same open `Hit` objects) and `url` are present only for indexes configured in federated-hits
mode. Whether an index answers with query suggestions or with federated hits is per-index server
configuration, not a request field, so the same client code works for both.

### `POST /api/xpsearch/events`

```json
{ "eventType": "click", "queryId": "generated-guid", "objectID": "web-page-42-en", "position": 1, "index": "site-content" }
```

The endpoint answers `202 Accepted` with an empty body — there is no event response type. `eventType` is
`"click"` or `"conversion"`; `queryId` and `objectID` are required; `position` is the one-based position in
the result list and is required for `click` and ignored for `conversion`; `index` is optional and resolved
from `queryId` when omitted.

The `queryId` from the search response is what correlates a click back to the search that produced it,
which is what makes click-through rate per query meaningful. A `202` means the event was accepted, not that
an activity was written: activity logging is consent-gated and never blocks or throws.

### Errors

Failures are [RFC 9457 Problem Details](https://learn.microsoft.com/aspnet/core/web-api/handle-errors),
`Content-Type: application/problem+json` — ASP.NET Core's native shape, not a custom envelope:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "index": ["The index field is required."]
  }
}
```

Read `status` and `title`; `errors` is present for validation failures and keyed by the offending JSON
field. The version header is on error responses too.

### Why the payloads look like Algolia

`objectID`, `nbHits`, `nbPages`, `hitsPerPage`, `facetFilters`, `_highlights` — the naming is Algolia's on
purpose. It is a migration path: a site already running an Algolia-driven UI can point the transport at
`/api/xpsearch/query` and keep most of its rendering, refinement and pagination code. It also means the
mental model, the docs and the InstantSearch-shaped widget layer all agree. The contract is generated from
`contract/xpsearch-api.schema.json`, which is the single source of truth for both the C# and the
TypeScript types; changing a field there is a coordinated, semver-major event.
