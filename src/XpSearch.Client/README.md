# @yourco/xperience-search

The JavaScript client and default widgets for Xperience Search: a search instance, nine widgets,
the behaviour API custom widgets are built on, and the `.xps-mount` bootstrap that hydrates the
Page Builder widgets.

```bash
npm install @yourco/xperience-search
```

## What the package ships

| Entry point | What it is |
|---|---|
| `@yourco/xperience-search` | `createSearch()`, the widgets, the templating helpers (`html`, `escapeHtml`, `highlight`), `registerWidgetType` / `mountAll` / `readMountConfig` / `widgetId`. |
| `@yourco/xperience-search/behaviors` | `withSearchBox`, `withResults`, `withFacetList`, `withPagination`, `withResultStats`, `withSortSelect`, `withActiveFilters`, `withRange` — the mechanics behind a custom widget. |
| `@yourco/xperience-search/themes/shell.css`, `.../themes/default.css` | The two stylesheets. `shell.css` is structure (layout, focus rings, screen-reader utilities); `default.css` is the opt-in visual theme. Load shell first. |

```html
<link rel="stylesheet" href="/node_modules/@yourco/xperience-search/themes/shell.css">
<link rel="stylesheet" href="/node_modules/@yourco/xperience-search/themes/default.css">
```

The package is ESM plus a UMD bundle (`dist/xpsearch.umd.js`, global `xpsearch`), fully typed, and
has no runtime dependencies.

## The mock server

A dependency-free mock of the search API, so you can build UI before the endpoint exists:

```bash
npx xpsearch-mock                                              # http://127.0.0.1:3131
node node_modules/@yourco/xperience-search/mock/server.mjs     # the same thing
PORT=4000 npx xpsearch-mock
```

## Guides

- **Quick start** — install, host setup, the first search page.
- **JavaScript client** — options, actions, routing, the event bus, the mock server.
- **Custom widgets** — the behaviour API, the worked dropdown-facet example, Page Builder.
- **Theming** — the two stylesheets, the variables, the markup contract.
- **Search API** — the JSON contract.

They live in `docs/guides/` in the repository and are published as the project wiki.

## Note for contributors

Scripts prefixed `repo:` (`repo:mock`, `repo:demo`) run only inside the repository — they reference
`mock/server.ts`, `demo/` and `../../themes`, none of which are in the tarball. From the package,
use `npx xpsearch-mock`.
