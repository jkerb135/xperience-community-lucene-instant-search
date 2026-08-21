# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public behaviour API (spec §5.7) or the JSON contract
(spec §4.2, as amended by ADR-0010) are always major-version events.

## [Unreleased]

- **Added:** the Page Builder widgets (spec §7) in `YourCo.Xperience.Search.Widgets` — seven
  view-component widgets (`XpSearch.SearchBox`, `.Results`, `.FacetList`, `.Pagination`, `.ResultStats`,
  `.SortSelect`, `.Suggestions`) that each render one configured `.xps-mount` element and self-assemble
  by `data-xps-instance`, the `XpSearchMountWidgetViewComponent<TProperties>` base class and
  `XpSearchMountWidgetProperties` (`Index`, `InstanceId` = `"default"`) third parties subclass, the
  `IXpSearchMountRenderer` seam, the editor-only unconfigured instruction block (invisible on a live
  page), `[RegisterSearchResultTemplate]` with `ISearchResultTemplateRegistry` behind the Results
  widget's template drop-down, sort-option validation against `XpSearchIndexOptions.SortKeys` and the
  `_asc`/`_desc` convention, `services.AddXpSearchWidgets()`, and `<xps-search-assets />` /
  `@Html.XpSearchAssets()` serving the bundle and both stylesheets as Razor Class Library static web
  assets under `_content/YourCo.Xperience.Search.Widgets/xpsearch/` (ADR-0012).
- **Added:** `FacetAttributeConfigurator` in `YourCo.Xperience.Search.Admin`, registered as
  `xpsearch.facetAttribute`, which fills a facet widget's attribute drop-down from the selected index's
  facetable schema fields and hides it until an index is chosen (spec §7.4). It is referenced by string
  identifier, so widget properties stay free of a dependency on `Kentico.Xperience.Admin`, and the pieces
  it shares with the widgets — `XpSearch.Core.XpSearchConstants` and
  `XpSearch.Core.Facets.FacetAttributeOptions` — live in Core, so `Admin` and `Widgets` stay independent
  of each other (spec §2.2).
- **Changed:** `mountAll()` now **merges** the `data-xps-instance-config` objects of every mount in a
  `data-xps-instance` group instead of using the first one that names an `index`. The first definition of
  a key wins and a mount that disagrees logs one `console.warn` naming the key and the instance, so the
  results widget's page size and retrieved fields apply wherever an editor placed it. Markup whose mounts
  already agreed is unaffected.
- **Added:** `resultStats` takes a string `textTemplate` with `{total}`, `{tookMs}`, `{query}`, `{page}`
  and `{totalPages}` placeholders (template and values escaped; `templates.text` still wins), which is
  what the Page Builder stats widget's **Text template** property emits.
- **Added:** `docs/guides/page-builder-widgets.md`, the C# half of `docs/guides/custom-widgets.md`, and
  ADR-0012.

- **Changed (breaking):** the wire contract and the JavaScript API are owned by this product rather
  than modelled on Algolia and InstantSearch (ADR-0010). `SearchRequest` takes a one-based `page`,
  `pageSize`, `fields` and structured `filters` (`{ facets: [{ attribute, values, operator }],
  numeric: [{ attribute, operator, value }] }`); `SearchResponse` carries `results[]` of closed
  `Result { id, score, attributes, highlights, ranking }`, `facets` as ordered `FacetValue[]` arrays
  that include the taxonomy tag title as `label`, and `total` / `totalPages` / `tookMs`. `/suggest`
  takes `limit` and answers with `Suggestion.result`; `/events` takes `{ type, resultId, queryId,
  position }`. Validation errors are keyed by JSON path. In JavaScript, `createSearch()` replaces
  `xpsearch()`, `SearchActions` replaces the helper, widget hooks are `prepareState`/`prepareRequest`,
  render options are `{ params, results, state, actions, search }`, the second entry point is
  `./behaviors` with `with*` factories and the verbs `apply` / `urlFor` / `isActive` / `canApply` /
  `isStalled`, and the widgets and their classes are `results`, `facetList`, `resultStats`,
  `sortSelect`, `clearFilters`, `activeFilters` and `toggleFilter`. The routes, the
  `X-XpSearch-Api-Version: 1` header and the Problem Details error shape are unchanged.
- **Added:** `XpSearchIndexOptions.SortKeys` — a per-index map from a request's `sort` value to a field
  and a direction, alongside the existing `_asc` / `_desc` suffix convention.
- **Added:** `docs/guides/migrating-from-algolia.md`, generated from `contract/algolia-map.json` by
  `npm run docs:migration` and kept honest by `npm run docs:check`.

- Fixed: fields a content type inherits from a reusable field schema are detected, so a taxonomy that
  reaches a type only through a schema (`ProductFieldTags` and `ProductFieldCategory` on Dancing Goat's
  products) becomes a facet like any other. A name defined by both the content type and one of its
  schemas keeps the content type's field and logs a warning.

- Added: the nine default widgets (spec §5.3) — `searchBox`, `results`, `facetList`, `pagination`,
  `resultStats`, `sortSelect`, `clearFilters`, `activeFilters`, `toggleFilter` — each a behaviour
  plus a renderer over the public API, the escaping-by-default `html`/`highlight`/`formatNumber`
  template helpers, click tracking in `results`, an axe-core gate in CI, and a demo page on
  `npm run demo`; core plus all nine is 12.3 KB gzip against the 20 KB budget (ADR-0009).
- Added: the theme layer (spec §6) — `themes/src/shell.css` (structure only) and
  `themes/src/default.css` (opt-in, CSS-variable driven), the frozen `xps-` markup contract in
  `themes/MARKUP.md` with a fixture per widget, the three-way verification page in `themes/test/`,
  and `npm run check` to keep the stylesheets, fixtures and contract honest.
- Added: the JavaScript client core — `createSearch()` with the widget lifecycle and per-widget error
  isolation, `SearchState`, `SearchClient` (debounce, cancellation, retry, analytics), `SearchActions`,
  URL routing, the `render`/`error`/`stateChange` event bus, eight behaviours, the `.xps-mount`
  bootstrap with `registerWidgetType`, and ESM/UMD bundles under a gzip budget (ADR-0007).

- Added: the JSON search contract is owned and frozen — `contract/xpsearch-api.schema.json` generates the C#
  (`XpSearch.Core.Contract`) and TypeScript (`@yourco/xperience-search`) types for `/api/xpsearch/query`,
  `/suggest` and `/events`, versioned by the `X-XpSearch-Api-Version` response header (ADR-0006).
- Added: `XpSearch.Core` serves the contract — an ordered, injectable query pipeline behind
  `POST /api/xpsearch/query`, `/suggest` and `/events`, with taxonomy facets and drill-sideways counts,
  structured facet and numeric filters, sorting, XSS-safe highlighting, a short-TTL response cache invalidated on
  index writes, and `XpSearchIndexingStrategy`, which binds Xperience taxonomies as facets with no
  per-content-type code (spec §4, ADR-0008).
