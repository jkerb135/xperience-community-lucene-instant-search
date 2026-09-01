## Custom widgets

Build any search UI on the public API without forking the library. A **behaviour** supplies the
mechanics — state subscription, data shaping, filter dispatch, URL building — and you supply the
rendering. The widgets shipped with Xperience Search are written the same way, on the same behaviours,
so anything they can do, your control can do.

### A worked example: a single-select dropdown facet

This exact file lives at `samples/CustomWidget.Dropdown/src/dropdownFacet.ts` in the library
repository, is built and tested against the **packed** packages in CI (`node samples/pack-and-build.mjs`), and is
reproduced here in full. Nothing is elided, and it typechecks under `strict` with no `any` and no
import from an internal path.

```ts
/**
 * `myCompany.dropdownFacet` — a single-select `<select>` facet built on the published
 * `withFacetList` behaviour. This file is the worked example in
 * `docs/guides/custom-widgets.md`; the two are the same text and CI builds this one.
 */
import { escapeHtml, readMountConfig, registerWidgetType, widgetId } from '@xperience-community/xperience-search';
import type { MountConfig, Widget } from '@xperience-community/xperience-search';
import { withFacetList } from '@xperience-community/xperience-search/behaviors';

/** The one identifier the JavaScript side uses, so the two registrations cannot drift. */
export const WIDGET_TYPE = 'myCompany.dropdownFacet';

export interface DropdownFacetParams extends Record<string, unknown> {
  /** The element to render into. In Page Builder this is the `.xps-mount` element itself. */
  container: HTMLElement;
  /** Visible label of the select. Defaults to "Filter". */
  label?: string;
  /** Text of the option that applies no filter. Defaults to "All". */
  allLabel?: string;
}

const option = (value: string, text: string, selected: boolean): string =>
  `<option value="${escapeHtml(value)}"${selected ? ' selected' : ''}>${escapeHtml(text)}</option>`;

export const dropdownFacet = withFacetList<DropdownFacetParams>((renderOptions, isFirstRender) => {
  const { items, apply, canApply, params } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = params;

  if (isFirstRender) {
    const id = widgetId(container, 'dropdown-facet', 'control');
    container.innerHTML = `<div class="xps xps-stack xps-select">
  <label class="xps-select__label" for="${id}">${escapeHtml(label)}</label>
  <select class="xps-select__control" id="${id}"></select>
</div>`;

    const control = container.querySelector('select');
    control?.addEventListener('change', () => {
      // The applied value is read back from the DOM, not from `renderOptions`: this listener is
      // registered once and would otherwise close over the first render's items. It is written
      // back here too, because a re-render is queued on a microtask — two changes in a row can
      // both happen before one arrives.
      const previous = control.dataset['xpsActive'] ?? '';
      control.dataset['xpsActive'] = control.value;
      if (previous !== '') apply(previous); // single select: clear what was chosen before
      if (control.value !== '') apply(control.value);
    });
  }

  const select = container.querySelector('select');
  const root = container.querySelector('.xps-select');
  if (!select || !root) return;

  const active = items.find((item) => item.isActive);
  select.innerHTML =
    option('', allLabel, active === undefined) +
    items.map((item) => option(item.value, `${item.label} (${item.count})`, item.isActive)).join('');
  // State is authoritative: routing or a clear-filters widget can change it behind our back.
  select.value = active?.value ?? '';
  select.dataset['xpsActive'] = active?.value ?? '';
  select.disabled = !canApply;
  root.classList.toggle('xps-select--disabled', !canApply);
});

/**
 * Makes the control resolvable from `data-xps-widget="myCompany.dropdownFacet"`.
 * The mount config is editor-supplied JSON, so `readMountConfig` narrows it; a missing
 * `attribute` throws, which the bootstrap turns into one `console.error` and a skipped widget.
 */
export function registerDropdownFacet(): void {
  registerWidgetType(WIDGET_TYPE, (config: MountConfig): Widget =>
    dropdownFacet({
      container: config.container,
      ...readMountConfig(config, {
        attribute: 'string',
        label: 'string?',
        allLabel: 'string?',
      }),
    })
  );
}
```

Add it to an instance like any built-in. `container` is an `HTMLElement`, so narrow the result of
`querySelector` before passing it:

```ts
const container = document.querySelector<HTMLElement>('#facet-brand');
if (container) {
  search.addWidgets([
    dropdownFacet({ container, attribute: 'brand', label: 'Brand', limit: 50, sortBy: ['name:asc'] }),
  ]);
}
```

No fetch, no request building, no facet-count math, no URL sync, no debouncing, no state management.
The behaviour supplied `items` and `apply`; everything else is inherited. `item.label` is already the
display text the server chose — the taxonomy tag title, not its code name.

Five things in it are not obvious, and each is the subject of a section below:

| Line | Why |
|---|---|
| `widgetId(container, 'dropdown-facet', 'control')` | A Page Builder mount element has no `id`, so deriving one from `container.id` produces `id="-control"` on every instance. See [Element ids](#element-ids). |
| `escapeHtml(...)` on every interpolation | Facet labels and editor-typed text are untrusted. See [Escaping](#escaping). |
| `readMountConfig(config, spec)` | `data-xps-config` is whatever an editor typed. See [Placing a custom widget in Page Builder](#placing-a-custom-widget-in-page-builder). |
| `dataset['xpsActive']`, written in both places | The single-select idiom. See [What `apply()` does, and when render runs](#what-apply-does-and-when-render-runs). |
| `xps`, `xps-stack`, `xps-select` | Documented shell classes, not invented ones. See [Theming](theming.md) and `themes/MARKUP.md`. |

### What `apply()` does, and when render runs

`apply(value)` on `withFacetList` is `actions.toggleFacet(attribute, value).search()` — it **toggles
and searches**. The toggle is synchronous, so `search.state` is already correct when `apply` returns;
the request is not, because it goes through the debounced transport (`debounceMs`, default 150 ms).
Two `apply` calls in one handler therefore produce **one** request, carrying the state after both.

A state change re-renders on a **microtask**, not synchronously: the instance coalesces the renders
of a chained mutation into one, so `render` has not run yet when the handler that called `apply`
returns. (Verified by `src/behaviors/facet-apply.test.ts` in the client.)

Both facts drive the **single-select idiom**: to replace the current value rather than add to it,
clear the old one and apply the new one in the same handler —

```ts
if (previous !== '') apply(previous);   // toggling an active value clears it
if (next !== '') apply(next);
```

— and keep `previous` somewhere that survives a handler running twice before a render. The example
uses `select.dataset.xpsActive`, written by `render` (state wins when routing or a clear-filters
widget changes it) *and* by the handler (so a second change before the render is still correct).
Deriving `previous` from the last render's `items` is the bug this example used to ship: choose one
value and then another quickly, and both end up active.

> The event handler is registered once, so it must read the behaviour's data at event time, not
> close over the `renderOptions` of the first render — capture the actions, not the data. Here
> "at event time" means the DOM, which is the only thing guaranteed current.

`actions.clearFilters(attribute)` looks like a one-call alternative. It is not equivalent: it also
clears numeric filters on the attribute, and it does not search — you would have to chain
`.search()` yourself.

### Escaping

Nothing escapes for you when you assign `innerHTML`. Facet labels come from the index, and
`label`/`allLabel` come from whatever an editor typed into a widget dialog, so both are untrusted:

```ts
import { escapeHtml, html } from '@xperience-community/xperience-search';

element.innerHTML = `<option value="${escapeHtml(item.value)}">${escapeHtml(item.label)}</option>`;
element.innerHTML = String(html`<option value="${item.value}">${item.label}</option>`);
```

- `escapeHtml(value: string): string` escapes `& < > " '`. Use it per interpolation when you are
  building a string, including inside a quoted attribute — a `"` in a taxonomy code name breaks out
  of `value="…"` otherwise.
- `` html`…` `` is the same guarantee for a whole template: everything interpolated is escaped, and
  results nest. `html.raw(value)` opts a string out, for markup you produced yourself — never for an
  index field or editor input. See [Widget reference → Templating helpers](widget-reference.md#templating-helpers).
- `textContent`/`setAttribute` need neither.

The C# side is already correct: `data-xps-config` is HTML-attribute-encoded by
`XpSearchMountRenderer`, and the bootstrap `JSON.parse`s the decoded value — so a quote an editor
types survives intact and arrives at your factory as a plain string. Escaping it again on the way
into markup is your job.

### Element ids

`widgetId(container, widget, part)` is the single implementation of `MARKUP.md` rule 4,
`id="xps-{instance}-{widget}-{part}"`:

```ts
const id = widgetId(container, 'dropdown-facet', 'control');   // xps-search-1-dropdown-facet-control
```

The instance segment is `data-xps-instance`, else the container's own `id`, else `default` — a Page
Builder mount element carries no `id`, which is why `container.id` alone yields `id="-control"`,
duplicated across every instance on the page. All parts of one widget share a prefix, so call it once
per part, and a second widget of the same name in the same instance gets `-2` appended rather than
colliding.

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
| `withSearchBox` | `queryHook?`, `followRedirects?`, `windowRef?` | `query`, `apply(q)`, `submit(q)`, `clear()`, `isStalled` |
| `withResults` | `transformItems?` | `items`, `results`, `redirect`, `sendEvent(type, result, position?)` |
| `withFacetList` | `attribute`, `operator?`, `limit?`, `showMore?`, `showMoreLimit?`, `sortBy?`, `transformItems?` | `items[{ label, value, count, isActive }]`, `apply(value)`, `urlFor(value)`, `canApply`, `canToggleShowMore`, `isShowingMore`, `toggleShowMore()`, `sendEvent` |
| `withCategoryTree` | `attribute`, `limit?` | `items` (a tree of `{ value, label, count, path, isActive, children }`), `selected`, `apply(value)`, `urlFor(value)`, `isActive(value)`, `canApply` |
| `withPagination` | `padding?`, `maxPages?` | `pages`, `current`, `totalPages`, `total`, `isFirstPage`, `isLastPage`, `canApply`, `apply(page)`, `urlFor(page)` |
| `withResultStats` | — | `total`, `tookMs`, `query`, `page`, `totalPages`, `pageSize`, `hasResults` |
| `withSortSelect` | `items` | `options`, `current`, `canApply`, `apply(value)`, `urlFor(value)` |
| `withActiveFilters` | `includedAttributes?`, `excludedAttributes?`, `transformItems?` | `items[{ attribute, type, value, operator?, label, apply(), urlFor() }]`, `canApply`, `clearAll()`, `clearAllUrl()` |
| `withRange` | `attribute`, `min?`, `max?` | `start`, `range`, `canApply`, `apply([min, max])` |
| `withLoadMore` | `transformItems?` | `items` (every page loaded so far), `total`, `isExhausted`, `isLoading`, `generation`, `loadMore()`, `sendEvent(type, result, position?)` |
| `withSuggestions` | `debounceMs?`, `minQueryLength?`, `limit?`, `language?`, `resultsUrl?`, `windowRef?` | `query`, `suggestions`, `isOpen`, `activeIndex`, `isLoading`, `seeAllUrl`, `setQuery(q)`, `move(offset \| 'first' \| 'last')`, `select(index)`, `submit()`, `close()`, `clear()` |

Page numbers are one-based everywhere, like the JSON contract. `withFacetList` declares its attribute to
the request, so facet counts arrive without extra configuration, and it declares its `operator`, so
`'and'` and `'or'` reach the wire correctly.

`withCategoryTree` builds its tree from `FacetValue.path` (see
[Search API](search-api.md#hierarchical-taxonomies)) and selects **one value at a time**: `apply(value)`
replaces whatever the attribute held, and applying the value that is already selected clears it. Its
`isActive` is true for the whole open path — the selected node *and* its ancestors — which is what a
renderer needs for `aria-current` on every level.

Accessibility state is handed to you rather than derived: `isActive` for `aria-pressed`/`aria-current`,
`canApply` for `disabled`, `isStalled` for a spinner or an `aria-busy` region.

`withLoadMore` bumps `generation` whenever it threw the accumulated list away instead of appending
to it; a renderer that appends compares it with the one it last painted and rebuilds when it differs.
A `withSuggestions` renderer must call `close()` from its unmount function — that is what drops a
debounced call that has not fired yet and makes an in-flight answer stale.

`withCategoryTree` is not published: a hierarchy needs a facet shape the contract does not have —
see [KNOWN-LIMITATIONS](../internal/KNOWN-LIMITATIONS.md).

### TypeScript

Behaviours are generic over your widget params (and, for results, over your document shape), so nothing
in a custom widget needs an `any` or an import from an internal path — the worked example above is the
proof, and `samples/CustomWidget.Dropdown` typechecks it under `strict` with
`noUncheckedIndexedAccess` on every CI run. Your params type must extend `Record<string, unknown>`,
which is the only constraint a behaviour puts on it.

Results are generic over the document shape:

```ts
import { withResults } from '@xperience-community/xperience-search/behaviors';

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

  // Optional: makes `?tags=coffee` in the URL routable (`kind: 'numeric'` for `?price_lte=50`).
  // Widgets built on a behaviour declare this from their `attribute` param already.
  $$routable: { attribute: 'tags', kind: 'facet' },

  init({ state, actions, search }) { /* once, before the first search */ },
  render({ results, state, actions, isFirstRender }) { /* after every response and state change */ },
  dispose() { /* remove listeners */ },
}]);
```

`prepareState(state)` shapes the state that becomes the request; `prepareRequest(request)` sets request
fields that are not state (`facets`, `highlight`, `fields`). `render` runs after every response and
again on every state change with the previous `results` — on a microtask, so several mutations in one
handler produce one render, and controls update before the network answers rather than after it. It
is not synchronous: the state is current the instant `actions` returns, the DOM is current one
microtask later. See [What `apply()` does](#what-apply-does-and-when-render-runs).

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

```ts
import { readMountConfig, registerWidgetType } from '@xperience-community/xperience-search';
import type { MountConfig, Widget } from '@xperience-community/xperience-search';

registerWidgetType('myCompany.dropdownFacet', (config: MountConfig): Widget =>
  dropdownFacet({
    container: config.container,
    ...readMountConfig(config, { attribute: 'string', label: 'string?', allLabel: 'string?' }),
  })
);
```

The factory receives the parsed `data-xps-config` plus `container`, the mount element itself.

**A mount config is a trust boundary.** `MountConfig` is `Record<string, unknown> & { container:
HTMLElement }`, because the JSON is whatever an editor typed into the widget dialog — so
`config.attribute` is `unknown` and passing it straight to a behaviour that wants a `string` is
(correctly) a compile error. `readMountConfig(config, spec)` narrows it:

| Spec value | Accepts |
|---|---|
| `'string'`, `'number'`, `'boolean'` | required; a missing, empty (`''`/`null`) or wrong-typed value throws an `Error` naming the key |
| `'string?'`, `'number?'`, `'boolean?'` | the same, but absent means the key is simply omitted, so your `?? 'default'` applies |

An empty string counts as absent: an editor who left a text field blank has not configured it. The
return type is derived from the spec, so the required keys are non-optional and the optional ones are
`?`. A throw inside a factory is contained — the bootstrap logs one `console.error` and skips that
mount.

The bootstrap scans for `.xps-mount`, groups the elements by `data-xps-instance` (default `"default"`),
builds one `createSearch()` instance per group and starts it:

```html
<div class="xps-mount"
     data-xps-widget="myCompany.dropdownFacet"
     data-xps-instance="search-1"
     data-xps-instance-config='{"index":"site-content","routing":true}'
     data-xps-config='{"attribute":"brand","label":"Brand"}'></div>
```

- Instance options are **merged** from the `data-xps-instance-config` of every mount in the group, so an
  option only one widget knows still applies wherever that widget sits. The first definition of a key
  wins; a mount that disagrees produces one `console.warn` naming the key. The group needs an `index`
  from at least one mount.
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
- **Keep the identifier in one place.** Three strings have to agree and nothing validates them at
  build time: `registerWidgetType('myCompany.dropdownFacet')` and the view component's
  `WidgetType => "myCompany.dropdownFacet"` must be *identical* (camel-cased by convention, like the
  built-ins), while `[RegisterWidget(identifier: "MyCompany.DropdownFacet")]` is the Xperience-side
  identifier and follows Xperience's Pascal-cased `Company.Object` convention — it is not the same
  string and does not have to be. Export a constant (`export const WIDGET_TYPE = …`) on the
  JavaScript side and a `const` on the C# side; a typo is otherwise a `console.error` on a live page.
- **Accessibility is yours, but scaffolded.** Use real form controls, and take `isActive`, `canApply`
  and `isStalled` from the behaviour instead of deriving them.
- **The shell CSS is available to you, and only it.** Layout primitives (`xps`, `xps-stack`,
  `xps-cluster`), `xps-button`, `xps-chip`, `xps-skeleton`, `xps-sr-only` and the shared `xps-select`
  block are documented utilities you may render — see [Theming](theming.md) and `themes/MARKUP.md`. Do **not** borrow
  another widget's block (`xps-facet-list__*`, `xps-results__*`): those are that widget's
  contract, and `themes/scripts/check.mjs` enforces a three-way agreement between the CSS, the
  fixtures and `MARKUP.md` that your class names are not part of. Anything else is your own block,
  which you style yourself.
- **The behaviour API is semver-major.** `RenderOptions`, `SearchActions` and the widget lifecycle only
  break on a major version.

### Page Builder widgets in C#

Make the same control placeable by editors. Subclass `XpSearchMountWidgetViewComponent<T>`: it
serializes your properties into `data-xps-config`, emits the instance grouping and the instance options,
and renders the editor-only unconfigured block. You never hand-write a mount div or JSON-encode anything.

```csharp
using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Core;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: "MyCompany.DropdownFacet",
    viewComponentType: typeof(DropdownFacetWidgetViewComponent),
    name: "Search - Dropdown filter",
    propertiesType: typeof(DropdownFacetWidgetProperties),
    Description = "Filters a search on one attribute, as a single-select drop-down.",
    IconClass = "icon-chevron-down",
    AllowCache = false)]

// `Index` (order 10) and `InstanceId` (order 20) come from the base class; start your own at 30.
public sealed class DropdownFacetWidgetProperties : XpSearchMountWidgetProperties
{
    // The attribute drop-down is filled from the selected index's facetable fields, and hidden
    // until an index is chosen. `Index` must be ordered before it, which the base class guarantees.
    [DropDownComponent(Label = "Attribute", Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = "Filter";

    [TextInputComponent(Label = "\"All\" option text", Order = OrderFirstWidgetProperty + 20)]
    public string AllLabel { get; set; } = "All";
}

public sealed class DropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties>
{
    public DropdownFacetWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    protected override string WidgetType => "myCompany.dropdownFacet";

    // Without an attribute there is nothing to filter on: instruct the editor instead of rendering.
    protected override string? ConfigurationHint(DropdownFacetWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties.Attribute) ? "Select the attribute to filter on." : null;
}
```

That is the whole C# side. The editor drags "Search - Dropdown filter" onto the page, picks an index and
an attribute, and the widget renders:

```html
<div class="xps-mount"
     data-xps-widget="myCompany.dropdownFacet"
     data-xps-instance="default"
     data-xps-config="{&quot;attribute&quot;:&quot;brand&quot;,&quot;label&quot;:&quot;Brand&quot;,&quot;allLabel&quot;:&quot;All&quot;}"
     data-xps-instance-config="{&quot;index&quot;:&quot;site-content&quot;}"></div>
```

Two registrations make it work end to end: `registerWidgetType('myCompany.dropdownFacet', ...)` on the
JavaScript side (above) and `[RegisterWidget]` on the C# side. Three identifier strings are in play
and the casing differs on purpose:

| String | Convention | Must equal |
|---|---|---|
| `registerWidgetType('myCompany.dropdownFacet')` | `company.widgetName`, camel-cased, like the built-ins | `WidgetType` |
| `protected override string WidgetType => "myCompany.dropdownFacet"` | the same value — it becomes `data-xps-widget` | the registration above |
| `[RegisterWidget(identifier: "MyCompany.DropdownFacet")]` | `Company.Object`, Pascal-cased — Xperience's own convention for a widget identifier | nothing on the JavaScript side |

Nothing checks the first pair at build time, so keep each in one constant (`export const WIDGET_TYPE`
in the module, a `const string` on the view component) rather than typing the string twice.

#### What the base class gives you

| Member | Default | Override when |
|---|---|---|
| `WidgetType` | abstract | always — it is the `data-xps-widget` value |
| `BuildConfig(properties, config)` | every public property except `Index` and `InstanceId`, camel-cased, skipping nulls and empty strings | a config key is not simply the camel-cased property name |
| `BuildInstanceConfig(properties, instanceConfig)` | nothing beyond `index` | a property really is instance-wide (page size, retrieved fields) |
| `ConfigurationHint(properties)` | `null` (configured) | the widget needs more than an index before it can render |
| `GetWidgetType(properties)` | `WidgetType` | a property decides *which* JavaScript widget to mount |
| `CurrentIndex` | the editor's index, or the project's only index | you need the resolved index inside the three methods above |
| `BuildEditorPreview(properties)` | one `xps-editor-preview__note` paragraph saying the widget is configured | you want editors to see a picture of *your* widget in the Page Builder |

#### What editors see in the Page Builder

Inside the Page Builder a configured widget renders no mount: `BuildModel` returns a static preview in
`model.Preview` (edit and read-only mode), because the builder re-renders widget markup over AJAX on
every add, move and configure and no search should run from the editor. The base class supplies the
preview root, its `data-xps-widget` attribute and the badge; `BuildEditorPreview` supplies the body,
and the nine first-party widgets override it exactly as your widget would:

```csharp
protected override IHtmlContent BuildEditorPreview(DropdownFacetWidgetProperties properties)
{
    var select = new TagBuilder("select");
    select.AddCssClass("xps-select__control");
    select.Attributes["disabled"] = "disabled";
    select.InnerHtml.AppendHtml(Element("option", null, properties.AllLabel));

    var box = new TagBuilder("div");
    box.AddCssClass("xps-select");
    box.InnerHtml.AppendHtml(Element("label", "xps-select__label", properties.Label));
    box.InnerHtml.AppendHtml(select);

    return new HtmlContentBuilder()
        .AppendHtml(box)
        .AppendHtml(Element("p", "xps-editor-preview__note", $"Attribute: {properties.Attribute}"));
}

private static TagBuilder Element(string tagName, string? cssClass, string text)
{
    var tag = new TagBuilder(tagName);

    if (cssClass is not null)
    {
        tag.AddCssClass(cssClass);
    }

    tag.InnerHtml.Append(text);   // encodes: an editor's text can never become markup

    return tag;
}
```

The base class wraps that in
`<div class="xps xps-editor-preview xps-editor-preview--my-company-dropdown-facet"
data-xps-widget="myCompany.dropdownFacet">` with the badge, and marks the body `aria-hidden="true"`.

(The worked example in `samples/CustomWidget.Dropdown` builds against the published package, so it
picks this override up from the release that carries it.)

Rules the first-party previews follow, and yours should: mirror the live markup with the widget's own
classes, `disabled` on every control, a `<span>` instead of every `<a href>`, `xps-skeleton` bars where
result data would be, and an `xps-editor-preview__note` paragraph for configuration the markup cannot
show. Build the markup with `TagBuilder` (or any `IHtmlContent`) so property values are HTML-encoded —
never string-concatenate an editor's text into markup.

Preview mode and the live site are unaffected: there `model.Mount` carries the mount element as before.

`BuildModel(properties)` is public, so a widget's markup can be asserted in a unit test without an
Xperience application: substitute `IXpSearchEditorContext` and `IXpSearchIndexCatalog`, use the real
`XpSearchMountRenderer`, and read `model.Mount`. Three details the compiler will otherwise teach you:

```csharp
// 1. IXpSearchIndexCatalog.GetIndexNames() returns IReadOnlyList<string>, not IEnumerable<string>.
private sealed class StubIndexCatalog : IXpSearchIndexCatalog
{
    public IReadOnlyList<string> GetIndexNames() => ["site-content"];
}

var model = new DropdownFacetWidgetViewComponent(
    new XpSearchMountRenderer(), new StubEditorContext(XpSearchEditorMode.Live), new StubIndexCatalog())
    .BuildModel(new DropdownFacetWidgetProperties { Index = "site-content", Attribute = "brand" });

// 2. XpSearchMountViewModel.Mount is IHtmlContent — write it out to get a string.
using var writer = new StringWriter();
model.Mount!.WriteTo(writer, HtmlEncoder.Default);
var markup = writer.ToString();

// 3. data-xps-config is HTML-attribute-encoded, so decode before asserting on the JSON.
Assert.That(markup, Does.Contain("data-xps-widget=\"myCompany.dropdownFacet\""));
Assert.That(WebUtility.HtmlDecode(markup), Does.Contain("\"attribute\":\"brand\""));
```

`model.Mount` is `null` when `ConfigurationHint` returned a message; `model.EditorMessage` carries it
in `XpSearchEditorMode.Edit` and is `null` on the live site. The full fixture is
`samples/CustomWidget.Dropdown/dotnet/CustomWidget.Dropdown.Tests/DropdownFacetWidgetTests.cs`.

#### Registration and services

`services.AddXpSearchWidgets()` registers `IXpSearchMountRenderer`, `IXpSearchEditorContext`,
`IXpSearchIndexCatalog` and `ISearchResultTemplateRegistry`; your view component's constructor takes
whichever of them it needs. See [Page Builder widgets](page-builder-widgets.md) for the host setup and
the asset tag helper.

### Related pages

- [Page Builder widgets](page-builder-widgets.md) — the shipped widgets, their editor properties, assets.
- [Server rendering and the mount contract](server-rendering.md) — the stable mount attributes, and
  first-paint rendering from `XpSearch.Core` without the widgets.
- [JavaScript client](js-client.md) — options, the actions, routing, the event bus, the mock server.
- [Search API](search-api.md) — the JSON contract behind `results`.
- [Widget reference](widget-reference.md) — the built-in widgets, the templating helpers, the XSS model.
- [Migrating from Algolia](migrating-from-algolia.md) — the name-by-name map, verb by verb.
- `samples/CustomWidget.Dropdown` in the library repository — the example above as a buildable
  project: the widget, the Page Builder view component, jsdom tests for the JavaScript and
  NUnit tests for the C#, all against the packed packages.
