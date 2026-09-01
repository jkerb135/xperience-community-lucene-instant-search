# Unit XP-1a — Experiments core: entity, bucketing, variant tuning, stamping

First half of XP-1 (owner-approved amendment `docs/spec/amendments/2026-08-25-experiments.md` —
read it first; it is the authority on scope). XP-1b (admin UI: Experiments pages, draft editing,
comparison report, Promote/Discard buttons) follows in a separate unit — build the domain
operations here, no admin pages.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/xp-1a`).

An **experiment** A/B-tests two tunings of ONE index against real traffic: variant A = the live
rules/synonyms/weights/stopwords, variant B = a draft set cloned at creation. One RUNNING
experiment per index, ever.

## 1. Storage

- New info type `XpSearchExperimentInfo` (follow the existing module-class pattern used by the
  rule/synonym infos): index reference, display name, guid, split percent (int 1–99, % of
  traffic to B), state (Draft / Running / Concluded — string or int code, your call), Started /
  Ended datetimes (UTC, nullable), and a concluded-outcome field (Promoted / Discarded / null).
- Each of the four tuning info types (rules, synonyms, weights, stopwords) gains a nullable
  experiment reference column (e.g. `RuleExperimentID`). NULL = live (variant A); set = that
  experiment's variant-B draft row. Follow the CR-4b startup-migration precedent ONLY if a
  schema migration is actually needed — new nullable columns on code-defined module classes
  should install themselves; verify, don't assume.
- All existing reads of tuning data MUST now exclude experiment-tagged rows unless explicitly
  asked for variant B — audit every query path (`InfoRelevanceTuningSource` and anything else
  reading these info types) so draft rows can never leak into live results. Tests for this are
  non-negotiable.

## 2. Domain service (`IExperimentService` or similar, Core)

- `CreateDraft(index, name, split)` — creates the entity and CLONES every live tuning row of the
  index into experiment-tagged copies. Refuse when a non-concluded experiment already exists for
  the index.
- `Start`, and `Conclude(promote: bool)`:
  - Promote = replace live: delete the index's NULL-variant tuning rows, clear the experiment id
    on the B rows (they become live), mark Concluded/Promoted.
  - Discard = delete the B rows, mark Concluded/Discarded.
  - Both must trigger the existing tuning/searcher cache invalidation path (CA-3 mechanism) so
    the change applies without restart.
- Guard rails in the service, not the UI: state transitions Draft→Running→Concluded only; split
  immutable once Running.

## 3. Bucketing (sticky, anonymous included)

- Functional first-party cookie (name e.g. `xpsearch_bucket`) holding a random stable id.
  Variant = hash(id + experiment guid) % 100 < split → B, else A — sticky for the visitor,
  reproducible, no server-side storage. **Register the cookie at the Kentico "functional"
  consent level — verify the current cookie-registration API in the docs MCP** (this must work
  WITHOUT marketing consent, unlike activities; that is the amendment's explicit requirement).
- Resolution happens in a pipeline stage (order it before the tuning stages; see
  `ResolveContactGroupsStage` order 150 as the reference pattern): resolve the index's RUNNING
  experiment (cached lookup — this runs on every query), read/assign the cookie, put
  experiment + variant on the search context.
- **Response-started guard:** the pipeline also runs during DX-2's server-side widget render,
  where the response body may already be streaming — appending Set-Cookie then throws. If the
  cookie is absent and the response has started (or there is no HttpContext at all), bucket as
  variant A, set nothing, and let the next API query assign the cookie. First server paint
  skewing to A is accepted — record it in KNOWN-LIMITATIONS.

## 4. Variant tuning + cache + stamping

- Variant B swaps the tuning view: the tuning source serves the experiment's rows for B, live
  rows for A. Keep the seam clean — XP-1b's query tester will want "simulate variant B", and the
  amendment demands whole-index variants stay possible later (variant reference stays abstract
  enough; do not build for it).
- The response cache key gains the variant (exactly like PZ-1 added contact groups — same
  mechanism, and only when an experiment is running so cache efficiency is untouched otherwise).
- The AN-4 request journal and the AN-1 query log rows gain nullable experiment + variant
  columns, stamped when a running experiment applied. Click/conversion metrics already correlate
  via queryId, so stamping the query log splits every existing metric — do NOT touch activities.
- No contract changes: `SearchResponse` is unchanged; the JS client knows nothing.

## Deliverables

- Code + tests in Core (and Admin storage classes if that's where the tuning infos physically
  live — follow the code, not this doc): bucketing determinism + split distribution sanity,
  draft isolation from live reads, clone/promote/discard round trip with cache invalidation
  asserted, response-started guard, stamping presence/absence.
- CHANGELOG `[Unreleased]`; KNOWN-LIMITATIONS entries (first-paint-A skew; anything else honest).
  A short ADR (`docs/adr/`) for the storage-and-bucketing shape, numbered after the latest.
- Guide updates belong to XP-1b (the feature has no UI yet) — skip guides here.
- All C# suites green; JS untouched. Conventional commits on `unit/xp-1a`; commit this spec file.

## Constraints

- No new dependencies. Kentico docs MCP for the cookie-consent API and any info-provider
  questions — do not guess platform APIs.
- Primer gotcha applies: `[PageCommand]` issues are Admin-side, not yours, but the searcher/cache
  invalidation and `AssemblyDiscoverable`/`RegisterObjectType` host facts are — new info types
  need the same registration treatment the existing ones got.
- If cloning tuning rows collides with something unforeseen (e.g. unique constraints on code
  names), pick the narrowest fix, record it under Assumptions.
