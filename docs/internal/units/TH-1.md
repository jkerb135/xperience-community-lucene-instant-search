# TH-1 — Default theme: Kentico-violet visual pass + result template polish

**Status:** SPEC READY — queued behind PK-1 (PK-1 restructures `themes/` into SCSS with frozen
custom-property names; TH-1 branches from post-PK-1 main and edits the SCSS sources, not the
generated CSS).
**Approved design:** owner approved 2026-09-01 — mockup canvas
https://claude.ai/code/artifact/4ce0cf5e-4e3d-49a3-8340-be4e9c78da9c (version `kentico-violet`;
v1 = Satellite-blue, v2 = terracotta are rejected alternates kept for comparison). Kentico brand
palette per https://brand.kentico.com/What-do-we-look-like/Colors — owner explicitly authorized
Kentico branding.

## Intent

Make the shipped default look "functionally appropriate" out of the box: a brand-flavored,
accessible visual layer plus small default-template upgrades, without breaking the theming
contract. Everything visual stays expressible through the existing 8 `--xps-*` tokens; the
single-token re-skin story (`.xps { --xps-color-accent: … }`) MUST keep working — no new
required tokens, all derived colors via `color-mix` from the tokens (`themes/scripts/check.mjs`
already enforces literal-free rules; keep it passing).

## 1. Token value changes (default theme only; names/mechanism unchanged)

| Token | Old | New |
|---|---|---|
| `--xps-color-accent` | `#0b5fff` | `#AF00FA` (Kentico Violet 60 — 5:1 on white, AA for link text) |
| `--xps-color-text` | `#111` | `#1f2430` |
| `--xps-color-muted` | `#666` | `#5c6370` |
| `--xps-color-border` | `#e2e2e2` | `#e3e5ea` |
| `--xps-color-surface`, `--xps-radius`, `--xps-space`, `--xps-font` | unchanged | |

Hover/darken derivations stay `color-mix` (target ≈ Violet 70 `#7F09B7`); tint derivations stay
`color-mix(accent N%, surface)` (targets ≈ Violet 20 `#EBD8FF`, Violet 10 `#F7F1FF`). Do NOT
hard-code the brand hexes outside the token block — a host swapping the accent must get coherent
tints for free. Heritage Orange `#F05A22` is deliberately NOT used (≈3:1, fails AA for links);
mention the one-token orange swap in the theming guide instead.

Dark-mode block (`data-xps-theme="auto"`): re-derive the five values for the violet family; the
accent must reach ≥4.5:1 on the dark surface (a lighter violet, in the `#C983F7` region — verify
with a contrast check, don't eyeball). Structure of the block unchanged.

## 2. default.css restyling (the "editorial spin" — approved elements)

- **Highlight** `.xps-highlight`: replace tinted-box with highlighter-underline —
  `background: linear-gradient(to top, <accent 18% tint via color-mix> 42%, transparent 42%)`,
  drop the border-radius. Text color untouched (a11y: color is never the only signal — the
  underline band is decoration under unchanged text).
- **Facet group titles** (`.xps-facet-list__title`, `.xps-category-tree__title`,
  `.xps-range-filter__title`): add bottom padding + `2px solid var(--xps-color-text)` rule.
- **Facet counts** (`.xps-facet-list__count`, `.xps-toggle-filter__count`,
  `.xps-category-tree__count`): plain muted text, `font-variant-numeric: tabular-nums` — no
  pill background/border anywhere.
- **Pagination**: remove per-link borders/boxes; links are plain text in muted, current page =
  accent, `font-weight 700`, `2px` accent bottom border. Chevron prev/next plain; disabled stays
  the opacity rule. Hit sizes from shell.css unchanged (2.25rem min).
- **Result type label** (new class, see §3): uppercase, `letter-spacing: 0.08em`, `font-weight
  700`, accent color, ~0.72em.
- **Active-filter chips**: keep current structure; tint background/border via accent color-mix
  (already the mechanism — just verify the violet renders well at the existing percentages).
- **Search inputs**: add the subtle inset shadow from the mockup
  (`inset 0 1px 3px` text-color mix ≈6%).
- **Refinement loading**: `.xps-results--loading` (stale results visible) gets a gentle
  `opacity: .55` dim on the list only — never blanks; skeletons remain first-search-only
  (existing behavior, don't change the logic).

## 3. Default result template upgrades (`results.ts` `defaultResultItem` + Razor parity)

Approved additions, all optional-attribute-driven (absent attribute ⇒ element not rendered):

1. **Path line** — new `pathAttribute?: string` param (default `'path'`), rendered as
   `.xps-result__path` (muted, 12px) between title and snippet. String attribute, plain text.
2. **Type micro-label** — the existing `contentType` meta item gets its own class
   `.xps-result__type` (first meta item) so §2 can style it; remaining meta items unchanged.
3. **Filetype icon slot** — when there is no `image` and a string attribute `fileType` is
   present, render the media slot with an inline SVG document glyph (stroke-based, 24px grid,
   `currentColor`) instead of nothing. No emoji, no external assets.
4. **`ResultItemOptions`** grows `pathAttribute`; `loadMore` reuses it automatically (it already
   reuses `defaultResultItem`).

**Razor parity is mandatory:** `_Result.cshtml` / `SearchResultViewModel` (DX-2's §5.8
server-rendered first paint) must render byte-equivalent markup for the same inputs, HW-12
taught us drift here is silent. Add/extend the parity check (node harness rendering vs generated
Razor inspection, as HW-12 did) so a future template edit fails loudly.

## 4. Richer empty state (results widget `defaultEmpty`)

- The empty template's data gains `hasRefinements: boolean` and a `clearRefinements(): void`
  action (plumbed from the behavior layer — the connectors know the refinement state; expose
  the same action `activeFilters`' Clear-all uses, do not reimplement).
- With refinements: "No results for "{query}" with these filters" + a primary
  `.xps-button--primary` "Clear filters" button calling the action.
- Without: current copy stays.
- The unfiltered-count preview from the mockup ("…and show 7 results") requires a second
  unfiltered probe query — implement ONLY if it drops out naturally from the pipeline client;
  otherwise ship the button without the count and record the ceiling + upgrade path in
  KNOWN-LIMITATIONS.
- OUT OF SCOPE (record in KNOWN-LIMITATIONS, do not stub): "Did you mean" (needs a Core/Lucene
  suggester + contract addition — future unit), "Popular searches" slot (needs a public
  analytics endpoint — future unit; the mockup note already frames it host-wired).
- A11y unchanged: empty announcement stays on the existing live region; the new button is a real
  `<button>`.

## 5. Suggestions footer keyboard hints

`suggestions.ts` footer (element exists — `.xps-suggestions__footer` with see-all): prepend a
hints cluster — `<kbd>` elements for ↑ ↓ (navigate), ↵ (select), esc (close) with keycap styling
in default.css (surface bg, border, 2px bottom border, 4px radius). Plain decorative text with
`aria-hidden="true"` on the kbd cluster (the combobox pattern already conveys the keyboard
model); see-all link keeps its place at the end. Hide the cluster below 768px
(`@media (pointer: coarse)` preferred) — keyboard hints on touch are noise.

## 6. Explicitly NOT in this unit

- Mobile Filter & Sort drawer/sheet (approved in mockups, but it is a new widget + composition
  pattern → **TH-2**, spec to follow).
- Any shell.css structural change beyond what §2 needs (shell stays colorless/structure-only).
- Any contract or server API change.

## 7. Verification

- `themes/` build + `check.mjs` green (color literals only in variable blocks).
- Vitest: template additions (path line, type class, filetype icon slot, empty-state
  refinement variant + clear action fired, kbd footer markup), single-token re-skin smoke
  (override accent, assert no stale blue/violet literals in computed styles of a rendered card).
- Contrast assertions for the new light and dark accents (compute, don't trust).
- Razor parity check per §3.
- Full suites: JS + Widgets C# (`npm run build` in Widgets client BEFORE `dotnet test` Widgets —
  standing rule), Core untouched but run anyway.
- Docs: update the theming guide page (token table with new values, orange-swap example, dark
  mode) and the widgets gallery page — wiki-ready with verified samples, per project docs rule.

## 8. References for the implementer

- Mockup canvas (visual source of truth): artifact URL above, version `kentico-violet`.
- Research digests informing the choices (Satellite/DocSearch/Coveo conventions, Baymard
  no-results guidance) are summarized in the canvas sticky notes; treat the canvas as spec,
  the notes as rationale.
- Read `docs/internal/agent-primer.md` before exploring (standing rule).
