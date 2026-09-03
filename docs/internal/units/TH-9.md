# Unit TH-9 — Sidebar card + autocomplete "no suggestions" state

Owner 2026-09-03, two board additions (already applied to the checked-in boards, which are the spec):

1. **`docs/internal/design/Main.dc.html` — facet sidebar is a card:** white surface, 1px border
   (border token), 6px radius, 20px padding, soft shadow `0 4px 16px rgba(31,36,48,0.08)`. The
   sidebar's contents (Filters heading + Clear all, facet groups, category tree, range, toggle)
   are unchanged inside it.
2. **`docs/internal/design/Autocomplete.dc.html` — second panel, "No suggestions":** when nothing
   matches the typed text the panel shows a centred empty state (28px muted magnifier-with-minus
   glyph, "No suggestions for “{query}”" 14px/600, hint "Press Enter to search anyway, or try a
   different spelling." 13px muted, 28/14/24px padding) and a footer with only the actions that
   still apply (↵ search, esc close). No group headers, no see-all link.

Read `docs/internal/agent-primer.md`, `TH-6.md`, `TH-7.md` (root idiom, isolation check, shell/
default split — visuals go in default, structure in shell), `themes/MARKUP.md`. Work only in your
worktree (branch `unit/th-9`). Both palettes must pass every themes check.

## 1. Sidebar card

- The "sidebar" is a host composition (the demo's `side-panel` zone holds ClearFilters, two
  FacetLists, CategoryTree, RangeFilter). The card cannot be a widget's own box — it wraps several
  widgets. Ship it as a documented **composition class**, `xps-sidebar` (shell: layout —
  flex column, gap; default: surface, border, radius, padding, shadow), applied by the host to the
  zone wrapper. Check how the demo's Section_25_75 renders the `side-panel` zone (`src/` host
  views) and add the class there (host edit, list file/line); the MB-1 mobile rule that hides the
  sidebar under 1024px (`.dg-side-panel:has(> .xps-mount)`) must keep working — verify.
- Guide: `page-builder-widgets.md` composition section + `theming.md` (the class, tokens used);
  fixture `themes/fixtures/sidebar.html` (the card around two facet groups) so the isolation and
  palette checks cover it; MARKUP.md.
- Not a Page Builder property, not a widget option.

## 2. Autocomplete empty state

- Shared panel module (`suggestionsPanel.ts`, both consumers): when the response carries no
  suggestions for a non-empty query and no recent matches, render the board's empty state
  instead of the current "No suggestions for X" text row: `.xps-suggestions__empty` (block) with
  `__empty-icon`, `__empty-title`, `__empty-hint`; footer reduced to ↵ search / esc close. Keep
  the combobox semantics valid (`aria-expanded` true with an empty listbox, or the documented
  live-region announcement the a11y test expects — check `a11y.test.ts` and keep axe green).
- Enter still submits the query (existing behaviour); Esc closes.
- Below the min query length / empty input: unchanged (panel closed).
- Theme: shell = layout/centring; default = glyph colour, sizes, weights, hint colour, footer
  background/border. Fixtures: `themes/fixtures/suggestions.html` gains the empty variant (and the
  search-box consumer's fixture); MARKUP.md; vitest cases for both consumers; parity test if the
  fixture comparison covers the panel.
- Guide: `widget-reference.md` one line; screenshot manifest row STALE.

## 3. Verification + commit

themes build + check (both palettes: contrast, re-skin, isolation with the new fixtures, sheet),
widgets client build + test (a11y), Widgets C# after the client build, Core. CHANGELOG
`**Added (widgets, themes):**` one entry. One commit on `unit/th-9`:
`feat(widgets,themes): sidebar card composition class and autocomplete no-suggestions state (TH-9)`.
Report: per-board-element table, host file/line for the class, suite lines, commit hash.
