# Amendment — A/B testing for an index's tuning (XP-1)

Owner decisions, 2026-08-25. Queued after PB-3 (Page Builder routing) and DX-2 (result templates);
before §10.5 clients, §12 perf and Phase 8 packaging.

## What ships (scope A: tuning variants)

An **Experiment** tests two tunings of ONE physical index against real traffic:

- Entity: index, name, traffic split (% to B), state (draft / running / concluded), started/ended,
  and a **variant-B tuning draft** — a second set of rules / synonyms / weights / stopwords cloned
  from live at creation and edited with the same pages the live tuning uses.
- **Bucketing:** a functional first-party cookie for every visitor (anonymous included) hashes into
  A or B stickily. The cookie must be registered at the appropriate Kentico cookie level
  (functional, not tracking — verify the level API in the docs; the experiment works without
  marketing consent, unlike activities). One running experiment per index.
- **Pipeline:** variant B swaps the `IRelevanceTuningSource` view the tuning stages read; the
  response cache key gains the variant; the AN-4 request journal and the query log stamp
  experiment + variant so every existing metric (CTR, zero-result rate, average clicked position,
  volume) splits cleanly.
- **Admin:** a new **Experiments** page in the index tuning sidebar (order after Analytics):
  create → edit draft → start → live comparison report (per-variant counts and rates, honest sample
  sizes, no fabricated significance) → conclude with **Promote B to live** or **Discard**.
- **Contract:** none required for the MVP (bucketing is a cookie; the response is unchanged). If the
  JS client ever needs to display the variant, that is a later additive member.

## Explicitly out of scope (for now)

- **Whole-index variants** (different schemas/analyzers, two physical indexes): the experiment
  model should not preclude it — keep the variant reference abstract enough that "a second index"
  can slot in later — but nothing is built for it in XP-1.
- Multi-variant (>2) tests; overlapping experiments per index; automatic winner promotion.

## Why this shape

Everything it needs already exists: per-index tuning storage (AD-1/CR-4), trustworthy per-request
journaling incl. cache hits (AN-4), the metric definitions (AN-1/AD-4a), and the Query tester's
with/without comparison — an experiment is that comparison run over real traffic with sticky halves.
