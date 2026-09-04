# Unit UX-2 — Every custom admin page follows the query tester's layout guidelines

Owner 2026-09-04, after QT-3: "make sure everything else follows these guidelines." The
guidelines are the ones QT-3 established and the owner refined (ADR-0028, QT-3 spec); they apply
to every custom client template in `src/XpSearch.Admin/Client/src/`: **Analytics dashboard**,
**Index status**, **Rule builder** (+ its panels and picker), **Experiment detail**. The query
tester is already compliant and is the reference implementation
(`query-tester/QueryTesterTemplate.tsx` + `.module.scss`).

The lead audited all four pages on the live host at 1440px on 2026-09-04; the findings in §2 are
measured, not guessed. The owner's Claude Design artboards for these pages (1a–1d Analytics,
3a–3c Status, 5a–5h Rule builder) are not in the repo, so this unit enforces the guidelines,
not board-level pixel parity; the lead has asked the owner for the boards.

Read `docs/internal/agent-primer.md`, ADR-0020, ADR-0028 and `docs/internal/units/QT-3.md`
(section A and the "Layout rule" paragraph). Work only in `.claude/worktrees/ux-2`
(branch `unit/ux-2`).

## 1. The guidelines (G1–G6) — write them into `docs/guides/admin-client-development.md`

Add a section **"Layout guidelines for custom pages"** stating, in this order:

- **G1 Kentico components stay.** `Table`, `Pagination`, `Card`, `Callout`, `SidePanel`, `Tag`,
  `Button`, `Input`, `Select`, `Checkbox`, `NameToggleButtons`, `Headline`, `Icon`, `Divider`,
  `Stack` are never re-implemented. Own markup only where no component exists (a chart, a
  stacked bar, a drag handle, an iconed chip) and each such region is listed in the page's ADR.
- **G2 Layout is done by wrappers.** Flex or CSS grid containers of our own (a `.module.scss`
  per page) set direction, `gap`, `justify-content`, `align-items`; stock components are placed
  inside them. Overrides of a stock component's geometry hook the hashed class prefix
  (`:global([class*="table-row___"])`) and are recorded in KNOWN-LIMITATIONS.
- **G3 One spacing rhythm, on the 8px grid.** Page sections / cards: **24px** (`Stack
  spacing={Spacing.XL}` or `gap: 24px`). Sections inside a card: **16px**. Inline groups: 8 / 12 /
  16px. Card padding is the stock 24px; **no page-level padding of our own** (the shell pads the
  page). Never 10 / 14 / 20 px.
- **G4 Page header pattern.** A flex row, `justify-content: space-between; align-items:
  baseline` (or `center` when the right side is a button): title left (card headline 24/32 when
  the page starts with a card, `Headline size=L` otherwise) with the muted meta line
  (`Index … · …`) beside or directly under it, actions right.
- **G5 Tokens only.** Colours come from the package's `--color-*` custom properties; **no literal
  hex, and no `var(--x, #fallback)` fallbacks** (a fallback hides a wrong token name — verify the
  name exists in `node_modules/@kentico/xperience-admin-components/dist/entry.js`). The package
  has colour tokens only; spacing and radii are the design's literals on the 8px grid
  (4 / 8 / 16 radius). Font sizes from the ramp: 11, 12, 14, 16, 24; weights 400 / 600 / 700;
  product typeface `"GT Walsheim", sans-serif` for headlines, tags, buttons; Inter for body.
- **G6 Text treatments from `src/theme.ts`** (`muted`, `figure`, `flexRow`); add a treatment
  there rather than an inline `style={{…}}` literal in a template.

## 2. Findings per page (fix all of them)

### 2.1 Analytics (`analytics/AnalyticsDashboardTemplate.tsx`, `ReportTable.tsx`, `VolumeChart.tsx`)
- Cards are stacked at **12px** (measured: filter card → chart card 12px, chart → zero-result
  table 12px, table → table 12px). G3: 24px.
- `VolumeChart.tsx` has four inline `style={{…}}` literals (legend row, swatch, svg block,
  axis row) — move them to a `VolumeChart.module.scss` (or the analytics module) per G6; the
  swatch colour stays a data-driven inline `background` (it is data, not style).
- `ReportTable.scss` (`.pager` flex) is fine — keep; rename to `.module.scss` only if it is
  imported as a module today (check; do not churn).
- Header: `Headline L` + meta under it is the ADR-0020 pattern for this page — keep, but wrap in
  the G4 header row so the Load/Range controls card starts 24px below.
- KPI tiles row: keep `Row`/`Column` (`Col3` → `Col6` at `sm`); confirm the tile spacing is 16px
  and the tile figure uses `figure` from `theme.ts`.

### 2.2 Index status (`status/IndexStatusTemplate.tsx`, `IndexStatusTemplate.module.css`)
- `.page { padding: 16px }` — remove (G3: the shell pads the page).
- Card gaps are inconsistent (figures card → sources card 24px, sources → ingestion **12px**);
  make every card gap 24px through one `Stack spacing={Spacing.XL}` (the owner's own stashed
  edit on this file did exactly this — `stash@{0}` on main: `Stack spacing={Spacing.L}` wrappers
  replacing the css gaps; adopt the Stack, at XL for cards / L inside).
- `.columns { gap: 20px }` → 24px; `.header { margin-bottom: 16px }` → the Stack gap.
- `.figureValue { font-size: 20px; font-weight: 600 }` → the `figure` treatment (32/38/700) or,
  if the board's tile is smaller, 24/32/700 — pick 24/32/700 (a status figure is not a KPI).
  `.figureLabel` 12px → `muted`.
- `.note` / `.alertNote` margins 16 / 8 → keep (on grid).
- The stacked "documents by source" bar and the swatches are own markup (no component) — keep,
  list in ADR-0020's "Consequences" as such.
- Convert the file to `.module.scss` only if you touch more than half of it; otherwise keep
  `.module.css`.

### 2.3 Rule builder (`rule-builder/RuleBuilderTemplate.module.scss` and the templates using it)
- `.page { padding: 16px; gap: 20px }` → no padding, gap 24px.
- Off-grid gaps: `.flowStack 14px` → 16; `.summaryRow 14px` → 16; `.toggleFields 10px / padding-left 46px`
  → 8 / 48; `.panelFooter 10px` → 12; `.addArea padding 10px` → 12; `.pickerOption padding 10px 14px`
  → 8px 16px; `.filterIs padding-bottom 10px` → 12; `.flow` narrow `gap: 12px` → 16.
- Off-ramp type: `.pickerTitle 13.5px` → 14; `.pickerUrl 11.5px` → 12.
- **Hex fallbacks removed everywhere** (G5): `#7d3fa0`, `#f5f0fa`, `#8f8f8f`, `#262626`,
  `#b5b5b5`, `#e2e2e2`, `#ececec`, `#b42318`. `--color-border-selected` **does not exist** in the
  package — use `--color-product-selected` for the drag insertion line / lifted outline;
  `--color-text-default` exists (alias) but prefer `--color-text-default-on-light`;
  `--color-border-default`, `--color-background-selected`, `--color-text-low-emphasis`,
  `--color-alert-text`, `--color-divider-default` exist — use them bare.
- Header row (`.header`) already follows G4 — keep.
- The drag grip, insertion line, dashed add area and the item picker list are own markup (no
  component) — list them in ADR-0022 (rule engine) or ADR-0020, whichever documents the builder.

### 2.4 Experiment detail (`experiments/ExperimentDetailTemplate.tsx`)
- The status card and the two variant cards touch: measured card 2 ends at y=272 and the
  variant cards start at y=272 — **0px gap**. Wrap the page in `Stack spacing={Spacing.XL}`.
- Variant cards: title 24/32 bold is right; the KPI mini-tiles inside use dashes for empty
  values — keep; check tile spacing is 16px and figures use `figure` / `muted`.
- Callout stays stock.

### 2.5 Everything else
- Grep every template under `src/` for `style={{` and `px` literals that are not on the 8px grid
  and fix them the same way; report any you deliberately left (with the reason).
- `src/theme.ts`: add what G6 needs (e.g. `cardTitle` 24/32/700 if a page needs the card-title
  outside the headline slot); delete nothing that is still used.

## 3. Deliverables and checks
- `docs/guides/admin-client-development.md` gains §1 (wiki-ready wording, no internal unit ids).
- ADR-0020 "Consequences": one paragraph naming the own-markup regions per page (status bar,
  chart, rule-builder grip/insert/add-area/picker) and pointing at the guideline section.
- `docs/internal/screenshot-manifest.md`: mark the Analytics, Status, Rule builder and Experiment
  detail rows STALE (layout changed); list them in the report.
- CHANGELOG `**Changed (admin):** custom pages follow one layout guideline (24px card rhythm, no
  page padding, tokens only) …`.
- Checks: `npm run typecheck`, `npm run build`, Admin suite. Then **measure**: with the host on
  27340 proxying the module to the dev server on 3010 (both owner-managed; do not stop them —
  the dev server serves the MAIN checkout, so your bundle is not what it shows), verify from a
  static harness or by reading computed values after the lead merges; in your report give the
  intended card-gap and header values per page and where each literal moved.
- One commit on `unit/ux-2`: `fix(admin): analytics, status, rule builder and experiment pages follow the layout guidelines (UX-2)`.
- Report: files changed, per-page before → after table of gaps/paddings/fonts, removed hex list,
  suite/build lines, stale screenshot rows, commit hash, anything you left and why.
