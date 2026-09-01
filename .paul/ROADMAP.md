---
description: "xperience-search — milestone and phase structure"
type: Roadmap
about: "xperience-search"
---

# Roadmap: xperience-search

## Overview

The engineering spec's Phases 0–7 (pipeline, tuning, widgets, analytics, ingestion, experiments, personalization) are built and gated; what remains between here and a public v1.0 is finishing the default front-end experience to match the approved mockup, adding fuzzy search as a configuration value, closing out host verification, landing the remaining spec scope, and packaging for NuGet/npm release. This roadmap covers that closing stretch — PAUL adopted mid-project on 2026-09-01; earlier history lives in `docs/internal/phase-log.md`.

## Current Milestone

**v1.0 Public Release** (v1.0.0)
Status: In progress
Phases: 1 of 5 complete

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with [INSERTED])

| Phase | Name | Plans | Status | Completed |
|-------|------|-------|--------|-----------|
| 1 | Default experience completion | 2 | Complete (2026-09-01) | 2026-09-01 |
| 2 | Fuzzy search configuration (FZ-1) | 1 | Not started | - |
| 3 | Verification closure & defect burn-down | TBD | In progress (owner items) | - |
| 4 | Remaining spec scope | 5 | Not started | - |
| 5 | Packaging & release (spec Phase 8) | TBD | Not started | - |

## Phase Details

### Phase 1: Default experience completion

**Goal:** The zero-override default search page matches the approved kentico-violet mockup exactly ("default is the design").
**Depends on:** Nothing (TH-1..TH-3 already merged)
**Research:** Unlikely (spec written, mockup approved)

**Scope:**
- TH-4: ActiveFilters + ClearFilters PB widgets, shell composition classes, canonical results-page recipe, default polish, collapsible facet groups
- Host adoption pass (pre-authorized): imports, PB composition via management API, vite + sln rebuild, final screenshot compare vs mockup

**Plans:**
- [x] 01-01: TH-4 unit — merged 637045b (2026-09-01); TH-5 range-slider parity fix folded in, merged 3bfbae4
- [x] 01-02: Host adoption pass + mockup parity check — done 2026-09-01 (HW-13/HW-14; final side-by-side delivered; residual cosmetic quirks in STATE.md deferred issues)

### Phase 2: Fuzzy search configuration (FZ-1)

**Goal:** Typo tolerance ships as a per-index admin toggle (default off) — misspelled queries match near-spellings with exact hits still ranked first; no contract or JS changes.
**Depends on:** Phase 1 (keeps the single-wave-in-flight discipline; technically independent)
**Research:** Unlikely (spec ready at `docs/internal/units/FZ-1.md`; Lucene FuzzyQuery mechanics verified in spec)

**Scope:**
- Per-index settings info + Synonyms-listing toggle (RK-1 popularity pattern)
- Fixed length-scaled edit-distance policy in BuildQueryStage, both query paths
- Cache-key participation, highlighter-rewrite verification (STOP clause), explain entry
- Guide section, CHANGELOG, host-pass checklist items

**Plans:**
- [ ] 02-01: FZ-1 unit — dispatch, review, merge, host retest

### Phase 3: Verification closure & defect burn-down

**Goal:** HW-11 host checklist fully passed and known defects cleared — the library is host-proven end to end.
**Depends on:** Phase 1 (checklist includes TH-wave items; owner walk continues in parallel)
**Research:** Likely (Kentico [PageCommand] discovery internals for the command-not-found defect)
**Research topics:** Why re-annotated/inherited PageCommand overrides intermittently miss command discovery on fresh builds

**Scope:**
- Owner HW-11 signed-in items (§A–C/E and remaining numbers) + item 30 retest after rebuild
- Dedicated fix unit for the 'command not found' admin defect
- Findings batches from the owner walk (triage → fix units as they land)

**Plans:**
- [ ] 03-01: Command-discovery defect unit
- [ ] 03-02: HW-11 findings batches (count TBD by walk results)

### Phase 4: Remaining spec scope

**Goal:** The last committed spec sections are built: typed clients, second extensibility example, performance pass.
**Depends on:** Phase 3 (host-proven base before perf work)
**Research:** Likely (§12 perf baselining approach)

**Scope:**
- §10.5 typed clients
- §10.7 example 2
- §12 performance pass (also: remove `SearchTimingStage` slot 99)
- SG-1 — mixed suggestion sources (HW-14 gaps 1–3, file:line evidence in the HW-14 report /
  session state): `SuggestMode.Mixed` (or a sources list) merging document + query suggestions
  into one `SuggestResponse`; a group/type member on the wire `Suggestion` (contract change —
  coordinated event); recency/visitor scoping option for `QuerySuggestionService` so a "Recent
  searches" group is honest. Unlocks the currently-dead `groupLabels` config in both suggestion
  consumers; demo then wires the grouped panel the mockup shows.
- IX-1 — indexing field API (HW-14 gap 4, an API defect): `XpSearchIndexingOptions.AddField`
  (or schema registration from `ContributeAsync`) so contributed fields reach the schema and
  the wire without the `IContentTypeFieldSource` decorator workaround the sample host ships;
  fix the misleading "Adding fields of your own" guide example; consider a smarter
  `SuggestField` default or a loud quick-start warning (HW-14 gap 5 — `title` = machine item
  names on real Kentico sites).

**Plans:**
- [ ] 04-01: §10.5 typed clients unit
- [ ] 04-02: §10.7 example 2 unit
- [ ] 04-03: §12 performance unit
- [ ] 04-04: SG-1 mixed suggestion sources + recent searches (spec to write; contract change)
- [ ] 04-05: IX-1 AddField indexing API + suggest-field default/docs (spec to write)

### Phase 5: Packaging & release (spec Phase 8)

**Goal:** `XperienceCommunity.Search.*` on NuGet and `@xperience-community/xperience-search` (+ `-themes`) on npm, docs shipped, v1.0.0 tagged.
**Depends on:** Phase 4 (everything merged and host-proven)
**Research:** Unlikely (packaging conventions established; npm pack + Vite ingestion already verified in PK-1)

**Scope:**
- Release versioning, CHANGELOG cut, breaking-changes page (deferred D2), Home/D3 landing, configuration reference (D1)
- Publish pipeline + final /docs-ship

**Plans:**
- [ ] 05-01: Release readiness (docs D1/D2/D3 + changelog cut)
- [ ] 05-02: Publish + tag

---
*Roadmap created: 2026-09-01*
*Last updated: 2026-09-01*
