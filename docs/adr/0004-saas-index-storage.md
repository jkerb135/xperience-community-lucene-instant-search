# ADR-0004: §13.4 — Lucene indexes on XbK SaaS blob storage

- **Status:** accepted (decided for now) — WF-1, 2026-09-08. Revisit when a SaaS project is available.
- **Date:** 2026-09-08
- **Spec reference:** §13.4

## Context

Spec §13.4 asks whether the library works on Xperience by Kentico SaaS, where the application's file
system is not a local disk.

The library never writes index files itself. It writes through
`Kentico.Xperience.Lucene`, which opens its index directory through Xperience's
[`CMS.IO`](https://docs.kentico.com/documentation/developers-and-admins/api/files-api-and-cms-io/file-system-providers)
abstraction. On SaaS that maps to blob-backed storage. So the question is not "does the code compile
there" — it does — but whether Lucene's assumptions hold on that storage:

- Lucene takes a **write lock** on the directory and expects the lock to be exclusive and to be
  released on process death. Blob storage lease semantics are not the same as file-system lock
  semantics.
- A rebuild is a `CREATE`-mode reopen of the directory plus thousands of small writes, and the
  faceting sidecar (ADR-0001) commits a second directory that must move with the first.
- A searcher reads segment files. On blob storage every read that misses a local cache is a network
  round trip, so both freshness and latency change shape.
- SaaS runs the application on more than one instance, and the scheduled tasks this library
  registers (analytics retention, popularity aggregation, synonym mining) assume they run once.

None of that has been measured. We have no SaaS project to measure it on, and this repository's
performance numbers (`docs/guides/performance-and-sizing.md`) are all local-disk numbers.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Claim SaaS support because `CMS.IO` abstracts storage | Widest stated compatibility | Unverified; the failure mode is a corrupt or locked index in someone's production, which is the worst possible way to learn |
| Say nothing | No wrong claim on paper | Every evaluator has to ask, and the answer is invented in a support thread |
| **State the supported targets and what a verification would test** | Honest, actionable, cheap; leaves the door open | Costs us the "works everywhere" line in the README |

## Decision

**Xperience by Kentico SaaS is not a verified target.** The library writes its index through the
Lucene integration, which uses `CMS.IO`; on SaaS that maps to blob-backed storage whose write/lock
behaviour we have not measured. Until a test on a SaaS project is run, the supported targets are
**self-hosted single instance** and **self-hosted web farms**, with the ceilings in
`docs/guides/performance-and-sizing.md`.

Nothing in the code prevents someone from trying it, and nothing is designed to fail there. What is
withheld is the claim, not the capability.

### What a SaaS verification would test

Executable as written, on a SaaS project, in this order. Any step failing ends the verification with
a documented reason.

1. **Module install.** Deploy the library and confirm the admin module classes install
   (`XpSearch.QueryLog`, `XpSearch.Settings`, the ingestion tables) and that the admin application
   opens against them.
2. **Index create and rebuild on blob storage.** Create an index, rebuild it with ~10,000 documents,
   and confirm the index directory *and* its `*_taxonomy` sidecar are both written, committed and
   readable afterwards. Repeat the rebuild twice to exercise `CREATE`-mode reopen over existing blobs.
3. **Write lock behaviour.** Trigger a rebuild and, while it runs, trigger a second write (an
   ingestion push). Confirm the second write blocks or fails cleanly rather than corrupting the
   directory. Then kill the instance mid-rebuild and confirm the next start can reopen the index
   (a stale `write.lock` that is never released is the failure we most expect).
4. **Searcher freshness and latency.** Measure p50/p95 of the same representative search used in
   `performance-and-sizing.md`, and measure how long after a write the document becomes findable.
   Compare against the local-disk table on that page; publish the delta rather than replacing it.
5. **Scheduled tasks on multiple instances.** Confirm the analytics retention, popularity
   aggregation and synonym mining tasks run once per interval across the SaaS instances rather than
   once per instance (duplicate runs are idempotent for retention, but not free).
6. **Analytics and health across instances.** With more than one instance serving, confirm what WF-1
   made farm-safe still holds: a click on instance B attributes to a search answered on instance A,
   the suggestion cache invalidates on both, and `/status` reports the same health on both.

## Evidence

None yet, by construction — that is the decision. The measured evidence that exists is for local
disk and is in `docs/guides/performance-and-sizing.md` and `docs/internal/perf-results-*.md`.

## Consequences

- The documentation carries a **Supported hosting** table (single instance ✓, self-hosted web farm ✓
  with notes, SaaS ✗ unverified) instead of a claim nobody has tested. An evaluator gets the answer
  in the guide rather than in a support thread.
- A SaaS-hosted prospect is a "not yet" rather than a "yes" — the honest cost of this decision.
- Nothing is foreclosed. The verification above is a scoped piece of work, and the only likely code
  outcomes of running it are storage-specific configuration (a local segment cache, or a directory
  implementation better suited to blobs) rather than a redesign.
- The web-farm half of the same question *is* answered and implemented (WF-1): shared caches on
  touch keys, click attribution through the query log, index health through the ingestion log.
