---
phase: 06-analytics-retention-setting
plan: 02
subsystem: api
tags: [named-options, IOptionsMonitor, IConfigureNamedOptions, kentico, admin-ui, retention, tooltips]

requires:
  - phase: 06-analytics-retention-setting
    provides: 06-01's row class, value-record seam, overlay, admin plumbing and suggestion prunes
provides:
  - Per-index search settings via named options (`IOptionsMonitor<XpSearchIndexSettings>.Get(registeredIndexName)`)
  - Search settings page in each index's tuning section; global page/row removed
  - Per-name invalidation + response-cache eviction on save (live without restart)
  - Retention and popularity mining per index; orphan indexes pruned with defaults
  - Tooltips + explanation text on every setting and widget property (UX-1)
  - Widgets own their sizes, the index owns the caps (AR-3): two defaults removed, widget sizes required ≥ 1, retired columns dropped on upgrade
affects: [07-artboard-parity (labels/wording only), 05 packaging (Breaking entry rewritten)]

tech-stack:
  added: []
  patterns: [named options per index keyed by the registered code name; IOptionsMonitorCache.TryRemove(name) + ISearchCache.Evict(name) on the row's save events; Core installer removes columns a form no longer declares]

key-files:
  created: [src/XpSearch.Core/Options/XpSearchIndexSettings.cs, src/XpSearch.Admin/UIPages/SearchSettings.cs, docs/internal/units/AR-2.md, docs/internal/units/AR-3.md, docs/internal/units/UX-1.md]
  modified: [src/XpSearch.Core/Options/XpSearchStoredSettings.cs, src/XpSearch.Core/Options/XpSearchSettingsInfo.cs, src/XpSearch.Core/Analytics/XpSearchAnalyticsModuleInstaller.cs, src/XpSearch.Core/Analytics/XpSearchQueryLogRetentionTask.cs, src/XpSearch.Core/Popularity/*, src/XpSearch.Core/Pipeline/*, src/XpSearch.Widgets/Components/Widgets/XpSearch/*Widget.cs, docs/guides/analytics.md, docs/guides/search-api.md, docs/guides/page-builder-widgets.md, CHANGELOG.md]

key-decisions:
  - "Per index, not global; named options because each request names one index and instances cache per name"
  - "Key by the registered code name (ordinal Get); only case can differ — names are sanitised by the admin"
  - "A save must evict the index's cached responses: settings shape the response below the cache key"
  - "Widgets own their sizes; the index owns the caps; API callers that send no size get the code default (owner)"
  - "Retired columns are dropped by the installer (RuleStorageMigration precedent), not left NOT NULL"

patterns-established:
  - "Add a per-index knob: XpSearchIndexSettings property + column + form + model field + overlay line"
  - "Every setting/widget property carries Tooltip + ExplanationText naming what it affects"

duration: ~6h across four rounds (AR-2, eviction fix, UX-1, AR-3 + revise)
started: 2026-09-03T03:45:00Z
completed: 2026-09-03T09:30:00Z
description: "Search settings are per index via named options with live per-index invalidation and response-cache eviction; retention per index; tooltips everywhere; two redundant defaults removed and widget sizes made required"
type: Summary
about: "xperience-search"
---

# Phase 6 Plan 02: AR-2 per-index settings — Summary

**Per-index search settings resolved through named options, edited on Lucene Search → index → Search settings, live on the next request after a save (options rebuilt per name, cached responses evicted), with retention and popularity mining per index; every setting and widget property explains itself; widgets own their sizes and the index owns the caps. Merged 9a11388 → 583fb6b → 4f6751f, all live on the demo host; owner walked §Y 121–125: all pass.**

## Acceptance Criteria Results

| Criterion | Status | Notes |
|-----------|--------|-------|
| AC-1 named options over code defaults (incl. case) | Pass | `StoredSettingsTests` through the real pipeline |
| AC-2 a save invalidates only that index | Pass | Real `OptionsCache` + recording `ISearchCache`; owner 122 (cap 2 → two cards on first reload; second index unaffected) |
| AC-3 Search settings page per index; global gone | Pass | Owner 121 (fourteen values after AR-3; save succeeds after the column drop) |
| AC-4 retention per index + orphans | Pass | Owner 123: `DancingGoatSample: 619 query log rows, 0 popularity suggestions, 1 synonym suggestion (older than 2026-09-02); Test: 0 …` — first attempt was operator confusion between the two "retention" labels → relabel follow-up dispatched |
| AC-5 pipeline/tasks read the request's index settings | Pass | Owner 122/124; API probe per index |
| AC-6 docs + changelog rewritten | Pass | Breaking entry rewritten twice (AR-2, AR-3); no appended duplicates |

## Deviations from Plan

| Type | Count | Impact |
|------|-------|--------|
| Host-found defect (response cache not evicted on save) | 1 | Fixed 583fb6b |
| Owner scope additions | 2 | UX-1 tooltips (text only); AR-3 removal of two defaults + required widget sizes |
| Checklist defect | 2 | 122 rewritten twice (demo page vs API; then cap-based); 123 label ambiguity → relabel |
| Merge conflicts | 2 | Checklist file edited on both sides; resolved to main each time |

## Issues Encountered

| Issue | Resolution |
|-------|------------|
| Retired NOT NULL columns would break the first save on upgraded DBs | Installer drops undeclared columns (verified live: `XpSearch_Settings` lost both on start) |
| Legacy widgets saved with 0 (former "use index setting") | Fallback to the C# default at the three read points, pinned by tests |
| Lead's PowerShell round-trip garbled STATE.md earlier in the phase | Rule recorded: never Get-Content/Set-Content BOM-less UTF-8 |

## Next Phase Readiness

**Ready:** Phase 5 packaging can cut the changelog — the Unreleased entries describe the final design only. Phase 7 (TH-6 artboard parity) is in APPLY in parallel.

**Concerns:** Two dead-label follow-up (unit/relabel) pending merge; screenshot rows STALE (search settings page, widget dialogs) for the next /docs-ship.

**Blockers:** None.

---
*Phase: 06-analytics-retention-setting, Plan: 02 — Completed 2026-09-03*
