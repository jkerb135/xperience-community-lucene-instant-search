# @xperience-community/xperience-search

The JavaScript client and default widgets for Xperience Search: a search instance, thirteen widgets,
the behaviour API custom widgets are built on, and the `.xps-mount` bootstrap that hydrates the
Page Builder widgets.

```bash
npm install @xperience-community/xperience-search
```

## What the package ships

| Entry point | What it is |
|---|---|
| `@xperience-community/xperience-search` | `createSearch()`, the widgets, the templating helpers (`html`, `escapeHtml`, `highlight`), `registerWidgetType` / `mountAll` / `readMountConfig` / `widgetId`. |
| `@xperience-community/xperience-search/widgets` | All thirteen widget factories and their types, without the rest of the entry point. |
| `@xperience-community/xperience-search/widgets/<name>` | One widget each: `search-box`, `results`, `facet-list`, `category-tree`, `sort-select`, `result-stats`, `toggle-filter`, `range-filter`, `load-more`, `pagination`, `suggestions`, `active-filters` (which carries `clearFilters`). |
| `@xperience-community/xperience-search/behaviors` | `withSearchBox`, `withResults`, `withFacetList`, `withPagination`, `withResultStats`, `withSortSelect`, `withActiveFilters`, `withRange` — the mechanics behind a custom widget. |
| `@xperience-community/xperience-search/themes/shell.css`, `.../themes/default.css` | The two stylesheets. `shell.css` is structure (layout, focus rings, screen-reader utilities); `default.css` is the opt-in visual theme. Load shell first. |
| `@xperience-community/xperience-search/scss/*` | The SCSS sources: `scss/shell`, `scss/default`, `scss/base` and `scss/widgets/<name>`. `@use … with (…)` configures the default values of the `--xps-*` custom properties. |
| `@xperience-community/xperience-search/styles/*` | The same à la carte layer compiled: `styles/base.css` plus `styles/widgets/<name>.css`. |

```html
<link rel="stylesheet" href="/node_modules/@xperience-community/xperience-search/themes/shell.css">
<link rel="stylesheet" href="/node_modules/@xperience-community/xperience-search/themes/default.css">
```

The package is ESM plus a UMD bundle (`dist/xpsearch.umd.js`, global `xpsearch`), fully typed, and
has no runtime dependencies. Importing `createSearch` and one widget bundles that widget and nothing
else; only the UMD bundle registers all thirteen for the `.xps-mount` bootstrap, an ESM consumer
passes what it bundled to `mountAll(root, { widgets })`.

## The mock server

A dependency-free mock of the search API, so you can build UI before the endpoint exists:

```bash
npx xpsearch-mock                                              # http://127.0.0.1:3131
node node_modules/@xperience-community/xperience-search/mock/server.mjs     # the same thing
PORT=4000 npx xpsearch-mock
```

## Guides

- **Quick start** — install, host setup, the first search page.
- **JavaScript bundler setup** — npm install, subpath imports, SCSS, version pairing.
- **JavaScript client** — options, actions, routing, the event bus, the mock server.
- **Custom widgets** — the behaviour API, the worked dropdown-facet example, Page Builder.
- **Theming** — the two stylesheets, the variables, the markup contract.
- **Search API** — the JSON contract.

They live in `docs/guides/` in the repository and are published as the project wiki.

## Note for contributors

Scripts prefixed `repo:` (`repo:mock`, `repo:demo`) run only inside the repository — they reference
`mock/server.ts`, `demo/` and `../../../themes`, none of which are in the tarball. From the package,
use `npx xpsearch-mock`.
