---
phase: 07-artboard-parity
plan: 02
subsystem: ui
tags: [themes, specificity, isolation, layout-parity, palettes, kentico-orange, active-filters, facets]
requires:
  - phase: 07-artboard-parity
    provides: 07-01 boards + panel/empty-state markup
provides:
  - Shipped default theme is a closed styling boundary (isolation check vs Dancing Goat CSS: 6677 → 0 leaks); shell is structure only
  - Readable selectors (`.xps.xps-<block> …`), reset keeps the only triple, guard against repeats
  - Layout-parity check (shell alone ≡ shell + theme), autocomplete spacing per board, lone-group header from the response
  - Two palettes from one source: kentico-violet (= default) and kentico-orange (brand #f05a22 fills, #c64300 ink, white labels owner-accepted at 3.39:1); demo host on orange
  - Sidebar card composition class; autocomplete no-suggestions state (boards updated)
  - Human-readable filters on every surface; facets always carry selected values with labels; first paint seeds labels
  - Filter sheet sized in dvh with a scroll check
affects: [05 packaging (CHANGELOG has several Changed/Breaking entries to cut), docs pass (contract prose on zero-count facets)]
key-files:
  created: [themes/scripts/check-isolation.mjs, check-layout.mjs, check-sheet.mjs, browser.mjs, themes/test/site-hostile.css, themes/src/scss/_boxes.scss, default/_root.scss, tokens/_kentico-{violet,orange}.scss, themes/src/kentico-{violet,orange}.css, src/XpSearch.Widgets/Client/src/labels.ts, docs/internal/units/TH-7..TH-12.md, FC-1.md]
  modified: [themes/src/scss/**, src/XpSearch.Widgets/Client/src/widgets/*, src/XpSearch.Core/Facets/TaxonomyFacetProvider.cs, src/XpSearch.Core/Rendering/ServerRenderedResults.cs, src/XpSearch.Widgets/Rendering/XpSearchAssets.cs, docs/guides/theming.md, widget-reference.md, search-api.md]
key-decisions:
  - "Shell = structure only; default theme owns every visual property at (0,3,0)+ without !important; tokens stay (0,1,0) as the override surface"
  - "Design rules `.xps.xps-<block>` (two arms for the editor preview); the reset is the only triple"
  - "Accent split into accent / accent-ink / on-accent; orange buttons white-on-#f05a22 by owner decision, printed as owner-accepted"
  - "No filter code reaches an end user: widget labels → remembered value labels → raw value; operators/attributes always phrased"
  - "Selected facet values always returned with labels at count 0 (server), first paint seeds the client"
duration: ~8h across TH-7, sheet-scroll, TH-8, TH-8b, TH-9, TH-10, TH-11, TH-12, FC-1
completed: 2026-09-03T14:00:00Z
description: "Theme hardened into a closed boundary with readable selectors and layout parity, two shipped palettes with the demo on brand orange, sidebar card and no-suggestions state, human-readable filters end to end — owner approved the full Phase 7 review"
type: Summary
about: "xperience-search"
---

# Phase 7 Plan 02 (+ 07-03 … 07-07): theme hardening through FC-1 — Summary

**Owner approved the consolidated review 2026-09-03: §Z 130–132, the sidebar card, the no-suggestions panel, the panel spacing, the chips and the selectors.** Units and merges (gates in `docs/internal/phase-log.md`): TH-7 b4d1425 · sheet-scroll 863ffd6 · TH-8 fa37700 · TH-8b · TH-9 944da28 · TH-10+TH-11 0e2d7d8 · TH-12 eed214c · FC-1 5e1003d.

## Acceptance (07-02 plan + the roadmap plans 07-03..07-07)

| Criterion | Status |
|---|---|
| AC-1 no site style bleeds (isolation check fails before, passes after) | Pass — 6677 → 0; 73 584 values × 2 palettes |
| AC-2 sheet + quartet on the host | Pass (owner 130) |
| AC-3 refinements at zero hits | Pass (owner 131; categoryTree was the real defect) |
| 07-03 two palettes, demo on orange | Pass (owner 132) |
| 07-04 sidebar card + no-suggestions state | Pass (owner) |
| 07-05 readable selectors + layout parity + panel spacing | Pass (owner; 144 layout diffs fixed) |
| 07-06 human-readable filters | Pass (owner; DOM code-scan test) |
| 07-07 selected facet values with labels | Pass (lead-verified: "Products: Hot tips" on a zero-hit deep link) |

## Deviations and follow-ups

- Sheet "doesn't scroll" was not reproducible in Chromium; shipped the real-phone cause (dvh) + hardening + check. Owner later passed 130.
- Brand orange white-on-fill is 3.39:1 (below AA text) — owner decision, printed on every build, KNOWN-LIMITATIONS + host escape token.
- Generated contract prose still says zero-count facet values disappear (`XpSearchContract.g.cs:352`) — contract-doc-only edit for Phase 5.
- First-paint label seed carries leaf labels only (no path) until the first response.
- Five Admin Client src files modified in the main working tree by the owner's own session — never staged by the lead.
- Host deploy rule: widgets client build → `src/` Vite build → sln build → restart; the in-app browser caches bundles (refetch with cache:'reload').

## Next Phase Readiness

**Ready:** Phase 5 packaging — CHANGELOG Unreleased carries the final designs (rewrite-in-place discipline held). **Concerns:** CSS grew to ~38.6 kB (+15% for the editor-preview arm); screenshot rows STALE for the docs pass. **Blockers:** none.

---
*Completed 2026-09-03*
