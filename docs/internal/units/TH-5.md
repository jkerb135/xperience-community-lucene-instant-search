# TH-5 — Range filter: one track, two thumbs, filled selection segment

Small parity-defect unit. Owner 2026-09-01: the live price slider "isn't like the design file".
The mockup (canvas `kentico-violet`, sidebar Price group: "40 to 260 USD") shows ONE slim track;
the two handles ride the SAME rail, and the segment BETWEEN them is filled with the accent while
the outer parts stay muted. TH-4's restyle themed the two native `<input type="range">` controls
individually, so they still render as two stacked rails.

[[feedback-default-is-the-design]] applies: this look is the default, no opt-in.

## Design (decided)

Keep the two native range inputs (keyboard/SR behavior stays — TH-4's deliberate choice). Make
them one visual control:

1. **Shell** (`themes/src/scss/shell/_range-filter.scss`): a positioning wrapper around the two
   inputs (add a `.xps-range-filter__slider` wrapper element in `rangeFilter.ts` if the markup
   lacks one — MARKUP.md + fixtures updated). Both inputs absolutely overlaid on one row, full
   width, transparent backgrounds; `pointer-events: none` on the inputs with `pointer-events:
   auto` restored on their thumb pseudo-elements (`::-webkit-slider-thumb`,
   `::-moz-range-thumb`) so each thumb stays draggable and clicks can't hit the wrong input.
   Wrapper reserves the control's height. Keyboard focus order unchanged (two inputs, min then
   max); `:focus-visible` ring must remain visible on the focused thumb.
2. **Fill segment**: `rangeFilter.ts` sets two custom properties on the wrapper each render —
   `--xps-range-from` and `--xps-range-to` as percentages of the bounds (clamped 0–100). The
   default theme paints the rail as a `linear-gradient` over the wrapper (or a dedicated
   `__rail` element if cleaner): border-token color outside, accent between the two
   percentages. Shell stays colorless — the rail lives in
   `themes/src/scss/default/_range-filter.scss`; shell-only sites get the overlaid thumbs on
   an unpainted rail (document in MARKUP.md, same spirit as TH-4's "shell keeps the native
   slider" note — which this supersedes).
3. Thumbs keep TH-4's look (≈0.875rem, round, accent, surface ring). Reduced motion: no
   transitions needed. RTL: percentages are logical (the gradient uses the same direction the
   inputs render in) — verify with `dir="rtl"` in a jsdom test if cheap, otherwise note it.

## Verification

- Vitest: wrapper present; custom properties set on init and updated after a bounds/value
  change; clamped when values sit outside bounds; both inputs still receive keyboard input
  (dispatch ArrowRight on max input → value changes, vars update).
- Fixture (`themes/fixtures/…`) updated to the wrapper markup incl. mid-range var values so
  the section pages show the filled segment; MARKUP.md rows for `__slider` (+ `__rail` if
  added) and the two custom properties.
- `themes/` build + `npm run build:test` + check green (contrast/re-skin guards unaffected);
  Client build/test/typecheck; Widgets C# suite after client build (mount markup unchanged —
  the wrapper is JS-rendered, but run MountMarkupTests anyway); other suites run anyway.
- CHANGELOG (fix, additive markup). KNOWN-LIMITATIONS: remove/supersede TH-4's
  "shell-only keeps the native slider" entry if its statement no longer holds; add an entry
  only for honest ceilings (e.g. gradient rail invisible in Windows High Contrast — check
  `forced-colors` and either handle with one rule or record it).

## Not in this unit

- Any change to values/labels/inputs row (TH-4's layout stands). No PB property changes.
- The host adopts automatically via its next `npm run build` in `src/` — note for the lead,
  not you.
