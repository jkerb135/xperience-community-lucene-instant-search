# ADR-0024 — Storage and bucketing of tuning experiments

- **Status:** accepted (unit XP-1a)
- **Context:** owner amendment `docs/spec/amendments/2026-08-25-experiments.md`

## Context

An experiment A/B-tests two tunings of one index against real traffic: variant A is the index's live
rules / synonyms / weights / stopwords, variant B is a draft set cloned at creation. One running
experiment per index. It must work for anonymous visitors who have not consented to tracking, and the
result contract must not change.

## Decision

**Storage: a tag on the existing rows, not a second set of tables.** Each of the four tuning module
classes gains one nullable `…ExperimentID` column. `NULL` means live; a value means "belongs to that
experiment's variant B". A fifth class, `XpSearch.Experiment`, holds the index, name, split, state
(Draft → Running → Concluded), the start/end stamps and the outcome.

Consequences: variant B is edited by exactly the pages that edit live tuning (XP-1b), promotion is an
`UPDATE … SET ExperimentID = NULL` rather than a copy, and no read path can accidentally use a
different shape of row. The cost is that *every* read must filter on the column — one forgotten query
would show a draft to live traffic. `VariantScope.Condition` is the single place that builds that
filter, and the four listing pages and the tuning source all go through it.

No startup migration: the installer merges missing fields into an installed class
(`CombineWithForm`), and a nullable column needs no backfill. The CR-4b migration precedent exists
because that change *retired* NOT NULL columns; this one only adds.

**Bucketing: a hash of a first-party cookie, no server state.** A random id in `xpsearch_bucket`,
registered through `CookieLevelOptions` at `CookieLevel.Essential` — Xperience has no "functional"
level, and Essential is the one that means "cookies I may need, but do not track me", so bucketing
works without the marketing consent an activity needs. The variant is
`SHA256(bucketId + ':' + experimentGuid) % 100 < split`. SHA-256 rather than `GetHashCode`, because
string hashing is randomized per process and would rebucket everyone on every restart and on every
other server. Hashing the experiment GUID in as well keeps two experiments from testing the same
halves of the audience.

Consequences: sticky, reproducible, zero storage, works for anonymous visitors, and the same visitor
is bucketed independently per experiment. There is no server-side record of who is in which half —
per-variant numbers come from the query log stamp, not from a membership table.

**The variant is a reference, not a row set.** `TuningVariant` (an experiment id, default = live) is
what `IRelevanceTuningSource` takes and what the tuning cache key carries;
`ExperimentAssignment.Tuning` maps A/B onto it. The amendment leaves whole-index variants open: that
would widen `ExperimentAssignment`, not every signature that takes a variant.

**Resolution happens once per request, in the decorator and the stage.** `ResolveExperimentStage`
(order 160, before the tuning stages) puts the assignment on the context; `CachedSearchPipeline` asks
the same resolver, because the variant belongs in the cache key and in the journal and a cache hit
never enters the pipeline. The answer is memoized on `HttpContext.Items`, exactly as ADR-0021 does for
contact groups. The cache key only grows a member while an experiment is actually running, so cache
efficiency is untouched otherwise.

**Response-started guard.** The pipeline also runs while DX-2's server-rendered widget is streaming,
where appending `Set-Cookie` throws. With no cookie and no way to assign one — response started, no
HTTP context, or a visitor below the Essential level — the request is bucketed into A and nothing is
written. Bucketing on a throwaway id instead would flip the visitor's variant on every request, which
is worse than a known lean towards A. Recorded in KNOWN-LIMITATIONS.

## Alternatives rejected

- **A second physical index per variant** — the amendment's own out-of-scope item; nothing about
  rules or synonyms needs a different Lucene index.
- **Bucketing on the contact** — puts the experiment behind tracking consent, which the amendment
  explicitly rules out, and leaves anonymous traffic unbucketable.
- **Separate draft tables** — doubles every read path and every edit page, and turns promotion into a
  copy that has to preserve identities.
