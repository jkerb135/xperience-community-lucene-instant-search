# Unit PK-1 — npm-first client packaging: granular subpath exports + SCSS themes

Owner decision 2026-09-01: bundler ingestion (Vite/webpack/rollup) of
`@xperience-community/xperience-search` is the PRIMARY distribution path; the
`<xps-search-assets />` tag helper + prebuilt UMD bundle is the no-build quick-start fallback.
This unit makes the npm package genuinely consumable that way: per-widget JS entry points that
tree-shake, SCSS theme sources alongside compiled CSS, and docs restructured npm-first.

Read `docs/internal/agent-primer.md` first. Work only in this worktree (branch `unit/pk-1`).
All work is in `src/XpSearch.Widgets/Client/` plus guides — no C# changes. The mount markup and
`data-xps-config` shapes are a frozen contract in this unit; do not alter them.

## Part 1 — tree-shakeable core (do first; it shapes Part 2)

Today `src/widgets/index.ts` exports `DEFAULT_WIDGETS` and `bootstrap.ts` consumes it, so any
import that touches `mountAll`/`createSearch` drags in all 13 widgets. Fix the graph, not the
API surface:

1. `bootstrap.ts` must not import the widget implementations. Move the
   `DEFAULT_WIDGETS`-wiring so that only the UMD entry (`src/umd.ts`) statically registers all
   first-party widgets. Keep `FIRST_PARTY_WIDGET_TYPES` (the names) available without pulling
   the implementations in.
2. Give ESM consumers explicit registration: `createSearch` (or `mountAll` — put it where the
   instance/registry seam already is, follow the existing shape) accepts a
   `widgets: Record<string, MountWidgetFactory>` / array of widget definitions option, and
   `registerWidgetType` keeps working for one-offs. Widget modules stay side-effect-free —
   importing `widgets/results` must register nothing.
3. UMD behavior is UNCHANGED: auto-registration of all widgets, same global, same auto-mount.
   The tag-helper path must not notice this unit happened.
4. Prove tree-shaking with a check, not a claim: a small script (wire into `npm run size` or a
   sibling script) that bundles a fixture importing only `createSearch` + `searchBox` via a real
   bundler pass (esbuild is already a devDependency) and asserts the output contains no marker
   from a heavy widget (e.g. a string unique to `results.ts`). This is the unit's regression
   guard; vitest cases cover the explicit-registration API.

Existing tests (bootstrap.test.ts, e2e tests) that rely on all widgets being registered should
construct the full map explicitly rather than depending on import side effects.

## Part 2 — subpath exports

Extend `package.json` `exports` (keep every existing entry working):

- `./widgets` → the barrel (all widget factories + types).
- `./widgets/<kebab-name>` → one widget each (`./widgets/results`, `./widgets/facet-list`,
  `./widgets/search-box`, `./widgets/category-tree`, `./widgets/sort-select`,
  `./widgets/result-stats`, `./widgets/toggle-filter`, `./widgets/range-filter`,
  `./widgets/load-more`, `./widgets/pagination`, `./widgets/suggestions`,
  `./widgets/active-filters` — clearFilters ships with active-filters as it does in source).
- Rollup: add the per-widget entries to the existing ESM config (multi-entry with shared
  chunks, as `xpsearch`/`behaviors` already do). Types: `tsc` already emits per-module `.d.ts`
  under `dist/types/` — point each export's `types` at the right file.
- Every subpath resolves for both `import` and `types`; add a vitest (or node script) case that
  walks the `exports` map and asserts each target file exists after build (guards the map and
  `files` drifting apart).

## Part 3 — SCSS themes

Re-author `themes/shell.css` and `themes/default.css` as SCSS source under `scss/`:

1. `scss/shell.scss` and `scss/default.scss`, with per-widget partials
   (`scss/widgets/_results.scss`, `_facet-list.scss`, … mirroring Part 2's names) so a consumer
   can `@use ".../scss/shell"` once and then only the widget styles they mounted. `shell.scss`
   = structural/base + the pieces every page needs; each widget partial = that widget's rules.
2. Build-time customization via `@use ... with (...)`: hoist the obvious knobs into
   `!default` SCSS variables. RULE: every CSS custom property the current themes emit must
   still be emitted with the same name — custom properties are the runtime theming contract
   and the tag-helper path's only override surface. SCSS variables may feed their default
   values; they must not replace them.
3. Compile with `sass` (add as devDependency — the one new dependency this unit is allowed)
   in `npm run build`. Outputs: `themes/shell.css` + `themes/default.css` exactly as today
   (same paths, so the RCL static-asset copy and existing exports keep working), PLUS
   per-widget compiled CSS at `styles/widgets/<kebab-name>.css` for non-sass pipelines.
4. Parity check: the compiled shell/default output must be rule-for-rule equivalent to the
   current files. Byte-identical is not required (formatting may differ); write a one-shot
   comparison during development and assert equivalence in your report. The existing
   `themes/fixtures` + a11y/markup tests must stay green unmodified.
5. Exports: `./scss/*` → `./scss/*` (raw source, sass resolves `.scss`/partials itself) and
   `./styles/widgets/*.css`. Add `scss/` and `styles/` to `files`.

## Part 4 — docs (npm-first restructure)

- New guide `docs/guides/javascript-bundler-setup.md`: install, Vite example importing
  `createSearch` + two widgets + `@use` of shell/one widget partial, explicit `widgets:`
  registration, custom widget via `registerWidgetType`, and the version-pairing rule (npm
  package version must match the installed `XperienceCommunity.Search.Core` version; state
  where the pairing table lives — start one in this guide).
- Reposition existing guides: the tag helper is introduced as "quick start / no build
  pipeline" with a pointer to the bundler guide as the recommended setup. Mixed mode gets one
  explicit paragraph: a page uses ONE runtime — tag-helper bundle OR your own bundle; if you
  bundle, don't emit `<xps-search-assets />`; Page Builder mounts hydrate from whichever
  runtime is present (do NOT document the mount JSON internals yet — the mount-contract
  guarantee ships with the SSR unit, PK-2).
- Per [[feedback-docs-wiki-ready]]: guide samples must be real and verified — run the Vite
  sample against the mock server (`mock/server.mjs`) and say so in the report.

## Deliverables

- Parts 1–4 code + tests: explicit-registration vitest cases, the tree-shake guard script, the
  exports-map walk, all existing JS suites green (`npm test`, `typecheck`, `contract:check`,
  `docs:check`). Widgets C# suite green untouched (run it anyway — the RCL copies dist/themes).
- CHANGELOG `[Unreleased]`: subpath exports, SCSS sources, explicit widget registration
  (additive; UMD unchanged). KNOWN-LIMITATIONS: honest ceilings only (e.g. if per-widget CSS
  duplicates shared rules rather than layering, record it with the upgrade path).
- Conventional commits on `unit/pk-1`; commit this spec file with the unit.

## Constraints

- `sass` is the only new dependency. No contract changes. No C# changes. Mount markup,
  `data-xps-config`, UMD global/auto-mount, and emitted CSS custom-property names are frozen.
- `package.json` stays `private: true` — publishing is Phase 8; this unit makes the package
  publishable, not published.
- If Part 1's decoupling can't avoid a breaking change to a public ESM export, STOP and report
  the conflict with options — do not ship a breaking rename on your own authority.
