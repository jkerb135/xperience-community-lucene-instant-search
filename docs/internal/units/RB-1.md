# RB-1 — Rebuild progress that survives a page reload

**Status:** DISPATCHED 2026-09-07 (owner: "let's work through these" — GTM review product gap).
**Origin:** GTM review 2026-09-06 + KNOWN-LIMITATIONS "Rebuild progress on `IndexStatusPage.Rebuild`": the
"rebuild in progress" state is a page-session value (`RebuildStartedAt`, `IndexStatus.cs` ~103-105, set by the
`Rebuild` command ~214-238, cleared by `Load`); `Kentico.Xperience.Lucene` 15.0.5 exposes no rebuild progress
(`ILuceneClient` = Rebuild/Upsert/Delete/DeleteIndex/GetStatistics; `LuceneIndexStatisticsModel` = Name, Entries,
UpdatedAt). The recorded upgrade path: derive "running" and a numerator from the ingestion log.

Scope: `XpSearch.Ingestion` (log + replay decorator) and `XpSearch.Admin` status page + its React template.
No Core change, no contract, no JS widgets. Do NOT invent a "44 of 152": if there is no honest total, show
"running since … · N documents so far".

## 1. Behaviour
- **Started/finished pair in the ingestion log.** `IndexStatusPage.Rebuild` already writes an `IngestionLogEntry`
  ("admin-ui", index, "rebuild", …). Add the matching **finished** row written by the code that KNOWS a rebuild
  ended: `ExternalDocumentReplayLuceneClient` (ADR-0005 — it decorates `ILuceneClient.Rebuild` and waits for
  quiescence via `LuceneQuiescenceWaiter` before replaying pushed documents). When its replay completes, log
  "rebuild" finished with the document count the replay observed (`GetStatistics().Entries` at that moment) —
  read the decorator; if the wait is heuristic (it is — KNOWN-LIMITATIONS says so) say so in the row's message.
  A rebuild started from Kentico's own Search app (not our page) also goes through the decorator → gets the
  finished row; it lacks a started row — the page then shows "finished at …" only. State this.
- **Status page derives state from the log**, not the session: latest "rebuild" started row without a later
  finished row → RUNNING (since …, elapsed, current `Entries` from statistics as "documents so far"); else last
  finished (when, count). `Load` no longer clears anything; the React template polls the existing status command
  every N seconds while RUNNING (find how the page already refreshes — QT/UX units may have a pattern; smallest
  honest mechanism, no new package).
- Stuck detection: a started row older than `RebuildStuckAfter` (option, default 30 min) with no finished row →
  "may have failed" tag (Kentico's queue swallows failures — cite the audit's `LuceneQueueWorker` note) with the
  hint to check the event log.
- Health: `XpSearchIndexer.GetStatusAsync` `Health` currently ignores a running rebuild; RUNNING → `Degraded`
  with reason "rebuilding" so the API-key status route tells an external system to wait (CL-1 clients surface
  `status`). Keep the JSON shape additive (a `rebuild` object: `running`, `startedAt`, `finishedAt`,
  `documents`) — the ingestion contract schema must be regenerated (contract codegen lives in the Widgets client
  per the primer; run the check).

## 2. Verification
- Ingestion.Tests: finished row written after replay (fake client + fake log store); started-without-finished
  → running; stuck threshold; status JSON additive fields. Admin.Tests: `IndexStatusDto` mapping for running /
  finished / stuck / never; `Load` idempotent across reloads (page-session field gone).
- Docs: `admin-ui-tour.md` Status page paragraph; `ingestion.md` status route JSON (regenerated table if it is
  generated — check); screenshot manifest row `tuning--status` STALE. CHANGELOG `**Added (ingestion, admin):**`
  + `**Changed (ingestion):** status health is Degraded during a rebuild`. KNOWN-LIMITATIONS: replace the
  rebuild-progress entry with the new ceiling (no total; heuristic finish; Kentico-started rebuilds lack a start
  row).
- Checklist row `§AE`: click Rebuild, reload the page → still "running"; wait → "finished N documents at …";
  `GET …/status` shows `degraded`/`rebuilding` during, `healthy` after.
