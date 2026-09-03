---
phase: 06-analytics-retention-setting
plan: 01
subsystem: api
tags: [kentico, options, IConfigureOptions, IOptionsMonitor, scheduled-task, retention, admin-ui]

requires:
  - phase: 04-remaining-spec-scope
    provides: analytics tables (query log, popularity/synonym suggestions) and the admin tuning app the Settings page hangs off
provides:
  - Typed `XpSearch.Settings` row holding all 16 global XpSearchOptions/Analytics scalars, seeded once from the host lambda
  - `IConfigureOptions<XpSearchOptions>` overlay + `IOptionsChangeTokenSource` (row insert/update events) — saves apply without restart
  - Search ingestion → Settings `ModelEditPage`
  - Retention task prunes query log + answered popularity/synonym suggestions per stored window (default 365)
affects: [06-02 per-index named options, 05 packaging (breaking-changes page)]

tech-stack:
  added: []
  patterns: [stored-row overlay via IConfigureOptions registered after services.Configure; consumers read IOptionsMonitor.CurrentValue per operation; value-record seam (SearchSettingsValues) because Kentico Info objects need the container]

key-files:
  created: [src/XpSearch.Core/Options/XpSearchSettingsInfo.cs, src/XpSearch.Core/Options/XpSearchStoredSettings.cs, src/XpSearch.Admin/UIPages/GlobalSettings.cs, tests/XpSearch.Core.Tests/StoredSettingsTests.cs, docs/internal/units/AR-1.md]
  modified: [src/XpSearch.Core/Analytics/XpSearchQueryLogRetentionTask.cs, src/XpSearch.Core/Analytics/XpSearchAnalyticsModuleInstaller.cs, src/XpSearch.Core/Popularity/InfoPopularitySignalStore.cs, src/XpSearch.Core/Popularity/InfoSynonymSuggestionStore.cs, src/XpSearch.Core/DependencyInjection/XpSearchServiceCollectionExtensions.cs, docs/guides/analytics.md, docs/adr/0015-analytics.md, CHANGELOG.md]

key-decisions:
  - "No SettingsKeyInfo rows: Kentico docs say custom settings are a custom info object with your own UI"
  - "RetentionDays kept (default 365) and fed from the row; lambda globals are used once to seed, then admin-owned (Breaking core)"
  - "Uncached row read on options rebuild — IOptionsMonitor is the cache; the IProgressiveCache layer served stale values for up to 30 min"
  - "Owner redirect at checkpoint: settings become per index via named options (plan 06-02); global page to be replaced"

patterns-established:
  - "Global admin knob = column on XpSearchSettingsInfo + installer form + model field + overlay line (primer updated)"
  - "Options consumers use IOptionsMonitor<T>.CurrentValue per operation, never captured in a field"

duration: ~4h (incl. owner scope change mid-APPLY and one host-found fix round)
started: 2026-09-02T22:00:00Z
completed: 2026-09-03T03:30:00Z
description: "All global search options editable on Search ingestion → Settings via a stored row + IConfigureOptions overlay with live updates; retention default 365 and answered-suggestion prune; superseded at checkpoint by the per-index named-options redesign (06-02)"
type: Summary
about: "xperience-search"
---

# Phase 6 Plan 01: AR-1 global settings via ConfigureOptions — Summary

**All sixteen global `XpSearchOptions`/`Analytics` values are stored in one `XpSearch.Settings` row, edited on Search ingestion → Settings, and loaded over the host lambda through `IConfigureOptions<XpSearchOptions>` with live invalidation; the retention task honours the stored window (default 365) and also prunes answered suggestions. Merged 07928a7 + fix 76bef0e. At the checkpoint the owner redirected to per-index settings via named options, so the global page is transitional.**

## Performance

| Metric | Value |
|--------|-------|
| Tasks | 3 auto + 1 checkpoint (partially walked) |
| Files modified | 49 (+4 in the fix round) |
| Review rounds | 1 (approved first pass) + 1 host-found fix (approved) |
| Suites | Core 357, Admin 261, Ingestion 47, Widgets 78, Client 16; full sln 0 errors |

## Acceptance Criteria Results

| Criterion | Status | Notes |
|-----------|--------|-------|
| AC-0: all globals load via ConfigureOptions | Pass | `StoredSettingsTests`: stored row wins, no row → lambda, unreadable storage → lambda |
| AC-1: settings visible/editable in admin | Pass | Owner item 116 pass (page lists all 16, retention 365); 118 validation pass |
| AC-2: retention task honours the setting | Pass (unit) / Superseded (host) | Unit test 10/29 kept, 31/400 deleted. Host item 117 failed on the first walk (stale cached read, fixed 76bef0e); re-walk superseded by the per-index redesign |
| AC-3: answered suggestions pruned, pending kept | Pass | Unit fakes + predicate tests; owner item 120 pass |
| AC-4: missing/unreadable row falls back | Pass | Overlay leaves lambda values; read failure logged at Debug |
| AC-5: seeding idempotent | Pass | Owner item 119 pass (value survives restart); DB shows one row |
| AC-6: docs + changelog | Pass | Guide, ADR-0015, search-api section, CHANGELOG Breaking + Added |

## Accomplishments

- Owner-requested scope change ("all settings, ConfigureOptions") absorbed mid-APPLY as a spec revision (AR-1 rev 2), not a patch — the rev 1 SettingsKeyInfo direction was retired before any code, on Kentico's own guidance.
- Live update mechanism proven on the host (API returned the saved page size) once the redundant cache layer was removed.
- Retention now covers the second unbounded table (answered suggestions) and reports three counts.

## Task Commits

| Task | Commit | Description |
|------|--------|-------------|
| Task 1: spec + worktree | `6ed7f75` (rev 1), rev 2 folded into `e097e6e` | AR-1 spec |
| Task 2: implement/review/merge | `e097e6e` → merge `07928a7` | Feature |
| Task 3: host + checklist | `215f936`, `41c56dc`, `e761d9f` | Checklist §X 116–120 (+116a rewritten to probe the API) |
| Checkpoint fix | `d2272b9` → merge `76bef0e` | Uncached row read |

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Custom Info row + own admin page, not `SettingsKeyInfo` | Kentico docs: settings-key cache dependencies are built-in only; custom settings = custom info object | No Settings-app spike |
| Keep `RetentionDays`, seed-once semantics | Options class stays the code shape; admin owns after first start | Breaking (core) entry; delete row to re-seed |
| Remove IProgressiveCache from the row read | Dependency didn't invalidate; monitor already caches | One SELECT per save |
| **Owner: per-index settings via named options (06-02)** | Every value is meaningful per index; named options cache per name; matches popularity/typo-tolerance pattern | Global page + global row replaced; consumers `Get(index)`; per-name invalidation via `IOptionsMonitorCache` |

## Deviations from Plan

| Type | Count | Impact |
|------|-------|--------|
| Scope change (owner) | 1 | Rev 2 spec; plan ACs amended in place |
| Auto-fixed | 1 | Stale cached read → uncached (host-found) |
| Checklist defect | 1 | 116a could not be proven on the demo page (widget sends its own page size) → rewritten to the API probe |
| Superseded | 2 | Re-walk of 116a/117 dropped in favour of 06-02 |

## Issues Encountered

| Issue | Resolution |
|-------|------------|
| Implementer's PowerShell bulk edit introduced BOM/mojibake | Repaired before commit; lead scan of 49 files clean |
| Lead's own PowerShell `Get-Content`/`Set-Content` round-trip garbled STATE.md and BOM'd paul.toml | Restored from git, re-applied with the Edit tool, BOM stripped (933c6f3). Rule: never round-trip BOM-less UTF-8 files through PS 5.1 `Get-Content` |
| Admin sign-in needed for browser items | Owner walked them; DB row + API probe gave the lead's evidence |

## Next Phase Readiness

**Ready:** row class, installer form, value-record seam, `ModelEditPage` plumbing, prune methods and tests — all reusable by 06-02 (add an index-name column, name the options).

**Concerns:** 06-02 removes the global page and the seed-at-startup; the Breaking (core) CHANGELOG entry must be rewritten, not appended, before Phase 5 cuts the changelog. `IOptionsMonitor.Get(name)` is ordinal — key by the registered index code name, not the raw request string.

**Blockers:** None.

---
*Phase: 06-analytics-retention-setting, Plan: 01 — Completed 2026-09-03*
