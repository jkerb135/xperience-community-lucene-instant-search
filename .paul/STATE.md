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
Phase: 1, 1.1, 2 and 4 COMPLETE; Phase 3 = only 03-02 HW-11 findings batches (owner walk); then Phase 5 packaging — next: Phase 3 (owner checklist walk + command-discovery unit) or more Phase 4
Plan: none in flight
Status: HL-1 merged cdbc7bb (fuzzy-highlight 135→8.2ms p50 via once-per-request reader rewrite on SearchContext.HighlightQuery; Core 347). PHASE 4 COMPLETE — PF-1 merged ad7da9f (bench 10k/100k/1M + performance-and-sizing guide; found fuzzy+highlight ~135ms defect → chip task_06905b09/HL-1, measured not fixed). CD-1 merged 6c92033 (command-not-found = stale host builds, not discovery; ADR-0027 + PageCommandDiscoveryTests guard, Admin 260; owner clicks §W 111–115). EX-2 merged 232a906 (computed relevance field worked example; host code + docs walkthrough; live-verified). Flaky filterSort test FIXED on main 7458bac by the chip session (SearchClient dispose now stops the retry loop; JS 286 stable). CL-1 merged fa5843d (Kentico-free XperienceCommunity.Search.Client + npm /ingestion subpath; host round trip passed both clients; known flaky filterSort vitest case chip-filed). IX-1 merged 7cddb43 (AddField contributed-field schema registration + undeclared-write warning + suggest-field default warning; host decorator swapped for AddField, verified live). SG-1 merged b2d5234 (mixed suggestions via SuggestMode.Mixed; contract +Suggestion.group/didYouMean/popularSearches; client-side localStorage recents as third panel group; did-you-mean via DirectSpellChecker verified by never-journaled probe searches; popular searches opt-in via empty-prefix suggestion source; checklist §S 95–100, items 85–86 off KNOWN FAIL). FZ-1 merged b5893ac (default OFF; checklist §R 87–94 = owner browser items incl. flipping the toggle); MB-1 merged dc4e86e (mobile swap live: sidebar hides <1024px via .dg-side-panel:has(>.xps-mount), mount-time matchMedia results→loadMore + inert pagination); ES-1 merged 879ebb6 (contract `probe` flag journal-skipped at the single Journal call site; SearchInstance.probe(); counted empty state + sheet live "Show N results"; skeleton/icon parity; also fixed FZ-1's migrating-from-algolia template drift). Counted empty state verified LIVE on the demo. Checklist §Q items 80–84 now walkable; 85–86 (did-you-mean, popular searches) remain KNOWN FAIL until SG-1.
Last activity: 2026-09-01 — Phase 1.1 both plans merged; host bundles rebuilt; live verification done

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

Last session: 2026-09-01
Stopped at: PAUL initialized; FZ-1 spec written; TH-4 in flight
Next action: Run /paul:plan to detail Phase 1's remaining plan (host adoption) and confirm phase structure
Resume context: Read auto-memory `xperience-search-session-state` first — it is the richer, authoritative session state; STATE.md is the PAUL-level digest

---
*STATE.md — Updated after every significant action*
