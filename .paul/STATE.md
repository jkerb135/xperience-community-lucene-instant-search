---
description: "xperience-search — current position and accumulated context"
type: ProjectState
about: "xperience-search"
---

# Project State

## Project Reference

See: .paul/PROJECT.md (updated 2026-09-01)

**Core value:** XbK developers get a complete, tunable, measurable site-search product on the Lucene index they already have.
**Current focus:** v1.0 Public Release — Phase 1 (Default experience completion)

## Current Position

Milestone: v1.0 Public Release (v1.0.0)
Phase: 2 (FZ-1) in flight; Phase 1.1 [INSERTED] queued (mobile composition + edge-state parity)
Plan: 02-01 FZ-1 dispatched (worktree `fz-1`; owner approved the three spec decisions)
Status: Phase 1 closed; owner design-note review 2026-09-01 found the mobile-note + edge-state artboards partially unimplemented → checklist §Q items 80–86 written as the acceptance test, Phase 1.1 inserted with plans MB-1/ES-1, SG-1 scope extended (did-you-mean, popular searches)
Last activity: 2026-09-01 — FZ-1 dispatched; §Q review check added; Phase 1.1 inserted

Progress:
- Milestone: [██░░░░░░░░] ~20% (of the closing stretch; spec Phases 0–7 already shipped pre-PAUL)
- Phase: [██████████] Phase 1 complete

## Loop Position

Current loop state:
```
PLAN ──▶ APPLY ──▶ UNIFY
  ✓        ◉        ○     [Applying — TH-4 in flight]
```

## Accumulated Context

### Decisions

| Decision | Phase | Impact |
|----------|-------|--------|
| Fuzzy search = per-index admin toggle, default OFF, no contract change (FZ-1 spec) | 2 | Owner may veto: default off, single toggle vs level dropdown, Synonyms-listing placement |
| Defaults must match approved mockup exactly | 1 | TH-4 spec amended; host mirrors docs 1:1 |
| Existing workflow stays authoritative | All | Unit specs in `docs/internal/units/`, gates in `docs/internal/phase-log.md`; PAUL layers on top, does not replace |

### Deferred Issues

| Issue | Origin | Effort | Revisit |
|-------|--------|--------|---------|
| 'command not found' on re-annotated [PageCommand] overrides | HW-10/11 | M | Phase 3 dedicated unit |
| ~~Suggestions widget renders own input (can't attach to searchBox)~~ RESOLVED by TH-3 (integrated suggestions param) | Mockup compare | - | Closed 2026-09-01 |
| Widget.$$routable single-attribute limit (composite widgets) | TH-2 | S | When a sheet-only page bites |
| Remove SearchTimingStage (slot 99) | Earlier | S | Phase 4 perf unit |
| SuggestMode Documents/QuerySuggestions mutually exclusive — grouped panel + groupLabels are dead config | HW-14 gap 1 | M | Phase 4 plan 04-04 (SG-1) |
| No per-visitor/recency "Recent searches" (QuerySuggestionService = all-visitor popularity only) | HW-14 gap 2 | M | Phase 4 plan 04-04 (SG-1) |
| Wire `Suggestion` carries no group/type member (contract change needed for a third group) | HW-14 gap 3 | S | Phase 4 plan 04-04 (SG-1) |
| No `XpSearchIndexingOptions.AddField` — ContributeAsync fields invisible on the wire; guide example misleading; host ships a field-source decorator workaround | HW-14 gap 4 | M | Phase 4 plan 04-05 (IX-1) |
| Default `SuggestField` = `title` → machine item names on real Kentico sites | HW-14 gap 5 | S | Phase 4 plan 04-05 (IX-1) |
| Cosmetic quartet from final mockup pass: suggestion option text right-aligned; empty-state Clear-filters button contrast/width; sheet header inherits site heading color; mobile checkboxes unthemed (native accent-color) — likely site-CSS bleed vs theme hardening | Final compare 2026-09-01 | S | Small TH follow-up unit when convenient |
| Mobile-note not held: host never applied sidebar-hide half of the swap; pagination↔loadMore swap needs a mount-time mechanism (loadMore owns state.page — cannot coexist w/ pagination) | Owner design-note review 2026-09-01 | M | Phase 1.1 plan 1.1-01 (MB-1); checklist items 80–81 |
| Edge-state mocks not held: no probe flag (sheet count + "show N results" empty-state count both blocked); skeleton predates thumbnail card; no empty-state icon | Owner design-note review 2026-09-01 | M | Phase 1.1 plan 1.1-02 (ES-1); checklist items 82–84 |
| Recovery state: no did-you-mean (suggester+contract), no popular-searches endpoint | Owner design-note review 2026-09-01 | M/L | Phase 4 plan 04-04 (SG-1, scope extended); checklist items 85–86 |

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

Last session: 2026-09-01
Stopped at: PAUL initialized; FZ-1 spec written; TH-4 in flight
Next action: Run /paul:plan to detail Phase 1's remaining plan (host adoption) and confirm phase structure
Resume context: Read auto-memory `xperience-search-session-state` first — it is the richer, authoritative session state; STATE.md is the PAUL-level digest

---
*STATE.md — Updated after every significant action*
