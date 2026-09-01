# DOC-1 — user-guide suite: configuration reference, breaking changes, wiki home, admin tour, screenshot passes

> **Scope directive (owner, 2026-09-01): document the ADMIN surface only for now** — the front end
> (widgets, JS client, public pages) is mid-change (PK-1/PK-2, HW-13). Active deliverables: D4
> (admin-ui-tour) and the admin-page screenshot passes from D5 (analytics, relevance-tuning,
> popularity-boosts, experiments, ingestion admin sections). D1, D2, D3, and all widget/public
> captures are DEFERRED until the front-end wave merges; the post-merge `/docs-ship` gap check
> re-opens them.

## Problem

The 16 guide pages are wiki-ready but have zero images and no single page that (a) lands a new
reader (wiki Home), (b) shows the admin surface visually, or (c) collects every configuration
option in one reference. Breaking changes are buried in CHANGELOG prose. The suite ships to the
GitHub wiki via `/docs-ship` (`.claude/skills/docs-ship/SKILL.md`).

## Design decisions (already made — do not relitigate)

- Images live at `docs/guides/images/<page-slug>--<state>.png`, referenced relatively
  (`images/x.png`), captured by the lead and listed in `docs/internal/screenshot-manifest.md`.
  Writers embed ONLY manifest-listed images; missing captures become capture requests.
- All pages follow `docs/README.md` wiki-readiness rules (## title, standalone, sample-first,
  relative links, `### Related pages` closer).
- There is NO appsettings/IConfiguration surface — all configuration is code-first lambdas on the
  `Add*` extensions. The reference must say so explicitly.
- Prose stays sample-first; images illustrate, never replace, samples.

## Deliverables

### D1. `docs/guides/configuration-reference.md`
Every configurable knob, defaults exactly as implemented (verify each against source, cite nothing
unverified):
- `AddXpSearch()` overloads + `UseXpSearch()` (`src/XpSearch.Core/DependencyInjection/XpSearchServiceCollectionExtensions.cs`),
  `AddXpSearchAdmin()`, `AddXpSearchWidgets()`, `AddXpSearchIngestion()`/`UseXpSearchIngestion()`,
  `AddXpSearchBucketCookie()`, `AddXpSearchStage<T>()`.
- `XpSearchOptions` (`src/XpSearch.Core/Options/XpSearchOptions.cs`), `XpSearchIndexOptions`
  (SortKeys/SuggestMode/SuggestField), `XpSearchAnalyticsOptions`
  (`src/XpSearch.Core/Analytics/XpSearchAnalyticsOptions.cs`), `XpSearchIndexingOptions` fluent API
  (`src/XpSearch.Core/Indexing/XpSearchIndexingOptions.cs`), `XpSearchIngestionOptions` +
  per-index ingestion options.
- The 9 Page Builder widget property tables (from `src/XpSearch.Widgets/Components/Widgets/XpSearch/`)
  plus the shared `Index`/`InstanceId` base (`Mounting/XpSearchMountWidgetProperties.cs`).
- JS `createSearch()` options (`src/XpSearch.Widgets/Client/src/types.ts`, `SearchOptions`).
- Scheduled tasks: `XpSearch.PopularitySignal` + analytics retention (see `docs/guides/analytics.md`).
- Lead sample: a realistic `AddXpSearch(o => …)` Program.cs block, RUN (compile it against the
  packages via the sample host project or a compile probe) before inclusion.
- Cross-link each section to its topic guide. This page is a reference; topic guides keep the
  narrative.

### D2. `docs/guides/breaking-changes.md`
Generated from CHANGELOG breaking entries (`**Breaking (scope):**` plus legacy
`**Changed (breaking, …)**` and inline bolded `**Breaking**` hits — judge each grep hit).
Structure: SemVer policy paragraph (mirror the CHANGELOG header rule), then per released version
plus Unreleased, each breaking entry with a one-line summary + migration note (migration notes
only from verified source reading; entry meaning stays faithful to CHANGELOG).

### D3. `docs/guides/Home.md`
Wiki landing page: what the product is (owned search for Xperience by Kentico on Lucene — see
root `README.md`), a hero screenshot (capture request if not in manifest yet), and a complete
guide index grouped by audience: content editors (widgets, tuning, analytics), developers
(quick-start, js-client, custom-widgets, theming, search-api, ingestion, configuration-reference…),
administrators (admin-ui-tour, breaking-changes, migrating-from-algolia). Every existing guide page
must appear exactly once.

### D4. `docs/guides/admin-ui-tour.md`
Screenshot-led tour (dispatched AFTER captures exist in the manifest): how to reach the tuning
pages (they are grafted onto the Lucene admin's index listing — Search → Indexes → index → tuning
menu — NOT under the "Search ingestion" app), then one section per page: Settings, Status,
Analytics, Query tester, Rules (+ builder, + from-query), Synonyms (+ suggestions), Stopwords,
Field weights (+ popularity toggle, + suggestions), Experiments (listing/create/detail + the
variant-scoped copies of the four tuning listings and their read-only-after-start state), and the
Search ingestion app (API keys, Ingestion log). Each section links to the topic guide for depth.

### D5. Screenshot passes over existing pages
(dispatched AFTER captures exist) Embed manifest images into: quick-start,
page-builder-widgets, widget-reference, analytics, relevance-tuning, popularity-boosts,
experiments, search-personalization, ingestion, theming. No prose restructuring beyond what the
image needs; samples stay first.

## Constraints

- Never touch `docs/api/`, production code, or other `docs/internal/` files.
- Scope all globs/greps to `docs/` and `src/` (`.claude/worktrees/` = stale full copies).
- PK-1 (npm packaging) and PK-2 (SSR lift) are in flight and will change the JS/rendering surface;
  write against current `main` — the post-merge `/docs-ship` gap check picks up the delta.
- CHANGELOG `[Unreleased]` entry for the docs suite (one entry covering DOC-1, type `Added (docs)`).

## Verification

- Run every included sample; report the command + output.
- Relative-link/image check over all touched pages (every target exists in `docs/guides/`).
- Option defaults cross-checked against source lines, not the spec.
