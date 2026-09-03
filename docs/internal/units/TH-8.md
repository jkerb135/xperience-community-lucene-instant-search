# Unit TH-8 — Two shipped themes: Kentico violet and Kentico orange

Owner 2026-09-03: "spin off two distinct themes, violet and the orange Kentico colors". Today the
package ships one design theme (`default.css`, the kentico-violet palette of the boards). After
TH-8 it ships two, built from ONE design source with two token sets, both pixel-equal to the
boards apart from the palette, both passing the same contrast, re-skin and (TH-7) hostile-CSS
checks.

Dispatch AFTER TH-7 merges (TH-7 rewrites the default scss; branch from that main). Read
`docs/internal/agent-primer.md`, `TH-1.md`, `TH-7.md`, `themes/MARKUP.md`, `themes/scripts/*`.
Work only in your worktree (branch `unit/th-8`).

## 1. Structure

- `themes/src/scss/default/` becomes the palette-agnostic **design** partials (already the case if
  TH-1's single-token rule held; verify no literal colour survives outside the token file).
- Two token files: `themes/src/scss/tokens/_kentico-violet.scss` (today's values — the boards) and
  `themes/src/scss/tokens/_kentico-orange.scss`.
- Two entry points → two built stylesheets, committed like today's: `themes/src/kentico-violet.css`
  and `themes/src/kentico-orange.css`. **`default.css` stays** and is byte-identical to
  `kentico-violet.css` (build it from the same entry) so nothing existing breaks; the guide calls
  violet the default.
- Per-widget à-la-carte SCSS (`scss/widgets/_<widget>.scss`) keeps working for both palettes: the
  widget partials import tokens through one `_tokens.scss` indirection that the entry selects —
  document how a bundler user picks the palette (`@use ".../themes/scss/kentico-orange"` vs
  `kentico-violet`) and how a custom palette is made (copy a token file).

## 2. The orange palette

Source the colours, don't invent them: Kentico's brand orange as used by the Xperience admin /
Kentico brand assets (check the `@kentico/xperience-admin-*` packages' CSS custom properties
already in `src/XpSearch.Admin/Client/node_modules`, and Kentico's brand guidance if present in
the repo or docs). Map: accent, accent-hover, accent-tint(s), focus ring, band highlight, chip
tint — every token the violet file defines. Neutrals (text, muted, surface, border, background)
stay identical to violet unless the source palette prescribes otherwise; say what you chose and
why. `themes/scripts/check.mjs` must compute AA contrast for BOTH palettes (light + dark) and the
re-skin check for both; if orange fails AA on any pair, adjust the *derived* tokens (hover/tint)
first, then the accent shade — report the final ratios.

## 3. Distribution + host

- npm: `./themes/kentico-violet.css`, `./themes/kentico-orange.css` exports beside the existing
  `./themes/default.css` (keep) and `./themes/shell.css`; `package:check` covers them.
- Tag helper / no-build fallback: whatever selects the theme today (`data-xps-theme`, the tag
  helper's theme attribute) gains the two names; `default` = violet. No Page Builder property.
- Demo host stays violet. Add ONE fixture render of the orange palette to `themes/test/` so the
  check and a screenshot can show it; screenshot manifest row `themes--kentico-orange` (STALE).

## 4. Docs + verification

- `docs/guides/theming.md`: "Two shipped palettes" section (pick, switch, make your own); the
  theme-selection snippet for bundler and tag-helper users; the token table with both columns.
- CHANGELOG `**Added (themes):**`.
- `cd themes && npm run build && npm run check` (contrast for both, re-skin for both, fixtures,
  TH-7 hostile check for both), widgets client `npm run build && npm test`, `package:check`,
  Widgets C# suite after the client build.
- One commit on `unit/th-8`: `feat(themes): ship kentico-violet and kentico-orange palettes from one design source (TH-8)`.

## Report

Token table (violet vs orange, source of each orange value), contrast ratios for both palettes,
anything that could not be derived from the accent alone, files changed, commit hash.
