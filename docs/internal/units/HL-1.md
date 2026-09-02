# Unit HL-1 — fuzzy-query highlighting cost

The PF-1 bench measured a real defect: with typo tolerance (FZ-1) on, highlighting IS the
search. At 10k docs, single-term p50: exact no-highlight 1.99 ms; exact WITH highlight
2.76 ms; fuzzy no-highlight 4.85 ms; fuzzy WITH highlight **135.18 ms** — ~170× the
exact-highlight cost, roughly flat across corpus sizes (135/137/167 ms at 10k/100k/1M), i.e.
it scales with page size × highlighted fields, not documents. The four matched workload rows
are permanent in `tests/XpSearch.Bench` and `docs/internal/perf-results-2026-09-01.md` —
they are your before/after instrument. Library unit (Core), worktree branch `unit/hl-1`
(already created for you). Read `docs/internal/agent-primer.md` first.

## Root cause (verify, then fix the mechanism you verify)

`src/XpSearch.Core/Highlighting/LuceneHighlighter.cs:45` — `Highlight` is called once per
document (`HighlightStage`), and each call constructs
`new Highlighter(new SimpleHTMLFormatter(...), new QueryScorer(context.BaseQuery))`. With a
fuzzy request, `context.BaseQuery` holds one `FuzzyQuery` per searchable field (FZ-1), and
`QueryScorer`'s term extraction expands multi-term queries — per document, per field, against
each document's own text (Lucene's `WeightedSpanTermExtractor` without a reader rewrites
against a `MemoryIndex` of the fragment text). The expansion is the ~130 ms; the allocation
is noise. Confirm this attribution before changing anything (the bench makes that a
ten-minute experiment).

## The fix

Rewrite the query ONCE per request against the real index reader, so what reaches the
highlighter is already concrete terms; construct the scorer once per request while you are
at it.

- **Prefer no public-surface break.** `IHighlighter.Highlight`'s per-document signature can
  stay if the per-request work is computed once and reused — e.g. `SearchContext` gains a
  lazily-computed rewritten-query (or prepared-highlighter) member the stage/highlighter
  share; you pick the seam, but it must be honest (no hidden static/weak-table magic keyed
  off context identity). If the clean fix genuinely needs an `IHighlighter` shape change
  (it is a public replaceable seam), STOP and report the proposed signature first — that is
  a source-breaking event with its own CHANGELOG convention.
- Reader access at highlight time: `HighlightStage` runs after execution — check what the
  stage/context still hold (searcher lease scope!). If the rewrite must happen while the
  searcher lease is open, do it at execute time and carry the result on the context; do not
  re-lease per document.
- **Semantics pinned:** FZ-1's real-fixture highlighter test (expresso →
  `<mark>espresso</mark>`) must stay green — a query rewritten against the reader yields the
  concrete matched terms, which is exactly what should highlight. The HTML-encode-first
  invariant in `LuceneHighlighter` (its remarks block) is untouchable. Exact-query
  highlighting must not regress (it is 2.76 ms today; a once-per-request rewrite is noise).

## Verification

- **Bench before/after is the acceptance test:** run
  `dotnet run --project tests/XpSearch.Bench -c Release -- --sizes 10k --runs 3
  --iterations 100` on your branch; the fuzzy-with-highlight row must land within a small
  multiple of exact-with-highlight (single-digit ms expected), with the other three matched
  rows unmoved (state the numbers in your report; do NOT overwrite the committed 2026-09-01
  results artifact — it is the historical record; a new dated artifact from your run is
  welcome).
- All five C# suites green (Core especially: FZ-1's highlighter fixtures, HighlightStage
  tests, cache behavior — the rewritten query must NOT leak into the cache key or the
  response). JS untouched.
- Docs: KNOWN-LIMITATIONS entry for this cost REMOVED; `performance-and-sizing.md`'s
  typo-tolerance cost paragraph updated to the fixed numbers (note the fix, keep the honest
  tone); grep for other places the 135 ms figure or "highlighting dominates" landed
  (relevance-tuning.md cross-link, CHANGELOG's PF-1 entry stays as history). CHANGELOG
  **Fixed (core)** entry with the before/after.
- No host-pass checklist section expected (FZ-1's §R already covers fuzzy behavior in the
  browser; nothing here is browser-observable beyond speed).
- Commit this spec with the unit (copy from `docs/internal/units/HL-1.md` on main if your
  worktree predates it).

## Constraints

- Core only. No new dependencies. No contract change. The public `IHighlighter` seam
  changes only via the STOP-and-report path above. Kentico docs MCP for any Xperience
  question. Never touch `src/Components/Widgets/CardWidget/`. Host is out of scope.
