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
| `tuning--status.png` | `…/edit/2/status` | built index (35 entries) | `src/XpSearch.Admin/UIPages/IndexStatus.cs`, `Client/src/status/IndexStatusTemplate.tsx` | 2026-09-01 |
| `tuning--analytics.png` | `…/edit/2/analytics` | query-log rows (HW-11 seeding) | `src/XpSearch.Admin/UIPages/Analytics/AnalyticsDashboardPage.cs`, `Client/src/analytics/AnalyticsDashboardTemplate.tsx` | 2026-09-01 |
| `tuning--query-tester.png` | `…/edit/2/query-tester` (runs "coffee") | indexed content | `src/XpSearch.Admin/UIPages/QueryTester/QueryTesterPage.cs`, `Client/src/query-tester/QueryTesterTemplate.tsx` | 2026-09-01 |
| `tuning--rules.png` | `…/edit/2/rules` | seeded rule | `src/XpSearch.Admin/UIPages/Rules.cs` | 2026-09-01 |
| `tuning--rule-builder.png` | `…/edit/2/rules` → open seeded rule | seeded rule | `src/XpSearch.Admin/UIPages/RuleBuilder/RuleBuilderPage.cs`, `Client/src/rule-builder/RuleBuilderTemplate.tsx` | 2026-09-01 |
| `tuning--popularity-suggestions.png` | `…/edit/2/suggestions` | popularity task run | `src/XpSearch.Admin/UIPages/PopularitySuggestions.cs` | 2026-09-01 |
| `tuning--synonyms.png` | `…/edit/2/synonyms` | seeded synonym | `src/XpSearch.Admin/UIPages/Synonyms.cs` | 2026-09-01 |
| `tuning--synonym-create.png` | `…/edit/2/synonyms/create` | — | `src/XpSearch.Admin/UIPages/Synonyms.cs` | 2026-09-01 |
| `tuning--synonym-suggestions.png` | `…/edit/2/synonym-suggestions` | mined pairs (HW-11 seeding) | `src/XpSearch.Admin/UIPages/SynonymSuggestions.cs` | 2026-09-01 |
| `tuning--stopwords.png` | `…/edit/2/stopwords` | seeded list | `src/XpSearch.Admin/UIPages/Stopwords.cs` | 2026-09-01 |
| `tuning--stopword-create.png` | `…/edit/2/stopwords/create` | — | `src/XpSearch.Admin/UIPages/Stopwords.cs` | 2026-09-01 |
| `tuning--field-weights.png` | `…/edit/2/weights` | seeded weight; popularity-boost callout | `src/XpSearch.Admin/UIPages/FieldWeights.cs` | 2026-09-01 |
| `tuning--field-weight-create.png` | `…/edit/2/weights/create` | schema fields discovered | `src/XpSearch.Admin/UIPages/FieldWeights.cs`, `src/XpSearch.Admin/Forms/WeightFieldConfigurator.cs` | 2026-09-01 |
| `experiments--listing.png` | `…/edit/2/experiments` | concluded experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs` | 2026-09-01 |
| `experiments--create.png` | `…/edit/2/experiments/create` | no running experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs` | 2026-09-01 |
| `experiments--detail.png` | `…/edit/2/experiments` → open experiment | concluded experiment | `src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs`, `Client/src/experiments/ExperimentDetailTemplate.tsx` | 2026-09-01 |
