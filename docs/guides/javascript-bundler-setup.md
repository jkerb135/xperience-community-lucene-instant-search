## JavaScript bundler setup

The recommended way to put Xperience Search on a page: install the npm package, import the two or
three widgets you actually use, and let your bundler ship them. You get tree-shaking, your own
minification and cache-busting, TypeScript types, and SCSS sources you can configure at build time.

No build pipeline? The [`<xps-search-assets />` tag helper](page-builder-widgets.md#static-assets)
serves a prebuilt UMD bundle and the two stylesheets straight from the NuGet package — nothing to
install, nothing to compile. Start there, come back here when you outgrow it.

### Install

```bash
npm install @xperience-community/xperience-search
```

The package is ESM with per-widget subpath exports and TypeScript declarations for every one of
them. It has no runtime dependencies.

| Subpath | What is in it |
|---|---|
| `@xperience-community/xperience-search` | `createSearch`, `mountAll`, `registerWidgetType`, the templates helper, the contract types — and every widget, for convenience |
| `.../widgets` | all thirteen widget factories, nothing else |
| `.../widgets/search-box`, `.../widgets/results`, `.../widgets/facet-list`, `.../widgets/category-tree`, `.../widgets/sort-select`, `.../widgets/result-stats`, `.../widgets/toggle-filter`, `.../widgets/range-filter`, `.../widgets/load-more`, `.../widgets/pagination`, `.../widgets/suggestions`, `.../widgets/active-filters` | one widget each (`clearFilters` ships with `active-filters`) |
| `.../behaviors` | the headless behaviours, for widgets you render yourself |
| `.../scss/*` | SCSS sources: `scss/shell`, `scss/default`, `scss/base`, `scss/widgets/<name>` |
| `.../themes/shell.css`, `.../themes/default.css` | the two compiled stylesheets, identical to the ones the tag helper serves |
| `.../styles/base.css`, `.../styles/widgets/<name>.css` | compiled per-widget CSS, for pipelines that cannot compile SCSS |

Importing a widget from the root entry tree-shakes just as well as importing its subpath — the
subpaths exist so that a bundler with no tree-shaking, or a browser importing modules directly,
can be precise too.

### A working search page

```js
// search.js
import { createSearch } from '@xperience-community/xperience-search';
import { searchBox } from '@xperience-community/xperience-search/widgets/search-box';
import { results } from '@xperience-community/xperience-search/widgets/results';

const search = createSearch({
  index: 'site-content',
  endpoint: '/api/xpsearch/query', // the default; the site serves it
  routing: true,
});

search.addWidgets([
  searchBox({ container: '#search-box', placeholder: 'Search…' }),
  results({ container: '#search-results' }),
]);

search.start();
```

```scss
// search.scss — the structure and theme for the two widgets on this page, and nothing else
@use '@xperience-community/xperience-search/scss/base';
@use '@xperience-community/xperience-search/scss/widgets/search-box';
@use '@xperience-community/xperience-search/scss/widgets/results';
```

```html
<div class="xps" id="search-box"></div>
<div class="xps" id="search-results"></div>
<script type="module" src="/search.js"></script>
```

With Vite that is the whole configuration — no plugin, no alias. `npm install -D sass` if you use
the SCSS entry; import `@xperience-community/xperience-search/styles/base.css` and
`.../styles/widgets/results.css` instead if you would rather not.

The full stylesheets are still one import away when you want every widget styled at once:

```js
import '@xperience-community/xperience-search/themes/shell.css';   // structure
import '@xperience-community/xperience-search/themes/default.css'; // the opt-in theme
```

### Build-time theming with SCSS

Every `--xps-*` custom property is still emitted, so [runtime theming](theming.md) works exactly as
it does on the tag-helper path. SCSS variables only change the *default* those properties are
emitted with, which is what you want when the values are known at build time:

```scss
@use '@xperience-community/xperience-search/scss/shell' with ($space: 1rem);
@use '@xperience-community/xperience-search/scss/default' with (
  $color-accent: #b8005c,
  $radius: 0,
  $font: (system-ui, sans-serif)
);
```

`scss/shell` takes `$space`, `$control-min-height`, `$focus-width`, `$focus-offset`.
`scss/default` takes `$color-accent`, `$color-text`, `$color-muted`, `$color-surface`,
`$color-border`, `$radius`, `$space`, `$font`, and the five `$dark-color-*` values used by
`data-xps-theme="auto"`.

### Page Builder mounts

If editors place the [Page Builder widgets](page-builder-widgets.md), the server emits `.xps-mount`
elements that a runtime has to hydrate. Tell the bootstrap which widgets you bundled:

```js
import { mountAll } from '@xperience-community/xperience-search';
import { searchBox } from '@xperience-community/xperience-search/widgets/search-box';
import { results } from '@xperience-community/xperience-search/widgets/results';
import { facetList } from '@xperience-community/xperience-search/widgets/facet-list';

mountAll(document, { widgets: { searchBox, results, facetList } });
```

Only the UMD bundle registers all thirteen widgets by itself; an ESM consumer says what it wants, so
that a page with a search box does not download a category tree. A `data-xps-widget` you did not
pass in is a console error and a skipped mount — the rest of the page keeps working.

To register the whole set anyway:

```js
import { DEFAULT_WIDGETS } from '@xperience-community/xperience-search/widgets';
mountAll(document, { widgets: DEFAULT_WIDGETS });
```

### One page, one runtime

A page runs **either** the tag helper's bundle **or** yours — never both. Two runtimes on one page
means two `xpsearch` copies, two mount passes and two searches per keystroke.

- Bundling it yourself? Do not emit `<xps-search-assets />` in that layout. Import the stylesheets
  (or SCSS) through your build as shown above.
- Using the tag helper? Then you do not need the npm package at all.

Page Builder mounts hydrate from whichever runtime is present, so editors are unaffected by the
choice — you can switch a site from one to the other without touching a single page.

### Custom widgets

A custom widget is a function that returns a `Widget`; `registerWidgetType` makes it resolvable by
`data-xps-widget` so editors can place it in the Page Builder. Third-party identifiers must contain
a dot — bare names are reserved for the built-ins.

```js
import { escapeHtml, mountAll, readMountConfig, registerWidgetType } from '@xperience-community/xperience-search';
import { withFacetList } from '@xperience-community/xperience-search/behaviors';

// A behaviour does the state, the request and the lifecycle; you only render.
const ratingFilter = withFacetList(({ items, apply, params }) => {
  params.container.innerHTML = items
    .map((item) => `<button data-value="${escapeHtml(item.value)}" aria-pressed="${item.isActive}">${escapeHtml(item.label)} (${item.count})</button>`)
    .join('');
  params.container.querySelectorAll('button').forEach((button) => {
    button.addEventListener('click', () => apply(button.dataset.value));
  });
});

// `config` is editor-supplied JSON, so readMountConfig narrows it at the trust boundary.
registerWidgetType('myCompany.ratingFilter', (config) =>
  ratingFilter({ container: config.container, ...readMountConfig(config, { attribute: 'string' }) })
);

mountAll(); // registered types resolve without being listed in `widgets`
```

A factory registered this way wins over a widget of the same name passed to `mountAll`, which is how
`registerWidgetType('results', myResults)` replaces the built-in renderer. See
[custom widgets](custom-widgets.md) for the behaviours available.

### Version pairing

**The npm package version must match the version of `XperienceCommunity.Search.Core` installed on
the site.** Both halves implement one wire contract (`X-XpSearch-Api-Version`); a client that is a
release ahead of the server will be rejected by the endpoint with a version error rather than
silently misbehaving.

| npm `@xperience-community/xperience-search` | NuGet `XperienceCommunity.Search.*` | Contract API version |
|---|---|---|
| 0.1.0 | 0.1.0 | 1 |

Keep the two in lockstep in one commit: bump the NuGet package reference and
`npm install @xperience-community/xperience-search@<same version>` together. The table above is the
pairing table — every release adds a row.

### Trying it locally

The package ships the mock search server the library's own tests use, so the front end can be
developed before the Xperience site exists:

```bash
npx xpsearch-mock            # serves /api/xpsearch/query, /suggest and /events on :3131
```

It sends no CORS headers, so proxy it through the dev server rather than pointing `endpoint` at
another origin — then the same site-relative `endpoint` works in development and in production:

```js
// vite.config.js
export default {
  server: { proxy: { '/api/xpsearch': 'http://localhost:3131' } },
};
```
