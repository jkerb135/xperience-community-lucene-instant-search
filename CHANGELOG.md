# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public connector API (spec §5.7) or the JSON contract
(spec §4.2) are always major-version events.

## [Unreleased]

- Added: the nine default widgets (spec §5.3) — `searchBox`, `hits`, `refinementList`, `pagination`,
  `stats`, `sortBy`, `clearRefinements`, `currentRefinements`, `toggleRefinement` — each a connector
  plus a renderer over the public API, the escaping-by-default `html`/`highlight`/`formatNumber`
  template helpers, click tracking in `hits`, an axe-core gate in CI, and a demo page on
  `npm run demo`; core plus all nine is 12.3 KB gzip against the 20 KB budget (ADR-0009).
- Added: the theme layer (spec §6) — `themes/src/shell.css` (structure only) and
  `themes/src/default.css` (opt-in, CSS-variable driven), the frozen `xps-` markup contract in
  `themes/MARKUP.md` with a fixture per widget, the three-way verification page in `themes/test/`,
  and `npm run check` to keep the stylesheets, fixtures and contract honest.
- Added: the JavaScript client core — `xpsearch()` with the widget lifecycle and per-widget error
  isolation, `SearchState`, `SearchClient` (debounce, cancellation, retry, analytics), `SearchHelper`,
  URL routing, the `render`/`error`/`stateChange` event bus, eight connectors, the `.xps-mount`
  bootstrap with `registerWidgetType`, and ESM/UMD bundles under a gzip budget (ADR-0007).

- Added: the JSON search contract is frozen — `contract/xpsearch-api.schema.json` generates the C#
  (`XpSearch.Core.Contract`) and TypeScript (`@yourco/xperience-search`) types for `/api/xpsearch/query`,
  `/suggest` and `/events`, versioned by the `X-XpSearch-Api-Version` response header (ADR-0006).
- Added: `XpSearch.Core` serves the contract — an ordered, injectable query pipeline behind
  `POST /api/xpsearch/query`, `/suggest` and `/events`, with taxonomy facets and drill-sideways counts,
  facet and numeric filters, sorting, XSS-safe highlighting, a short-TTL response cache invalidated on
  index writes, and `XpSearchIndexingStrategy`, which binds Xperience taxonomies as facets with no
  per-content-type code (spec §4, ADR-0008).
