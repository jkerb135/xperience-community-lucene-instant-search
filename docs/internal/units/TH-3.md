# TH-3 — Search box mockup parity: icon, inline clear, integrated suggestions

**Status:** SPEC READY — DISPATCH AFTER TH-2 MERGES (both units regenerate the committed
`themes/src/*.css`, fixtures, and MARKUP.md; parallel dispatch guarantees conflicts).
**Origin:** owner's 2026-09-01 comparison of the live host against the approved mockup canvas
(claude.ai/code/artifact/4ce0cf5e-4e3d-49a3-8340-be4e9c78da9c, `kentico-violet`): the search box
has no magnifier icon, the clear control reads as outside the field, and autocomplete cannot be
attached to the search box at all.

## The gap (verified on the live host 2026-09-01)

The mockup shows ONE search field: magnifier icon at the left inside the input, inline clear at
the right inside the input, autocomplete panel below the field. The library has no way to build
it: `searchBox` renders input + reset with no icon, and `suggestions` is a standalone combobox
that renders **its own input** (`.xps-suggestions__input`) — placing both on a page produces two
search fields (observed live). Only `searchBox` carries the Page Builder `SyncStateToUrl`
routing switch, so a page cannot drop it in favour of `suggestions` without losing URL routing.

## 1. `searchBox` visual parity (icon + inline clear)

- Add a decorative magnifier icon inside `.xps-search-box__field`: `.xps-search-box__icon`,
  inline stroke-based SVG on the 24px grid, `currentColor`, `aria-hidden="true"` (same idiom as
  TH-1's `FILE_ICON`). No emoji, no external assets.
- Field layout in shell (`themes/src/scss/shell/_search-box.scss`): the field becomes the
  positioning context — icon absolutely placed at the left, input padded left for it, the
  existing `__reset` and `__loading` placed inside at the right, input padded right so text
  never underlaps them. Hit target for the reset stays ≥ 2.25rem. Colorless, structure only.
- Default theme: icon in muted, reset hover per existing button hover mechanism. Tokens and
  `color-mix` only; `themes/scripts/check.mjs` (contrast + re-skin) stays green.
- The icon is always rendered (the mockup shows it in every state); no param for it unless one
  already exists in the params shape — do not add an option nobody asked for.

## 2. Integrated suggestions on `searchBox`

Design (decided): `searchBox` gains an opt-in `suggestions` param group; the standalone
`suggestions` widget is unchanged (it remains the right widget for a landing-page box that
navigates via `resultsUrl`).

- `SearchBoxWidgetParams.suggestions?: { debounceMs?; minQueryLength?; limit?; language?;
  groupLabels? }` — the standalone widget's request-shaping params, minus the input/form ones
  it owns because it renders its own field.
- When set, the search box's OWN input becomes the combobox: `role="combobox"`,
  `aria-expanded`, `aria-controls`, active-descendant management, the panel rendered under the
  field, ↑/↓/↵/Esc handling, and the TH-1 keyboard-hints footer — the full WAI-ARIA pattern the
  standalone widget already implements. Reuse `withSuggestions` (the behaviour) and extract the
  standalone widget's panel-rendering internals into a shared module BOTH widgets consume
  rather than duplicating them (`Client/src/widgets/` module, not exported from the package
  root; the a11y test file covers the pattern once, both consumers pinned by markup fixtures).
- Selecting a suggestion sets the query and searches in place (the search box lives on the
  results page; the `resultsUrl` navigate mode stays a standalone-widget concern).
- Panel markup reuses the existing `.xps-suggestions__*` panel classes so TH-1/TH-2 styling
  applies unchanged; `themes/MARKUP.md` documents the combined pattern and a fixture pins it.
- The behaviours-only rule applies: if `withSuggestions` cannot drive a caller-owned input,
  that is a public-API gap — STOP and report rather than reaching into internals.

## 3. Page Builder + host

- `SearchBoxWidgetProperties`: `EnableSuggestions` (checkbox, default false), `SuggestionLimit`
  (number, only meaningful when enabled). Emit into `data-xps-config` as the nested
  `suggestions` group only when enabled (mount base reflects flat properties — follow the
  existing `Remove(...)`/shape-adjustment precedent in `BuildConfig` overrides).
  MountMarkupTests additions. Editor preview: the existing search-box preview gains the icon;
  do not preview the panel.
- Guide updates: `page-builder-widgets.md` (new properties), `widget-reference.md` (the
  `suggestions` param group + when to use integrated vs standalone), and a one-paragraph
  composition note: "one page, one search field — searchBox with integrated suggestions on
  results pages; the standalone suggestions widget for landing pages".
- NO host changes in this unit (the Dancing Goat page then just needs `EnableSuggestions`
  ticked — host follow-up, owner or a later host pass).

## 4. Verification

- Vitest: icon present + decorative; input padding/hit-target fixtures; combobox semantics on
  the searchBox input when enabled and ABSENT when not; suggestion select → query set +
  searched; shared panel module renders identically for both consumers (fixture comparison);
  existing standalone-widget tests untouched and green.
- `themes/` build + check; Client build/test/typecheck; Widgets C# suite (mount tests) after
  client build; other suites run anyway. CHANGELOG (additive). KNOWN-LIMITATIONS only for
  honest ceilings met en route.

## 5. Not in this unit

- Any change to the standalone widget's behavior or markup beyond extracting the shared panel
  module.
- Host page edits. Contract/server changes (suggest endpoint is already sufficient).
