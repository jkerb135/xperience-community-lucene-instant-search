## Custom widgets

Build any search UI on the public API without forking the library. A **behaviour** supplies the
mechanics — state subscription, data shaping, filter dispatch, URL building — and you supply the
rendering. The widgets shipped with Xperience Search are written the same way, on the same behaviours,
so anything they can do, your control can do.

### A dropdown facet in 40 lines

```js
import { withFacetList } from '@yourco/xperience-search/behaviors';

export const dropdownFacet = withFacetList((renderOptions, isFirstRender) => {
  const { items, apply, params } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = params;

  if (isFirstRender) {
    container.innerHTML = `
      <label class="xps-dropdown__label" for="${container.id}-select">${label}</label>
      <select class="xps-dropdown__select" id="${container.id}-select"></select>`;

    container.querySelector('select').addEventListener('change', (event) => {
      const current = renderOptions.items.find((item) => item.isActive);
      if (current) apply(current.value);              // clear the previous one (single select)
      if (event.target.value) apply(event.target.value);
    });
  }

  const select = container.querySelector('select');
  select.innerHTML =
    `<option value="">${allLabel}</option>` +
    items.map((item) => `
      <option value="${item.value}" ${item.isActive ? 'selected' : ''}>
        ${item.label} (${item.count})
      </option>`).join('');
  select.value = items.find((item) => item.isActive)?.value ?? '';
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
The behaviour supplied `items` and `apply`; everything else is inherited. `item.label` is already the
display text the server chose — the taxonomy tag title, not its code name.

> The event handler is registered once and reads `renderOptions.items` at click time, because
> `renderOptions` is rebuilt on every render — capture the behaviour's actions, not its data.

### How a behaviour works

```js
const myWidget = withX(renderFn, unmountFn?);      // -> a widget factory
search.addWidgets([myWidget(params)]);             // -> a widget
```

`renderFn(renderOptions, isFirstRender)` runs once on `init` with `isFirstRender: true` and
`results: null`, then after every render pass. `unmountFn` runs on `dispose`. Every `renderOptions`
carries:

| Member | Meaning |
|---|---|
| `params` | Exactly what you passed to the factory. |
| `results` | The last `SearchResults`, or `null` before the first response. |
| `state` | The current `SearchState`. Frozen — never write to it. |
| `actions` | The `SearchActions`; see [JavaScript client](js-client.md). |
| `search` | The instance, for `urlFor()`, `sendEvent()` and the event bus. |

plus the behaviour's own data and actions:

| Behaviour | `params` | Render state |
|---|---|---|
| `withSearchBox` | `queryHook?` | `query`, `apply(q)`, `clear()`, `isStalled` |
| `withResults` | `transformItems?` | `items`, `results`, `sendEvent(type, result, position?)` |
| `withFacetList` | `attribute`, `operator?`, `limit?`, `showMore?`, `showMoreLimit?`, `sortBy?`, `transformItems?` | `items[{ label, value, count, isActive }]`, `apply(value)`, `urlFor(value)`, `canApply`, `canToggleShowMore`, `isShowingMore`, `toggleShowMore()`, `sendEvent` |
| `withPagination` | `padding?`, `maxPages?` | `pages`, `current`, `totalPages`, `total`, `isFirstPage`, `isLastPage`, `canApply`, `apply(page)`, `urlFor(page)` |
| `withResultStats` | — | `total`, `tookMs`, `query`, `page`, `totalPages`, `pageSize`, `hasResults` |
| `withSortSelect` | `items` | `options`, `current`, `canApply`, `apply(value)`, `urlFor(value)` |
| `withActiveFilters` | `includedAttributes?`, `excludedAttributes?`, `transformItems?` | `items[{ attribute, type, value, operator?, label, apply(), urlFor() }]`, `canApply`, `clearAll()`, `clearAllUrl()` |
| `withRange` | `attribute`, `min?`, `max?` | `start`, `range`, `canApply`, `apply([min, max])` |

Page numbers are one-based everywhere, like the JSON contract. `withFacetList` declares its attribute to
the request, so facet counts arrive without extra configuration, and it declares its `operator`, so
`'and'` and `'or'` reach the wire correctly.

Accessibility state is handed to you rather than derived: `isActive` for `aria-pressed`/`aria-current`,
`canApply` for `disabled`, `isStalled` for a spinner or an `aria-busy` region.

`withSuggestions` and `withCategoryTree` are not published yet — see
[KNOWN-LIMITATIONS](../internal/KNOWN-LIMITATIONS.md).

### TypeScript

Behaviours are generic over your widget params (and, for results, over your document shape), so nothing
in a custom widget needs an `any` or an import from an internal path:

```ts
import { withFacetList, withResults } from '@yourco/xperience-search/behaviors';

const dropdownFacet = withFacetList<{ container: HTMLElement; label?: string }>(
  ({ items, apply, params }) => {
    params.container.textContent = `${params.label ?? 'Filter'}: ${items.length}`;
    if (items[0]) apply(items[0].value);
  }
);

interface Product extends Record<string, unknown> {
  title: string;
  price: number;
}

const productResults = withResults<Product>(({ items }) => {
  for (const result of items) {
    console.log(result.attributes.title, result.attributes.price.toFixed(2), result.id);
  }
});
```

`Result<TAttributes>` is the generated contract `Result` — `id`, `score`, `highlights`, `ranking` — with
your document shape applied to `attributes`, so the contract members and your own fields are both typed
and neither can shadow the other.

### Fully custom widgets

When no behaviour models the UI, implement the lifecycle interface directly. Every member is optional.

```js
search.addWidgets([{
  $$type: 'myCompany.recentSearches',            // used in error messages

  // Contribute to the outgoing request; applied in widget-add order.
  prepareState(state) { return { ...state, pageSize: 5 }; },
  prepareRequest(request) { return { ...request, facets: [...(request.facets ?? []), 'tags'] }; },

  init({ state, actions, search }) { /* once, before the first search */ },
  render({ results, state, actions, isFirstRender }) { /* after every response and state change */ },
  dispose() { /* remove listeners */ },
}]);
```

`prepareState(state)` shapes the state that becomes the request; `prepareRequest(request)` sets request
fields that are not state (`facets`, `highlight`, `fields`). `render` runs after every response and
again on every state change with the previous `results`, so controls update the moment they are clicked
rather than when the network answers.

Widgets never talk to each other and never write to state: `actions` is the only sanctioned mutation
path, and the state object is frozen so a stray assignment throws instead of silently diverging.

### Error isolation

Each widget's `init`, `render`, `prepareState` and `dispose` is wrapped. A widget that throws is
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
builds one `createSearch()` instance per group and starts it:

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
  (`searchBox`, `results`, `facetList`, `pagination`, `resultStats`, `sortSelect`, `clearFilters`,
  `activeFilters`, `toggleFilter`, `suggestions`, `rangeFilter`, `categoryTree`, `loadMore`) are
  reserved for the built-ins so a future release never collides with your control.
- **Accessibility is yours, but scaffolded.** Use real form controls, and take `isActive`, `canApply`
  and `isStalled` from the behaviour instead of deriving them.
- **The shell CSS is available to you.** Layout primitives, focus rings and skeleton classes are
  documented utilities — see [Theming](theming.md).
- **The behaviour API is semver-major.** `RenderOptions`, `SearchActions` and the widget lifecycle only
  break on a major version.

### Page Builder widgets in C#

_Coming with the Page Builder widgets._ The `XpSearchMountWidgetViewComponent<T>` base class, the
`[RegisterWidget]` registration and the facet-attribute selector that populate the mount markup above
ship with the `XpSearch.Widgets` package; this page will grow the C# half then. Everything on the
JavaScript side is already in place: emit the mount div with a serialized config object and the
bootstrap does the rest.

### Related pages

- [JavaScript client](js-client.md) — options, the actions, routing, the event bus, the mock server.
- [Search API](search-api.md) — the JSON contract behind `results`.
- [Migrating from Algolia](migrating-from-algolia.md) — the name-by-name map, verb by verb.
