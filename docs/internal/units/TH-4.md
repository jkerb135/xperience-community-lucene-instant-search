# TH-4 — Default theme round 2: mockup-parity polish + missing composition pieces

**Status:** SPEC READY — DISPATCH AFTER TH-3 MERGES (same generated `themes/src/*.css`,
fixtures, MARKUP.md surface; TH-3 is also reworking the search-box field these recipes sit
around).
**Origin:** owner's 2026-09-01 side-by-side of the live host against the mockup canvas
(claude.ai/code/artifact/4ce0cf5e-4e3d-49a3-8340-be4e9c78da9c, `kentico-violet`): "default
shell is still lacking in comparison to the design". This unit covers everything in that gap
that is the LIBRARY's to fix.

**Governing principle (owner, 2026-09-01, binding for this unit):** the shipped default IS the
design. Everything stays customizable, but out of the box — no overrides, no extra params — the
widgets must produce the mockup's look, and the documentation must read as a walkthrough of
exactly that default, annotating each element with its customization hook (param / PB property /
SCSS variable / template slot). Concretely: where a spec section below offers an option whose
"on" state matches the mockup, the mockup state is the DEFAULT (CHANGELOG the default change;
pre-1.0). When you must choose between "document an opt-in" and "change the default", change
the default. Already fixed by composition on the host (not this unit): stats
moved to the results column, numbered pagination restored. Host-content items (product images
in the index, richer host card) are host follow-ups, also not this unit.

## 1. Page Builder widgets for `activeFilters` and `clearFilters` (the biggest gap)

The mockup's chips row ("Category: Grinders ×  Clear all") and sidebar header ("Filters" +
"Clear all") cannot be built today: the two JS widgets exist but have NO Page Builder
counterparts.

- `XpSearch.ActiveFilters` — mount `data-xps-widget="activeFilters"`. Properties: the JS
  widget's existing params surfaced the way sibling widgets do (labels/templates stay JS-side;
  keep properties minimal — Index/InstanceId plus whatever params the JS widget actually has;
  do not invent new ones). Include the trailing inline "Clear all" if the JS widget already
  renders one; otherwise the row composes with the ClearFilters widget beside it.
- `XpSearch.ClearFilters` — mount `data-xps-widget="clearFilters"`. Add an optional `Label`
  property if the JS param exists.
- Both: follow the `SearchBoxWidget.cs` pattern, editor previews per `BuildEditorPreview`
  (static chip row / button), MountMarkupTests, `page-builder-widgets.md` sections.
- JS: give `activeFilters` the `--scroll` modifier styling TH-2 added a hook for, verified in
  a fixture (chips row scrolls horizontally on overflow, no wrap).

## 2. Composition classes + the "results page" recipe (shell)

The mockup's layout idioms need small, colorless shell helpers plus documentation — not new
widgets:

- `.xps-toolbar` — one row, stats left, sort right (`display:flex; justify-content:
  space-between; align-items:center; gap`), wrapping below 640px. The host recipe: wrap the
  ResultStats and SortSelect widget mounts (guide shows the Razor/section markup).
- `.xps-sidebar__header` — "Filters" heading + trailing clear-all link on one row, matching
  the facet-group title treatment (TH-1's 2px rule).
- `.xps-button--link` (default theme) — the muted-link look the mockup uses for "Clear all",
  applied to the clearFilters button via a param-driven class or documented class hook —
  follow whichever mechanism the widget already has; smallest change wins.
- Guide: a "Composing the results page" section in `widget-reference.md` (or the guide page
  that owns recipes) with the full annotated layout — sidebar header, facet stack, toolbar
  row, chips row, results, pagination — as a verified sample. This is the CANONICAL WALKTHROUGH
  per the governing principle: a fresh install following it, with zero overrides, must
  reproduce the mockup's skeleton; every element in it carries a one-line "customize this via
  …" annotation naming the param/property/variable/slot. The recipe's PRIMARY form is plain
  HTML + the npm bundle (owner principle 2026-09-01: the UI must be creatable with standard
  HTML and JavaScript bundling, no Kentico required) — hand-written `data-xps-widget` mounts
  (or programmatic `addWidgets` containers) inside the composition classes, runnable against
  the mock server. Each element then carries a second note: "in Page Builder, place the
  <name> widget (XperienceCommunity.Search.Widgets)" — the NuGet is the optional PB extension
  of the same widgets, never the prerequisite. Also update the ResultStats default
  `TextTemplate` (C# property default) to the mockup wording
  `{total} results for “{query}” ({tookMs} ms)` so a freshly placed widget matches the design
  without editing (CHANGELOG the default change).

## 3. Default-theme polish (SCSS only, tokens/`color-mix` only)

- **Range filter**: thin track (2px), compact round handles (12–14px, accent), muted rail,
  the From/To inputs and unit on ONE inline row ("40 to 260 USD" idiom — `FromLabel`/`ToLabel`
  props already exist), bounds hint (`0 to 500`) restyled as small muted text right-aligned
  under the row. Structure changes that shell owns (row layout) go in shell, colorless.
- **Sort select**: replace the native look — border, radius, padding, custom chevron
  (inline SVG background or wrapper span, `currentColor`), compact variant that sits right in
  the toolbar. Keep the native `<select>` element (a11y) — style only.
- **Snippets**: clamp `.xps-result__snippet` to 3 lines in the default theme
  (`-webkit-line-clamp` + fallback `max-height`), killing the wall-of-text look for hosts
  whose snippet attribute holds long copy.
- **Result stats emphasis**: the number reads bold in the mockup ("**14 results** for …").
  Have the widget wrap the substituted `{total}` in `<strong class="xps-result-stats__total">`
  (additive markup, fixture updated, default theme styles it; plain-text fallback unchanged
  when the template lacks `{total}`).

## 4. Collapsible facet groups (mockup chevrons)

`facetList` and `categoryTree` gain `collapsible?: boolean` (default TRUE, expanded initially —
the mockup shows chevrons on every group; `collapsible: false` is the opt-out): the group title
becomes a `<button aria-expanded>` with a chevron (inline SVG, `currentColor`), toggling the
body with `hidden`. No animation needed; `prefers-reduced-motion` moot. Per-widget PB checkbox
property (`Collapsible`). Keyboard/a11y covered in the a11y test file; fixtures + MARKUP.md
updated. Collapsed state is per-render local state — do NOT persist it or put it in the URL.

## 5. Verification

- Vitest: chips scroll modifier fixture; stats `<strong>` wrap + fallback; collapsible
  toggle semantics (aria-expanded, hidden body, absent when off); clearFilters label/class
  hook. Mount tests for the two new PB widgets. axe green with a collapsed group.
- `themes/` build + check (contrast + re-skin guards stay green); Client build/test/
  typecheck; all four C# suites. CHANGELOG (additive). KNOWN-LIMITATIONS for honest ceilings.
- Docs: the §2 recipe with verified sample; new PB widget sections; `widget-reference.md`
  param additions.

## 6. Not in this unit

- Product thumbnails/path data on the Dancing Goat host (index/content work, host follow-up).
- Sticky sidebar, page background/typography of the host site (site CSS, not the theme).
- TH-3's search-box work. TH-2's sheet. Any contract/server change.
