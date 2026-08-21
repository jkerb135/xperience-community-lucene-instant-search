# Spec amendment — owned contract and API naming (ACCEPTED)

- **Status:** accepted — owner approval 2026-08-21; implemented by unit CR-1 after core-api and js-widgets merge
- **Date:** 2026-08-21
- **Amends:** §4.2 (contract note, request/response), §4.3, §5.2, §5.3, §5.7 (names only), §6 class names, §7.1 mount attribute values, §9.1 event payload, §11.3 ("Migrating from Algolia" deliverable)
- **ADR:** [ADR-0010](../../adr/0010-owned-contract.md) (supersedes ADR-0006 on acceptance; amends ADR-0007)

## Principle (replaces the §4.2 "Contract note")

The wire contract and the JavaScript API are **owned by this product**. Names and shapes are chosen for
clarity and for Xperience's content model (taxonomy tags have code names *and* titles; fields are
typed), not for drop-in compatibility with Algolia. Algolia compatibility is delivered as a **migration
guide** (§11.3) — a maintained mapping from their concepts, fields and widgets to ours — not by
mirroring their wire format. Consequence: we can evolve the shape on our own terms, and a migrating
team gets an honest, versioned map instead of a near-copy that silently diverges.

## §4.2 — `POST /api/xpsearch/query`

Request:

```jsonc
{
  "index": "site-content",            // required
  "query": "espresso",                // "" = match all
  "page": 1,                          // ONE-based (was 0-based)
  "pageSize": 20,                     // was hitsPerPage
  "facets": ["contentType", "tags"],
  "filters": {                        // structured; no "attr:value" or "price<=50" string grammars
    "facets": [                       // AND across entries
      { "attribute": "contentType", "values": ["Article", "Product"], "operator": "or" },
      { "attribute": "tags", "values": ["coffee"] }            // operator defaults to "or"
    ],
    "numeric": [                      // AND
      { "attribute": "price", "operator": "lte", "value": 50 },  // lt | lte | eq | ne | gte | gt
      { "attribute": "publishedAt", "operator": "gte", "value": 1700000000 }
    ]
  },
  "sort": "relevance",                // or a sort key configured per index (e.g. "newest")
  "highlight": { "fields": ["title", "content"], "preTag": "<mark>", "postTag": "</mark>", "snippetLength": 200 },
  "fields": ["title", "url", "summary", "image"],   // was attributesToRetrieve
  "language": "en",
  "queryId": "generated-guid",
  "explain": false
}
```

Response:

```jsonc
{
  "results": [                        // was hits
    {
      "id": "web-page-42-en",         // was objectID
      "score": 8.42,                  // was _score
      "attributes": {                 // CLOSED object: retrieved fields live here, never beside reserved members
        "title": "Espresso Basics",
        "url": "/articles/espresso-basics",
        "summary": "..."
      },
      "highlights": { "title": "<mark>Espresso</mark> Basics" },   // was _highlights
      "ranking": {                    // only when explain=true; was _rankingInfo
        "baseScore": 6.1,
        "boosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
        "position": 1
      }
    }
  ],
  "facets": {                         // arrays, not maps: ordered, and carry display labels
    "contentType": [ { "value": "Article", "label": "Article", "count": 34 }, { "value": "Product", "label": "Product", "count": 12 } ],
    "tags":        [ { "value": "coffee", "label": "Coffee", "count": 40 } ]   // value = tag code name, label = tag title
  },
  "page": 1,
  "pageSize": 20,
  "total": 46,                        // was nbHits
  "totalPages": 3,                    // was nbPages
  "tookMs": 14,                       // was processingTimeMs
  "queryId": "generated-guid"
}
```

Unchanged: routes, `X-XpSearch-Api-Version: 1`, RFC 9457 Problem Details, `queryId` semantics,
`explain` semantics, `url` must be root-relative or absolute.

Why the structural changes (not just renames): a closed `attributes` object removes the reserved-name
collision (a product with a `score` attribute) and the C# extension-data workaround; structured filters
remove two string grammars and their escaping rules; facet arrays carry the taxonomy tag **title** so
widgets never display code names.

## §4.3

`POST /api/xpsearch/suggest`: `{ index, query, limit, language }` → `{ "suggestions": [ { "text", "url"?, "result"? } ] }` (`maxItems` → `limit`, `hits` → single `result`).
`POST /api/xpsearch/events` (§9.1): `{ "type": "click" | "conversion", "queryId", "resultId", "position" }` → `202`.

## §5.2 / §5.3 / §5.7 — JavaScript API names

| Concept | Current (InstantSearch-modelled) | Owned |
|---|---|---|
| Factory / instance type | `xpsearch()`, `InstantSearch` | `createSearch()` (also default export), `SearchInstance` |
| State mutator | `SearchHelper` / `helper` | `SearchActions` / `actions` |
| Mutators | `toggleFacetRefinement`, `clearRefinements`, `addNumericRefinement`, `setHitsPerPage` | `toggleFacet`, `clearFilters`, `setNumericFilter`, `setPageSize` (unchanged: `setQuery`, `setPage`, `setSort`, `search`, `getState`) |
| Widget lifecycle | `getSearchParameters`, `getRequestParameters` | `prepareState`, `prepareRequest` |
| Render options | `widgetParams`, `helper`, `instantSearchInstance` | `params`, `actions`, `search` |
| Connector naming | `connectRefinementList(render)` | `withFacetList(render)` (the "connector" concept is kept; the prefix is `with`) |
| Connector verbs | `refine`, `createURL`, `isRefined`, `canRefine`, `isSearchStalled` | `apply`, `urlFor`, `isActive`, `canApply`, `isStalled` |
| Widgets (Phase 2) | `searchBox`, `hits`, `refinementList`, `pagination`, `stats`, `sortBy` | `searchBox`, `results`, `facetList`, `pagination`, `resultStats`, `sortSelect` |
| Widgets (2.5) | `clearRefinements`, `currentRefinements`, `toggleRefinement`, `autocomplete`, `rangeSlider`, `hierarchicalMenu`, `infiniteHits` | `clearFilters`, `activeFilters`, `toggleFilter`, `suggestions`, `rangeFilter`, `categoryTree`, `loadMore` |
| Result item | `hit` | `result` (`result.attributes.title`, `result.highlights.title`) |
| Template helpers | `html`, `highlight(field, hit)`, `formatNumber` | unchanged, with `highlight(field, result)` |
| Events bus | `render`, `error`, `stateChange` | unchanged |
| Mount attributes | `data-xps-widget="refinementList"` | `data-xps-widget="facetList"` etc. |

Class names (§6) follow the widget names: `xps-results`, `xps-result` (was `xps-hit`), `xps-facet-list`,
`xps-result-stats`, `xps-sort-select`, `xps-active-filters`, `xps-clear-filters`, `xps-toggle-filter`,
`xps-suggestions`, `xps-range-filter`, `xps-category-tree`, `xps-load-more`. Shared blocks and utilities unchanged.
Custom widget type identifiers still require a dot (`myCompany.dropdownFacet`).

## §11.3 — "Migrating from Algolia" becomes a maintained migration guide

`docs/guides/migrating-from-algolia.md` is a first-class, wiki-ready deliverable with:

1. **Concept map** — index/records → index/documents; hits → results; `facetFilters`/`numericFilters` → `filters.facets`/`filters.numeric`; `attributesToRetrieve` → `fields`; `nbHits`/`nbPages`/`hitsPerPage` → `total`/`totalPages`/`pageSize`; `_highlightResult` → `highlights`; insights events → `/events`.
2. **Widget map** — InstantSearch widget → our widget, option by option; connector → `with*` behaviour; `refine` → `apply`, `createURL` → `urlFor`.
3. **Migration steps** — swap the client, re-point templates (`hit.title` → `result.attributes.title`), re-map routing params, port rules/synonyms into Search tuning (Phase 5), port click analytics.
4. **Honest gaps** — typo tolerance, query suggestions index, personalization, Algolia Rules DSL specifics.
5. **Versioned** — every contract change adds a row; the guide is generated from a mapping table kept next to the schema (`contract/algolia-map.json`) so the doc and the schema cannot drift.

## Implementation (one coordinated unit after core-api and js-widgets merge)

1. Schema v1 rewritten; regenerate C#/TS; `Hit.cs` extension-data partial deleted; fixtures/tests/guide/ADR updated.
2. `js-core` renamed and reshaped (state, actions, lifecycle, connectors, routing params `page` one-based everywhere, bootstrap registry).
3. `theming` fixtures/MARKUP/CSS renamed; `npm run check` green.
4. `core-api` DTO mapping, structured filter parsing, facet projection with labels.
5. `js-widgets` renamed; demo + axe green.
6. `migrating-from-algolia.md` + `contract/algolia-map.json` written; CHANGELOG marks the contract as owned v1.
