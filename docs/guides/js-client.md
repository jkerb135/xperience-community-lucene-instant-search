## JavaScript client

`@yourco/xperience-search` is the browser half of Xperience Search: a transport (`SearchClient`), an
observable state (`SearchState`), and independent widgets that subscribe to it. Widgets can live
anywhere in the DOM — search box in the header, facets in a left rail, results in `<main>` — with no
shared parent and no order requirement.

> **Status:** the core and the default widgets ship today: state, transport, the widget lifecycle,
> the [behaviours](custom-widgets.md), routing, the event bus, the `.xps-mount` bootstrap, and the
> nine renderers documented in the [widget reference](widget-reference.md) — `searchBox`, `results`,
> `facetList`, `pagination`, `resultStats`, `sortSelect`, `clearFilters`, `activeFilters`,
> `toggleFilter`. `suggestions`, `rangeFilter`, `categoryTree` and `loadMore` are reserved names
> without renderers yet; build them on the behaviours, exactly as the built-ins are built.

### A working search page

```html
<div id="search-box"></div>
<div id="search-results"></div>

<script src="/js/xpsearch.umd.js"></script>
<script>
  const search = xpsearch.createSearch({
    endpoint: '/api/xpsearch/query',
    index: 'site-content',
    routing: true,
    searchOnInitialLoad: false,
    debounceMs: 150,
    highlight: { fields: ['title', 'content'] },
  });

  // The default widgets: a behaviour plus a renderer, nothing you have to write.
  search.addWidgets([
    xpsearch.searchBox({ container: '#search-box', placeholder: 'Search…' }),
    xpsearch.results({ container: '#search-results' }),
  ]);

  search.start();
</script>
```

Every option, template signature and piece of markup is in the
[widget reference](widget-reference.md). When no widget fits, drop to a behaviour and render it
yourself — the built-ins have nothing else available to them either:

```js
const myResults = xpsearch.withResults(({ items, sendEvent, params }) => {
  params.container.innerHTML = items
    .map((r) => `<article><a href="${r.attributes.url}" data-id="${r.id}">${r.attributes.title}</a></article>`)
    .join('');

  params.container.querySelectorAll('a').forEach((link, position) => {
    link.addEventListener('click', () => sendEvent('click', items[position], position + 1));
  });
});
```

The same thing through a bundler:

```js
import createSearch, { searchBox, results } from '@yourco/xperience-search';

const search = createSearch({ index: 'site-content', routing: true });
search.addWidgets([
  searchBox({ container: '#search-box' }),
  results({ container: '#search-results' }),
]);
search.start();
```

`dist/xpsearch.umd.js` defines the global `xpsearch`: the factory function itself, with every named
export, every widget and every behaviour hanging off it (`xpsearch.createSearch`, `xpsearch.searchBox`,
`xpsearch.html`, `xpsearch.withResults`, `xpsearch.registerWidgetType`, `xpsearch.QUERY_ROUTE`, …). The
ESM build is `dist/xpsearch.mjs` plus `dist/behaviors.mjs`, and the package `exports` map points `.` and
`./behaviors` at them, with TypeScript declarations for both.

### Options

`createSearch(options)` — only `index` is required.

| Option | Default | What it does |
|---|---|---|
| `index` | — | Lucene index code name. Required. |
| `endpoint` | `'/api/xpsearch/query'` | Search endpoint. |
| `suggestEndpoint` | `'/api/xpsearch/suggest'` | Autocomplete endpoint. |
| `eventsEndpoint` | `'/api/xpsearch/events'` | Analytics endpoint. |
| `routing` | `false` | `true` for the default URL mapping, or `{ stateToRoute, routeToState }`. |
| `initialState` | `{}` | Partial state: `query`, `page`, `filters`, `sort`, `pageSize`. |
| `searchOnInitialLoad` | `true` | `false` renders the widgets once with `results: null` and waits for the first filter. |
| `debounceMs` | `150` | Trailing debounce on searches. |
| `facets` | — | Facet attributes to always count, on top of those the widgets ask for. |
| `highlight` | — | `{ fields, preTag, postTag, snippetLength }`, passed straight through to the contract. |
| `fields` | — | Document fields to project into `result.attributes`. |
| `language` | — | Language code to search in. |
| `headers` | `{}` | Extra request headers, e.g. an API key. |
| `fetchFn` | `globalThis.fetch` | Injectable `fetch`, for tests and SSR. |
| `retries` | `2` | Retries after a network error, `429` or `5xx`. Never after another `4xx`. |
| `retryDelayMs` | `200` | Base backoff, doubled per attempt (200ms, 400ms). |
| `stalledSearchDelayMs` | `200` | How long a request may run before `status` becomes `'stalled'` and `isStalled` flips. |

The instance exposes `addWidgets(widgets)`, `removeWidgets(widgets)`, `start()`, `dispose()`,
`on(event, handler)`, `off(event, handler)`, `urlFor(state?)`, `sendEvent(type, resultId, position?)`,
and the read-only `state`, `results`, `status`, `actions` and `index`.

### Changing state: the actions

`search.actions` is the only sanctioned way to mutate state. Mutators are chainable and none of them
searches; `search()` executes. (The `apply()` a *behaviour* hands a widget is the exception by design:
it is the mutation **and** `search()`, because a control that is clicked should search. See
[Custom widgets](custom-widgets.md#what-apply-does-and-when-render-runs).)

```js
search.actions
  .setQuery('espresso')
  .toggleFacet('contentType', 'Article')
  .setNumericFilter('price', 'lte', 50)
  .setSort('newest')
  .setPage(1)
  .search();
```

| Member | Notes |
|---|---|
| `setQuery(q)` | Resets to the first page, like every filter change. |
| `toggleFacet(attribute, value)` | Adds or removes one value. |
| `clearFilters(attribute?)` | Clears facet *and* numeric filters, all of them or one attribute's. |
| `setPage(page)` | One-based. The only mutator that does not reset the page. |
| `setNumericFilter(attr, op, value)` | `op` is `lt`, `lte`, `eq`, `ne`, `gte` or `gt`. Replaces an existing bound with the same operator. |
| `removeNumericFilter(attr, op?)` | Removes numeric filters on an attribute. |
| `setSort(key)` | `'relevance'` (the default) or a sort key the index accepts. |
| `setPageSize(n)` | `undefined` restores the server default. |
| `setFacetOperator(attribute, 'and' \| 'or')` | How that attribute's values combine. Defaults to `'or'`. |
| `getState()` | The current state. Frozen — assigning to it throws. |
| `search()` | Executes; debounced and cancellable. |

`state.filters` has the same shape as the wire, so there is one vocabulary to learn:

```js
search.actions.setFacetOperator('tags', 'and')
  .toggleFacet('tags', 'coffee')
  .toggleFacet('tags', 'milk');

search.actions.getState().filters;
// { facets: [{ attribute: 'tags', values: ['coffee', 'milk'], operator: 'and' }], numeric: [] }
```

`'or'` is the default and is left out of the payload; `'and'` requires every selected value to be present
on the document.

### URL routing

`routing: true` syncs state to query params, restores it on back/forward, and makes every behaviour's
`urlFor()` produce the same links, so they are shareable and crawlable.

| State | Param | Example |
|---|---|---|
| `query` | `q` | `?q=espresso` |
| `page` | `page`, one-based in state and in the URL | `page=3` is `state.page === 3` |
| `sort` | `sort`, omitted when `relevance` | `sort=price_asc` |
| `filters.facets` | one param per attribute, comma-joined, each value `encodeURIComponent`-escaped | `contentType=Article,Product` |
| a facet's `operator: 'and'` | `<attribute>_op=and` | `tags_op=and` |
| `filters.numeric` | `<attribute>_<lt\|lte\|eq\|ne\|gte\|gt>` | `price_lte=50` |

Defaults are omitted, so an untouched search leaves the URL alone. Params the mapping does not own
(`utm_source`, …) are preserved. A change to the query alone uses `history.replaceState` — typing must
not fill the back stack — and anything else pushes.

Bring your own mapping when the defaults collide with your page:

```js
createSearch({
  index: 'site-content',
  routing: {
    stateToRoute: (state) => (state.query === '' ? {} : { search: state.query }),
    routeToState: (route) => ({ query: route.search?.[0] ?? '' }),   // route values are arrays
  },
});
```

`urlFor()` works whether or not routing is on, so links render correctly either way; only the
address bar is left alone when routing is off.

### The event bus

```js
search.on('stateChange', ({ state }) => console.log('filtered', state));
search.on('render', ({ results }) => spinner.hidden = true);
search.on('error', ({ error, phase, widget }) => report(error, { phase, widget }));
search.off('render', handler);
```

- `stateChange` — after every mutation that actually changes the state, before the response.
- `render` — after every widget render pass: once per response, and once per state change with the
  previous results (`null` before the first one). A state change renders on a **microtask**, so
  several mutations in one handler are one render, and the DOM is up to date one microtask after
  `actions` returns rather than synchronously.
- `error` — `phase` is `'init' | 'render' | 'dispose' | 'search' | 'contract'`, and `widget` names the
  widget when one is at fault. A throwing widget never takes the page down; see
  [Custom widgets](custom-widgets.md).

`'contract'` is how a `X-XpSearch-Api-Version` mismatch surfaces: the client reports it once per
version and keeps using the response rather than throwing.

### Several searches on one page

Every instance owns its own state, transport and widgets; two of them never interfere. Give at most one
of them `routing: true` — the URL has room for a single default mapping.

```js
const products = createSearch({ index: 'products', routing: true });
const help = createSearch({ index: 'help-centre' });
```

With Page Builder markup this is automatic: mounts are grouped by `data-xps-instance`, one instance per
group. The options of a group are **merged** from the `data-xps-instance-config` of every mount in it, so
an option only one widget carries (for example the Page Builder results widget's `initialState.pageSize`
and `fields`) applies whatever the widget order is. The first definition of a key wins, and a mount that
gives the same key a different value logs one `console.warn` naming the key and the instance. See
[Page Builder widgets](page-builder-widgets.md) and [Custom widgets](custom-widgets.md).

### Run it against the mock server

The package ships a dependency-free mock of the search API, so you can build UI before the endpoint
exists. It serves 54 fixture documents with three facet attributes (`contentType`, `tags`, `language`),
a numeric `price` and a `publishedAt`.

It ships in the npm package as `mock/server.mjs`, with a `xpsearch-mock` bin entry:

```bash
npm install @yourco/xperience-search
npx xpsearch-mock                                            # http://127.0.0.1:3131/api/xpsearch/query
PORT=4000 npx xpsearch-mock                                  # another port
node node_modules/@yourco/xperience-search/mock/server.mjs   # the same thing, no npx
```

Working in this repository instead? `cd src/XpSearch.Client && npm ci`, then `npm run repo:mock` for
the same server from source, or `npm run repo:demo` to build the bundles and serve
`src/XpSearch.Client/demo/index.html` with the theme stylesheets — a complete search page assembled
from every default widget, plus a second, independent instance built from `.xps-mount` markup. The
`repo:` scripts read `mock/*.ts`, `demo/` and `../../themes`, none of which are in the tarball.

```bash
curl -s -X POST http://127.0.0.1:3131/api/xpsearch/query \
  -H 'content-type: application/json' \
  -d '{"index":"site-content","query":"espresso","pageSize":2,
       "facets":["contentType","tags"],
       "filters":{"facets":[{"attribute":"contentType","values":["Article"]}],
                  "numeric":[{"attribute":"price","operator":"lte","value":60}]},
       "highlight":{"fields":["title","content"]},
       "fields":["title","url","price","contentType"]}'
```

```jsonc
{
  "results": [
    {
      "id": "doc-1",
      "attributes": {
        "title": "Espresso Basics 1",
        "url": "/docs/espresso-basics-1",
        "price": 5,
        "contentType": "Article"
      },
      "score": 2,
      "highlights": {
        "title": "<mark>Espresso</mark> Basics 1",
        "content": "A guide to <mark>espresso</mark> basics. coffee and <mark>espresso</mark> for every barista."
      }
    }
    // …
  ],
  "facets": {
    "contentType": [
      { "value": "Article", "label": "Article", "count": 9 },
      { "value": "Product", "label": "Product", "count": 7 },
      { "value": "FAQ", "label": "FAQ", "count": 5 }
    ],
    "tags": [
      { "value": "espresso", "label": "Espresso", "count": 8 },
      { "value": "coffee", "label": "Coffee", "count": 4 },
      { "value": "grinder", "label": "Grinders", "count": 4 }
      // …
    ]
  },
  "page": 1, "pageSize": 2, "total": 9, "totalPages": 5,
  "tookMs": 6, "queryId": "87343943-…", "redirect": null
}
```

Point an instance at it with `endpoint: 'http://127.0.0.1:3131/api/xpsearch/query'`. Facet counts are
disjunctive — a value's count ignores the filter on its own attribute — so an `or` facet list keeps
showing the alternatives a visitor can still pick, and each value carries the `label` a widget displays
(`grinder` → *Grinders*), exactly as a taxonomy dimension does in a real index.

The mock is not a model of the Lucene pipeline: it matches substrings, scores by term matches and
implements the wire behaviour only. It exists so the client, the docs and the default widgets have
something contract-shaped to run against.

### Related pages

- [Custom widgets](custom-widgets.md) — the behaviour API, the widget lifecycle, `registerWidgetType`.
- [Search API](search-api.md) — the JSON contract the client speaks.
- [Widget reference](widget-reference.md) — the default renderers.
- [Migrating from Algolia](migrating-from-algolia.md) — the name-by-name map.
