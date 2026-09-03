# Unit TH-6 — Artboard parity: autocomplete panel + edge states

Owner 2026-09-03: "verify the artboard to the demo theme". The live demo matches the *structure*
of the **Autocomplete panel** and **Edge states** artboards but not their look, and one state is
missing outright. This unit makes the default theme reproduce those two artboards exactly
("default is the design" — the shipped default must match the approved mockup, owner rule
2026-09-01) and adds the missing state. No contract change. No new widget options unless an
artboard element cannot be rendered from data the wire already carries (see STOP rules).

Read `docs/internal/agent-primer.md`, `docs/internal/units/TH-3.md`, `SG-1.md`, `ES-1.md`, `MB-1.md`
(the units that built these states), then the artboards themselves — they are now in the repo:

- `docs/internal/design/Autocomplete.dc.html` — the panel (720×680 board)
- `docs/internal/design/States.dc.html` — edge states: empty query, loading skeleton, no results
  **with filters** (counted), no results **with recovery** (did-you-mean + popular searches)
- `docs/internal/design/Main.dc.html` — tokens/typography reference; `canvas.json` — board list

Open the `.dc.html` files in a browser as the reference (they are self-contained HTML). Work only
in your worktree (branch `unit/th-6`). The Widgets JS client and `themes/` are the surface; C#
changes only where the server-rendered first paint has to match (see §3).

## 1. Autocomplete panel (`Client/src/widgets/suggestionsPanel.ts` + `themes/src/scss/{shell,default}/_suggestions.scss`)

Bring the shared panel module (both consumers: `searchBox` integrated + standalone `suggestions`)
to the board:

- **Row leading icon:** clock glyph on *Recent* rows, magnifier glyph on *Suggestions* rows (inline
  stroke SVG on the 24px grid, `currentColor`, `aria-hidden`, TH-1/TH-3 idiom). *Pages* rows have
  no icon (board).
- **Recent rows: remove control** at the end of the row (X glyph, real button with an aria-label
  "Remove from recent searches"), removing that entry from the client-side recents store
  (`recentSearches.ts`) without closing the panel. Keyboard: reachable, Delete key on an active
  recent row also removes (document it in MARKUP.md).
- **Prefix highlight:** the typed prefix is marked with the board's underline band (background
  gradient covering the lower ~42% of the line-height, token-based — use the violet tint token
  the theme already has for the active row; `themes/scripts/check.mjs` re-skin + contrast must stay
  green). Applies to Suggestions and Pages rows.
- **Pages rows:** title semibold in the accent colour + a second, muted 12px line
  "*Type · detail*" (board: "Product · $49.00", "Article · Brewing guides"). Render it from what the
  wire `Suggestion` already carries for document suggestions (check `contract/generated.ts` —
  `Suggestion` members; the `group`, any attributes/`contentType`/`url`). If the wire carries no
  attribute values for document suggestions, render "*Type*" only from the content-type label and
  **report the gap** (do not touch the frozen contract). Do not add a widget option for it.
- **Active row:** tint background per board (already close — confirm token).
- **Footer:** keycap style (white keycap with 2px bottom border, 20px tall) per board; confirm the
  existing footer matches and adjust only what differs.
- Group header text/case/spacing per board (11px, 700, 0.05em, uppercase, muted; top rule between
  groups).

## 2. Edge states (`Client/src/widgets/results.ts`, `behaviors/results.ts` where the empty render lives, `themes/src/scss/{shell,default}/_results.scss`)

Both no-results boards render **inside a white bordered card** (border token, 6px radius, 24px
padding), content **centred** (`align-items: center; text-align: center`):

- **No results — with recovery:** 36px muted magnifier-slash icon; headline "No results for
  “{query}”" 16px/600; "Did you mean *{correction}*?" 14px muted with the correction as the accent
  link (runs the corrected query on click — already wired by SG-1); then a **Popular searches**
  group: 12px uppercase label + **pill** chips (13px, 5px 12px padding, 999px radius, border
  token, no fill; hover = tint). The current "Try fewer words, or clear some filters." line is
  NOT on the board — remove it from this state.
- **No results — with filters (counted, ES-1):** same card + centring; "No results for “{query}”
  with these filters" headline; "There are **N results** without them." line; primary violet
  button "Clear filters and show N results" (38px tall, 18px side padding, 6px radius, 600).
- **Empty query** and **loading skeleton** boards: compare and fix only what differs (ES-1 already
  did the skeleton; confirm the thumbnail-square + lines shape and the card border).
- Keep the existing class names (`.xps-results__empty*`); add modifiers, don't rename — the
  fixtures in `themes/fixtures/` and MARKUP.md pin them; update both.

## 3. Missing state: empty state in Load more mode (MB-1)

Under 1024px the mount-time swap replaces `results`+`pagination` with `loadMore`, and `loadMore`
never renders an empty state — a no-results query shows only "0 results" and the "No more results"
button (verified live 2026-09-03 at 781px). Make `loadMore` render the same empty state as `results`
(reuse the shared empty-state render; do not duplicate markup) with the "No more results" control
hidden while the state is empty. Server-rendered first paint: `ServerRenderedResults` renders the
empty state on the server for the results mount — confirm the swap path shows it too (the SSR
markup is inside the results mount that the swap hides — if so, the client render is what shows;
make sure it does).

## 4. Three-way parity rule

The default result card exists three times (client, `_Result.cshtml`, `ServerRenderedResults`);
the **empty state** exists in the client and in `ServerRenderedResults` (ES-1). Change both together
and extend the parity tests that pin the strings/classes (`card-parity.test.ts`,
`ServerRenderedResultsTests` in Core + Widgets) to cover the empty-state markup.

## 5. Verification (this is the unit's point — do it, don't describe it)

- Render each artboard `.dc.html` in a browser at its board size and the live demo at 1280×900
  and 390×844 (`http://localhost:27340/search?q=gri` for the panel, `?q=expreso` for recovery, a
  filtered query with 0 hits for the counted state) — the reviewer runs the host; you compare
  against the fixtures and the boards' inline styles (the boards ARE the spec: every px/colour/
  weight above comes from them; when this text and a board disagree, the board wins — say so in
  the report).
- Update `themes/fixtures/{suggestions,search-box,results}.html` (and MARKUP.md) to the new
  markup; `cd themes && npm run build && npm run check` green; committed `themes/src/*.css`
  regenerated.
- `cd src/XpSearch.Widgets/Client && npm run build && npm test` green (the a11y gate included —
  the new remove button and icons must pass); Widgets C# suite after the client build; Core suite
  (SSR parity tests).
- `docs/guides/theming.md` / `widget-reference.md`: only if a documented class or behaviour
  changed (recent-row removal is new behaviour → one sentence + MARKUP.md). CHANGELOG:
  `**Fixed (widgets, themes):**` one entry listing the parity items + the Load more empty state.
- `docs/internal/screenshot-manifest.md`: mark the search-box/suggestions and empty-state rows
  STALE.
- One commit on `unit/th-6`: `fix(widgets,themes): autocomplete panel + edge states match the artboards (TH-6)`.

## STOP rules

- A board element that needs data the wire does not carry → render what exists, report the gap;
  never regenerate the contract.
- If `withSuggestions`/the shared panel cannot express the remove control without reaching into
  behaviour internals → STOP and report (public-API gap, TH-3 rule).

## Report

Per-item table (board element → done / partial + reason), suite lines (JS, themes check, Widgets,
Core), fixture files updated, files changed, commit hash, gaps.
