## Migrating from Algolia

The hand-written half of `migrating-from-algolia.md`. The tables are generated from
`contract/algolia-map.json` by `src/XpSearch.Client/scripts/migration-guide.mjs`; run
`npm run docs:migration` after editing either file, and `npm run docs:check` fails the build if the
committed guide has drifted from them. Do not edit the generated page directly.

<!-- section: intro -->
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
import createSearch, { results, facetList, pagination } from '@yourco/xperience-search';

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

<!-- section: concept-map -->
### Concept map

Request and response fields, client options, and the analytics events.

<!-- section: widget-map -->
### Widget map

Every InstantSearch widget we have an answer for, and the four names reserved for the widgets that are
not written yet.

<!-- section: behavior-map -->
### Behaviour map

A connector becomes a **behaviour**: same idea, `with` prefix, and verbs that say what they do.

<!-- section: steps -->
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
  application, not a rule language with its own consequences and validity windows.
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
