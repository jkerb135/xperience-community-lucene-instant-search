# Unit AR-1 — Global search settings in the administration, loaded via ConfigureOptions

**Revision 2 (2026-09-02, supersedes rev 1 in this same file's history).** Owner widened the unit
mid-flight: not one retention key, but **every global value of `XpSearchOptions` and
`XpSearchAnalyticsOptions`** becomes editable in the administration and is loaded into the options
object through `IConfigureOptions<XpSearchOptions>`. The retention threshold is the headline case:
**"Remove search analytics older than X days", default 365**, honoured by the existing
`XpSearch.QueryLogRetention` task, which also prunes *answered* popularity and synonym suggestions.

What changed from rev 1 — read this if you already started:
- **No `SettingsKeyInfo` / `SettingsCategoryInfo` rows.** Kentico's docs state cache dependencies
  on settings keys work only for built-in keys and that custom settings are a custom info object
  with your own UI (cache-dependencies page, "Settings keys"). We follow that: one typed Info row
  in our module + one edit page in our own admin application.
- **`RetentionDays` is NOT removed.** It stays on `XpSearchAnalyticsOptions` (default now 365) and
  is *fed* from the stored row like every other global. The task keeps reading options.
- Everything about the suggestion prune (§4) and the tests for it is unchanged.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/ar-1`). Kentico API
questions go to the docs MCP; do not guess column semantics.

## 1. Scope — which settings move

**In (global scalars, one stored row):**

| Options property | Stored column | UI label | Default | Validation |
|---|---|---|---|---|
| `CacheTtl` | `SettingsCacheTtlSeconds` (int) | Response cache lifetime (seconds) | 60 | ≥ 0 |
| `MaxQueryLength` | `SettingsMaxQueryLength` | Maximum query length | 256 | 1–1000 |
| `DefaultPageSize` | `SettingsDefaultPageSize` | Default page size | 20 | 1–1000 |
| `MaxPageSize` | `SettingsMaxPageSize` | Maximum page size | 100 | 1–1000 |
| `MaxFacetValues` | `SettingsMaxFacetValues` | Maximum values per facet | 100 | ≥ 1 |
| `MaxResultWindow` | `SettingsMaxResultWindow` | Maximum result window | 10000 | ≥ 1 |
| `DefaultSuggestLimit` | `SettingsDefaultSuggestLimit` | Default suggestion count | 5 | 1–100 |
| `MaxSuggestLimit` | `SettingsMaxSuggestLimit` | Maximum suggestion count | 20 | 1–100 |
| `Analytics.RetentionDays` | `SettingsRetentionDays` | **Remove search analytics older than X days** | **365** | ≥ 1 |
| `Analytics.RetentionBatchSize` | `SettingsRetentionBatchSize` | Retention batch size | 1000 | ≥ 1 |
| `Analytics.QuerySuggestionDays` | `SettingsQuerySuggestionDays` | Query suggestion window (days) | 30 | ≥ 1 |
| `Analytics.PopularityLookbackDays` | `SettingsPopularityLookbackDays` | Popularity lookback (days) | 30 | ≥ 1 |
| `Analytics.PopularityDocumentLimit` | `SettingsPopularityDocumentLimit` | Popularity documents per index | 100 | ≥ 1 |
| `Analytics.PopularitySuggestionQueries` | `SettingsPopularitySuggestionQueries` | Popularity suggestion queries | 10 | ≥ 1 |
| `Analytics.SynonymWindowSeconds` | `SettingsSynonymWindowSeconds` | Synonym reformulation window (seconds) | 60 | ≥ 1 |
| `Analytics.SynonymMinimumOccurrences` | `SettingsSynonymMinimumOccurrences` | Synonym minimum occurrences | 3 | ≥ 1 |

Where a property already has a documented ceiling (the contract's 1000 page-size ceiling, the
`/suggest` limit), use it as the upper validation bound; otherwise "≥ 1" (`CacheTtl` may be 0 =
no caching, as today). Descriptions = the existing XML-doc summary of each property, trimmed.

**Out (stay code-only):** everything per index — `Indexes[...]` (sort keys, suggest field/mode,
did-you-mean, popular searches) and `XpSearchIndexingOptions` (flatten/AddField). They name
content-type fields and types; that is code, not an admin knob. Say so in the guide.

## 2. Storage — one typed row in our module

- New Info class `XpSearchSettingsInfo` in `src/XpSearch.Core/Options/` (object type
  `xpsearch.settings`, class `XpSearch.Settings`), modelled on `Fuzzy/XpSearchFuzzyIndexInfo`:
  `SettingsID`, `SettingsGuid`, then one integer column per row of the table in §1.
  `TYPEINFO.TouchCacheDependencies = true` (the primer's per-index pattern) so `ForInfoObjects<T>().All()`
  dependencies fire on save.
- Form registered in `XpSearchAnalyticsModuleInstaller` next to the others (`SettingsForm()`,
  `InstallClass(...)`). All columns non-nullable → `InfoCreationSiteTests` will insist every
  creation site sets every column (RK-2); there is exactly one creation site (§2 seeding).
- **Seeding (idempotent, upgrade-safe):** on install, if no row exists, create ONE row whose
  values are the *effective code-configured options* (`IOptions<XpSearchOptions>` — the host's
  `AddXpSearch(o => …)` lambda has run by then; retention defaults to 365 via the option's new
  default). If a row exists, never touch its values. A later upgrade adding a column is handled by
  the existing `CombineWithForm` merge; a missing column value reads as 0 → the overlay in §3
  treats 0 as "unset" **only** for columns whose validation is ≥ 1, and seeds… no: keep it simple
  — new columns get their default written by the installer when `CombineWithForm` reports the
  column was added (check `existing` vs merged field lists; if that is awkward, the overlay's
  "0 means use the code value" rule for ≥ 1 columns is the accepted fallback, recorded in
  KNOWN-LIMITATIONS).

## 3. Loading — `IConfigureOptions<XpSearchOptions>` + live change tracking

- `XpSearchStoredSettingsConfigureOptions : IConfigureOptions<XpSearchOptions>` in Core, registered
  in `AddXpSearch` **after** `services.Configure(configure)` so it runs after the host lambda and
  the stored values win. It reads the single row (via `IInfoProvider<XpSearchSettingsInfo>`,
  through `IProgressiveCache` with a `ForInfoObjects<XpSearchSettingsInfo>().All()` dependency —
  exactly the seam shape `InfoTypoToleranceSource` uses) and copies each column onto the options.
  No row (Core-only test hosts, first request before the installer ran) → options untouched.
  Must never throw when the database is unreachable (log at Debug, leave options as configured) —
  `ServerRenderedResultsTests` builds `AddXpSearch()` with no Kentico DB.
- **Live updates:** register an `IOptionsChangeTokenSource<XpSearchOptions>` whose token is
  cancelled when a `XpSearchSettingsInfo` row is inserted/updated (`TYPEINFO.Events.Insert.After`
  / `Update.After`, or the cache dependency's own notification if simpler — verify one works with
  a real save on the host, the reviewer will check it live). Then `IOptionsMonitor<XpSearchOptions>`
  re-runs the host lambda + our overlay on the next `.CurrentValue` read after a save.
- **Consumers switch from `IOptions<XpSearchOptions>` to `IOptionsMonitor<XpSearchOptions>`**
  and read `.CurrentValue` per operation (not cached in a field). The 12 src sites:
  `ProgressiveSearchCache`, `CachedSearchPipeline`, `DocumentSuggestService`,
  `QuerySuggestionService`, `RecoverySearchPipeline`, `CollectFacetsStage`,
  `NormalizeRequestStage`, `XpSearchQueryLogRetentionTask`, `XpSearchPopularityTask`,
  `FilterSortWidget`, `SortSelectWidget`, and whatever else `grep IOptions<XpSearchOptions>` finds
  outside `.claude/`. Tests: add ONE helper in `tests/XpSearch.Core.Tests/Fixtures`
  (`StaticOptionsMonitor<T>` — `CurrentValue` = the instance, `Get` = same, `OnChange` = no-op) and
  replace the `Options.Create(...)` call sites that feed those consumers (Widgets/Ingestion/Bench
  test projects have their own sites — fix each; Bench is a console app, not a suite).
- `SearchCacheKey` / response cache: options values that shape a response (page size clamps,
  facet limits) already flow through the request normalisation, and the cache TTL is read at
  insert time — confirm no cached entry can outlive a lowered TTL by more than the old TTL and
  say so in the report; no cache-key change is expected.

## 4. The retention task (unchanged from rev 1 except the read path)

`XpSearchQueryLogRetentionTask` (identifier `XpSearch.QueryLogRetention` — **unchanged**; class
name unchanged): reads `options.CurrentValue.Analytics.RetentionDays` / `RetentionBatchSize` once
per run; cutoff `UtcNow.AddDays(-Math.Max(1, days))`. Deletes, same batch loop, in order:
1. query log rows (`IQueryLogStore.DeleteOlderThanAsync` — unchanged);
2. answered popularity suggestions — new
   `IPopularitySignalStore.DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken)`:
   `SuggestionState != Pending && SuggestionComputed < cutoff`;
3. answered synonym suggestions — new `ISynonymSuggestionStore.DeleteAnsweredOlderThanAsync(...)`:
   `SynonymSuggestionState != Pending && SynonymSuggestionLastSeen < cutoff`.
Pending rows are never touched; popularity *score* rows are not touched. Result message:
`Deleted N query log rows, N popularity suggestions, N synonym suggestions older than <cutoff:u>.`
KNOWN-LIMITATIONS entry: a pruned answered suggestion can be re-suggested if mined again after
the window (upgrade path: tombstone flag).

## 5. Admin page

- `XpSearch.Admin`: one `ModelEditPage<XpSearchSettingsModel>` (copy the `TuningEditPage<TModel>`
  plumbing; **not** index-scoped) registered as a page **"Settings"** directly under the
  `xpsearch-tuning` application (`SearchTuningApplication`), slug `settings`, ordered last.
  One form, the §1 rows in that order with the labels/validation given (Kentico validation-rule
  attributes; the retention field's explanation text says which task consumes it and that pending
  suggestions are never deleted). Submit loads the single row by `TYPEINFO` and `Set`s it; the
  change token (§3) makes the new values live without a restart.
- Add the page's command name(s) to `tests/XpSearch.Admin.Tests/PageCommandDiscoveryTests.cs`
  (ADR-0027 guard) if the page exposes any beyond what `ModelEditPage` provides.
- `docs/internal/screenshot-manifest.md`: add a row for the Settings page (stale → the lead
  captures it) and note it in the report.

## 6. Options defaults + breaking notes

- `XpSearchAnalyticsOptions.RetentionDays` default 180 → **365** (XML doc + guide updated).
- Semantics change to document (CHANGELOG `**Breaking (core):**`, stands alone): global values in
  the host's `AddXpSearch(o => …)` lambda are used **once**, to seed the stored row on first start;
  from then on the administration's Settings page owns them and lambda edits to those globals are
  ignored (delete the row to re-seed — say how). Per-index and indexing options are unaffected.
- CHANGELOG `**Added (core, admin):**` for the Settings page, the overlay, live updates, and the
  broader retention prune.

## 7. Tests (existing suites; no new frameworks)

- Overlay: given a fake `IInfoProvider<XpSearchSettingsInfo>`/store seam returning a row, the
  configured `XpSearchOptions` carries the row's values over the lambda's; given no row, the
  lambda's values stand. (Design the seam so it is fakeable without a DB — an internal
  `IStoredSearchSettingsSource` returning the row-or-null is fine and mirrors `ITypoToleranceSource`.)
- Retention (`QueryLogTests`): rewrite the existing test to inject the `StaticOptionsMonitor`;
  AC-2 boundaries 10/29 kept, 31/400 deleted with `RetentionDays = 30`; AC-3 via in-memory fakes of
  both suggestion stores recording the `DeleteAnsweredOlderThanAsync` calls, plus a pure predicate
  helper ("is this row prunable") unit-tested for both row shapes; result message asserted.
- Seeding: if the installer can take a fakeable provider, test "no row → one row with the
  effective options; row exists → untouched". Otherwise host-only (say so).
- `InfoCreationSiteTests`, `AssemblyDiscoveryTests`, `PageCommandDiscoveryTests` stay green.

## 8. Docs

- `docs/guides/analytics.md`: config sample drops `RetentionDays`; **### Retention** rewritten —
  the Settings page path (Search ingestion app → Settings), default 365, the three tables, pending
  excluded, the *Last result* message, the still-manual task configuration, and the pointer that
  Kentico's own activities are pruned by Kentico's *Delete inactive contacts* setting.
- New short section in `docs/guides/search-api.md` (or wherever `AddXpSearch` options are
  documented today — find it) **"Global settings in the administration"**: the table from §1,
  seeding-once semantics, what stays in code.
- `docs/adr/0015-analytics.md`: amend the retention paragraph (admin setting, default 365,
  overlay via ConfigureOptions, why not `SettingsKeyInfo`: Kentico's own guidance).
- `docs/internal/KNOWN-LIMITATIONS.md`: the re-suggest entry; the "0 = unset" fallback if used.
- `docs/internal/agent-primer.md`: one line under *Patterns to copy* — "global admin setting:
  add a column to `XpSearchSettingsInfo` + form + model field + overlay line".

## 9. Constraints

- Core must not gain an Admin or Page Builder dependency (the page is in Admin; the Info class,
  installer form, overlay and change token are in Core).
- No new NuGet dependency. No contract regeneration. Do not touch `src/Components/Widgets/CardWidget/`.
- Build only `libraries/**` projects from this worktree; the reviewer runs the full solution.
- No root-level props / inline package versions to "fix" a build.
- One conventional commit on `unit/ar-1`: `feat(core,admin): global search settings page loaded via ConfigureOptions (AR-1)`.
  (Rev 1 work already committed? Amend/squash into the one commit — the branch is unmerged.)

## 10. Verification expected in your report

- `dotnet test` summary lines for Core and Admin (and Widgets if you touched its sites; Widgets
  needs `npm run build` in its Client first) — 0 failed, totals ≥ the primer's baseline + new cases.
- The change-token mechanism you chose and the evidence it fires on save (event name / API).
- The seeding path taken (installer test or host-only) and the "new column" strategy from §2.
- The cache-TTL statement from §3.
- Files changed list, commit hash, any DONE_WITH_CONCERNS items. No diffs, no whole files.
