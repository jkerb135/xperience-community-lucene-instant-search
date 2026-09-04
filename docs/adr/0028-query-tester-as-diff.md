# ADR-0028 — The query tester is a diff, and the pipeline explains itself

- **Status:** accepted (unit QT-2)
- **Context:** the owner's redesigned prototype `docs/internal/design/QueryTester.dc.html` is the
  source of truth for the page; where the library could not supply what the prototype shows, the
  library was extended

## Context

The query tester shipped as two ranked lists side by side, each with its own score column and its own
explanation lines. Reading it meant holding two lists in your head and comparing them by eye, and the
one number that would have made the comparison trivial — the score before tuning — was wrong:
`ranking.baseScore` reported the **final** score, because every boost is folded into the query before
the search runs, so `score` and `baseScore` were always identical.

The prototype answers the question the page exists for ("did my rule do what I meant?") in a different
shape: one list holding two rankings, a verdict in words, and a way to act on any row without leaving
the page.

## Decisions

**1. One list, two rankings.** The default view is a diff table: every document with its tuned
position, its raw position, a change marker, the score with the delta against the pre-boost score, and
the rules that applied to it. *Side by side* keeps the old two-column view for people who want to read
the rankings as lists; the page never shows both views at once. *Only changes* hides the untouched
rows (diff view only). Unchanged rows are low-emphasis, never hidden by default: "nothing changed" is
an answer.

**2. A verdict before the data.** A `Callout` (`QuickTip`, `onPaper`) says what the tuning did in one
line — *Tuning changed 3 of 6 results*, with the tally, or *Tuning made no difference to this query* —
because that is the sentence a marketer would otherwise have to derive from two tables.

**3. Per-stage scores are computed, not read.** Lucene returns one score per hit and every boost is
already inside the query that produced it, so "the score after each stage" cannot be read off the
search. Each scoring stage instead leaves a **checkpoint** (`SearchContext.ScoreCheckpoints`: a label
and the query as it stood after that stage), and a new `ScoreBreakdownStage` (order 850, between
`Execute` and `PinnedAndBuried`) asks Lucene to `Explain(checkpointQuery, docId)` for every document
on the page. A page of ≤ 50 hits and a handful of checkpoints is a few hundred cheap explains, and it
only runs for `explain=true` — the tester always sets it, visitors never do. The result travels on the
contract as `ranking.steps` (additive), so any API consumer gets the same breakdown.

Two consequences worth writing down:

- A boost is a `SHOULD` clause, so Lucene's coordination factor **lowers** the score of every document
  the rule does not name. That is a real step of the score and is shown as one; a rule is listed under
  *Rules that touched this result* only for the documents whose score it **raised**.
- The breakdown is explained against a second searcher lease, taken after the search released its own.
  See `docs/internal/KNOWN-LIMITATIONS.md`.

**4. `baseScore` is the raw Lucene score.** It is now the first score step, which is what its
documentation has promised since spec §4.2. This is a **fix**, and it changes what an existing
consumer reads: `score - baseScore` was always `0` and is now what the tuning did.

**5. Acting on a row never leaves the page.** The row detail `SidePanel` offers **Open rule** per rule
that touched the result, and **Pin for '<query>'** / **Bury for '<query>'** in its footer; the verdict
offers **Create a rule for this query**. All four are page commands that navigate to the rule builder,
seeded through `RuleSeed`, which grew three optional segments (`action`, `target`, `position`) while
its two-segment form still decodes.

## Component mapping (ADR-0020: no hand-rolled markup where a stock component exists)

| Region | Component |
|---|---|
| Header, pipeline, results, rule rows | `Card` (+ `Headline`) |
| Verdict | `Callout` `type=QuickTip` `placement=OnPaper`, `subheadline` / `headline` / `actionButton` |
| Error | `Callout` `type=FriendlyWarning` `placement=OnDesk` |
| Query row | `Input` (`markAsRequired`), `Select`, primary `Button` (`inProgress`, `disabled`) |
| Simulate-as drawer | tertiary `Button` with `icon`, `Divider`, `Select`s, applied choices as `Tag`s |
| Recent chips | tertiary `Button`s, from `localStorage` (`xpsearch.query-tester.recent.<index>`, cap 5) |
| Pipeline trail | `Tag` (clickable) separated by `Icon name="xp-arrow-right"` |
| View toggle / filter | `NameToggleButtons`, `Checkbox` |
| Both tables | `Table` with `ComponentCell`s and `onRowClick` |
| Row detail | `SidePanel` `size=Stackable` (`Full` under `sm`), with `footer` |
| Text treatments | `src/theme.ts` only (`muted`, `mono`, `flexRow`) |

### QT-3a amendment — styled to the design, still on the stock components

The owner's review of the live page (2026-09-03: *"spacing seems off in a lot of places; icons aren't
in the change buttons; score column not stacked; header row of the results card doesn't look the
same; arrows not aligned vertically"*) was answered **without replacing a single component**. The
layout is supplied by flex containers of our own and by a handful of targeted rules on our own
wrappers, in `Client/src/query-tester/QueryTesterTemplate.module.scss` (the
`IndexStatusTemplate.module.css` / `ReportTable.scss` precedent). The owner's rule: rows are flex or
grid, never `<table>` markup, and a stock component is never re-implemented.

What the module overrides, and why each override is on a wrapper of ours rather than a fork:

| Region | Stock component | Wrapper / override |
|---|---|---|
| Card headers (query, results) | `Card` `headline` slot (the only 24/32 bold text the package has — `Headline` size L is 16/24) | flex row inside the slot; `card-body` padded 16 instead of 24 so the design's 16px header-to-row gap holds |
| Pipeline card | `Card` with no headline | `card-body` 16 top, card 16 bottom → the design's `16px 24px` |
| Verdict | `Callout` `QuickTip` `OnPaper`, children only | the callout's inner `Stack` spacing set to 4px; the body copy and the **Create a rule** button in one `space-between` row (the `actionButton` prop stacks it underneath) |
| Pipeline trail | `Tag` (dark query chip = `Colors.TextDefaultOnLight`, stage chips grey / sky), stock `Icon` arrows | each arrow **and** its chip in one `inline-flex` span, so an arrow can never wrap away from its chip and is centred on it by construction |
| Diff / side-by-side lists | `Table` with `ComponentCell`s and `onRowClick` | the fixed 48px row is relaxed to `height: auto; min-height: 48px` with 8px cell padding, which is all the two-line cells (title over url, score over delta) need; each cell root states its alignment (Score right, the rest left) against the shell's centred cell |
| Row detail | `SidePanel` `Stackable`, children only | body is a 24px flex column; score rows `space-between` with 4px padding; rule rows bordered `8px 16px`; footer a right-aligned 12px row |

**The one piece of own markup is the change chip.** `Tag` has no icon slot, and the owner's first
complaint was that the icon was not inside the button; the chip is therefore our own `<span>` with
the stock `Icon` inside it, styled to the `Tag`'s exact geometry (14/16 product typeface, `8px 16px`,
radius 8, the `--color-background-tag-*` token per change). It is `pointer-events: none`, so the
row — not the chip — takes the click.

Two QT-2 constraints are lifted by the row-height override and no longer hold: **two-line cells** are
back (Result is title over url, Score is the value over its delta), and the **selected row** is
filled with `--color-background-selected` again. `Table` still only offers selection through
checkboxes, so the marker is applied from the cells but the **fill is on the row**: every component
cell carries `data-row-selected` when its row is the open one, and the module fills the row that
`:has()` one (filling the cells instead left the stock cell padding as white gaps between six lilac
patches). The row keeps its hover, at `--color-background-selected-hover`.

The stock cell also inherits a centred `text-align` from the admin shell, so each component cell's
root states its own alignment: Tuned, Raw, Change, Result and Why read left, Score reads right, and
the header captions are aligned to match (`.results` overrides the caption row, with Score picked
out by its position). Vertical centring stays with `align-items: center` on the cell root, which is
that flex row's cross axis.

Two badges could not be honoured literally, so the ADR-0020 rule applies — nearest stock component,
recorded here:

- The prototype's **dark query tag** has no tag token, but `Colors.TextDefaultOnLight` *is* the
  prototype's `#151515`, so the stock `Tag` carries it as its background (QT-3a; QT-2 used
  `BackgroundTagDefault`, which is orange on the light theme).
- The prototype's **selected table row** is a filled row. `Table` only offers selection through
  checkboxes (`selectable`), which would add a column the page has no use for; QT-3a fills the row
  from its cells instead (`data-row-selected` + `:has()`, above).
- The **column widths are still pinned**: a cell is `min-width: <units>x8px` plus 16px of padding
  either side inside a grid of `auto` tracks, so a column whose `maxWidth` is larger than its
  `minWidth` grows with its content and pushes the row past the card (a 1026px row inside an 887px
  card, at a 1366px viewport). Every column pins `maxWidth` to `minWidth`: 86 units for the diff
  table (882px, laid out as the design's 64 · 64 · 176 · 304 · 120 · 152) and 36 for each
  side-by-side table (418px). Single-line text inside a cell still ellipsizes and carries a `title`.

## Consequences

- A consumer's own scoring stage joins `ranking.steps` with one line
  (`context.ScoreCheckpoints.Add(new ScoreCheckpoint("My boost", context.BaseQuery))`); it is
  documented in the pipeline-extension guide.
- The admin's `QueryTesterSideResult` carries `AppliedRules`, captured from the context by the tester's
  own terminal stage — the response carries the steps but not which rule caused them.
- Screenshots of the query tester are stale; the manifest records it.
