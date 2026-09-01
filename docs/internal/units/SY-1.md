# Unit SY-1 — Synonym mining from query reformulations

Owner-approved amendment `docs/spec/amendments/2026-08-31-analytics-relevance.md` (read first;
note the 2026-09-01 correction footnote — it does not affect this unit). RK-1 is merged: the
scheduled task `XpSearchPopularityTask` reads the lookback window's query-log rows once per run
and hands each index's rows to `PopularityAggregator.Aggregate`; its remarks explicitly reserve
a SIBLING aggregation called from the same loop for this unit. Extend that seam — do not build a
second task or a second window read.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/sy-1`).
Zero-external-cost: no AI services, no new dependencies. WordNet seeding was considered and
rejected by the amendment — do not add it.

## 1. Correlation ground truth (INVESTIGATE FIRST, then build)

A reformulation pair is: a query that failed (zero results, or no click) followed shortly by a
different query from the SAME visitor that succeeded (got a click). Before building, establish
what visitor/session correlation the query log actually offers:

- Inspect `XpSearchQueryLogInfo` / the journal / what the JS client sends with searches and
  click events. If a usable same-visitor correlator already exists (contact id, journal chain,
  anything), use it.
- If none exists: use **time adjacency per index** — failed query followed within a
  configurable window (default 60 seconds) by a different query that got a click, evaluated in
  timestamp order. No new cookie, no new visitor identifier, no consent surface (this is the
  deciding constraint: do NOT add visitor linkage to the query log for this).
- Whichever you find, state it in the report and the ADR. Noise from adjacency (two different
  visitors interleaving) is accepted because of the occurrence threshold below.

## 2. Mining (sibling aggregation in the RK-1 task)

- Candidate pair = (failed query → succeeded query), normalized the way the log normalizes
  queries; ignore pairs where one text contains the other (prefix typing: "coff" → "coffee" is
  autocomplete behaviour, not a synonym) and pairs differing only by case/whitespace.
- A pair must occur at least `MinimumOccurrences` times (configurable, default 3) across the
  window before it becomes a suggestion.
- Suggestions stored per index in a module class (join the RK-1 popularity family/installer
  pattern; replace-per-run for pending rows), with occurrence count and last-seen. Approved and
  dismissed pairs are remembered and never resurface (RK-1's
  `PopularitySuggestionMerge.Pending` is the exact precedent — reuse or mirror it).

## 3. Admin surface

- The AD-1 **Synonyms** page (live variant only) gains a "Suggestions" area — follow RK-1's
  choice: a sibling stock listing page next to the Synonyms listing (RK-1 put suggestions at
  order 250 beside Rules for the RoutingContentPlaceholder reason; same pattern here), plus a
  pending-count callout/link on the Synonyms listing.
- **Approve** creates a normal synonym group via the existing storage (failed query + succeeded
  query as synonyms; the editor can adjust like any group) — decide direction honestly:
  the amendment calls them synonym/rewrite candidates; creating a standard bidirectional
  synonym group is the MVP, note one-directional rewrite as the editor's manual alternative in
  the guide. **Dismiss** persists.
- `[PageCommand]`s as plain methods on final classes; labelled Buttons (primer).

## 4. Docs

- Guide: extend or sibling the popularity-boosts guide (your call — the two suggestion
  workflows should read as one "mined suggestions" story), wiki-ready, with the honest noise
  ceiling (adjacency heuristic if that is what shipped) and the threshold.
- CHANGELOG `[Unreleased]`; KNOWN-LIMITATIONS for real ceilings; ADR only if the correlation
  decision warrants one (it probably does — keep it short).
- Append host-pass items to `docs/internal/host-pass-hw11-checklist-2026-08-26.md` (§ next
  letter): seed a reformulation on the host, run the task, approve a suggestion, see the synonym
  apply to /search.

## Deliverables

- Code + tests: pair extraction (failure→success, adjacency window, containment exclusion,
  normalization), threshold, replace-per-run with answered pairs preserved, approve writes a
  real synonym group (round-trip the storage shape like RK-1's rule test), dismiss persists.
- All C# suites green; Admin client build only if touched; JS untouched. Conventional commits on
  `unit/sy-1`; commit this spec file.

## Constraints

- No new dependencies; no contract changes; no new cookies or visitor identifiers; do not touch
  RK-1's aggregation math or the experiment code. Kentico docs MCP for platform questions.
- DB round trips untestable outside the container → host-pass items, XP-1a precedent.
