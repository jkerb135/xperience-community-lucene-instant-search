# ADR-0025 — Shape of the popularity signal, its cap, and its boundary with experiments

- **Status:** accepted (unit RK-1)
- **Context:** owner amendment `docs/spec/amendments/2026-08-31-analytics-relevance.md`

## Context

The library already collects everything a "learning to rank, lite" needs: an anonymous query log with
one row per search, and a click event carrying the clicked result and its position. The amendment
asks for that evidence to become a bounded query-time boost, plus suggested rules a human approves —
at zero external cost, with no new dependency and no AI service.

## Decision

**The click source is the query log, extended with the clicked result id.** The log already stored
the clicked *position*; the clicked *document* only existed on the consent-gated `xpsearch_click`
activity, which is per contact and not queryable as an aggregate. Rather than introduce a second
click source, `XpSearch.QueryLog` gains one nullable `LogClickedResultID` column, written by the same
event that already writes the position. The signal is therefore computed from the same rows every
analytics figure is computed from, and is available for visitors who never consented to tracking
(the row holds a document id, not a person).

**Position damping: `weight = log2(position + 1)`.** A click at position 8 is worth ~3.2, a click at
position 1 is worth 1.0. The point is to remove the old ranking's own bias: a click on the first
result is what the ranking already suggested, while a click eight rows down is a visitor overruling
it. An unknown position counts as position 1, which is the smallest weight this function can produce
— the most conservative reading of ambiguous evidence.

*Deviation recorded:* the amendment and the unit spec both name `1 / log2(position + 1)` while also
requiring that "a click at position 8 outweighs one at position 1" and that "unknown position counts
as position 1 — the most conservative read". The reciprocal form satisfies neither: it makes position
1 the *largest* weight. `log2(position + 1)` satisfies both stated behaviours, so the behaviour was
implemented and the literal formula was not.

**Decay by omission.** Each run aggregates the last `PopularityLookbackDays` (default 30) and
replaces the index's rows wholesale. Popularity that stops happening disappears at the next run. No
half-lives, no exponential schedules, nothing to tune.

**The signal keeps the strongest 100 documents per index** (`PopularityDocumentLimit`). It bounds the
stored rows and, more importantly, the boosted query: the stage adds one SHOULD clause per scored
document, and an unbounded signal would grow the query with the site's traffic.

**Cap: the top document reaches 2.0x, the rest scale linearly.**
`factor = 1 + (score / topScore) * (2.0 - 1.0)`. A document with no evidence is untouched. The
boost is applied exactly the way a rule's boost is (a SHOULD clause on the document id next to the
query, Lucene 4.8 has no `BoostQuery`), so popularity is expressed in the same currency as the rules
a marketer already writes, and it can lift a document but never suppress a better text match.

**Opt-in is a property of the index, not of a tuning variant.** `XpSearch.PopularityIndex` holds one
row per index with the flag and the last computed-at. It is deliberately *not* a fifth
variant-cloned tuning type (ADR-0024): an XP-1 experiment must test the tuning the marketer wrote,
not the popularity signal, so both variants of a running experiment see the same boost. Per-variant
popularity is a possible future and is not built.

**Cache key.** The signal version (the computed-at ticks) joins the response cache key only when the
index has opted in — an index that has not reports version zero and keys exactly as it did before
RK-1. A task run therefore invalidates the cached responses of opted-in indexes and nothing else.

**Suggestions are stored by the run, never applied.** For the window's 10 most frequent queries, a
document is suggested when it takes at least 5 clicks *and* at least 50% of that query's damped click
mass. Approving one writes an ordinary rule (query `is` + boost 2.0x) through the existing rule
storage; dismissing one marks the row. Both answers are remembered per query+document pair, so a
recomputation never resurfaces a suggestion a human has already answered.

## Alternatives rejected

- **Reading the `xpsearch_click` activities** — consent-gated, per contact, and a second click source
  that would disagree with every existing analytics figure.
- **A multiplicative rescoring pass over the hits** — needs a custom collector or a post-execution
  re-sort that breaks paging and totals; the SHOULD clause is the mechanism the rules already use.
- **Exponential time decay** — one more constant to explain and to tune for the same practical effect
  as a lookback window.
- **Auto-applying strong suggestions** — the amendment forbids it, and a rule nobody chose is a rule
  nobody can explain when it misfires.
