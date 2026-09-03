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
Phase: 6 of 6 (Analytics retention setting & cleanup task) — Planning. Phases 1, 1.1, 2, 4 COMPLETE; Phase 3 = only 03-02 HW-11 findings batches (owner walk); Phase 5 packaging waits on 6.
Plan: 06-01 created, awaiting approval
Status: PLAN created, ready for APPLY. Prior context: HL-1 merged cdbc7bb; PF-1 ad7da9f; CD-1 6c92033; EX-2 232a906; CL-1 fa5843d; IX-1 7cddb43; SG-1 b2d5234; FZ-1 b5893ac; MB-1 dc4e86e; ES-1 879ebb6 (details in auto-memory xperience-search-session-state).
Last activity: 2026-09-02 — Created .paul/phases/06-analytics-retention-setting/06-01-PLAN.md

Progress:
- Milestone: [██████░░░░] ~60% (of the closing stretch; spec Phases 0–7 already shipped pre-PAUL)
- Phase 6: [░░░░░░░░░░] 0%

## Loop Position

Current loop state:
```
PLAN ──▶ APPLY ──▶ UNIFY
  ✓        ○        ○     [Plan 06-01 created, awaiting approval]
```

## Accumulated Context

### Decisions

| Decision | Phase | Impact |
|----------|-------|--------|
| AR-1: retention setting is the single source (default 365); `RetentionDays` option removed (Breaking, pre-1.0); Settings app first, library edit page as evidence-driven fallback; answered suggestions pruned, pending never | 6 | Owner may veto the option removal or the fallback placement at the human-verify checkpoint |
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
Stopped at: Plan 06-01 created
Next action: Review and approve plan, then run /paul:apply .paul/phases/06-analytics-retention-setting/06-01-PLAN.md
Resume file: .paul/phases/06-analytics-retention-setting/06-01-PLAN.md
Resume context: Read auto-memory `xperience-search-session-state` first — it is the richer, authoritative session state; STATE.md is the PAUL-level digest

---
*STATE.md — Updated after every significant action*
