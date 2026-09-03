# Screenshot manifest

Every image under `docs/guides/images/` has a row here; every row has an image. Captures are
taken by the lead in the in-app browser at a 1440-wide desktop viewport, light theme, against
the Dancing Goat host (`localhost:27340`, admin at `/admin`). A row is STALE when any file in
its *Source files* column changed since its *Captured* date — `/docs-ship` step 1 checks this.

File naming: `<page-slug>--<state>.png` (e.g. `rules--builder-boost.png`). Alt text lives in the
guide pages, not here.

All rows below are captured by `tools/screenshots/` (`npm run capture [-- shotName]`); the
route + interaction steps live in `routes.json` under the same shot name, so "steps" here only
records data prerequisites. Seeded demo data on index `DancingGoatSample` (id 2, kept
deliberately): synonym group `espresso expresso`, stopword list `the a an of and`, field weight
`ArticleTitle ×2`, rule "Boost products for coffee searches" (Query contains "coffee" → Boost
`contentType:DancingGoat.ProductPage` ×2), one concluded experiment "Dancing Goat Experiment".

| Image | URL / route | Data prerequisites | Source files (staleness triggers) | Captured |
|---|---|---|---|---|
| `ingestion--api-keys.png` | `/admin/xpsearch-tuning/api-keys` | ≥1 API key (dev-sample) | `src/XpSearch.Admin/UIPages/ApiKeys.cs` | 2026-09-01 |
| `ingestion--api-key-create.png` | `…/api-keys/create` | — | `src/XpSearch.Admin/UIPages/ApiKeys.cs` | 2026-09-01 |
| `ingestion--log.png` | `/admin/xpsearch-tuning/ingestion-log` | ingestion smoke-test rows | `src/XpSearch.Admin/UIPages/IngestionLog.cs` | 2026-09-01 |
| `lucene--index-listing.png` | `/admin/lucene/indexes` | 2 indexes registered | Kentico Lucene listing + `src/XpSearch.Admin/UIPages/IndexTuning.cs` | 2026-09-01 |
| `index--edit.png` | `/admin/lucene/indexes/edit/2` | — | `src/XpSearch.Admin/UIPages/IndexTuning.cs` | 2026-09-01 |
| `tuning--settings.png` | `…/edit/2/settings` | — | `src/XpSearch.Admin/UIPages/IndexTuning.cs` | 2026-09-01 |
| `index--search-settings.png` | `…/edit/2/search-settings` | — (the form shows the code defaults until saved) | `src/XpSearch.Admin/UIPages/SearchSettings.cs` | STALE - never captured (AR-2); UX-1 gave every field a tooltip and an explanation line, and AR-3 removed *Default page size* and *Default suggestion count*, so the form is fourteen fields and taller than it was |
| `tuning--status.png` | `…/edit/2/status` | built index (35 entries) | `src/XpSearch.Admin/UIPages/IndexStatus.cs`, `Client/src/status/IndexStatusTemplate.tsx` | 2026-09-01 |
| `tuning--analytics.png` | `…/edit/2/analytics` | query-log rows (HW-11 seeding) | `src/XpSearch.Admin/UIPages/Analytics/AnalyticsDashboardPage.cs`, `Client/src/analytics/AnalyticsDashboardTemplate.tsx` | 2026-09-01 |
| `tuning--query-tester.png` | `…/edit/2/query-tester` (runs "coffee") | indexed content | `src/XpSearch.Admin/UIPages/QueryTester/QueryTesterPage.cs`, `Client/src/query-tester/QueryTesterTemplate.tsx` | 2026-09-01 |
| `tuning--rules.png` | `…/edit/2/rules` | seeded rule | `src/XpSearch.Admin/UIPages/Rules.cs` | 2026-09-01 |
| `tuning--rule-builder.png` | `…/edit/2/rules` → open seeded rule | seeded rule | `src/XpSearch.Admin/UIPages/RuleBuilder/RuleBuilderPage.cs`, `Client/src/rule-builder/RuleBuilderTemplate.tsx` | 2026-09-01 |
| `tuning--popularity-suggestions.png` | `…/edit/2/suggestions` | popularity task run | `src/XpSearch.Admin/UIPages/PopularitySuggestions.cs` | 2026-09-01 |
| `tuning--synonyms.png` | `…/edit/2/synonyms` | seeded synonym | `src/XpSearch.Admin/UIPages/Synonyms.cs` | 2026-09-01 — **STALE** (FZ-1 added the typo tolerance callout and header button) |
| `tuning--typo-tolerance.png` | `…/edit/2/synonyms` (typo tolerance off) | seeded synonym; typo tolerance off | `src/XpSearch.Admin/UIPages/Synonyms.cs` | **PENDING** — new in FZ-1; add the shot to `routes.json` and capture with the rest |
| `tuning--synonym-create.png` | `…/edit/2/synonyms/create` | — | `src/XpSearch.Admin/UIPages/Synonyms.cs` | 2026-09-01 |
| `tuning--synonym-suggestions.png` | `…/edit/2/synonym-suggestions` | mined pairs (HW-11 seeding) | `src/XpSearch.Admin/UIPages/SynonymSuggestions.cs` | 2026-09-01 |
| `tuning--stopwords.png` | `…/edit/2/stopwords` | seeded list | `src/XpSearch.Admin/UIPages/Stopwords.cs` | 2026-09-01 |
| `tuning--stopword-create.png` | `…/edit/2/stopwords/create` | — | `src/XpSearch.Admin/UIPages/Stopwords.cs` | 2026-09-01 |
| `tuning--field-weights.png` | `…/edit/2/weights` | seeded weight; popularity-boost callout | `src/XpSearch.Admin/UIPages/FieldWeights.cs` | 2026-09-01 |
| `tuning--field-weight-create.png` | `…/edit/2/weights/create` | schema fields discovered | `src/XpSearch.Admin/UIPages/FieldWeights.cs`, `src/XpSearch.Admin/Forms/WeightFieldConfigurator.cs` | 2026-09-01 |
| `experiments--listing.png` | `…/edit/2/experiments` | concluded experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs` | 2026-09-01 |
| `experiments--create.png` | `…/edit/2/experiments/create` | no running experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs` | 2026-09-01 |
| `experiments--detail.png` | `…/edit/2/experiments` → open experiment | concluded experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs`, `Client/src/experiments/ExperimentDetailTemplate.tsx` | 2026-09-01 |
| `experiments--variant-rules-draft.png` | `…/edit/2/experiments/2/rules` | draft "Docs demo experiment" (left in place) | `ExperimentPages.cs`, `ExperimentScope.cs`, `Rules.cs` | **PENDING** — run `npm run capture -- experiments--variant-rules-draft experiments--variant-rules-readonly` while the capture profile is signed in (session was lost 2026-09-01; sign in once in the window it opens) |
| `experiments--variant-rules-readonly.png` | `…/edit/2/experiments/1/rules` | concluded experiment 1 (read-only banner, empty after discard) | `ExperimentPages.cs`, `ExperimentScope.cs`, `Rules.cs` | **PENDING** — same run as above |

No widget property dialog has a row here yet. UX-1 rewrote every widget property's tooltip and
explanation, and AR-3 made the Results widget's *Results per page* and the two suggestion counts
required (one or greater), so any Page Builder dialog shot added later must be taken after those
changes.
No row covers the front-end widgets, so TH-6 (autocomplete panel + empty states + the Load more
empty state) and TH-7 (theme hardening: themed checkboxes, sheet heading, empty-state button, the
did-you-mean link) have nothing to mark stale here: the theme is verified against
`docs/internal/design/*.dc.html`, `themes/test/section-*.html` and, since TH-7, by
`themes/scripts/check-isolation.mjs`. Add `widgets--autocomplete-panel` and `widgets--empty-states`
rows when the guides start showing them.

| Image | URL / route | Data prerequisites | Source files (staleness triggers) | Captured |
|---|---|---|---|---|
| `themes--kentico-orange.png` | `themes/test/section-kentico-orange.html` (opens from disk, no server) | — | `themes/src/scss/tokens/_kentico-orange.scss`, `themes/src/kentico-orange.css`, `themes/fixtures/*.html` | **STALE** — new in TH-8, never captured. Shoot it beside `section-default.html` so the guide's *Two shipped palettes* section can show the pair. |

Pending captures worth adding once data exists: `tuning--popularity-suggestions` and
`tuning--synonym-suggestions` with ≥1 mined row (current captures show empty tables; recapture
after a popularity-task run produces suggestions).
