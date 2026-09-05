# Unit UX-3 — Analytics, Status, Rule builder and Experiment detail rebuilt to the approved boards

Owner 2026-09-04: the redesign canvas https://claude.ai/code/artifact/45fc44c5-264f-4aab-b920-208009984310
is **approved**; "go ahead and redo the UI". The boards are in the repo and are the source of
truth for these four pages, exactly as `QueryTester.dc.html` is for the query tester:

| Page | Board | Template |
| --- | --- | --- |
| Analytics | `docs/internal/design/Analytics.dc.html` | `src/XpSearch.Admin/Client/src/analytics/AnalyticsDashboardTemplate.tsx` (+ `ReportTable.tsx`, `VolumeChart.tsx`, `AnalyticsDashboard.module.scss`) |
| Status | `docs/internal/design/Status.dc.html` | `src/XpSearch.Admin/Client/src/status/IndexStatusTemplate.tsx` (+ `.module.css`) |
| Rule builder | `docs/internal/design/RuleBuilder.dc.html` | `src/XpSearch.Admin/Client/src/rule-builder/*` |
| Experiment detail | `docs/internal/design/ExperimentDetail.dc.html` | `src/XpSearch.Admin/Client/src/experiments/ExperimentDetailTemplate.tsx` |

Open each board in a browser at 1440px; its `<style>` block carries every value. The layout
guidelines (G1–G6 in `docs/guides/admin-client-development.md`, written by UX-2) and the QT-3
rules apply unchanged: **stock Kentico components stay** (`Table`, `Pagination`, `Card`,
`Callout`, `SidePanel`, `Tag`, `Button`, `Input`, `Select`, `Checkbox`, `NameToggleButtons`,
`DateTimeRangeInput`, `Headline`, `Icon`, `Divider`, `Stack`); layout is done by flex/grid
wrappers in the page's `.module.scss`; own markup only where no component exists (chart, stacked
bar, drag grip, dashed add area, picker list), each listed in ADR-0020; colour tokens only, no
hex, no fallbacks (`src/layout.test.ts` enforces it); 24px between cards, 16px inside; no page
padding. The reference implementation is `query-tester/QueryTesterTemplate.tsx` +
`QueryTesterTemplate.module.scss` (row-height override, chip, header rows, panel spacing).

Two slices, two worktrees, two implementers. **A** = Analytics + Experiment detail
(`unit/ux-3a`). **B** = Status + Rule builder (`unit/ux-3b`). Shared files (`theme.ts`,
ADR-0020, CHANGELOG, screenshot manifest) are edited additively; the lead merges.

Read `docs/internal/agent-primer.md`, ADR-0020, ADR-0028, `docs/internal/units/QT-3.md` §A and
`UX-2.md` §1 first.

---

## A.1 Analytics (board `Analytics.dc.html`)

1. **Header card** (the page starts with a card; no bare headline above it). `Card` whose body
   is a flex column, gap 16:
   - row 1: `justify-content: space-between; align-items: baseline` — card title 24/32 **Analytics**
     left; muted 12/16 meta right: `Index <mono>DancingGoatSample</mono> · Lucene · <range> · <N> searches`.
   - row 2 (filters): flex row, gap 16, `align-items: flex-end` — **Range** = stock
     `NameToggleButtons` (7 / 30 / 90 days) under a 12/16 bold label; **Date range** = stock
     `DateTimeRangeInput` (width 240); **Rows per page** = stock `Select` (width 120); primary
     **Load** `Button` at the row end.
2. **KPI tiles**: a flex row, gap 16, four equal `Card`s (`flex: 1`), each a flex column gap 4:
   muted 12/16 label, `figure` 32/38/700, muted 12/16 hint. Values as today (total searches /
   zero-result rate / click-through rate / avg clicked position). At `sm` two per row.
3. **Chart card**: header row `space-between` — `Headline` L (16/24) **Searches over time** left,
   legend right (two swatches 16×2 with muted 12/16 labels, gap 24). The SVG chart is own markup
   (`.plot` 224px). Axis labels row `space-between` muted 12/16. **Show the numbers** = tertiary
   `Button` with the `xp-chevron-down` icon (existing behaviour).
4. **Zero-result queries card**: header row `align-items: center` — left: card title 24/32
   **Zero-result queries** + sky `Tag` `<N> searches · <M> queries` (gap 16); right: muted 12/16
   `Only actionable table on this page`. Then the stock `Table` (columns Query bold · Volume
   right-aligned · Last seen · Action = secondary S `Button` with `xp-plus` **Create rule**), then a
   bottom row `space-between`: muted 12/16 `Page 1 of 3 · 28 rows` left, stock `Pagination`
   right. Then the muted note `Create rule opens the Rules form seeded with the query.`
5. **Top queries** and **Slowest queries** cards: card title 24/32, stock `Table` (Query bold ·
   Volume right · p95 time right), same bottom pagination row.
6. Column alignment: numbers right (`.cellEnd` pattern from the query tester), text left.
7. Narrow (`sm`): tiles two per row; tables unchanged (they already fit).

## A.2 Experiment detail (board `ExperimentDetail.dc.html`)

1. **Header card**: row `space-between; align-items: center` — left: card title 24/32 = the
   experiment name, under it muted 12/16 `Index <mono>…</mono> · <traffic>% of traffic to variant B ·
   started <ts> · ended <ts>`; right: the state `Tag`s (grey `Concluded` / orange
   `--color-background-tag-kentico-orange` `Variant B discarded`, or `Draft` / `Running`… as the page
   knows them), gap 8.
2. **Variant cards**: a flex row gap 24, two `Card`s `flex: 1`, each: card title 24/32
   (`Variant A — live tuning` / `Variant B — draft tuning`), muted 12/16 `<N> searches` directly
   under (no extra gap), then a flex row gap 16 of four mini figures (flex column gap 4: muted
   12/16 label, `figure-s` 24/32/700 value or `—`, muted 12/16 hint). At `sm` the two cards stack.
3. **Quick tip** `Callout` stays stock (subheadline `Quick tip`, headline, 16/24 body).
4. Existing actions (conclude / keep B / discard) stay where the page has them today, as primary /
   secondary `Button`s in the header row's right cluster before the tags if they exist.

---

## B.1 Status (board `Status.dc.html`)

1. **Header card**: row `space-between; align-items: center` — left: card title 24/32 **Status**
   with muted 12/16 `Index <mono>…</mono> · Lucene` under it; right: **Rebuild index** = `Button`
   `color=Alert` with the `xp-bin` icon (or the icon the page uses today), or the
   `Spinner` + `Rebuilding` `Tag` while a rebuild runs.
2. **Figure tiles**: flex row gap 16 — first tile `flex: 0 0 200px` holding only the health `Tag`
   (success `Healthy` with `xp-check`, alert `Degraded`, or `Spinner` + `Rebuild in progress`),
   vertically centred; then three `Card`s `flex: 1` (muted label / `figure-s` 24/32/700 /
   muted hint): Documents · `In the index now`; Sources · `Content types and external systems`;
   Last external write · `Through the ingestion API` (degraded: Failed writes; rebuilding:
   Started — as today).
3. **Degraded callout** (when applicable): stock `Callout FriendlyWarning` between the tiles and
   the sources card, children in a flex row with the two buttons right (Copy failure details,
   Rebuild).
4. **Documents by source card**: card title 24/32, the stacked bar (own markup, 16px, radius 8,
   2px gaps), then stock `Table` (Source with swatch + bold name + muted kind · Documents right ·
   Share right).
5. **Recent ingestion card**: header row `space-between; align-items: center` — card title
   24/32 left, muted 12/16 `Last 10 entries` right. Stock `Table`: Timestamp mono · Source ·
   Operation sky `Tag` · Count right · Result success/alert `Tag` · Message muted 12/16 (wraps —
   keep the existing narrow-viewport override).
6. Delete `.figureLabel`/`.figureValue`/`.header` leftovers; the module keeps only what the board
   needs.

## B.2 Rule builder (board `RuleBuilder.dc.html`)

1. **Header card**: row `space-between; align-items: center` — left: card title 24/32 (`New rule`
   / the rule name) with muted 12/16 `Index <mono>…</mono>` under it; right: secondary **Cancel** +
   primary **Save rule** (disabled until valid), gap 12. Then a `Divider`, then the **settings
   row**: flex row gap 16 `align-items: flex-end` — Rule name `Input` (`flex: 1`, required mark,
   hint under), `Checkbox` **Enabled** aligned to the input baseline, Priority `Input` (120px, hint
   `Lower wins.`), Runs `DateTimeRangeInput` (280px, hint `Empty = always.`).
2. **Condition flow** (`.flow`, gap 24): label column 200px — `Headline` L **Condition** + violet
   `Tag` **If** in a row (gap 8), muted 12/16 helper under; stack column (gap 16): one `Card` per
   condition with a row `space-between; align-items: center` — summary (14/16 bold title +
   muted 12/16 detail line, gap 4) left, tertiary S **Edit** / **Delete** right (gap 8); then the
   dashed add area (1px dashed `--color-border-default`, radius 8, padding 12, centred tertiary
   **Add condition** with `xp-plus`).
3. **Action flow**: same anatomy — label **Action** + violet **Then**; each action `Card`: header
   row with the drag grip icon + 14/16 bold action title left, tertiary **Delete** right; fields
   row (flex, gap 16, `align-items: flex-end`) with the action's inputs (e.g. Result picker
   `flex: 1`, Position 120px); dashed **Add action** with `xp-chevron-down`.
4. Side panels (condition editor, action picker) keep the QT-3 panel spacing (24px sections,
   footer buttons right, gap 12). The item picker list stays own markup, rows padding 8px 16px,
   14 / 12 type.
5. Narrow (`sm`): label column above its stack (existing media query, gap 16).

---

## Deliverables and checks (both slices)
- Each page's `.module.scss` (Status may move from `.module.css` to `.module.scss`) built from the
  board's values; `layout.test.ts` must stay green (no hex, no off-grid).
- Templates keep every command, state and test id they have today — this is a visual rebuild;
  `PageCommandDiscoveryTests` and the Admin suite must pass unchanged.
- ADR-0020: amend the Consequences paragraph with the boards' file names and the own-markup list
  per page. `docs/internal/screenshot-manifest.md`: rows stay STALE (already marked by UX-2).
- CHANGELOG: `**Changed (admin):** Analytics, Status, Rule builder and Experiment pages rebuilt to
  the approved design boards …` (one line per slice, additive).
- Checks: `npm run typecheck`, `npm run build`, `npm test`, Admin suite. Do NOT start or stop the
  host (27340) or the dev server (3010) — both are lead-managed and serve the main checkout.
- One commit per slice: `feat(admin): analytics and experiment pages rebuilt to the approved boards (UX-3a)` /
  `feat(admin): status and rule builder rebuilt to the approved boards (UX-3b)`.
- Report: files, region → component table per page, decisions taken, suite/build lines, commit.
