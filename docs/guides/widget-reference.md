## Widget reference

The nine widgets that ship with `@yourco/xperience-search`, the templating helpers they hand to your
templates, and the markup each one emits. Every widget is a behaviour plus a default renderer — the
same public API a [custom widget](custom-widgets.md) uses — so anything a built-in can do, yours can
too.

```html
<div id="search-box"></div>
<div id="facet-content-type"></div>
<div id="search-stats"></div>
<div id="search-results"></div>
<div id="search-pagination"></div>

<link rel="stylesheet" href="/css/shell.css">
<link rel="stylesheet" href="/css/default.css">
<script src="/js/xpsearch.umd.js"></script>
<script>
  const { createSearch, searchBox, facetList, resultStats, results, pagination } = xpsearch;

  const search = createSearch({
    endpoint: '/api/xpsearch/query',
    index: 'site-content',
    routing: true,
    debounceMs: 150,
  });

  search.addWidgets([
    searchBox({ container: '#search-box', placeholder: 'Search…', showReset: true }),
    facetList({ container: '#facet-content-type', attribute: 'contentType', label: 'Content type' }),
    resultStats({ container: '#search-stats' }),
    results({ container: '#search-results' }),
    pagination({ container: '#search-pagination', padding: 2 }),
  ]);

  search.start();
</script>
```

Through a bundler the widgets are named exports of the package root:

```js
import createSearch, { searchBox, results, facetList, pagination, resultStats, sortSelect } from '@yourco/xperience-search';
```

Every widget takes `container`, which accepts **a CSS selector string or an `HTMLElement`**. The
container is emptied and the widget root is rendered inside it, so a container may hold one widget.
A widget never removes its own root: optional parts are toggled with the `hidden` attribute, which
keeps focus and layout stable.

See the [JavaScript client guide](js-client.md) for `createSearch()` options, routing and the event bus,
and `themes/MARKUP.md` for the full class contract the renderers implement.

---

## Templating helpers

Every `templates` function receives its data first and a helper bag second:
`{ html, highlight, formatNumber }`. The same three are exported from the package root and hang off
the UMD global.

### `html`

A tagged template that returns trusted HTML. **Everything interpolated into it is escaped**, so a
title containing `<script>` is text, not markup.

```js
html`<p class="note">${untrustedText}</p>`
```

- Nest results and arrays of results freely: `` html`<ul>${items.map((i) => html`<li>${i}</li>`)}</ul>` ``.
- `null`, `undefined`, `true` and `false` render as nothing; `0` renders as `0`.
- Attribute values must be quoted — `href="${url}"`, never `href=${url}`. Escaping covers quotes,
  not the absence of them.
- A template that returns a **plain string** has that string escaped like any other value. Return an
  `html` result when you mean markup.

### `html.raw(value)`

The one opt-out. `html.raw('<b>bold</b>')` marks a string as already-safe HTML and inserts it
verbatim. Use it only for markup you produced, never for a value that came from a user or from an
index field you do not control.

### `escapeHtml(value)`

`escapeHtml(value: string): string` escapes `&`, `<`, `>`, `"` and `'`. Exported from the package
root, and the right tool when you are assembling a string rather than a template — a custom widget
assigning `innerHTML`, for instance:

```js
element.innerHTML = `<option value="${escapeHtml(item.value)}">${escapeHtml(item.label)}</option>`;
```

`html` already does this for everything interpolated into it, so a `templates` function does not need
it. Attribute values are the case people miss: a `"` in a taxonomy code name breaks out of
`value="…"` unless it is escaped. See [Custom widgets → Escaping](custom-widgets.md#escaping).

### `highlight(field, result)`

Returns the server's highlighted form of `field` — already HTML-encoded before `<mark>` was inserted
(spec §4.6) — with `class="xps-highlight"` added to each `<mark>`. When the response carried no
highlight for that field, it falls back to `result.attributes[field]`, escaped. Ask for the fields you
want highlighted in the instance options:

```js
createSearch({ index: 'site-content', highlight: { fields: ['title', 'content'], snippetLength: 160 } });
```

### `formatNumber(value, locale?)`

`Intl.NumberFormat` with the page's locale by default: `formatNumber(1234)` → `1,234`.

---

## `searchBox`

```js
searchBox({
  container: '#search-box',
  placeholder: 'Search…',
  label: 'Search this site',
  showLabel: false,
  showReset: true,
  showSubmit: false,
  autofocus: false,
  queryHook: (query, apply) => apply(query.trim()),
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `placeholder` | `'Search…'` | `placeholder` on the input. |
| `label` | `'Search this site'` | Text of the always-rendered `<label>`. |
| `showLabel` | `false` | `false` keeps the label but adds `xps-sr-only`. |
| `showReset` | `true` | The reset button is always in the DOM; it is `hidden` while the query is empty, and permanently `hidden` when this is `false`. |
| `showSubmit` | `false` | `true` renders a submit button. |
| `autofocus` | `false` | Focuses the input on the first render. |
| `queryHook` | — | `(query, apply) => void`. Nothing reaches the state unless you call `apply`. |

Markup (`themes/MARKUP.md` → *searchBox*): `<form class="xps xps-search-box" role="search" novalidate>`
with `xps-search-box__label`, `__field`, `__input`, `__loading`, `__reset` and optionally `__submit`.
The root gains `xps-search-box--stalled` while a request outlives the stall threshold.

Accessibility: `role="search"`, the label is associated by `for`/`id` in every configuration, both
icon buttons carry an `aria-label` with their glyph `aria-hidden`. Typing searches (debounced by the
instance), `Enter` submits, the reset button clears and returns focus to the input. **Re-rendering
never moves the caret**: the input element is created once and its `value` is only assigned when it
differs from the state.

## `results`

```js
results({
  container: '#search-results',
  transformItems: (items) => items,
  templates: {
    item: (result, { html, highlight }) => html`
      <article class="xps-result">
        <div class="xps-result__body">
          <h3 class="xps-result__title">
            <a class="xps-result__link" href="${result.attributes.url}">${highlight('title', result)}</a>
          </h3>
          <p class="xps-result__snippet">${highlight('content', result)}</p>
        </div>
      </article>`,
    empty: ({ query }, { html }) => html`<p>No results for <strong>${query}</strong>.</p>`,
    loading: ({ html }) => html`<div class="xps-skeleton"></div>`,
  },
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `templates.item` | title link + highlighted snippet + content-type meta, and an image when the result has one | `(result, helpers) => Renderable` |
| `templates.empty` | "No results for …" | `({ query }, helpers) => Renderable` |
| `templates.loading` | `loadingRows` skeleton rows | `(helpers) => Renderable` |
| `transformItems` | — | `(results) => results`, applied before rendering. |
| `loadingRows` | `3` | Skeleton rows in the default loading template. |
| `titleAttribute` | `title` | Attribute the default template reads the heading from. Highlights win over the raw value. |
| `urlAttribute` | `url` | Attribute the default template reads the link `href` from. |
| `snippetAttributes` | `['summary', 'content', 'excerpt']` | Tried in order; the first one with a value (highlighted where there is a highlight) becomes the snippet. |

The defaults are the names the server projects every document's base fields under — `title`, `url`,
`contentType` — so the default template renders an Xperience result without configuration. Point them
at your own fields (`titleAttribute: 'ProductFieldName'`) when the content type carries a better one,
and ask for those fields in `fields` so they come back.

Markup: `<div class="xps xps-results">` containing the live region `xps-results__status` and either
`xps-results__list` (an `<ol>` of `xps-results__item`) or `xps-results__empty`. Modifiers `--empty` and
`--loading` (with `aria-busy="true"`) mirror the state.

Accessibility: `xps-results__status` is a `role="status"` element (`aria-live="polite"` by implication)
that is **created once and only has its text replaced**, so a re-render that does not change the
count is not announced twice. `resultStats` renders the same number without a live region, so a page
with both announces once. The default item template emits a real heading and a real link.

Click tracking (spec §9.1): the widget delegates `click` on its root, and any `<a>` inside a result
sends `{ type: 'click', resultId, position, queryId }` to `/api/xpsearch/events`.
`position` is one-based across pages. The call is fire-and-forget and never throws; without a
`queryId` on the last response it is dropped silently.

## `facetList`

```js
facetList({
  container: '#facet-tags',
  attribute: 'tags',
  label: 'Tags',
  operator: 'or',
  limit: 10,
  showMore: true,
  searchable: true,
  sortBy: ['count:desc', 'name:asc'],
  transformItems: (items) => items,
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `attribute` | — | The facet attribute. Required; the widget asks the server to count it. |
| `label` | the attribute name | Heading text, and the facet-search label. |
| `operator` | `'or'` | How selected values combine on the wire. |
| `limit` | `10` | Values shown before "show more". |
| `showMore` | `false` | Renders the show-more button. |
| `showMoreLimit` | `20` | Values shown after "show more". |
| `showMoreLabels` | `{ more: 'Show more', less: 'Show less' }` | Button text. |
| `searchable` | `false` | Adds an input that filters the rendered values **client-side** (see below). |
| `searchablePlaceholder` | `Search in {label}` | Placeholder of that input. |
| `sortBy` | `['isActive', 'count:desc', 'name:asc']` | Applied left to right. |
| `transformItems` | — | `(items) => items`, after sorting and capping. |

Markup: `<div class="xps xps-facet-list">` (plus `--searchable`) with `__title`, the optional
`__search` block, `__list` (a `<ul>` labelled by the title), one `__item` per value — modifiers
`--selected` and `--disabled` — and the `__show-more` button. When the facet search matches nothing,
the list is `hidden` and `__no-results` is shown.

Accessibility: real `<input type="checkbox">` elements inside `<label>` (§5.6), so the whole row is
the click target and no `for`/`id` pairing is needed. The show-more button carries `aria-expanded` and
is `disabled` rather than removed, so focus survives. A value that drops to count 0 renders `disabled`
rather than disappearing.

The visible text of each value is the server's `label` — the taxonomy tag title — while the value sent
back in `filters.facets` is its code name, so a facet list never displays an internal identifier.

**`searchable` filters the values already returned for this facet**, in the browser. There is no
facet-search endpoint in the JSON contract, so it cannot reach values beyond `limit`/`showMoreLimit`.

## `pagination`

```js
pagination({ container: '#search-pagination', padding: 2, showFirst: true, showLast: true });
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `padding` | `3` | Numbered pages either side of the current one. |
| `showFirst` / `showLast` | `true` | The « and » controls. |
| `maxPages` | — | Caps `totalPages` for indexes where deep paging is unwanted. |
| `labels` | `{ first: 'First page', previous: 'Previous page', next: 'Next page', last: 'Last page' }` | Screen-reader names of the four end controls. |

Markup: `<nav class="xps xps-pagination" aria-label="Search results pages">` with
`xps-pagination__list`, one `__item` per control carrying exactly one kind modifier (`--first`,
`--previous`, `--page`, `--current`, `--ellipsis`, `--next`, `--last`, plus `--disabled` on the ends).
The whole root is `hidden` when there is one page or none.

Accessibility: every enabled control is a real `<a href>` whose target is `urlFor(page)` — the
same URL routing would produce — so results pages are crawlable and open in a new tab; a plain left
click is intercepted and pages instead. A disabled end control is a `<span aria-disabled="true">`
with no `href`, so a keyboard user never lands on a dead control. The current page carries
`aria-current="page"`, and each control's glyph is `aria-hidden` beside an `xps-sr-only` name.

## `resultStats`

```js
resultStats({
  container: '#search-stats',
  templates: {
    text: (data, { html, formatNumber }) => html`${formatNumber(data.total)} matches`,
  },
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `templates.text` | `46 results in 14 ms` | `(data, helpers) => Renderable`. `data` is the render state: `total`, `tookMs`, `query`, `page`, `totalPages`, `pageSize`, `hasResults`. |
| `textTemplate` | — | A plain string with `{total}`, `{tookMs}`, `{query}`, `{page}` and `{totalPages}` placeholders, for callers that cannot pass a function — the Page Builder stats widget's **Text template** property. The template and every substituted value are escaped, so markup in it is shown rather than rendered. Numbers use `formatNumber`. `templates.text` wins when both are given. |
| `emptyText` | `'Type to search.'` | Shown before the first response. |

```js
resultStats({
  container: '#search-stats',
  textTemplate: '{total} results for "{query}" — page {page} of {totalPages}',
});
```

Markup: `<div class="xps xps-result-stats">` (plus `--empty`) containing `xps-result-stats__text`, and
in the default template `xps-result-stats__time` around the timing. **Not** a live region — `results`
owns the announcement.

## `sortSelect`

```js
sortSelect({
  container: '#search-sort',
  label: 'Sort by',
  items: [
    { label: 'Relevance', value: 'relevance' },
    { label: 'Newest first', value: 'newest' },
  ],
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `items` | — | `{ label, value }[]`. `value` is `relevance`, a sort key configured for the index, or a sortable attribute suffixed `_asc` / `_desc`. Required. |
| `label` | `'Sort by'` | Label text. |
| `hideLabel` | `false` | Adds `xps-sr-only` to the label; it stays associated. |

Markup: `<div class="xps xps-sort-select xps-select">` — the widget adds only its identity class and
renders the shared `xps-select` block, so it is the same `xps-select__label` (`<label for>`) and
`xps-select__control` (a native `<select name="sort">`) a custom widget renders. The select is built
once and only its `value` is patched, so changing the sort does not destroy the element you are
using.

## `clearFilters`

```js
clearFilters({ container: '#search-clear', label: 'Clear filters' });
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `label` | `'Clear filters'` | Button text. |
| `includedAttributes` / `excludedAttributes` | — | Which filters count towards "is there anything to clear". |

Markup: `<div class="xps xps-clear-filters">` (plus `--disabled`) with
`xps-clear-filters__button`. The button is `disabled` — never removed — when nothing is filtered,
so pressing it repeatedly does not throw focus to the body. It clears facet *and* numeric filters; it
does not clear the query.

## `activeFilters`

```js
activeFilters({
  container: '#search-current',
  attributeLabels: { contentType: 'Content type', tags: 'Tag' },
});
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `attributeLabels` | — | Display name per attribute. Falls back to the raw attribute name. |
| `title` | `'Active filters'` | Screen-reader-only heading that labels the list. |
| `includedAttributes` / `excludedAttributes` | — | Which filters to show. |
| `transformItems` | — | `(items) => items`. |

Markup: `<div class="xps xps-active-filters">` (plus `--empty`) with an `xps-sr-only` `__title`,
`__list` and one `__item` per filter, each holding an `xps-chip` with `xps-chip__attribute`,
`xps-chip__label` and `xps-chip__remove`.

Accessibility: the remove button's name is the whole sentence —
`aria-label="Remove filter Content type: Article"` — never a bare "×". The empty list is rendered
rather than removed, so the layout does not jump. Numeric filters read as `price lte 50`.

## `toggleFilter`

```js
toggleFilter({ container: '#toggle-english', attribute: 'language', value: 'en', label: 'English only' });
```

| Option | Default | What it does |
|---|---|---|
| `container` | — | Selector or element. Required. |
| `attribute` | — | The facet attribute. Required. |
| `value` | `'true'` | The single value the checkbox filters on. |
| `label` | the attribute name | Visible text. |
| `showCount` | `true` | `false` hides the count element. |

Markup: `<div class="xps xps-toggle-filter">` (plus `--disabled`) with `__label` wrapping
`__checkbox`, `__value` and `__count`. A real checkbox, disabled when no document carries the value
and it is not already selected.

---

## The XSS model

Server-side, highlight tags are inserted into content that was HTML-encoded first (spec §4.6), so
`result.highlights` is safe to insert. Client-side, that is the *only* markup the widgets trust
automatically:

| Source | Treated as |
|---|---|
| Anything interpolated into `` html`…` `` | Escaped text |
| A plain string returned by a template | Escaped text |
| A nested `html` result, or an array of them | Trusted markup |
| `highlight(field, result)` | Trusted markup (the server's `highlights[field]`), else the escaped `attributes[field]` |
| `html.raw(value)` | Trusted markup — your explicit decision |

If you assemble markup yourself, quote your attribute values and keep untrusted values inside an
interpolation. `render(result, container)` performs exactly one `innerHTML` assignment; there is no
virtual DOM and no sanitizer downstream of you.

A **custom widget** is outside that table: it owns its own DOM writes, so nothing is escaped for it
automatically. Use `` html`…` `` and it inherits the same guarantees; use a string and `innerHTML`
and every interpolation is yours to `escapeHtml`. The untrusted values are facet labels and values
(index content) and anything an editor typed into a widget dialog, which arrives through
`data-xps-config` already attribute-decoded. `textContent` and `setAttribute` are always safe.

## Page Builder mounts

All nine widgets resolve by name from a `.xps-mount` element, so the Page Builder widgets (spec §7.1)
need no JavaScript of their own:

```html
<div class="xps-mount"
     data-xps-widget="facetList"
     data-xps-instance="search-1"
     data-xps-instance-config='{"index":"site-content","routing":true}'
     data-xps-config='{"attribute":"contentType","limit":10,"showMore":true}'></div>
```

`data-xps-config` is the widget's options object minus `container`, which is the mount element.
`registerWidgetType('searchBox', …)` overrides a built-in of the same name; custom types must
contain a dot. See [Building a custom widget](custom-widgets.md) and
[Page Builder widgets](page-builder-widgets.md).

## Not in this release

`suggestions`, `rangeFilter`, `categoryTree` and `loadMore` have markup contracts and reserved names,
and range has a behaviour, but none of them has a default renderer yet. `withRange` is public today —
see [Building a custom widget](custom-widgets.md) — and the remaining behaviours follow the open
decisions recorded in `docs/internal/KNOWN-LIMITATIONS.md`.
