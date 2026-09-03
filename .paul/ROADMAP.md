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
Phases: 3 of 6 complete (plus inserted 1.1)

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with [INSERTED])

| Phase | Name | Plans | Status | Completed |
|-------|------|-------|--------|-----------|
| 1 | Default experience completion | 2 | Complete (2026-09-01) | 2026-09-01 |
| 1.1 | [INSERTED] Mobile composition + edge-state parity | 2 | Complete (2026-09-01) | 2026-09-01 |
| 2 | Fuzzy search configuration (FZ-1) | 1 | Complete (2026-09-01; §R browser items owner-pending) | 2026-09-01 |
| 3 | Verification closure & defect burn-down | TBD | In progress (owner items) | - |
| 4 | Remaining spec scope | 5 | Complete (2026-09-02) | 2026-09-02 |
| 5 | Packaging & release (spec Phase 8) | TBD | Not started | - |
| 6 | Analytics retention setting & cleanup task | 2 | Complete (2026-09-03) | 2026-09-03 |
| 7 | Artboard parity: autocomplete panel + edge states | 1 | Planning (07-01 written 2026-09-03) | - |

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

### Phase 1.1: [INSERTED] Mobile composition + edge-state parity

**Goal:** The mockup's `mobile-note` and the three edge-state artboards hold on the live demo —
owner review 2026-09-01 found them partially unimplemented (checklist §Q items 80–86 is the
acceptance test; KNOWN-FAIL items flip to pass as these plans ship).
**Depends on:** Nothing hard (FZ-1 runs in parallel; contract addition in 1.1-02 must not collide
with SG-1's later contract work — coordinate the contract regen).
**Research:** Unlikely (gaps are diagnosed with evidence)

**Specs:** `docs/internal/units/MB-1.md` + `docs/internal/units/ES-1.md` (written 2026-09-01;
owner-directed: dispatch after FZ-1 merges — ES-1 must branch from post-FZ-1 main, shared
pipeline/cache files).

**Scope & Plans:** (both merged 2026-09-01: MB-1 dc4e86e, ES-1 879ebb6; counted empty state
verified live — "There are 2 results without them / Clear filters and show 2 results")
- [x] 1.1-01 MB-1 — mobile swap: host applies the guide's sidebar-hide half of the swap
  (`.search-sidebar { display:none }` under 1024px — guide has it, host never adopted it), AND a
  supported mechanism for the pagination↔load-more swap. NOT a CSS swap: `loadMore` replaces
  `results`+`pagination` and owns `state.page`, so this needs a mount-time viewport decision —
  design the smallest honest mechanism (host boot-time choice via `matchMedia` documented as the
  recipe, or a library `pagination` style that degrades; decide in the unit spec) and apply it to
  the demo. Checklist items 80–81.
- [x] 1.1-02 ES-1 — probe flag + counted recovery: contract gains an opt-out-of-journaling probe
  member on `SearchRequest` (coordinated contract regen); the results widget's filtered-empty
  state runs the unfiltered probe and renders "There are N results without them" + "Clear filters
  and show N results"; TH-2's sheet apply button consumes the same flag for its live "Show N
  results" preview; empty-state magnifier-slash icon per the mock; skeleton updated to match the
  thumbnail card layout (media square + lines). Checklist items 82–84; removes the TH-2/TH-1
  KNOWN-LIMITATIONS entries it obsoletes.

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
- [x] 03-01: Command-discovery defect (CD-1) — merged 6c92033 (2026-09-02): NOT a code defect; stale-host-build race diagnosed w/ decompiled proof, ADR-0027 + 70-case discovery guard; owner clicks §W 111–115
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
  consumers; demo then wires the grouped panel the mockup shows. EXTENDED 2026-09-01 (owner
  edge-state mock review, checklist items 85–86): also "Did you mean" on the no-results state
  (Lucene spellcheck/suggester + contract member + widget rendering; corrected query runs on
  click) and "Popular searches" chips (public endpoint over the analytics query log, host
  opt-in section) — both live on the NO RESULTS–WITH RECOVERY artboard.
- IX-1 — indexing field API (HW-14 gap 4, an API defect): `XpSearchIndexingOptions.AddField`
  (or schema registration from `ContributeAsync`) so contributed fields reach the schema and
  the wire without the `IContentTypeFieldSource` decorator workaround the sample host ships;
  fix the misleading "Adding fields of your own" guide example; consider a smarter
  `SuggestField` default or a loud quick-start warning (HW-14 gap 5 — `title` = machine item
  names on real Kentico sites).

**Plans:**
- [x] 04-01: §10.5 typed clients (CL-1) — merged fa5843d (2026-09-01); lead host round trip passed both clients; checklist §U 104–107
- [x] 04-02: §10.7 example 2 (EX-2) — merged 232a906 (2026-09-02); live-verified on the host; checklist §V 108–110
- [x] 04-03: §12 performance (PF-1) — merged ad7da9f (2026-09-02); bench + honest sizing guide; highlighter defect found→chip HL-1. PHASE 4 COMPLETE
- [x] 04-04: SG-1 mixed suggestion sources + recent searches — merged b2d5234 (2026-09-01); checklist §S 95–100 = owner browser items
- [x] 04-05: IX-1 AddField indexing API + suggest-field warning — merged 7cddb43 (2026-09-01); host decorator swapped for AddField same day; checklist §T 101–103

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

### Phase 6: Analytics retention setting & cleanup task

**Goal:** Search-analytics cleanup is admin-configurable: a Kentico settings key "Remove search analytics older than X days" (default 365) drives a scheduled task that prunes stale analytics rows — the same shape as Xperience's own "delete inactive contacts" setting.
**Depends on:** Phase 4 (analytics tables final). Should land before Phase 5 tags v1.0.0 so the default and the setting ship in the release.
**Research:** Likely — XbK settings-key registration from a library (no `ISettingsService`/`SettingsKeyInfo` usage exists in the repo yet); confirm the platform API for module-contributed settings categories/keys.

**Scope:**
- Settings key in the admin **Settings** application (category "Search" or similar): integer days, default 365, validated ≥ 1
- Retention task reads the settings key; `XpSearchAnalyticsOptions.RetentionDays` (code default 180) becomes the fallback/override story — decide precedence in the unit spec (setting wins when present is the expected answer)
- Extend cleanup beyond `XpSearchQueryLogInfo`: evaluate the other analytics-derived tables (popularity scores/suggestions, synonym suggestions, ingestion log) and prune what is honest to prune on the same cutoff
- Existing `XpSearchQueryLogRetentionTask` / `XpSearch.QueryLogRetention` identifier kept (already documented + registered); rename only if the broader scope makes the name a lie
- Guide (`docs/guides/analytics.md` §Retention) updated to point at the setting; ADR-0015 retention paragraph amended; CHANGELOG entry; host-pass checklist items

**Plans:**
- [x] 06-01: AR-1 — global settings row + Search ingestion → Settings page + IConfigureOptions overlay (live), retention 365 + answered-suggestion prune — merged 07928a7 + fix 76bef0e (2026-09-02); owner redirected at checkpoint → per index
- [x] 06-02: AR-2 per-index named options (9a11388) + eviction fix (583fb6b) + UX-1 tooltips + AR-3 widgets-own-sizes (4f6751f) — owner walked §Y 121–125, all pass 2026-09-03. PHASE 6 COMPLETE

### Phase 7: Artboard parity: autocomplete panel + edge states

**Goal:** The default theme reproduces the *Autocomplete panel* and *Edge states* artboards exactly (row icons, recent-row remove, prefix band highlight, Pages meta line; centred white-card empty states with pill chips), and Load more mode gains the missing empty state.
**Depends on:** Nothing hard (runs in parallel with the Phase 6 close-out; different files). Should land before Phase 5 packaging.
**Research:** Unlikely — the artboards are checked in at `docs/internal/design/` and are the spec.

**Plans:**
- [x] 07-01: TH-6 — merged 0f52194 (2026-09-03); owner: 126/127/121 pass, 128/129 → TH-7
- [ ] 07-02: TH-7 — theme hardening against site styles (closed styling boundary, hostile-CSS computed-style check; shell = structure only) + selected refinements at zero hits; spec `docs/internal/units/TH-7.md`, dispatched 2026-09-03
- [x] 07-02: TH-7 — merged b4d1425 (2026-09-03); owner re-walk §Z 130–131 pending
- [x] 07-03: TH-8 — merged fa37700 (2026-09-03): kentico-violet (= default, byte-identical) + kentico-orange from one source; demo host on orange; owner visual sign-off pending (§Z 132). Follow-ups: sheet-scroll 863ffd6 (dvh + check); TH-8b in flight (brand #f05a22 fills + white labels, #c64300 ink)
- [x] 07-04: TH-9 — merged 944da28 (2026-09-03): `xps-sidebar` card + autocomplete no-suggestions state; TH-8b (brand orange fills, ink, on-accent) merged before it
- [ ] 07-05: TH-10 — readable specificity: `.xps.xps-<block>` scoped design rules, the reset keeps the only triple; spec `docs/internal/units/TH-10.md`; dispatched 2026-09-03 — plus TH-11 (layout-parity check shell vs theme; autocomplete row spacing regression) in the same round
- [ ] 07-06: TH-12 — human-readable active filter chips (value labels remembered from responses, numeric ranges as sentences); spec `docs/internal/units/TH-12.md`

---
*Roadmap created: 2026-09-01*
*Last updated: 2026-09-02*
