# Unit RK-1 — Popularity boosts (learning-to-rank lite)

Owner-approved amendment `docs/spec/amendments/2026-08-31-analytics-relevance.md` — read it
first; it is the authority. SY-1 (synonym mining) is a SEPARATE later unit that will extend the
scheduled task you build here — design the task so a second aggregation can join it, but build
nothing SY-1-specific.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/rk-1`).
Zero-external-cost constraint: no AI services, no new dependencies — the signal comes entirely
from data the library already collects.

## 1. Aggregation (scheduled task)

- A scheduled task following the AN-1 retention task's registration pattern aggregates the query
  log's click data per index into a per-document signal: for each clicked document id, sum
  `1 / log2(position + 1)` per click (position bias: a click at position 8 outweighs one at
  position 1; a click with unknown position counts as position 1 — the most conservative read).
  Find where clicks/positions actually live by following `SearchAnalyticsService`'s
  click-through computation — do not invent a second click source.
- Configurable lookback window (default: 30 days) so stale popularity decays by omission —
  simplest honest decay; no exponential schemes.
- Signal stored in a new module class (AD-1 module pattern; `RegisterObjectType` +
  installer columns like XP-1a's): index, document id, score, computed-at. The task fully
  replaces an index's rows per run (idempotent). Store a per-index **signal version**
  (computed-at ticks serve) for the cache key.
- Structure the task so SY-1 can later add a second aggregation pass without rework — a seam,
  not an abstraction: e.g. the task walks the window's rows once and hands them to registered
  aggregators, or simply keeps the aggregation in one well-named method SY-1 can add a sibling
  to. Your call; smallest thing that doesn't paint SY-1 into a corner.

## 2. Boost stage (opt-in per index)

- A pipeline stage after the existing tuning stages applies the signal as a query-time boost,
  **bounded**: normalize the raw signal to a multiplier capped so popularity can never drown
  text relevance (cap the boost factor at 2.0× for the top document, scale the rest linearly —
  state the formula in the ADR). Empty signal table → stage is a no-op.
- **Opt-in flag per index**, surfaced on the existing Field weights page ("Boost by popularity"
  toggle), default OFF. Storage: an index-scoped setting — deliberately NOT one of the four
  variant-cloned tuning types, so an XP-1 experiment tests tuning, not popularity (record this
  boundary in the ADR; per-variant popularity is a possible future, do not build it).
- Response cache key gains the signal version ONLY when the flag is on (PZ-1/XP-1a mechanism) —
  so a task run invalidates cached responses for opted-in indexes and nothing else changes.
- Boost applies identically to experiment variants A and B.

## 3. Suggested rules (never auto-applied)

- For the top-N frequent queries of the window (N configurable, default 10), when one document
  clearly wins that query's clicks (define "clearly" simply and state it — e.g. ≥50% of the
  query's position-damped click mass and ≥5 clicks), surface it as a **suggested boost rule** on
  the AD-1 Rules listing: a separate "Suggestions" area above/beside the table showing query,
  document, evidence ("N clicks, M% of mass"), with **Approve** (creates an ordinary rule via
  the existing rule storage — pre-filled query condition + boost/pin action, owner edits like
  any rule) and **Dismiss** (persisted; a dismissed suggestion never resurfaces for that
  query+document pair even after recomputation).
- Suggestions are stored by the task run (same module-class family), not computed at page load.
- Live rules listing only — no variant-B suggestion surface.
- Primer gotchas are load-bearing: new `[PageCommand]`s as plain methods on final classes;
  labelled Buttons.

## 4. Docs

- New wiki-ready guide page (popularity boosts: what the signal is, the damping, the cap, the
  opt-in, suggested rules workflow, the honest ceilings) + cross-link from relevance-tuning.md
  and analytics.md. ADR for signal shape + cap + experiment boundary, numbered after the latest.
- CHANGELOG `[Unreleased]`; KNOWN-LIMITATIONS for real ceilings (e.g. click data reflects the
  old ranking — feedback-loop bias; whatever else is honest).

## Deliverables

- Code + tests: damping math, replace-per-run idempotence, cap/normalization, no-op on empty
  signal, cache-key on/off, suggestion threshold + dismissal persistence, stage ordering.
  Admin client (if the Suggestions area needs React) must pass strict TS + webpack.
- All C# suites + Admin client build green; JS widgets untouched. Conventional commits on
  `unit/rk-1`; commit this spec file.

## Constraints

- No new dependencies; no contract changes; do not touch experiment/bucketing code. Kentico docs
  MCP for scheduled-task and admin API questions.
- The DB-round-trip parts (task against real tables) may be untestable outside the container —
  follow the XP-1a precedent: test the pure logic, record the host-pass item.
