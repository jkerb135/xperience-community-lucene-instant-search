---
description: "Kentico Xperience developers get Algolia-class instant search — facets, tuning, analytics, A/B testing — self-hosted on Lucene with no SaaS bill"
type: Project
about: "xperience-search"
---

# xperience-search

## What This Is

A suite of packages (NuGet `XperienceCommunity.Search.{Core,Widgets,Admin,Ingestion}`, npm `@xperience-community/xperience-search` + `-themes`) that gives Xperience by Kentico sites an instant-search experience on top of the native Kentico.Xperience.Lucene integration: an owned JSON search contract, a server-side query pipeline (facets, synonyms, stopwords, field weights, rules, popularity boosts, experiments, personalization), Page Builder widgets with a typed JS widget library, an admin Search tuning application, external-document ingestion, and full analytics.

## Core Value

Xperience by Kentico developers and editors get a complete, tunable, measurable site-search product — the kind teams usually buy from Algolia — running entirely on the Lucene index they already have.

## Current State

| Attribute | Value |
|-----------|-------|
| Type | Application (library suite + sample host) |
| Version | 0.9.0 (unpublished; Phase 8 packaging pending) |
| Status | Beta — spec Phases 0–7 gated, front-end polish wave in flight |
| Last Updated | 2026-09-01 |

**Production URLs:**
- https://jkerb135.github.io/xperience-community-lucene-instant-search — published guides (GitHub Pages)
- https://github.com/jkerb135/xperience-community-lucene-instant-search — repository

## Requirements

### Core Features

- Owned search contract + query pipeline: free-text search with facets, numeric filters, highlighting, sorting, paging over Kentico's Lucene indexes (native taxonomy faceting, ADR-0001)
- Relevance tuning admin app: synonyms, stopwords, field weights, rules (boost/pin/bury/redirect), popularity boosts, A/B experiments, mined suggestions
- Page Builder widgets + typed JS widget library (13 widgets), npm-first bundler ingestion with tag-helper fallback, server-rendered first paint, themeable (Kentico Violet default)
- Search analytics: query journal, no-results, click tracking, volume reports, activities for personalization/contact groups
- External-document ingestion API with durable replay across index rebuilds

### Validated (Shipped)

- [x] Spec Phases 0–7 gated (core pipeline, admin tuning, widgets, analytics, ingestion, experiments, personalization, popularity/synonym mining) — merged through main, suites green 2026-09-01 (Core 286, Admin 187, Widgets 76, Ingestion 47, JS 224)
- [x] Packaging/theming wave: PK-1 npm-first distribution, PK-2 rendering extraction, TH-1 default theme, TH-2 mobile filter sheet, TH-3 searchBox suggestions
- [x] Docs pipeline: GitHub Pages guides (19 pages), screenshot tooling, /docs-ship skill

### Active (In Progress)

- [ ] TH-4 — default shell polish + ActiveFilters/ClearFilters PB widgets (dispatched, worktree `th-4`)
- [ ] Host adoption pass after TH-4 (pre-authorized: imports, PB composition, rebuild, mockup compare)
- [ ] Owner HW-11 host-pass checklist (signed-in items §A–C/E and others outstanding)

### Planned (Next)

- [ ] FZ-1 — fuzzy search (typo tolerance) as a per-index configuration value; spec ready at `docs/internal/units/FZ-1.md`
- [ ] Admin command-discovery defect unit ('command not found' on re-annotated [PageCommand] overrides)
- [ ] §10.5 typed clients, §10.7 example 2, §12 performance pass
- [ ] Phase 8 — packaging & public release (NuGet/npm publish)

### Out of Scope

- Algolia API compatibility — owned contract instead (owner decision 2026-08-21)
- Sourcemap/host bundle forks — extension surface is custom widgets + pipeline stages only (owner 2026-08-23)
- Did-you-mean / fuzzy rule matching — future units, noted in KNOWN-LIMITATIONS

## Target Users

**Primary:** Xperience by Kentico developers integrating site search
- Consume NuGet + npm packages, compose PB widgets or bundle JS widgets
- Need working defaults that match the approved design out of the box

**Secondary:** Editors/marketers in the Xperience admin
- Tune relevance, review analytics, run experiments without developer help

## Context

**Business Context:** Community open-source project (jkerb135). Docs ship to GitHub Pages; distribution via public NuGet/npm once Phase 8 lands.

**Technical Context:** Built on Kentico.Xperience.Lucene 15.0.5 (XbK ≥ 31.0.0), Lucene.Net pinned 4.8.0-beta00017. Sample host = Dancing Goat at `F:\Personal\CommunityProjects\src` (port 27340). Full internal record: `docs/internal/phase-log.md`, unit specs in `docs/internal/units/`, ADRs, KNOWN-LIMITATIONS.md.

## Constraints

### Technical Constraints

- Lucene.Net pinned to exactly 4.8.0-beta00017 (NU5104 suppressed)
- Core must work without Admin installed (seams with empty defaults); Widgets is the only package referencing Page Builder
- Frozen owned JSON contract — changes are versioned, never casual
- Never touch `src/Components/Widgets/CardWidget/`; Kentico docs MCP mandatory for Xperience API questions

### Business Constraints

- Shipped defaults must match the approved mockup exactly ("default is the design", owner 2026-09-01)
- Docs are wiki-ready guide pages with verified samples per unit, not batched at the end
- Host verification pass required after every C# wave before the next gate

## Key Decisions

| Decision | Rationale | Date | Status |
|----------|-----------|------|--------|
| Owned contract, not Algolia mirror | Control + no trademark/compat burden | 2026-08-21 | Active |
| Native Lucene taxonomy faceting (option A) | Built into Kentico.Xperience.Lucene; 1.4–1.9× faster, ADR-0001 | 2026-08-21 | Active |
| npm-first distribution, tag helper as fallback | Bundler ingestion is the primary integration path | 2026-09-01 | Active |
| Extend via plugins (widgets + pipeline stages) | No bundle forks | 2026-08-23 | Active |
| Spec → implement → review loop | Fable specs `docs/internal/units/`, Opus implements, Fable reviews | 2026-08 | Active |
| Fuzzy search = per-index admin toggle, default off | No contract/JS change; opt-in like popularity boost (FZ-1 spec) | 2026-09-01 | Active |

## Success Metrics

| Metric | Target | Current | Status |
|--------|--------|---------|--------|
| C#/JS test suites | All green per merge | Core 286 / Admin 187 / Widgets 76 / Ingestion 47 / JS 224 | Achieved (rolling) |
| Default UI vs approved mockup | Pixel-faithful out of the box | TH-4 closing the gap | At risk until TH-4 + host pass |
| HW-11 host checklist | All items pass | Headless subset passed; owner items open | In progress |
| Public release | NuGet + npm published | Unpublished | Not started |

## Tech Stack / Tools

| Layer | Technology | Notes |
|-------|------------|-------|
| Search engine | Lucene.Net 4.8.0-beta00017 via Kentico.Xperience.Lucene 15.0.5 | Pinned |
| Platform | Xperience by Kentico ≥ 31.0.0, .NET | Sample host: Dancing Goat |
| Admin UI | Xperience admin framework (React) | Search tuning app |
| Front-end | TypeScript widget library, SCSS themes, Vite/Rollup builds | 13 widgets, per-widget subpaths |
| Docs | GitHub Pages (Jekyll primer), Playwright screenshots | /docs-ship skill |
| CI/verification | dotnet test ×4 suites, vitest, theme fixture checks | Host pass per wave |

## Links

| Resource | URL |
|----------|-----|
| Repository | https://github.com/jkerb135/xperience-community-lucene-instant-search |
| Documentation | https://jkerb135.github.io/xperience-community-lucene-instant-search |
| Internal record | docs/internal/phase-log.md, docs/internal/units/ |

---
*PROJECT.md — Updated when requirements or context change*
*Last updated: 2026-09-01*
