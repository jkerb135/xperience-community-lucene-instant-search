<!-- Generated from contract/algolia-map.json and contract/migrating-from-algolia.template.md
     by src/XpSearch.Widgets/Client/scripts/migration-guide.mjs. DO NOT EDIT.
     Regenerate with: npm run docs:migration   CI guard: npm run docs:check -->

## Migrating from Algolia

Xperience Search does not mirror Algolia's wire format or InstantSearch's JavaScript API. It has its own,
and this page is the map. Everything below is generated from the same file the JSON schema lives next to,
so it cannot quietly go stale.

Here is the whole migration in one screenful — the same result list, before and after:

```js
// Before — InstantSearch
const search = instantsearch({ indexName: 'site-content', searchClient });
search.addWidgets([
  instantsearch.widgets.hits({
    container: '#results',
    templates: {
      item: (hit) => `<a href="${hit.url}">${instantsearch.highlight({ attribute: 'title', hit })}</a>`,
    },
  }),
  instantsearch.widgets.refinementList({ container: '#facet', attribute: 'contentType' }),
  instantsearch.widgets.pagination({ container: '#pages' }),
]);
search.start();
```

```js
// After — Xperience Search
import createSearch, { results, facetList, pagination } from '@xperience-community/xperience-search';

const search = createSearch({ index: 'site-content' });        // same-origin endpoint by default
search.addWidgets([
  results({
    container: '#results',
    templates: {
      item: (result, { html, highlight }) =>
        html`<a href="${result.attributes.url}">${highlight('title', result)}</a>`,
    },
  }),
  facetList({ container: '#facet', attribute: 'contentType' }),
  pagination({ container: '#pages' }),
]);
search.start();
```

Three things changed and everything else follows from them:

1. **A result is closed.** Document fields live in `result.attributes`, never beside `id` and `score`.
2. **Filters are structured.** No `"attribute:value"` or `"price<=50"` strings, and nothing to escape.
3. **Pages count from one**, on the wire, in state and in the URL.

### Concept map

Request and response fields, client options, and the analytics events.

| Xperience Search | Algolia / InstantSearch | Notes |
|---|---|---|
| `SearchRequest.index` | the index a client is constructed for | Ours is a request field, so one client can search several indexes. |
| `SearchRequest.query` | `query` | Same meaning; an empty string matches all documents. |
| `SearchRequest.page` | `page` | Ours is ONE-based. Add one when porting stored URLs or saved searches. |
| `SearchRequest.pageSize` | `hitsPerPage` | Renamed only. |
| `SearchRequest.facets` | `facets` | Same meaning. Ours never accepts "*": an index declares what is facetable. |
| `SearchRequest.filters.facets[]` | `facetFilters` | Structured objects instead of the nested "attribute:value" arrays; nothing to escape. |
| `SearchRequest.filters.facets[].attribute` | the part before the colon in "attribute:value" | One entry per attribute, ANDed with the other entries. |
| `SearchRequest.filters.facets[].values` | the parts after the colon inside one inner array | The values of one attribute, combined by operator. |
| `SearchRequest.filters.facets[].operator` | the array nesting itself (inner array = OR, outer array = AND) | Explicit "or" (default) or "and" instead of encoding the combination in the shape of the arrays. |
| `SearchRequest.filters.numeric[]` | `numericFilters` | Structured objects instead of the "price<=50" string grammar. |
| `SearchRequest.filters.numeric[].operator` | the comparison inside the string (<, <=, =, !=, >=, >) | Spelled lt, lte, eq, ne, gte, gt. |
| `SearchRequest.filters.numeric[].value` | the number inside the string | Dates compare as Unix epoch seconds, as they do there. |
| `SearchRequest.sort` | a replica index with its own ranking | Ours is a sort key configured per index, so sorting does not multiply indexes. |
| `SearchRequest.highlight.fields` | `attributesToHighlight` | Renamed and grouped under one highlight object. |
| `SearchRequest.highlight.preTag` | `highlightPreTag` | Same default, <mark>. |
| `SearchRequest.highlight.postTag` | `highlightPostTag` | Same default, </mark>. |
| `SearchRequest.highlight.snippetLength` | attributesToSnippet ("field:20" word count) | Ours is a character budget for every highlighted field, not a per-field word count. |
| `SearchRequest.fields` | `attributesToRetrieve` | Renamed. The projected fields land in results[].attributes, not beside the contract members. |
| `SearchRequest.language` | no equivalent | Xperience content is language-variant; ours filters the language variant server-side instead of needing one index per language. |
| `SearchRequest.queryId` | queryID (returned, not sent) | Ours may be supplied by the caller and is echoed back, so a client can correlate before the response arrives. |
| `SearchRequest.explain` | `getRankingInfo` | Renamed; fills results[].ranking. |
| `SearchResponse.results` | `hits` | Renamed. |
| `SearchResponse.results[].id` | `objectID` | Renamed. |
| `SearchResponse.results[].score` | no equivalent (only _rankingInfo when requested) | Ours always carries the final score; values are comparable within one response only. |
| `SearchResponse.results[].attributes` | the hit object itself | The biggest structural change: document fields are nested, so a field called score or id can never shadow a contract member. |
| `SearchResponse.results[].highlights` | `_highlightResult` | A flat field-to-snippet map instead of per-field objects with matchLevel and matchedWords. |
| `SearchResponse.results[].ranking` | `_rankingInfo` | Present only when explain is true. |
| `SearchResponse.results[].ranking.baseScore` | no equivalent | Lucene score before boost rules. |
| `SearchResponse.results[].ranking.boosts` | no equivalent (their ranking criteria are fixed) | The boosts and rules that changed the score, in application order. |
| `SearchResponse.results[].ranking.position` | _rankingInfo has no position; insights positions are client-computed | Ours is one-based across all pages. |
| `SearchResponse.facets` | `facets` | Ordered arrays instead of a value-to-count map: JSON objects have no guaranteed order and no room for a label. |
| `SearchResponse.facets[].path` | the "lvl0 > lvl1" values of a hierarchical facet attribute | Ours names a value's ancestors as an array of tag code names on the value itself, so one taxonomy attribute is one facet rather than one attribute per level. |
| `SearchResponse.facets[].value` | the map key | For a taxonomy dimension this is the tag code name. |
| `SearchResponse.facets[].label` | no equivalent | The tag title, so a facet list never displays a code name. Xperience taxonomy tags have both. |
| `SearchResponse.facets[].count` | the map value | Same meaning. |
| `SearchResponse.page` | `page` | One-based here, zero-based there. |
| `SearchResponse.pageSize` | `hitsPerPage` | Renamed; reports the server-clamped value. |
| `SearchResponse.total` | `nbHits` | Renamed. |
| `SearchResponse.totalPages` | `nbPages` | Renamed. |
| `SearchResponse.tookMs` | `processingTimeMS` | Renamed. |
| `SearchResponse.queryId` | `queryID` | Same meaning: the correlation id for click and conversion events. |
| `SearchResponse.redirect` | renderingContent.redirect.url, set by a Rule with the Redirect action | Always present and null when no rule matched, instead of a key that only appears sometimes. Neither service navigates for you; ours also returns the results, and the searchBox widget follows the URL only for a query the visitor submitted. |
| `SearchResponse.redirect.url` | `renderingContent.redirect.url` | Same meaning. |
| `SearchResponse.redirect.rule` | no equivalent (their redirect carries no rule identity) | Display name of the rule that matched, for logging and for the query tester. |
| `SearchResponse.ruleData` | `userData` | Renamed, and one object instead of an array: the data of every matching rule is shallow-merged in rule order, later rules winning a key, so a client reads ruleData.banner rather than searching a list. Absent when no rule returned data. |
| `SuggestRequest.query` | query against a query-suggestions index | Ours is one endpoint; whether it answers with documents or with popular queries is index configuration. |
| `SuggestRequest.limit` | `hitsPerPage` | Renamed and capped server-side. |
| `SuggestResponse.suggestions[].text` | the query attribute of a suggestion record | Same meaning. |
| `SuggestResponse.suggestions[].result` | the federated hits of a suggestion | One result, not a list: a suggestion that stands for several documents is a query suggestion. |
| `SuggestResponse.suggestions[].url` | no equivalent | Root-relative or absolute link, resolved server-side from the Xperience URL retriever. |
| POST /api/xpsearch/events with type: "click" | clickedObjectIDsAfterSearch (Insights) | One endpoint, one event at a time, 202 Accepted, consent-gated server-side. |
| POST /api/xpsearch/events with type: "conversion" | convertedObjectIDsAfterSearch (Insights) | Same meaning. |
| `EventRequest.resultId` | `objectIDs[]` | Singular: one event describes one result. |
| `EventRequest.queryId` | `queryID` | Same meaning. |
| `EventRequest.position` | `positions[]` | Singular, one-based, required for a click. |
| no equivalent | viewedObjectIDs / viewedFilters | Impression events are not part of the contract. |
| `createSearch({ index })` | `instantsearch({ searchClient, indexName })` | No search client to construct: the endpoint is same-origin by default. |
| `SearchInstance` | `InstantSearch` | The type a search instance has. |
| actions (SearchActions) | helper (SearchHelper) | Same role: the only sanctioned way to mutate state. |
| actions.setQuery / setPage / setSort / search / getState | helper.setQuery / setPage / setIndex / search / getState | Unchanged names, except that sorting is a key rather than a replica index. |
| `actions.toggleFacet` | `helper.toggleFacetRefinement` | Renamed. |
| `actions.clearFilters` | `helper.clearRefinements` | Renamed. |
| actions.setNumericFilter / removeNumericFilter | helper.addNumericRefinement / removeNumericRefinement | Set rather than add: one bound per attribute and operator. |
| `actions.setPageSize` | `helper.setQueryParameter('hitsPerPage', n)` | A named action instead of a generic parameter setter. |
| `actions.setFacetOperator` | the conjunctive/disjunctive facet distinction | Set per attribute; it becomes filters.facets[].operator on the wire. |
| state.filters.facets / state.filters.numeric | SearchParameters.facetFilters / numericFilters | The state mirrors the wire shape, so there is one vocabulary to learn. |
| `Widget.prepareState` | `widget.getSearchParameters` | Renamed. |
| `Widget.prepareRequest` | widget.getWidgetSearchParameters (partly) | Ours contributes request fields that are not state: facets, highlight, fields. |
| render options: params | `widgetParams` | Renamed. |
| render options: search | `instantSearchInstance` | Renamed. |
| `templates.item(result, { html, highlight })` | `templates.item(hit)` | Document fields moved: hit.title becomes result.attributes.title. |
| `highlight(field, result)` | the Hogan {{#helpers.highlight}} helper | A plain function returning trusted HTML; reads result.highlights[field]. |
| routing: { stateToRoute, routeToState } | routing: { stateMapping, uiState } | One function each way; the page param is one-based in the URL and in state. |

### Widget map

Every InstantSearch widget we have an answer for, and the four names reserved for the widgets that are
not written yet.

| Xperience Search | Algolia / InstantSearch | Notes |
|---|---|---|
| `searchBox` | `searchBox` | Same name and options (placeholder, showReset, showSubmit, queryHook). |
| `results` | `hits` | templates.item receives result, not hit. |
| `facetList` | `refinementList` | Same options (attribute, operator, limit, showMore, showMoreLimit, sortBy, searchable); item labels come from the server. |
| `pagination` | `pagination` | Same name; pages are one-based. |
| `resultStats` | `stats` | Renamed; the render state carries total, totalPages, pageSize and tookMs. |
| `sortSelect` | `sortBy` | Renamed; items map a label to a sort key rather than to a replica index. |
| `clearFilters` | `clearRefinements` | Renamed. |
| `activeFilters` | `currentRefinements` | Renamed. |
| `toggleFilter` | `toggleRefinement` | Renamed. |
| suggestions (reserved, Phase 2.5) | `autocomplete` | Name reserved; not implemented yet. |
| rangeFilter (reserved, Phase 2.5) | rangeSlider / rangeInput | Name reserved; withRange already ships. The contract carries no numeric facet statistics, so bounds must be supplied. |
| `categoryTree` | `hierarchicalMenu` | Same role, one option instead of a list of per-level attributes: it reads FacetValue.path. Selection is one value at a time, because a parent's count already includes its descendants. |
| loadMore (reserved, Phase 2.5) | `infiniteHits` | Name reserved; not implemented yet. |

### Behaviour map

A connector becomes a **behaviour**: same idea, `with` prefix, and verbs that say what they do.

| Xperience Search | Algolia / InstantSearch | Notes |
|---|---|---|
| `withSearchBox` | `connectSearchBox` | The connector concept is kept; the prefix is with. |
| `withResults` | `connectHits` | Render state exposes results, not hits. |
| `withFacetList` | `connectRefinementList` | Items carry value, label and count straight from the response. |
| `withCategoryTree` | `connectHierarchicalMenu` | Items are a tree built from FacetValue.path; apply(value) replaces the attribute's filter, and applying the open node clears it. |
| `withPagination` | `connectPagination` | pages and current are one-based. |
| `withResultStats` | `connectStats` | Renamed. |
| `withSortSelect` | `connectSortBy` | Renamed. |
| `withActiveFilters` | `connectCurrentRefinements` | Renamed. |
| `withRange` | `connectRange` | Renamed. |
| `apply(...)` | `refine(...)` | The verb every behaviour uses to change state and search. |
| `urlFor(...)` | `createURL(...)` | Builds the URL for the state a control would apply, so links stay crawlable. |
| `isActive` | `isRefined` | Whether this value is currently selected. |
| `canApply` | `canRefine` | Whether the control has anything to do. |
| `isStalled` | `isSearchStalled` | Whether a request has been running longer than stalledSearchDelayMs. |
| no equivalent | connectAutocomplete, connectGeoSearch, connectRatingMenu | Not implemented; see the gaps section. |

### Migration steps

1. **Swap the client.** Replace the Algolia client and `instantsearch()` with `createSearch({ index })`.
   There is no API key and no application id: the endpoint is same-origin
   (`/api/xpsearch/query`) and served by your own Xperience application. Point `endpoint` elsewhere only
   if you host the API on another origin.
2. **Re-point templates.** `hit.title` becomes `result.attributes.title`, `hit.objectID` becomes
   `result.id`, and `instantsearch.highlight({ attribute, hit })` becomes `highlight('title', result)`
   from the helper bag every template receives. Templates return `html` results, which escape everything
   interpolated into them.
3. **Re-map routing params.** The default mapping owns `q`, `page`, `sort`, one param per facet attribute
   and `<attribute>_<lt|lte|eq|ne|gte|gt>` for numeric filters. `page` is one-based on both sides now, so
   a stored URL keeps working; a custom `stateMapping` becomes
   `routing: { stateToRoute, routeToState }`.
4. **Port rules and synonyms.** There is no Rules DSL. Synonyms, stopwords and boost rules are configured
   in the Search application and applied by the query pipeline (Phase 5); until that ships, an index's
   ranking is Lucene's plus the index-time field boosts.
5. **Port click analytics.** `aa('clickedObjectIDsAfterSearch', …)` becomes
   `search.sendEvent('click', result.id, position)`, or nothing at all — the `results` widget already
   sends a click event for any link inside a result. Conversions are
   `search.sendEvent('conversion', resultId)`.

### Honest gaps

Things Algolia does that this product does not, stated plainly rather than approximated:

- **Typo tolerance.** No fuzzy matching. A misspelled query returns nothing rather than a near match.
  Lucene supports it; exposing it is a contract decision that has not been made.
- **A query-suggestions index.** `/suggest` prefix-matches an index's suggest field and returns matching
  documents. Suggesting previously typed queries needs the analytics store (Phase 6).
- **Facet-value search.** There is no `searchForFacetValues` route. `facetList({ searchable: true })`
  filters the values already returned, in the browser.
- **Personalization and A/B testing.** No equivalent, and none planned in this library.
- **The Rules DSL.** Pinning, burying and conditional boosts are admin configuration in the Search
  application, not a rule language with its own actions and validity windows.
- **Numeric facet statistics.** The response carries no min/max per attribute, so a range control's
  bounds are configured rather than discovered.
- **Impression events.** `/events` accepts clicks and conversions only.

### Versioning

This page is generated from `contract/algolia-map.json`, which sits next to
`contract/xpsearch-api.schema.json` — the single source both type sets are generated from. Every contract
change adds or edits a row there in the same commit, and `npm run docs:check` fails if this page and that
file disagree. A row that says *no equivalent* is a deliberate statement, not an omission.

### Related pages

- [Search API](search-api.md) — the contract in full.
- [JavaScript client](js-client.md) — options, actions, routing, the event bus.
- [Widget reference](widget-reference.md) — every widget, option by option.
- [Custom widgets](custom-widgets.md) — the behaviour API.
