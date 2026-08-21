## JavaScript client

`@yourco/xperience-search` is the browser half of Xperience Search: a transport (`SearchClient`), an
observable state (`SearchState`), and independent widgets that subscribe to it. Widgets can live
anywhere in the DOM — search box in the header, facets in a left rail, results in `<main>` — with no
shared parent and no order requirement.

> **Status:** the core ships today: state, transport, the widget lifecycle, the
> [connectors](custom-widgets.md), routing, the event bus and the `.xps-mount` bootstrap. The six
> default widget renderers (`searchBox`, `hits`, `refinementList`, `pagination`, `stats`, `sortBy`)
> land next; until then you render with the connectors, exactly as the built-ins will.

### A working search page

```html
<div id="search-box"></div>
<div id="search-results"></div>

<script src="/js/xpsearch.umd.js"></script>
<script>
  const search = xpsearch({
    endpoint: '/api/xpsearch/query',
    index: 'site-content',
    routing: true,
    searchOnInitialLoad: false,
    debounceMs: 150,
  });

  // Two widgets built from connectors: behaviour is supplied, rendering is yours.
  const searchBox = xpsearch.connectSearchBox(({ query, refine, widgetParams }, isFirstRender) => {
    if (isFirstRender) {
      widgetParams.container.innerHTML = '<input type="search" aria-label="Search">';
      widgetParams.container.querySelector('input')
        .addEventListener('input', (event) => refine(event.target.value));
    }
    widgetParams.container.querySelector('input').value = query;
  });

  const hits = xpsearch.connectHits(({ hits, results, sendEvent, widgetParams }) => {
    widgetParams.container.innerHTML = results === null
      ? ''
      : hits.map((hit) => `<article><a href="${hit.url}" data-id="${hit.objectID}">${hit.title}</a></article>`).join('')
        || '<p>No results.</p>';

    widgetParams.container.querySelectorAll('a').forEach((link, position) => {
      link.addEventListener('click', () => sendEvent('click', hits[position], position + 1));
    });
  });

  search.addWidgets([
    searchBox({ container: document.querySelector('#search-box') }),
    hits({ container: document.querySelector('#search-results') }),
  ]);

  search.start();
</script>
```

The same thing through a bundler:

```js
import xpsearch from '@yourco/xperience-search';
import { connectSearchBox, connectHits } from '@yourco/xperience-search/connectors';

const search = xpsearch({ index: 'site-content', routing: true });
search.addWidgets([/* … */]);
search.start();
```

`dist/xpsearch.umd.js` defines the global `xpsearch`: the factory function itself, with every named
export and every connector hanging off it (`xpsearch.connectHits`, `xpsearch.registerWidgetType`,
`xpsearch.QUERY_ROUTE`, …). The ESM build is `dist/xpsearch.mjs` plus `dist/connectors.mjs`, and the
package `exports` map points `.` and `./connectors` at them, with TypeScript declarations for both.

### Options

`xpsearch(options)` — only `index` is required.

| Option | Default | What it does |
|---|---|---|
| `index` | — | Lucene index code name. Required. |
| `endpoint` | `'/api/xpsearch/query'` | Search endpoint. |
| `suggestEndpoint` | `'/api/xpsearch/suggest'` | Autocomplete endpoint. |
| `eventsEndpoint` | `'/api/xpsearch/events'` | Analytics endpoint. |
| `routing` | `false` | `true` for the default URL mapping, or `{ stateToRoute, routeToState }`. |
| `initialState` | `{}` | Partial state: `query`, `page`, `facetFilters`, `numericFilters`, `sort`, `hitsPerPage`. |
| `searchOnInitialLoad` | `true` | `false` renders the widgets once with `results: null` and waits for the first refinement. |
| `debounceMs` | `150` | Trailing debounce on searches. |
| `facets` | — | Facet attributes to always count, on top of those the widgets ask for. |
| `highlight` | — | `{ fields, preTag, postTag, snippetLength }`, passed straight through to the contract. |
| `attributesToRetrieve` | — | Projection for every hit. |
| `language` | — | Language code to search in. |
| `headers` | `{}` | Extra request headers, e.g. an API key. |
| `fetchFn` | `globalThis.fetch` | Injectable `fetch`, for tests and SSR. |
| `retries` | `2` | Retries after a network error, `429` or `5xx`. Never after another `4xx`. |
| `retryDelayMs` | `200` | Base backoff, doubled per attempt (200ms, 400ms). |
| `stalledSearchDelayMs` | `200` | How long a request may run before `status` becomes `'stalled'` and `isSearchStalled` flips. |

The instance exposes `addWidgets(widgets)`, `removeWidgets(widgets)`, `start()`, `dispose()`,
`on(event, handler)`, `off(event, handler)`, `createURL(state?)`, `sendEvent(type, objectID, position?)`,
and the read-only `state`, `results`, `status`, `helper` and `index`.

### Changing state: the helper

`search.helper` is the only sanctioned way to mutate state. Mutators are chainable and none of them
searches; `search()` executes.

```js
search.helper
  .setQuery('espresso')
  .toggleFacetRefinement('contentType', 'Article')
  .addNumericRefinement('price', '<=', 50)
  .setSort('date_desc')
  .setPage(0)
  .search();
```

| Member | Notes |
|---|---|
| `setQuery(q)` | Resets to the first page, like every refinement. |
| `toggleFacetRefinement(attribute, value)` | Adds or removes one value. |
| `clearRefinements(attribute?)` | Clears facet *and* numeric refinements, all of them or one attribute's. |
| `setPage(page)` | Zero-based. The only mutator that does not reset the page. |
| `addNumericRefinement(attr, op, value)` | `op` is `<`, `<=`, `=`, `>=` or `>`. |
| `setSort(key)` | `'relevance'` (the default) or an index-configured key. |
| `search()` | Executes; debounced and cancellable. |
| `getState()` | The current state. Frozen — assigning to it throws. |
| `setNumericRefinement(attr, op, value)` | Replaces an existing bound with the same operator instead of adding one. |
| `removeNumericRefinement(attr, op?)` | Removes numeric refinements on an attribute. |
| `setHitsPerPage(n)` | `undefined` restores the server default. |
| `setFacetOperator(attribute, 'and' \| 'or')` | How that attribute's values combine on the wire. Defaults to `'or'`. |

The last five are extensions to the published SDK contract — see
[ADR-0007](../adr/0007-js-client-architecture.md).

On the wire, `'or'` puts all selected values of an attribute in one ORed group and `'and'` gives each
its own group, which is exactly the contract's outer-AND / inner-OR `facetFilters` shape:

```js
search.helper.setFacetOperator('tags', 'and')
  .toggleFacetRefinement('tags', 'coffee')
  .toggleFacetRefinement('tags', 'milk');
// facetFilters: [["tags:coffee"], ["tags:milk"]]      // 'or' would give [["tags:coffee","tags:milk"]]
```

### URL routing

`routing: true` syncs state to query params, restores it on back/forward, and makes every connector's
`createURL()` produce the same links, so they are shareable and crawlable.

| State | Param | Example |
|---|---|---|
| `query` | `q` | `?q=espresso` |
| `page` | `page`, **one-based** (state is zero-based) | `page=3` is `state.page === 2` |
| `sort` | `sort`, omitted when `relevance` | `sort=price_asc` |
| `facetFilters` | one param per attribute, comma-joined, each value `encodeURIComponent`-escaped | `contentType=Article,Product` |
| `numericFilters` | `<attribute>_<lt\|lte\|eq\|gte\|gt>` | `price_lte=50` |

Defaults are omitted, so an untouched search leaves the URL alone. Params the mapping does not own
(`utm_source`, …) are preserved. A change to the query alone uses `history.replaceState` — typing must
not fill the back stack — and anything else pushes.

Bring your own mapping when the defaults collide with your page:

```js
xpsearch({
  index: 'site-content',
  routing: {
    stateToRoute: (state) => (state.query === '' ? {} : { search: state.query }),
    routeToState: (route) => ({ query: route.search?.[0] ?? '' }),   // route values are arrays
  },
});
```

`createURL()` works whether or not routing is on, so links render correctly either way; only the
address bar is left alone when routing is off.

### The event bus

```js
search.on('stateChange', ({ state }) => console.log('refined', state));
search.on('render', ({ results }) => spinner.hidden = true);
search.on('error', ({ error, phase, widget }) => report(error, { phase, widget }));
search.off('render', handler);
```

- `stateChange` — after every mutation, before the response.
- `render` — after every widget render pass: once per response, and once per state change with the
  previous results (`null` before the first one).
- `error` — `phase` is `'init' | 'render' | 'dispose' | 'search' | 'contract'`, and `widget` names the
  widget when one is at fault. A throwing widget never takes the page down; see
  [Custom widgets](custom-widgets.md).

`'contract'` is how a `X-XpSearch-Api-Version` mismatch surfaces: the client reports it once per
version and keeps using the response rather than throwing.

### Several searches on one page

Every instance owns its own state, transport and widgets; two of them never interfere. Give at most one
of them `routing: true` — the URL has room for a single default mapping.

```js
const products = xpsearch({ index: 'products', routing: true });
const help = xpsearch({ index: 'help-centre' });
```

With Page Builder markup this is automatic: mounts are grouped by `data-xps-instance`, one instance per
group (see [Custom widgets](custom-widgets.md)).

### Run it against the mock server

The package ships a dependency-free mock of the search API, so you can build UI before the endpoint
exists. It serves 54 fixture documents with three facet attributes (`contentType`, `tags`, `language`),
a numeric `price` and a `publishedAt`.

```bash
cd libraries/xperience-search/src/XpSearch.Client
npm ci
npm run mock            # xpsearch mock server on http://127.0.0.1:3131/api/xpsearch/query (54 documents)
```

```bash
curl -s -X POST http://127.0.0.1:3131/api/xpsearch/query \
  -H 'content-type: application/json' \
  -d '{"index":"site-content","query":"espresso","hitsPerPage":2,
       "facets":["contentType","tags"],"facetFilters":[["contentType:Article"]],
       "numericFilters":["price<=60"],"highlight":{"fields":["title","content"]},
       "attributesToRetrieve":["title","url","price","contentType"]}'
```

```jsonc
{
  "hits": [
    {
      "objectID": "doc-1",
      "title": "Espresso Basics 1",
      "url": "/docs/espresso-basics-1",
      "price": 5,
      "contentType": "Article",
      "_score": 2,
      "_highlights": {
        "title": "<mark>Espresso</mark> Basics 1",
        "content": "A guide to <mark>espresso</mark> basics. coffee and <mark>espresso</mark> for every barista."
      }
    }
    // …
  ],
  "facets": {
    "contentType": { "Article": 9, "FAQ": 5, "Product": 7 },
    "tags": { "coffee": 4, "espresso": 8, "grinder": 4, "beans": 1, "milk": 1 }
  },
  "page": 0, "hitsPerPage": 2, "nbHits": 9, "nbPages": 5,
  "processingTimeMs": 6, "queryId": "0b1a883e-…"
}
```

Point an instance at it with `endpoint: 'http://127.0.0.1:3131/api/xpsearch/query'`. Facet counts are
disjunctive — a value's count ignores the filters on its own attribute — so an `or` refinement list
keeps showing the alternatives a visitor can still pick.

The mock is not a model of the Lucene pipeline: it matches substrings, scores by term hits and
implements the wire behaviour only. It exists so the client, the docs and the default widgets have
something contract-shaped to run against.

### Related pages

- [Custom widgets](custom-widgets.md) — the connector API, the widget lifecycle, `registerWidgetType`.
- [Search API](search-api.md) — the JSON contract the client speaks.
- [Widget reference](widget-reference.md) — the default renderers.
