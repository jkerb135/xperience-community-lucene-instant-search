# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public connector API (spec §5.7) or the JSON contract
(spec §4.2) are always major-version events.

## [Unreleased]

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
