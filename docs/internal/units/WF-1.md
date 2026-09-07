# WF-1 — Web-farm-safe analytics state (+ the hosting statement)

**Status:** DISPATCHED 2026-09-07 (owner: "let's work through these" — GTM review product gap).
**Origin:** GTM review 2026-09-06, platform audit §6: caches on `IProgressiveCache` with touch keys are farm-safe,
but four things are per process: (1) `QueryContextMap` (`ConcurrentDictionary`, 30 min, 10k — click→query
attribution fails when the click lands on another node; `QueryContextMap.cs` ~33-35 says so); (2)
`QuerySuggestionService`'s in-process cache (no dependency — stale across nodes until TTL); (3) ingestion
`FailedCount` = static on the queue worker (`XpSearchIngestionQueueWorker.cs:82`, health per node); (4) the SSR→
client journal handoff (`ISearchRequestJournal`, KNOWN-LIMITATIONS "first-load journal handoff is per application
instance") double-counts behind a load balancer. Plus (5): ADR-0004 (SaaS blob-backed index storage) is an empty
stub and no guide says whether SaaS is supported.

Scope: `XpSearch.Core/Analytics`, `XpSearch.Ingestion` queue/health, docs + ADR-0004. No contract change, no JS.
Unit SC-1 runs in parallel and adds checks BEHIND `IQueryContextMap` (unknown queryId → drop, per-query budget,
position bound) — keep the interface; your replacement must serve those checks. Expect a CHANGELOG/KL merge.

## 1. Query context: shared, not per process
- Facts first: the query log row IS the query context (`LogQueryID` = queryId, text, index, page size?) — read
  `XpSearchQueryLogInfo`. Rows are queued in memory and drained within ~10 s (`XpSearchQueryLogQueueWorker`).
- Design (smallest honest): `IQueryContextMap` stays the seam. Implementation becomes **two-tier**: the local
  map first (fast path, unchanged), then on a miss a **database lookup of the query log row by queryId**
  (`IQueryLogStore` — add `GetByQueryIdAsync` if absent). A click within the ~10 s drain window on another node
  can still miss — accept and DOCUMENT (a click that fast is rare; the local hit covers the same-node case).
  `Get` is sync today; the sink is async — add `GetAsync` and make the sink use it; keep `Get` for callers that
  are sync (find them).
- If the log row lacks what SC-1 needs (page size), add the column via the analytics installer (additive;
  installer reconciles) and write it from the journal.

## 2. The other three
- `QuerySuggestionService` cache → `IProgressiveCache` with a touch-key dependency on the query log class
  (`xpsearch.querylog|all` or whatever the Info's TYPEINFO touch key is) so a new popular query invalidates
  farm-wide; keep the TTL.
- Ingestion `FailedCount` → persisted: write failures to the ingestion log (they may already be — check) and have
  health read "failures in the last window" from the log store instead of the static. Health then agrees across
  nodes. KNOWN-LIMITATIONS entry "`health` in `XpSearchIndexer.GetStatusAsync`" updated.
- Journal handoff: read the KNOWN-LIMITATIONS entry and the `initialQueryId` mechanism (DX-2/PB-6): the client
  reuses the server's queryId, so the double count should already be gone when hydration reuses the id — verify
  whether the entry is stale; if the double count still happens cross-node, dedupe by queryId in the log queue
  (the row is keyed by it). Delete the entry or fix the code; report which.

## 3. Hosting statement (docs + ADR)
- ADR-0004: fill it in as **decided-for-now**: "Xperience by Kentico SaaS is not a verified target. The library
  writes its index through the Lucene integration, which uses `CMS.IO`; on SaaS that maps to blob-backed
  storage whose write/lock behaviour we have not measured. Until a test on a SaaS project is run, the supported
  targets are self-hosted single-instance and self-hosted web farms with the ceilings in `performance-and-sizing.md`."
  List exactly what a SaaS verification would test (index create/rebuild on blob storage, searcher freshness,
  scheduled task on a single node, admin module install), so it can be executed later.
- `performance-and-sizing.md` "Multi-instance" section: after this unit, say precisely what is farm-safe
  (caches, tuning, attribution via DB fallback) and what is not (the ~10 s drain window, rate-limit windows per
  node — SC-1's entry). Add a "Supported hosting" table: single instance ✓, web farm ✓ with notes, SaaS ✗
  unverified (link ADR-0004).

## 4. Verification
- Core.Tests: two-tier map — local hit, local miss + DB hit, miss both → null; async sink path; suggestion cache
  invalidates on the touch key (the suite has progressive-cache fakes — find them). Ingestion.Tests: health from
  the log. CHANGELOG `**Changed (core):**`, `**Changed (ingestion):**`, `**Docs:**`. Checklist `§AF`: two host
  processes (owner runs a second instance on another port against the same DB) → a click on B for a search on A
  attributes to the query text.
