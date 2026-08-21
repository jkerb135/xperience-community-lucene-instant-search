## Custom widgets

Build any search UI on the public API without forking the library. A **connector** supplies the
behaviour — state subscription, data shaping, refinement dispatch, URL building — and you supply the
rendering. The widgets shipped with Xperience Search are written the same way, on the same connectors,
so anything they can do, your control can do.

### A dropdown facet in 40 lines

```js
import { connectRefinementList } from '@yourco/xperience-search/connectors';

export const dropdownFacet = connectRefinementList((renderOptions, isFirstRender) => {
  const { items, refine, widgetParams } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = widgetParams;

  if (isFirstRender) {
    container.innerHTML = `
      <label class="xps-dropdown__label" for="${container.id}-select">${label}</label>
      <select class="xps-dropdown__select" id="${container.id}-select"></select>`;

    container.querySelector('select').addEventListener('change', (event) => {
      const current = renderOptions.items.find((item) => item.isRefined);
      if (current) refine(current.value);              // clear the previous one (single select)
      if (event.target.value) refine(event.target.value);
    });
  }

  const select = container.querySelector('select');
  select.innerHTML =
    `<option value="">${allLabel}</option>` +
    items.map((item) => `
      <option value="${item.value}" ${item.isRefined ? 'selected' : ''}>
        ${item.label} (${item.count})
      </option>`).join('');
  select.value = items.find((item) => item.isRefined)?.value ?? '';
});
```

```js
search.addWidgets([
  dropdownFacet({
    container: document.querySelector('#facet-brand'),
    attribute: 'brand',
    label: 'Brand',
    limit: 50,
    sortBy: ['name:asc'],
  }),
]);
```

No fetch, no request building, no facet-count math, no URL sync, no debouncing, no state management.
The connector supplied `items` and `refine`; everything else is inherited.

> The event handler is registered once and reads `renderOptions.items` at click time, because
> `renderOptions` is rebuilt on every render — capture the connector's actions, not its data.

### How a connector works

```js
const myWidget = connectX(renderFn, unmountFn?);   // -> a widget factory
search.addWidgets([myWidget(widgetParams)]);       // -> a widget
```

`renderFn(renderOptions, isFirstRender)` runs once on `init` with `isFirstRender: true` and
`results: null`, then after every render pass. `unmountFn` runs on `dispose`. Every `renderOptions`
carries:

| Member | Meaning |
|---|---|
| `widgetParams` | Exactly what you passed to the factory. |
| `results` | The last `SearchResults`, or `null` before the first response. |
| `state` | The current `SearchState`. Frozen — never write to it. |
| `helper` | The `SearchHelper`; see [JavaScript client](js-client.md). |
| `instantSearchInstance` | The instance, for `createURL()`, `sendEvent()` and the event bus. |

plus the connector's own data and actions:

| Connector | `widgetParams` | Render state |
|---|---|---|
| `connectSearchBox` | `queryHook?` | `query`, `refine(q)`, `clear()`, `isSearchStalled` |
| `connectHits` | `transformItems?` | `hits`, `results`, `sendEvent(type, hit, position?)` |
| `connectRefinementList` | `attribute`, `operator?`, `limit?`, `showMore?`, `showMoreLimit?`, `sortBy?`, `transformItems?` | `items[{ label, value, count, isRefined }]`, `refine(value)`, `createURL(value)`, `canRefine`, `canToggleShowMore`, `isShowingMore`, `toggleShowMore()`, `sendEvent` |
| `connectPagination` | `padding?`, `totalPages?` | `pages`, `currentRefinement`, `nbPages`, `nbHits`, `isFirstPage`, `isLastPage`, `canRefine`, `refine(page)`, `createURL(page)` |
| `connectStats` | — | `nbHits`, `processingTimeMS`, `query`, `page`, `nbPages`, `hitsPerPage`, `hasResults` |
| `connectSortBy` | `items` | `options`, `currentRefinement`, `canRefine`, `refine(value)`, `createURL(value)` |
| `connectCurrentRefinements` | `includedAttributes?`, `excludedAttributes?`, `transformItems?` | `items[{ attribute, type, value, operator?, label, refine(), createURL() }]`, `canRefine`, `clearAll()`, `createClearAllURL()` |
| `connectRange` | `attribute`, `min?`, `max?` | `start`, `range`, `canRefine`, `refine([min, max])` |

Page numbers are zero-based everywhere, like the JSON contract. `connectRefinementList` declares its
attribute to the request, so facet counts arrive without extra configuration, and it declares its
`operator`, so `'and'` and `'or'` reach the wire correctly.

Accessibility state is handed to you rather than derived: `isRefined` for `aria-pressed`/`aria-current`,
`canRefine` for `disabled`, `isSearchStalled` for a spinner or an `aria-busy` region.

`connectAutocomplete` and `connectHierarchicalMenu` are not published yet — see
[KNOWN-LIMITATIONS](../internal/KNOWN-LIMITATIONS.md).

### TypeScript

Connectors are generic over your widget params (and, for hits, over your document shape), so nothing in
a custom widget needs an `any` or an import from an internal path:

```ts
import { connectRefinementList, connectHits } from '@yourco/xperience-search/connectors';

const dropdownFacet = connectRefinementList<{ container: HTMLElement; label?: string }>(
  ({ items, refine, widgetParams }) => {
    widgetParams.container.textContent = `${widgetParams.label ?? 'Filter'}: ${items.length}`;
    if (items[0]) refine(items[0].value);
  }
);

interface Product extends Record<string, unknown> {
  title: string;
  price: number;
}

const productHits = connectHits<Product>(({ hits }) => {
  for (const hit of hits) console.log(hit.title, hit.price.toFixed(2), hit.objectID);
});
```

`Hit<TItem>` is the generated contract `Hit` — `objectID`, `_score`, `_highlights`, `_rankingInfo` —
intersected with your document shape, so reserved members and your own attributes are both typed.

### Fully custom widgets

When no connector models the UI, implement the lifecycle interface directly. Every member is optional.

```js
search.addWidgets([{
  $$type: 'myCompany.recentSearches',            // used in error messages

  // Contribute to the outgoing request; applied in widget-add order.
  getSearchParameters(state) { return { ...state, hitsPerPage: 5 }; },
  getRequestParameters(request) { return { ...request, facets: [...(request.facets ?? []), 'tags'] }; },

  init({ state, helper, instantSearchInstance }) { /* once, before the first search */ },
  render({ results, state, helper, isFirstRender }) { /* after every response and state change */ },
  dispose() { /* remove listeners */ },
}]);
```

`getSearchParameters(state)` shapes the state that becomes the request; `getRequestParameters(request)`
sets request fields that are not state (`facets`, `highlight`, `attributesToRetrieve`). `render` runs
after every response and again on every state change with the previous `results`, so controls update
the moment they are clicked rather than when the network answers.

Widgets never talk to each other and never write to state: `helper` is the only sanctioned mutation
path, and the state object is frozen so a stray assignment throws instead of silently diverging.

### Error isolation

Each widget's `init`, `render`, `getSearchParameters` and `dispose` is wrapped. A widget that throws is
logged with `console.error`, reported on the `error` event with its `$$type`, and skipped — every other
widget on the page still renders and search keeps working.

```js
search.on('error', ({ error, phase, widget }) => {
  // widget: 'myCompany.recentSearches', phase: 'render'
});
```

### Placing a custom widget in Page Builder

Register the factory under a namespaced identifier and the `.xps-mount` bootstrap will resolve it:

```js
import { registerWidgetType } from '@yourco/xperience-search';
registerWidgetType('myCompany.dropdownFacet', (config) => dropdownFacet(config));
```

The factory receives the parsed `data-xps-config` plus `container`, the mount element itself. The
bootstrap scans for `.xps-mount`, groups the elements by `data-xps-instance` (default `"default"`),
builds one `xpsearch()` instance per group and starts it:

```html
<div class="xps-mount"
     data-xps-widget="myCompany.dropdownFacet"
     data-xps-instance="search-1"
     data-xps-instance-config='{"index":"site-content","routing":true}'
     data-xps-config='{"attribute":"brand","label":"Brand"}'></div>
```

- Instance options come from `data-xps-instance-config` on **any** mount in the group — the first one
  that parses and names an `index` wins — so widgets can be dropped in any order.
- The UMD bundle runs `mountAll()` itself on `DOMContentLoaded`. From a bundler, call
  `mountAll(root = document)` after your `registerWidgetType` calls; already-mounted elements are
  skipped, so it is safe to call again after injecting markup.
- Nothing here throws. An unknown widget type, malformed JSON or a group with no `index` is a
  `console.error` and a skipped mount; the rest of the page still works.

### Guardrails

- **Namespace your identifiers.** `registerWidgetType` rejects an id without a dot. Bare names
  (`searchBox`, `hits`, `refinementList`, `pagination`, `stats`, `sortBy`, `autocomplete`,
  `clearRefinements`, `currentRefinements`, `rangeSlider`, `hierarchicalMenu`, `infiniteHits`,
  `toggleRefinement`) are reserved for the built-ins so a future release never collides with your
  control.
- **Accessibility is yours, but scaffolded.** Use real form controls, and take `isRefined`, `canRefine`
  and `isSearchStalled` from the connector instead of deriving them.
- **The shell CSS is available to you.** Layout primitives, focus rings and skeleton classes are
  documented utilities — see [Theming](theming.md).
- **The connector API is semver-major.** `RenderOptions`, `SearchHelper` and the widget lifecycle only
  break on a major version.

### Page Builder widgets in C#

_Coming with the Page Builder widgets._ The `XpSearchMountWidgetViewComponent<T>` base class, the
`[RegisterWidget]` registration and the facet-attribute selector that populate the mount markup above
ship with the `XpSearch.Widgets` package; this page will grow the C# half then. Everything on the
JavaScript side is already in place: emit the mount div with a serialized config object and the
bootstrap does the rest.

### Related pages

- [JavaScript client](js-client.md) — options, the helper, routing, the event bus, the mock server.
- [Search API](search-api.md) — the JSON contract behind `results`.
