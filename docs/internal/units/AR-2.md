# Unit AR-2 — Search settings per index via named options

Owner decision 2026-09-03, replacing AR-1's global layer: every value AR-1 made editable is edited
**per index**. `IOptionsMonitor<XpSearchIndexSettings>.Get(indexCodeName)` returns that index's
settings — its stored row over the code defaults — a save on one index rebuilds only that index's
instance, and the retention and popularity tasks apply each index's own numbers. The global
Settings page and the global row go away. No contract change, no JS change, no widget change.

Read `docs/internal/agent-primer.md`, then `docs/internal/units/AR-1.md` (this unit reshapes AR-1's
files; it does not start over). Work only in this worktree (branch `unit/ar-2`). Kentico/Options
API questions: docs MCP for Kentico; the `Microsoft.Extensions.Options` semantics below are stated
so you do not have to rediscover them.

## 1. Options model

- New `XpSearchIndexSettings` (Core, `Options/`): the sixteen values from AR-1's §1 table as
  properties — `CacheTtl` (TimeSpan), `MaxQueryLength`, `DefaultPageSize`, `MaxPageSize`,
  `MaxFacetValues`, `MaxResultWindow`, `DefaultSuggestLimit`, `MaxSuggestLimit`, `RetentionDays`
  (365), `RetentionBatchSize`, `QuerySuggestionDays`, `PopularityLookbackDays`,
  `PopularityDocumentLimit`, `PopularitySuggestionQueries`, `SynonymWindowSeconds`,
  `SynonymMinimumOccurrences` — with the defaults `XpSearchOptions`/`XpSearchAnalyticsOptions`
  carry today.
- `XpSearchOptions` keeps its root scalars and `Analytics` **as the code defaults** for every index,
  and keeps `Indexes[...]` for the code-only per-index members (sort keys, suggest field/mode,
  did-you-mean, popular searches). The host lambda does not change. Document the root values as
  "defaults for every index; the administration overrides them per index".

## 2. Loading — `IConfigureNamedOptions<XpSearchIndexSettings>`

- `XpSearchIndexSettingsSetup : IConfigureNamedOptions<XpSearchIndexSettings>` in Core.
  `Configure(string? name, settings)`: copy the root defaults from `IOptions<XpSearchOptions>`
  (root scalars + `Analytics`), then, when `name` is not null/empty, read that index's row
  (`IStoredSearchSettingsSource.Get(indexName)` — now takes a name) and overlay it. `Configure(settings)`
  = defaults only. Read the row **directly, uncached** (76bef0e reasoning: `OptionsMonitor` caches
  per name until invalidated; this runs once per index per save). A failed read is logged at
  Debug and leaves the defaults.
- Registered in `AddXpSearch` after `services.Configure(configure)`, via
  `TryAddEnumerable(Singleton<IConfigureOptions<XpSearchIndexSettings>, ...>)` — a class that
  implements `IConfigureNamedOptions<T>` is registered as `IConfigureOptions<T>`; that is the
  documented shape.
- **Invalidation per name:** delete `XpSearchSettingsChangeTokenSource`. Add one singleton
  (`XpSearchIndexSettingsInvalidator`, started from `XpSearchAnalyticsModule.OnInit` or resolved
  eagerly in `AddXpSearch` the way AR-1's token source was) subscribing once to
  `XpSearchSettingsInfo.TYPEINFO.Events.Insert.After / Update.After / Delete.After` and calling
  `IOptionsMonitorCache<XpSearchIndexSettings>.TryRemove(row.SettingsIndexName)`. Nothing else
  is invalidated; `Get(otherIndex)` keeps its instance.
- **Name resolution:** `IOptionsMonitor.Get(name)` is ordinal; index code names are sanitised by
  the admin (no spaces/invalid characters) so only case can differ between a request's `index`
  and the registered name. Add `string? ILuceneIndexAccessor.ResolveName(string indexName)`
  (registered name matched OrdinalIgnoreCase over `indexManager.GetAllIndices()`, or null). Every
  `Get(...)` in the pipeline uses the resolved name; an unknown index passes the raw name through
  (the existing IndexNotFound paths still throw where they do today). Do not normalise case any
  other way.

## 3. Storage — one row per index

- `XpSearchSettingsInfo` gains `SettingsIndexName` (text 100, not empty); installer form updated
  (`CombineWithForm` adds the column on upgrade). The installer **deletes rows whose index name is
  empty** (AR-1's global row — never released) and no longer seeds anything at startup: a row
  exists only after a save from the page. `StoredSearchSettings.Read/NewRow` carry the index name;
  `SearchSettingsValues.From(XpSearchIndexSettings)` / `ApplyTo(XpSearchIndexSettings)` replace
  the `XpSearchOptions` overloads. Keep the single Info creation site (RK-2 guard).

## 4. Consumers

Inject `IOptionsMonitor<XpSearchIndexSettings>` and call `Get(resolvedIndex)` **per operation**
(never captured in a field) in: `CachedSearchPipeline` (resolve the name first — it reads
`MaxQueryLength`/`CacheTtl` before validation), `ProgressiveSearchCache` (TTL by index),
`NormalizeRequestStage`, `CollectFacetsStage`, `DocumentSuggestService`, `QuerySuggestionService`,
`RecoverySearchPipeline` (if it reads any of the sixteen; `Indexes[...]` reads stay on
`XpSearchOptions`), `XpSearchQueryLogRetentionTask`, `XpSearchPopularityTask`. Widgets'
`FilterSortWidget`/`SortSelectWidget` only read `Indexes[...]` — unchanged. `SearchCacheKey` is
unchanged (values shape the request below the cache, as AR-1 established).

## 5. Tasks per index

- Retention: for each registered index (`indexManager.GetAllIndices()` through the accessor —
  add `IReadOnlyList<string> IndexNames()` if `IndexNamesForStrategy` does not fit), `Get(name)` →
  cutoff/batch → the three prunes **filtered by index name**: `IQueryLogStore.DeleteOlderThanAsync`
  and both `DeleteAnsweredOlderThanAsync` gain a `string indexName` parameter (WhereEquals on the
  index-name column, OrdinalIgnoreCase is not needed — stored names are the registered ones).
  Then orphans: distinct index names present in the three tables but not registered → pruned with
  `Get(string.Empty)`-style defaults (call the unnamed `Configure` path: `Get(Options.DefaultName)`),
  logged at Information naming the orphan index. Result message compact per index:
  `DancingGoatSample: 619 query log rows, 0 popularity suggestions, 0 synonym suggestions (older than 2026-09-02); Other: …`.
- Popularity/synonym mining: `Get(group.Key)` for `PopularityLookbackDays`, `PopularityDocumentLimit`,
  `PopularitySuggestionQueries`, `SynonymWindowSeconds`, `SynonymMinimumOccurrences` per group.

## 6. Admin

- Delete `src/XpSearch.Admin/UIPages/GlobalSettings.cs`.
- New `src/XpSearch.Admin/UIPages/SearchSettings.cs`: `SearchSettingsModel : IIndexScopedModel`
  (AR-1's sixteen annotated fields + hidden `IndexName`) and
  `SearchSettingsPage : IndexScopedEditPage<SearchSettingsModel>` registered
  `[UIPage(parentType: typeof(IndexTuningSection), slug: "search-settings", name: "Search settings",
  templateName: TemplateNames.EDIT, order: 110)]` — the Lucene integration's own "Settings" page
  already owns slug `settings` at order 100; do not collide. `CreateModel` = `Get(IndexName)`;
  `PersistAsync` loads the row by index name → update, else insert (`NewRow` + index name). Same
  cross-index refusal as the other scoped pages. Add the page to `PageCommandDiscoveryTests`.
- Screenshot manifest: rename AR-1's `ingestion--settings.png` row to `index--search-settings.png`
  (STALE, never captured); note it in the report.

## 7. Tests (existing suites, no new frameworks)

- Named overlay: stored row wins for its name, defaults for a name with no row, unnamed = defaults,
  and `ResolveName` maps a different-cased request to the registered name (fake accessor).
- Invalidation: with a fake `IOptionsMonitorCache<XpSearchIndexSettings>` (or the real
  `OptionsCache<T>`), a save event for index A removes A only.
- Retention: in-memory fakes now record the index-name argument; AC-4 = A (retention 1) pruned,
  B (365) kept, orphan pruned with defaults; message asserted.
- Adapt `StoredSettingsTests`, `QueryLogTests`, `PopularityTests`/`SynonymMiningTests` call sites;
  `InfoCreationSiteTests` (new column set at the one creation site), `PageCommandDiscoveryTests`.
- All five suites green (Widgets/Admin clients built first).

## 8. Docs

- `CHANGELOG.md` `[Unreleased]`: **rewrite** AR-1's two entries (do not append a third): Breaking
  (core) = root lambda values are defaults per index, the administration overrides per index on
  Lucene Search → index → Search settings, `IOptions<XpSearchOptions>` consumers should use
  `IOptionsMonitor<XpSearchIndexSettings>.Get(index)`, `Analytics.RetentionDays` default 365;
  Added (core, admin) = the per-index page, live per-index updates, per-index retention incl.
  answered suggestions.
- `docs/guides/analytics.md` Retention: page path per index; per-index Last result; orphan rule.
- `docs/guides/search-api.md`: the AR-1 section becomes "Per-index settings in the administration"
  (table, defaults-from-code, no seeding, case rule is invisible to hosts).
- `docs/adr/0015-analytics.md`: amend the AR-1 amendment (per index, named options, why: every
  value is per index; multiple indexes = one cached instance per name).
- `docs/internal/KNOWN-LIMITATIONS.md`: orphan-index rows pruned with defaults; keep the
  re-suggest entry; drop AR-1's "upgrade-added column repair" entry (no seeding any more —
  a missing column simply reads 0 → treat `<= 0` as "use default" in the overlay for the ≥1
  columns, `CacheTtl` exempt; say so).
- `docs/internal/agent-primer.md`: replace AR-1's pattern line with the per-index one.

## 9. Constraints

- Core must not gain an Admin or Page Builder dependency. No new NuGet dependency. No contract
  regeneration. Do not touch `src/Components/Widgets/CardWidget/`.
- No global override layer; no per-index settings for `Indexes[...]` code-only members.
- Build only `libraries/**` from this worktree; the reviewer runs the full solution.
- One conventional commit on `unit/ar-2`:
  `feat(core,admin): per-index search settings via named options (AR-2)`.

## 10. Report

Suite summary lines (all five), the invalidation mechanism + which event names you subscribed,
how `ResolveName` is used in `CachedSearchPipeline`, the orphan handling, files changed, commit
hash, DONE_WITH_CONCERNS items. No diffs.
