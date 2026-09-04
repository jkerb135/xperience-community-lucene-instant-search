# Unit QT-3 — Query tester: pixel fidelity to the approved design, and the injected score

Owner review of the live page 2026-09-03 (after QT-2): "spacing seems off in a lot of places; icons
aren't in the change buttons; score column not stacked; header row of the results card doesn't
look the same; arrows not aligned vertically. Visually check the design against what is
implemented and issue styles to make it look like the approved design. Use flex."

The approved design is `docs/internal/design/QueryTester.dc.html` (open it in a browser). Every
value below is lifted from it. Where the stock `@kentico/xperience-admin-components` component
cannot produce the design (the `Table` has a fixed 48px row and no custom cells, the `Tag` has no
icon slot, the `Callout` puts its action under the text), **the design wins**: the region is
authored as our own markup styled by a CSS module — the precedent is
`src/status/IndexStatusTemplate.module.css` and `src/analytics/ReportTable.scss`, both already in
the webpack pipeline. Record each such region in ADR-0028 (amend, do not rewrite).

**Layout rule from the owner (precise, 2026-09-03):** Kentico components stay — the stock `Table` and any Pagination are never replaced by own markup. Layout-shifting elements are fixed by WRAPPING them in flex / CSS grid utility containers (both support align-items / justify-content) and by targeted styles on our own wrapper (e.g. letting the stock table row grow: `.results :global([class*="table-row"]) { height: auto; min-height: 48px; }`). Example: the Query tester card header is `display:flex; justify-content:space-between` so the title sits left and the index meta right. Never `<table>` markup of our own.

Two slices, two worktrees, two implementers. **A** (styles) never touches Core; **B** (score)
never touches the client.

---

## A. Styles (`unit/qt-3-style`, `src/XpSearch.Admin/Client/src/query-tester/`)

Create `QueryTesterTemplate.module.scss`. Every colour, spacing, radius and font comes from the
package's CSS custom properties (`var(--color-…)`, `var(--spacing-…)`, `var(--radius-…)`,
`var(--typeface-product-primary)`, see the values in the design's `<style>`); no literal hex
except where the package has no token (the design's dark query chip `#151515` =
`var(--color-text-default-on-light)`).

### A.1 Page rhythm
- Cards stacked with a **24px** gap (`Stack spacing={Spacing.XL}`), each card padding 24, radius 16.
- Inside a card, sections are flex columns with **16px** gaps unless stated.

### A.2 Query card (keep the stock components; fix the rows)
- Header: flex row, `justify-content: space-between; align-items: baseline;` card title 24/32 bold
  left, muted 12/16 meta right (`Index … · live tuning · N pipeline stages`).
- Row 1 (margin-top 16): flex row, `gap: 16px; align-items: flex-end;` Query `Input` grows
  (`flex: 1`), Language `Select`, primary Run `Button` (40px tall, aligned to the input bottom).
- Row 2 (margin-top 16): ONE flex row, `gap: 12px; align-items: center; flex-wrap: wrap;`
  Simulate-as tertiary button with the `xp-user` icon, then `Applied:` muted, then the two sky
  `Tag`s, then a spacer (`margin-left: auto`), `Recent:` muted, then the recent tertiary chips.
  Nothing wraps at ≥ 1366.
- Drawer (open): a `Divider` with 16px above and below, then a flex row `gap: 16px;
  align-items: flex-end;` of the three `Select`s and the muted note.

### A.3 Verdict callout
- Keep `Callout type=QuickTip placement=OnPaper` for the tint, radius 16 and the subheadline
  (`Verdict for ‘q’` with the info icon).
- Do NOT use the `actionButton` prop. Children: headline 14/16 bold, then a flex row
  `justify-content: space-between; align-items: center; gap: 16px;` with the body copy
  (16/24, `var(--typeface-product-primary)`) left and the secondary **Create a rule for this
  query** button right. Total vertical gap between subheadline / headline / row = 4px.

### A.4 Pipeline card
- Card padding **16px 24px**; ONE flex row `align-items: center; gap: 12px; flex-wrap: wrap;`.
- `Pipeline` label 14/16 bold, `margin-right: 4px`.
- Query chip: dark (`background: var(--color-text-default-on-light); color: #fff`), 14/16
  `var(--typeface-product-primary)`, padding **8px 16px**, radius 8 (the `Tag` geometry).
- Each stage = one `<span class="stage">` that is itself `display: inline-flex; align-items:
  center; gap: 12px;` holding the arrow AND its chip, so an arrow can never wrap away from its
  chip and is vertically centred on the chip by construction. Arrow: 16×16 inline SVG
  (`M2 8h11M9 4l4 4-4 4`, stroke `var(--color-text-low-emphasis)`, width 1.5, round caps).
- Stage chip: same geometry as the query chip; background `var(--color-background-tag-grey)`,
  white text, `cursor: pointer`; selected = `var(--color-background-tag-sky-blue)`. Label = the
  explanation line, `max-width: 320px; overflow: hidden; text-overflow: ellipsis;
  white-space: nowrap;` with the full line in `title`.
- Selected line's full text: below the row, `flex-basis: 100%`, mono 12/18, padding 12px 16px,
  background `var(--color-background-subtle)`, radius 8, margin-top 4.

### A.5 Results card header (the owner's "header row doesn't look the same")
ONE flex row, `align-items: center; gap: 16px; margin-bottom: 16px;`:
- card title **24/32 bold** `Results for ‘q’` (not Headline S),
- muted stats `<b>5</b> tuned · <b>5</b> raw · <b>5</b> changed · 47 ms / 9 ms` (numbers bold,
  colour default; rest low-emphasis),
- right cluster `margin-left: auto; display: flex; gap: 24px; align-items: center;` = stock
  `Checkbox` **Only changes** (diff view only) and stock `NameToggleButtons` Diff / Side by side.

### A.6 Diff table — stock `Table`, rows allowed to grow
Keep the stock `Table` with `ColumnContentType.Component` cells; wrap it in `.results` and override the hashed row class so rows grow to their content. The target look of a row (the stock look, for reference):

```
.thead  : display:flex; align-items:center; height:48px; padding:0 16px;
          font: 400 14px/16px var(--typeface-product-primary); color: var(--color-text-low-emphasis);
.trow   : display:flex; align-items:center; min-height:48px; padding:8px 16px; box-sizing:border-box;
          background:#fff; border:1px solid var(--color-divider-default); border-radius:8px; cursor:pointer;
.trow + .trow : margin-top:4px;
.trow:hover   : background: linear-gradient(var(--color-hover), var(--color-hover)), #fff;
.trow.selected: background: var(--color-background-selected);   /* #e0dcff */
.trow.dim     : color: var(--color-text-low-emphasis);           /* Unchanged rows */
```
Column flex-bases (design): Tuned **64** · Raw **64** · Change **180** · Result **flex: 1** ·
Score **120** (right-aligned) · Why **300**. Cells are flex items with `min-width: 0`.
- Tuned: 700 `var(--typeface-product-primary)`; Raw: low-emphasis.
- **Change chip with the icon inside** (the owner's "icons aren't in the change buttons") — the ONE place own markup is allowed, because `Tag` has no icon slot; goes in ADR-0028:
  `display:inline-flex; align-items:center; gap:8px; padding:8px 16px; border-radius:8px;
  font: 400 14px/16px var(--typeface-product-primary); color:#fff; white-space:nowrap;` background
  per change: Unchanged `--color-background-tag-grey`, Moved up `--color-background-tag-sky-blue`,
  Moved down `--color-background-tag-yellow`, Added `--color-background-tag-neon-green`, Removed
  `--color-background-tag-rose`. Icon = stock `Icon` (`xp-minus` / `xp-arrow-up` /
  `xp-arrow-down` / `xp-plus` / `xp-ban-sign`) at 16px, white.
- Result: two lines — title 600 14/20, then url mono 12/18 low-emphasis. Ellipsis on both.
- **Score stacked** (the owner's "score column not stacked"): flex column, `align-items:
  flex-end;` final score 600 14/20 on top, then the delta 12/16 low-emphasis
  (`base 0.026` / `−0.027 vs base` / `not in raw` / `not in tuned`).
- Why: 12/16 low-emphasis, may wrap to two lines (row grows; that is why rows are `min-height`).
- Narrow (`sm`): drop the Why column; everything else identical.

### A.7 Side by side
Two stock `Table`s in a two-column flex wrapper `gap: 24px`, each column `flex: 1; min-width: 0`: title 16/24 bold, muted 12/16 subtitle (margin-bottom 12), then the table (position · result · chip only when changed · score).

### A.8 SidePanel (stock `SidePanel size=Stackable`; style the CONTENT)
- Header (the panel's own): headline = title; directly under it the url mono 12/18 low-emphasis.
- Body: flex column `gap: 24px`, padding-top 3px:
  1. change chip (same chip as A.6, icon inside) reading `Moved up · raw #3 → tuned #2` /
     `Added · not in raw ranking → tuned #3`, `align-self: flex-start`;
  2. **How the score was built**: label 14/16 bold, margin-bottom 12; then rows
     `display:flex; justify-content:space-between; gap:16px; padding:4px 0;` label left,
     value right (tabular numerals); every row low-emphasis except the LAST (bold, default
     colour); value column right-aligned.
  3. **Rules that touched this result**: label as above; rows `display:flex;
     justify-content:space-between; align-items:center; padding:8px 16px; border:1px solid
     var(--color-divider-default); border-radius:8px; gap: 8px between rows;` name 600 + kind
     12/16 low-emphasis on the left, tertiary **Open rule** on the right; or the muted
     `None. Only the query-level stages apply.`
- Footer (the panel's `footer` slot): flex row `justify-content: flex-end; gap: 12px;` secondary
  **Bury for ‘q’**, primary **Pin for ‘q’**.

### A.8a Owner additions (2026-09-03)
- **Panel padding**: the SidePanel content must carry the A.8 spacing exactly (24px header, body sides 24px / top 3px, sections `gap: 24px`, score rows `padding: 4px 0` with a 16px label/value gap, rule rows `8px 16px`, footer 24px with the buttons 12px apart, right-aligned). Verify with a row selected.
- **Whole row clickable**: clicking anywhere on a row (Tuned #, Change chip, Score…) opens the panel, not only the Result cell — use the stock `Table` row click for the full `TableRow`; component cells must not swallow the click; `cursor: pointer` across the full row.

### A.9 Checks and deliverables
- `npm run typecheck`, `npm run build` (the SCSS module must compile through the existing
  sass-loader rule; do NOT change webpack config), Admin suite green (`PageCommandDiscoveryTests`
  unaffected).
- **Prove it visually**: start the admin client dev server (`npm start`, port 3010 — the host
  proxies the module to it, `src/appsettings.json` `Mode: Proxy`) and, with the host running on
  27340, use the screenshot tooling in `tools/screenshots` (it has Playwright and the admin login
  flow) to capture the query tester at **1440×1300** after running `espresso`, once with a row
  selected. Save both PNGs under `docs/internal/design/checks/` as
  `query-tester-1440.png` and `query-tester-1440-panel.png` and compare them yourself against the
  design before reporting. If the tooling cannot log in, say so and attach what you could capture.
- ADR-0028 amended: which regions are own markup and why (fixed row height, no icon in Tag, no
  side-by-side action in Callout), flex/grid rule (no table markup).
- `docs/internal/screenshot-manifest.md`: the two query tester rows stay STALE / PENDING.
- One commit: `fix(admin): query tester styled to the approved design - flex rows, iconed change chips, stacked score, panel spacing (QT-3a)`.

---

## B. Injected score (`unit/qt-3-score`, Core only)

A document a pin injects (it did not match the query) currently reports the **id-lookup**
score that loaded it (`PinnedAndBuriedStage.Load` → `hits.ScoreDocs[0].Score`, e.g. 4.060 next
to hits scoring 0.4), and its steps are `Lucene score 4.060 → pin`. That number is meaningless
to the user.

Decided behaviour:
- The injected document's `ScoredDocument.Score` = the value of `searcher.Explain(BaseQuery,
  docId)` — **0** when the document does not match the query at all, its real score when it
  matched but was off the page.
- Its steps are computed exactly like every page document's: one explain per
  `ScoreCheckpoint` (extract the per-document loop of `ScoreBreakdownStage` into an internal
  static helper, e.g. `ScoreBreakdown.StepsFor(IndexSearcher, SearchContext, docId)`, used by
  both stages), then the pin step `rule:… → #N` appended with the final score. So a
  non-matching injected document reads `Lucene score 0.000 → rule:Demo: Espresso accessories → #3 0.000`.
- `AppliedRules` unchanged. `Total` handling unchanged.
- `ranking.baseScore` for such a hit = 0 (first step), `score` = 0.
- Tests (Core): injected non-matching document → score 0, steps `["Lucene score", "rule:… → #3"]`
  both 0; injected matching-but-off-page document → its explain score, steps carry the boost
  checkpoints that apply; page documents untouched (existing `ScoreBreakdownTests` green).
- CHANGELOG `**Fixed (core):** an injected (pinned-in) result now reports its score under the
  query (0 when it does not match) instead of the id lookup's score`.
- The KNOWN-LIMITATIONS entry for `ScoreBreakdownStage` gains nothing new unless you find a
  ceiling; remove any sentence it has about injected documents if one exists.
- One commit: `fix(core): injected pinned documents are scored and stepped under the query, not the id lookup (QT-3b)`.

Report per slice as before: files, decisions taken, suite lines, build/screenshot outputs,
commit hash.
