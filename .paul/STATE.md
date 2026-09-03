---
description: "xperience-search — current position and accumulated context"
type: ProjectState
about: "xperience-search"
---

# Project State

## Project Reference

See: .paul/PROJECT.md (updated 2026-09-01)

**Core value:** XbK developers get a complete, tunable, measurable site-search product on the Lucene index they already have.
**Current focus:** v1.0 Public Release — Phase 6 (Analytics retention setting & cleanup task)

## Current Position

Milestone: v1.0 Public Release (v1.0.0)
Phase: 6 of 6 (Analytics retention setting & cleanup task) — In progress: 06-01 closed, 06-02 (per-index named options) next. Phases 1, 1.1, 2, 4 COMPLETE; Phase 3 = only 03-02 HW-11 findings batches (owner walk); Phase 5 packaging waits on 6.
Plan: 06-02 in APPLY — AR-2 9a11388 + eviction fix 583fb6b + UX-1/AR-3 4f6751f all merged and live on the host; at the human-verify checkpoint (owner walks checklist §Y 121–125). Phase 7 / 07-01 (TH-6 artboard parity) in APPLY in parallel (worktree th-6)
Status: Ready for next PLAN. Owner decision at the 06-01 checkpoint: settings per index via named options (`IOptionsMonitor<XpSearchIndexSettings>.Get(indexCodeName)`), page in each index's tuning section, per-name invalidation via IOptionsMonitorCache, retention per index; global page/row replaced. AR-1 MERGED 07928a7 + fix 76bef0e (approved first pass; rev 2 scope: all global options on Search ingestion → Settings, IConfigureOptions overlay + live change token; retention default 365; answered-suggestion prune). Core 357/Admin 261 lead-verified; full sln 0 errors; pushed 4714efa; host rebuilt+running on 27340; `XpSearch_Settings` row seeded (365/100/20/60/1000) — DB-verified. Prior context: HL-1 merged cdbc7bb; PF-1 ad7da9f; CD-1 6c92033; EX-2 232a906; CL-1 fa5843d; IX-1 7cddb43; SG-1 b2d5234; FZ-1 b5893ac; MB-1 dc4e86e; ES-1 879ebb6 (details in auto-memory xperience-search-session-state).
Last activity: 2026-09-03 — 06-01 UNIFY closed (SUMMARY); owner redirected to per-index named options → plan 06-02

Progress:
- Milestone: [██████░░░░] ~60% (of the closing stretch; spec Phases 0–7 already shipped pre-PAUL)
- Phase 6: [█████░░░░░] 50% (1 of 2 plans)

## Loop Position

Current loop state:
```
PLAN ──▶ APPLY ──▶ UNIFY
  ✓        ◉        ○     [06-02 APPLY: AR-2 spec committed (9adfdfb), implementer dispatched]
```

## Accumulated Context

### Decisions

| Decision | Phase | Impact |
|----------|-------|--------|
| AR-1 rev 2 (owner, 2026-09-02, mid-APPLY): ALL global `XpSearchOptions`/`Analytics` values editable on a Settings page in the library's admin app, stored as one typed `XpSearchSettingsInfo` row, loaded via `IConfigureOptions<XpSearchOptions>` with live change tracking; seeded once from the host lambda; `RetentionDays` kept, default 365; per-index/indexing options stay code-only; no `SettingsKeyInfo` (Kentico docs: custom settings = custom info object + own UI) | 6 | Consumers move from `IOptions` to `IOptionsMonitor`; semantics change is a Breaking (core) CHANGELOG entry |
| AR-3 (owner, 2026-09-03): widgets own their sizes, the index owns the caps — per-index "Default page size" and "Default suggestion count" REMOVED (code defaults stay for API callers); Results / Search box / Suggestions size properties required, min 1; caps (Maximum page size, Maximum suggestion count) stay. UX-1 tooltips/explanations on every setting and widget property. Save also evicts the index's cached responses (583fb6b) | 6 | Demo Results widget back to an explicit 6; one final round on unit/ux-1 then 06-02 closes |
| Fuzzy search = per-index admin toggle, default OFF, no contract change (FZ-1 spec) | 2 | Owner may veto: default off, single toggle vs level dropdown, Synonyms-listing placement |
| Defaults must match approved mockup exactly | 1 | TH-4 spec amended; host mirrors docs 1:1 |
| Added Phase 6: Analytics retention setting & cleanup task (admin settings key, default 365 days) | Phase 3 | Extends milestone scope; should ship before Phase 5 tags v1.0.0 |
| Existing workflow stays authoritative | All | Unit specs in `docs/internal/units/`, gates in `docs/internal/phase-log.md`; PAUL layers on top, does not replace |

### Deferred Issues

| Issue | Origin | Effort | Revisit |
|-------|--------|--------|---------|
| ~~'command not found' on [PageCommand]s~~ RESOLVED by CD-1: stale host builds, not discovery (ADR-0027; guard test) | HW-10/11 | - | Closed 2026-09-02; owner clicks §W 111–115 |
| ~~Suggestions widget renders own input (can't attach to searchBox)~~ RESOLVED by TH-3 (integrated suggestions param) | Mockup compare | - | Closed 2026-09-01 |
| Widget.$$routable single-attribute limit (composite widgets) | TH-2 | S | When a sheet-only page bites |
| Remove SearchTimingStage (slot 99) | Earlier | S | Phase 4 perf unit |
| ~~SuggestMode mutually exclusive / groupLabels dead config~~ RESOLVED by SG-1 (Mixed mode) | HW-14 gap 1 | - | Closed 2026-09-01 |
| ~~No per-visitor "Recent searches"~~ RESOLVED by SG-1 (client-side localStorage recents) | HW-14 gap 2 | - | Closed 2026-09-01 |
| ~~Wire `Suggestion` has no group member~~ RESOLVED by SG-1 (`Suggestion.group`) | HW-14 gap 3 | - | Closed 2026-09-01 |
| ~~No `AddField` — ContributeAsync fields invisible~~ RESOLVED by IX-1 (AddField + once-per-field warning; host decorator deleted) | HW-14 gap 4 | - | Closed 2026-09-01 |
| ~~Default `SuggestField` = machine item names~~ RESOLVED by IX-1 (once-per-index warning + docs; no heuristic default) | HW-14 gap 5 | - | Closed 2026-09-01 |
| Cosmetic quartet from final mockup pass: suggestion option text right-aligned; empty-state Clear-filters button contrast/width; sheet header inherits site heading color; mobile checkboxes unthemed (native accent-color) — likely site-CSS bleed vs theme hardening | Final compare 2026-09-01 | S | Small TH follow-up unit when convenient |
| Mobile-note not held: host never applied sidebar-hide half of the swap; pagination↔loadMore swap needs a mount-time mechanism (loadMore owns state.page — cannot coexist w/ pagination) | Owner design-note review 2026-09-01 | M | Phase 1.1 plan 1.1-01 (MB-1); checklist items 80–81 |
| Edge-state mocks not held: no probe flag (sheet count + "show N results" empty-state count both blocked); skeleton predates thumbnail card; no empty-state icon | Owner design-note review 2026-09-01 | M | Phase 1.1 plan 1.1-02 (ES-1); checklist items 82–84 |
| ~~Recovery state: no did-you-mean / popular searches~~ RESOLVED by SG-1 (SearchResponse members) | Owner design-note review 2026-09-01 | - | Closed 2026-09-01; owner walks §S 95–100 |

### Blockers/Concerns

| Blocker | Impact | Resolution Path |
|---------|--------|-----------------|
| Owner HW-11 signed-in checklist items outstanding | Gates Phases 4–5 | Owner walks checklist; findings triaged in Phase 3 |
| Host needs sln rebuild + restart for latest bundles | Host verification accuracy | One restart covers PB-5/XP-1a/HW-13/TH wave |

## Boundaries (Active)

- Never touch `src/Components/Widgets/CardWidget/`
- Frozen JSON contract — no casual regeneration
- Core must not gain Admin/Page Builder dependencies

## Session Continuity

Last session: 2026-09-02
Stopped at: 06-02 PLAN created (per-index named options)
Next action: Approve → /paul:apply .paul/phases/06-analytics-retention-setting/06-02-PLAN.md
Resume file: .paul/phases/06-analytics-retention-setting/06-02-PLAN.md
Resume context: Read auto-memory `xperience-search-session-state` first — it is the richer, authoritative session state; STATE.md is the PAUL-level digest

---
*STATE.md — Updated after every significant action*
