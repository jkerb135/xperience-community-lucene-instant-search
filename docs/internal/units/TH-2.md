# TH-2 — Mobile Filter & Sort sheet (`filterSort` widget)

**Status:** SPEC READY — DISPATCH AFTER TH-1 MERGES (TH-1 rewrites the default theme SCSS this
unit adds partials to, and restyles the widgets this composes; branch from post-TH-1 main).
**Approved design:** owner-approved mockup canvas
https://claude.ai/code/artifact/4ce0cf5e-4e3d-49a3-8340-be4e9c78da9c, version `kentico-violet`,
mobile artboard + `mobile-note` sticky. The written description below is authoritative for the
implementer (the canvas is not readable from an agent context).

## What the mockup shows (authoritative summary)

Below 1024px the facet sidebar is replaced by a toolbar row: a **Filter & Sort** button (funnel
icon, label, accent-filled count badge showing the number of active refinements) with the result
count to its right, and under it a horizontally scrolling row of active-filter chips. The button
opens a **bottom sheet**: dimmed backdrop (`text` color at 40%), sheet 92% viewport height,
12px top radii, header "Filter & Sort" + circular close button, body of sections divided by
hairline rules — **Sort by** as a row of choice pills, then one section per facet group
(checkable value rows with counts, same data as `facetList`). Selections in the sheet are
**pending** — nothing refines while the sheet is open. The sticky footer's primary button reads
**"Show N results"**, where N is a live preview of what the pending selection would return
(mockup: pending Product selection previews 8 of the current 14); tapping it applies everything
at once and closes. The mockup also swaps pagination for load-more below 1024px — that is page
composition, not this widget (guide note only).

## 1. JS widget `filterSort` (`Client/src/widgets/filterSort.ts`, subpath `./widgets/filter-sort`)

Dogfooding rule applies (spec §5.7): behaviour + renderer over the public behaviour API. The
widget composes the existing connectors — the facet-list behaviour once per configured
attribute and the sort behaviour — it must NOT reach into private internals. If composition
needs something the behaviours don't expose, STOP and report (that is a public-API gap, the
"if a first-party widget needs private internals, the public API is wrong" rule).

Params (`FilterSortWidgetParams`):
- `container` (required, as all widgets)
- `facets: Array<{ attribute: string; label?: string; limit?: number }>` — the groups, in order
- `sortOptions` — same shape `sortSelect` takes; omitted ⇒ no Sort by section
- `label?: string` (default "Filter & Sort"), `applyLabel?: string` (default "Show {count}
  results" with a `{count}` placeholder; when the preview is unavailable render "Show results")
- `templates?` — optional item-level overrides mirroring `facetList`'s, if that widget has them;
  do not invent new template slots beyond what the mockup needs

Behavior:
- The trigger renders in the mount: `.xps-filter-sort__trigger` with icon, label, and
  `.xps-filter-sort__badge` (hidden at zero) counting active refinements across the configured
  attributes + non-default sort. The sheet element is created lazily on first open, appended to
  `document.body`, and torn down on `destroy()`.
- **Pending model:** opening snapshots the current refinements; taps toggle a local pending set
  rendered as overlay state on the connector data (checked = committed XOR pending-toggled).
  Nothing calls a refine action until **Apply**, which replays the pending toggles + sort change
  through the same public actions the live widgets use, in one batch (state layer already
  coalesces renders — verify, don't build batching). **Clear all** in the sheet resets pending
  to "remove every refinement on the configured attributes" (still pending until Apply). Closing
  by backdrop/Esc/X discards pending.
- **Live preview count:** on each pending change, one debounced (~250ms) count-probe via the
  public `SearchClient` — the current query + committed-state filters with pending applied,
  smallest legal page size, reading only the total. It must NOT touch instance state, journal a
  search (check what the server counts as journal-worthy — if a probe would journal, send the
  existing dontJournal-equivalent flag if the contract has one; if it doesn't, STOP and report
  the gap rather than polluting analytics), or race Apply (in-flight probe result arriving after
  Apply is discarded). Probe failure ⇒ fall back to the countless label, never an error state.
- A11y: `role="dialog"` `aria-modal="true"`, labelled by the header; focus moves into the sheet
  on open, is trapped, and returns to the trigger on close; Esc closes; body scroll locked while
  open; the slide-up transition respects `prefers-reduced-motion` (fade or none). The apply
  button is a real `<button>`; the count change is announced via the button's text (no extra
  live region).
- Markup contract: new classes under `.xps-filter-sort` (trigger/badge) and `.xps-sheet`
  (backdrop/panel/header/close/section/section-title/footer/apply/clear) — document them in
  `themes/MARKUP.md` and add fixtures like the other widgets have.

Wiring: add to `scripts/widget-entries.mjs` (rollup entry, exports walk, per-widget CSS — the
primer documents this map), export from the `./widgets` barrel and root entry, register in
`DEFAULT_WIDGETS` so the UMD path has it.

## 2. Styles

- `themes/src/scss/shell/_filter-sort.scss` — structure only, colorless per the shell rule:
  trigger layout, sheet positioning (fixed, bottom, 92% max-height, top radii token), backdrop,
  section layout, sticky footer, scroll containment, the ≥1024px rule hiding the trigger
  (`.xps-filter-sort { display:none }` at the breakpoint — the sidebar/sheet swap's other half,
  hiding the sidebar below 1024px, is the page's concern; give the guide the two-line snippet).
- `themes/src/scss/default/_filter-sort.scss` — the violet treatment per the mockup: accent
  badge, choice-pill sort row (selected = accent), hairline section rules, backdrop
  `color-mix(text 40%, transparent)`, primary apply button (`.xps-button--primary` exists after
  TH-1 — reuse it). All colors via the 8 tokens/`color-mix`; `themes/scripts/check.mjs` stays
  green.
- `scss/widgets/_filter-sort.scss` à la carte bundle + generated `styles/widgets/filter-sort.css`
  (the entries map drives this).
- Chips row: no new widget — add an `.xps-active-filters--scroll` modifier (horizontal scroll,
  no wrap, edge fade optional) to the active-filters partials; the page opts in with the class.

## 3. Page Builder widget (XpSearch.Widgets)

`XpSearch.FilterSort` mount widget following the existing pattern (`SearchBoxWidget.cs` +
mount base): properties = facet attributes (reuse the schema-driven attribute selector
precedent from PB-6 if reusable without new plumbing; else the textarea-lines pattern the other
widgets used before PB-6), labels, sort options (same property shape as the Sort widget),
results-per-page NOT needed. Editor preview per the `BuildEditorPreview` pattern (a static
trigger-button preview is enough; do not preview the sheet). MountMarkupTests additions. No SSR
for this widget (the sheet is interaction-only; the trigger is client-rendered like the other
interactive widgets — confirm the mount base's default behavior and follow it).

## 4. Docs

- `docs/guides/page-builder-widgets.md`: the new widget's section (properties, preview note).
- `docs/guides/js-client.md` or the widgets gallery page (follow where other widgets document
  their JS params): `filterSort` params + the pending/apply model + the preview-count probe.
- The responsive composition recipe (hide sidebar below 1024px, show trigger, chips-scroll
  modifier, load-more instead of pagination) as a short "Mobile filtering" section with a
  verified sample — per [[feedback-docs-wiki-ready]].
- CHANGELOG `[Unreleased]` (additive). KNOWN-LIMITATIONS for honest ceilings — expected at
  minimum: the preview probe is a second query per pending change (debounced) and its
  count can drift from the applied result if the index changes between probe and Apply.

## 5. Verification

- Vitest: trigger badge counts; pending overlay (tap ≠ refine until Apply); Apply replays and
  closes; discard on Esc/backdrop; probe debounce + stale-probe discard + failure fallback;
  focus trap/return; markup fixtures; a11y test file additions like the other widgets have.
- `themes/` build + check green; Client build (exports walk picks up the new subpath) + full JS
  suite; Widgets C# suite (mount tests) after `npm run build`; other C# suites run anyway.
- The e2e-widgets test (`e2e-widgets.test.ts`) gains a sheet open→pend→apply flow against the
  mock server.

## 6. Not in this unit

- Load-more/pagination swap logic (page composition; guide only).
- Any host (Dancing Goat) wiring — that is a follow-up host unit; note that the host's Vite
  entry (HW-13) must import the new subpath + partial when it adopts the widget.
- Contract/server changes. If the journal-flag gap in §1 is real, report it; a probe flag would
  be a coordinated contract change for a future unit.
