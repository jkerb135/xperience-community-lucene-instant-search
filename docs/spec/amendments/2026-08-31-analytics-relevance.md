# Amendment — Analytics-driven relevance: popularity boosts and mined synonyms (RK-1, SY-1)

Lead proposal, 2026-08-31, per the owner's zero-external-cost constraint (no paid AI services;
no new external dependencies). Queued after PB-3, DX-2 and XP-1; before §10.5 clients, §12 perf
and Phase 8 packaging. AIRA integration was investigated and rejected: AIRA exposes no developer
API — agents are Kentico-hosted, configurable only via enable/disable and instruction text.

## RK-1 — popularity boosts (learning-to-rank lite)

- A scheduled task (same registration pattern as the AN-1 retention task) aggregates the AN-2
  click/conversion activities and the AN-4 journal into a per-document signal per index:
  click count, conversion count, **position-bias damped** (a click at position 8 outweighs one at
  position 1; use a simple 1/log2(position+1) discount — no fabricated ML).
- Signal stored in a custom module class (module pattern of AD-1), refreshed on the task's
  cadence; empty table = stage is a no-op.
- A new pipeline stage applies the signal as a bounded query-time boost (cap the multiplier so
  popularity never drowns text relevance). Stage is registered by default but off until an index
  opts in via a tuning flag on the existing weights page.
- **Per-query suggested rules:** for the top-N frequent queries, documents that historically win
  clicks for that query surface as *suggested* boost rules in the AD-1 rules listing —
  human approves or dismisses; approval creates an ordinary rule. No rule is ever auto-applied.
- Cache: boost signal version joins the response cache key (same mechanism as PZ-1 groups).

## SY-1 — synonym mining from reformulations

- Candidate source: the AN-1 query log plus journal. When a query yields zero results (or zero
  clicks) and the same visitor re-searches a different phrase within the session and clicks,
  the pair (failed → successful) is a synonym/rewrite candidate. Aggregated by the same
  scheduled task; a candidate needs a minimum occurrence count before surfacing.
- **Admin:** the AD-1 synonyms page gains a *Suggestions* section: approve (creates a normal
  synonym or rewrite rule) or dismiss (remembered, never resurfaces). Nothing changes live
  behaviour without approval.
- WordNet seeding considered and rejected: generic English pairs, no domain value; mined pairs
  improve with traffic instead.

## Explicitly out of scope (deferred, not precluded)

- Any LLM-backed feature (synonym generation, semantic re-ranking) — belongs in a future
  optional bring-your-own-key package (`XperienceCommunity.Search.AI`), never in Core.
- Local embedding re-rank via ONNX Runtime: feasible without charges but adds a native
  dependency + ~90 MB model; separate optional package if ever requested.
- Automatic rule application, fabricated significance/statistics, per-contact ranking.

## Why this shape

The training data already exists (AN-1 query log, AN-2 activities with resultId + position,
AN-4 trustworthy journal incl. cache hits), the approval surfaces already exist (AD-1 rules and
synonyms pages), and the delivery mechanism already exists (pipeline stages, tuning storage).
Both units are aggregation + one stage + one admin section each — no new services, no charges.
