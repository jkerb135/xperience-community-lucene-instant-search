# ADR-0007: JavaScript client architecture

- **Status:** accepted; amended by ADR-0010 (API names and result shape change — see the owned-contract amendment)
- **Date:** 2026-08-21
- **Spec reference:** §5.1, §5.2, §5.5, §5.7, §5.9

## Context

`@yourco/xperience-search` has to ship as ESM and UMD under 20 KB gzipped for the core *and* the six
default widgets, expose a connector API that third parties depend on (semver-major to break), and keep
its public surface free of `any`. Four decisions were forced along the way, and each of them constrains
the widget unit that builds on this one.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Toolchain:** rollup + `@rollup/plugin-typescript` | The conventional setup | `typescript@7.0.2` (the native port) exposes no JS compiler API, so the plugin cannot run |
| **Toolchain:** esbuild alone | One dependency, fastest | No UMD output — only `iife`, which is not what a `<script>`-tag consumer with AMD/CJS expects |
| **Toolchain:** rollup + a 40-line inline esbuild plugin | Real UMD, two dependencies, no babel | The plugin owns extension resolution and minification itself |
| **Numeric route params:** `price=<=50` | One param per attribute | `%3C%3D` in every shared link, and ambiguous to parse |
| **Numeric route params:** `price_lte=50` | Readable, crawlable, unambiguous | A facet attribute literally named `price_lte` cannot be routed |
| **Helper:** keep exactly the §5.7 members | Smallest possible contract | `connectRange` cannot replace a bound, `connectRefinementList` cannot declare `and`/`or`, nothing can read state |

## Decision

**Toolchain.** rollup for the module graph and the UMD wrapper, with an inline plugin that calls
`esbuild.transformSync` to strip types and to minify (`rollup.config.mjs`, ~40 lines). Declarations
come from `tsc -p tsconfig.build.json`. No babel, no `@rollup/plugin-node-resolve` — the plugin's
`resolveId` handles the extensionless relative imports the source uses, and there are no runtime
dependencies to resolve. `npm run size` gzips the output with `node:zlib` against `size-limit.json`
(no `size-limit` package): 10 KB for the ESM core, 12 KB for UMD.

**Routing.** `q`, one-based `page` (state stays zero-based), `sort` (omitted when `relevance`), one
comma-joined param per facet attribute with each value `encodeURIComponent`-escaped so commas
round-trip, and `<attribute>_<lt|lte|eq|gte|gt>` for numeric refinements. Defaults are omitted;
unknown params are preserved; a query-only change replaces the history entry and everything else
pushes. `createURL()` uses the same mapping whether or not routing is enabled, so connector links are
always crawlable.

**Retry.** Up to 2 retries (configurable) with 200ms doubling backoff, on network errors, `429` and
`5xx`. Never on another `4xx` — a malformed request will not fix itself. An aborted request is never
retried. A contract-version mismatch (`X-XpSearch-Api-Version`) is not an exception: it is reported
once per version on the `error` event with `phase: 'contract'`, and the response is used anyway,
because a minor drift must not blank the page.

**`Hit<TItem>`.** `export type Hit<TItem extends Record<string, unknown> = Record<string, unknown>> =
WireHit & TItem`. The generated contract type stays the single source of the reserved members
(`objectID`, `_score`, `_highlights`, `_rankingInfo`) and its index signature; intersecting it with the
caller's document shape gives `hit.title` a real type without redeclaring the wire shape and without an
`any`. `SearchResults<TItem>` is `Omit<SearchResponse, 'hits'>` with typed hits, for the same reason.

**`SearchHelper` beyond §5.7.** Five members were added, because the connectors could not be written
without them. They are proposed additions to the published SDK contract:

| Member | Why |
|---|---|
| `getState()` | Widgets need to read state outside a render pass; the alternative is exposing the store. |
| `setNumericRefinement(attr, op, value)` | `connectRange` must *replace* a bound; `addNumericRefinement` would accumulate `price>=10, price>=20`. |
| `removeNumericRefinement(attr, op?)` | `connectRange` and `connectCurrentRefinements` must remove one bound without clearing the attribute. |
| `setHitsPerPage(n)` | `infiniteHits` and a page-size control need it, and it is part of the state already. |
| `setFacetOperator(attribute, 'and' \| 'or')` | The outer-AND/inner-OR mapping is per attribute, and only the widget knows which it wants. It is configuration, not state: it never reaches the URL. |

`clearRefinements(attribute)` clears both the facet values and the numeric refinements of that
attribute; that is the only reading that makes "clear this control" work.

Widgets also get one lifecycle hook beyond §5.7: `getRequestParameters(request)`, applied after
`getSearchParameters(state)`, for request fields that are not state (`facets`, `highlight`,
`attributesToRetrieve`). Without it a refinement list cannot ask for its own facet counts.

## Evidence

- `npm run size` on the shipped bundles: ESM 7 879 B gzip against a 10 240 B budget (core, connectors
  and the shared chunk), UMD 7 344 B against 12 288 B. That leaves ~12 KB of the 20 KB spec budget for
  the six default widgets.
- 72 tests (`npm test`) covering wire serialization, debounce and stale-response suppression, the retry
  matrix, version mismatch, lifecycle order, error isolation, multi-instance, routing round-trips and
  `popstate`, every connector's render state on first and later renders, the mount bootstrap, and an
  end-to-end run against the mock server.
- The documented dropdown-facet example is compiled and executed as a test
  (`src/connectors/dropdown-example.test.ts`) with no `any` and no internal imports, per spec §12.

## Consequences

- **Easy:** shipping the six default widgets — they are `connector + renderer` with the API proven by
  the connector tests; adding a connector (`connectAutocomplete`, `connectHierarchicalMenu`) without
  touching the core; running the docs' examples, because the mock server is contract-typed.
- **Expensive:** changing `SearchHelper` or `RenderOptions` later — that is a major version. Replacing
  the toolchain if rollup's UMD output ever stops being needed.
- **Foreclosed:** nothing in the transport assumes Lucene, so a second backend behind the same JSON
  contract needs no client change. The framework adapters (§5.7) stay possible: they are connectors
  wrapped in hooks, and the connectors are framework-free.
