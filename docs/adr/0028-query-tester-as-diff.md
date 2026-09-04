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

Two badges could not be honoured literally, so the ADR-0020 rule applies — nearest stock component,
recorded here:

- The prototype's **dark query tag** has no token: `Colors` offers no black tag background, so the
  query chip uses `BackgroundTagDefault`.
- The prototype's **selected table row** is a filled row. `Table` only offers selection through
  checkboxes (`selectable`), which would add a column the page has no use for, so selection is shown
  by emphasising the row's title and by the panel being open on it.

## Consequences

- A consumer's own scoring stage joins `ranking.steps` with one line
  (`context.ScoreCheckpoints.Add(new ScoreCheckpoint("My boost", context.BaseQuery))`); it is
  documented in the pipeline-extension guide.
- The admin's `QueryTesterSideResult` carries `AppliedRules`, captured from the context by the tester's
  own terminal stage — the response carries the steps but not which rule caused them.
- Screenshots of the query tester are stale; the manifest records it.
