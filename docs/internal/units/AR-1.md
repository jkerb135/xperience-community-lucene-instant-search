# Unit AR-1 — Analytics retention as an admin setting (default 365 days)

An administrator sets **"Remove search analytics older than X days"** in the Xperience
administration and the existing `XpSearch.QueryLogRetention` scheduled task honours it. Default
**365**. The task also prunes the *answered* (accepted/dismissed) popularity and synonym
suggestion rows older than the same cutoff — the other table nothing ever deletes. The C# option
`XpSearchAnalyticsOptions.RetentionDays` is **removed**; the setting is the single source of truth.
No contract change, no JS change, no widget change.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/ar-1`). Use the
Kentico docs MCP for every `SettingsKeyInfo` / `SettingsCategoryInfo` field question — do not guess
column semantics.

## 1. The setting

| | |
|---|---|
| Code name | `XpSearchAnalyticsRetentionDays` |
| Display name | `Remove search analytics older than X days` |
| Description (tooltip) | `Search query log rows and answered popularity/synonym suggestions older than this many days are deleted by the "XpSearch.QueryLogRetention" scheduled task. Minimum 1.` |
| Type | integer |
| Default / seeded value | `365` |
| Validation | integer ≥ 1 (`KeyValidation` regex if the Settings app honours it; the task additionally clamps with `Math.Max(1, …)` exactly as today) |
| Scope | global — not per channel, not per index |

Mirror the shape of Kentico's own *Settings → Digital marketing → Contact management →
"Delete contacts who have not been active for the last X days"*: one integer, one sentence.

### 1a. Placement — try the built-in Settings application FIRST

`XpSearchAnalyticsModuleInstaller.Install()` (Core, runs on every start via
`XpSearchAnalyticsModule`) additionally seeds, **idempotently**:

1. a `SettingsCategoryInfo` for the library — a category named e.g. `XpSearch.Search` /
   display "Search", plus whatever the Settings tree needs between the root and a key
   (category → group; confirm with the docs MCP and, if the docs are silent, by reading how an
   existing built-in key such as `CMSLogSize` is parented in `CMS_SettingsCategory` on the demo DB).
   Set `CategoryResourceID` to our `ResourceInfo` (`CMS.Integration.XpSearchAnalytics`) so the key
   belongs to the module the same way the object types do.
2. the `SettingsKeyInfo` from §1, `KeyCategoryID` = the group, `KeyValue` = `KeyDefaultValue` = `365`,
   `KeyType` = `int`, `KeyIsGlobal` = true.

Idempotent means: look the category and the key up **by code name** before inserting; when they
exist, do NOT touch `KeyValue` (a host that set 90 keeps 90 across restarts — that is AC-5); only
update display name/description/validation if they changed (the `HasChanged` pattern the
installer already uses for the resource).

**Proof required:** after the host is rebuilt and restarted (see §5), the setting must be visible
and editable in **Settings** in the running administration (port 27340). Include the screenshot or
a `read_page` excerpt in your report. Search the Settings app for "search analytics" — the
built-in search box lists any key whose name/description matches, so it also proves the row is
wired even if the category tree placement is imperfect.

### 1b. STOP condition and fallback

If the Settings application does **not** render custom categories/keys (empty category, key not
listed, search finds nothing), **STOP and report** with what you observed. Do not spend more than
one focused attempt on tree-placement variations. The reviewer will then authorise the fallback:

- keep the same `SettingsKeyInfo` row (seeded exactly as above, it is still the storage), and
- add one `ModelEditPage` "Analytics settings" under the library's own `xpsearch-tuning`
  application in `XpSearch.Admin` (copy `TuningEditPage<TModel>` plumbing; one integer field,
  `[MinimumIntegerValueValidationRule(1)]`-style validation; persist by loading the
  `SettingsKeyInfo` by code name and `Update()`).

Either way the **read path in the task is identical** (§2), so the fallback touches no Core code.

## 2. The retention task

`XpSearchQueryLogRetentionTask` (identifier `XpSearch.QueryLogRetention` — **unchanged**, every
host's task configuration references it; class name unchanged too):

- Inject `ISettingsService` and `IConversionService` (both `CMS.Core`, DI-registered by Kentico).
  Read `settingsService["XpSearchAnalyticsRetentionDays"]` **once per run** and convert with
  `conversionService.GetInteger(value, 365)`.
- If the value is null/empty/non-integer → use 365 **and** `LogWarning` naming the setting code
  name (AC-4). Never throw for a bad setting.
- Cutoff = `DateTime.UtcNow.AddDays(-Math.Max(1, days))`. `RetentionBatchSize` stays a code option
  and keeps its meaning.
- Delete, in this order, all with the same cutoff and batch loop shape the task already has:
  1. query log rows (`IQueryLogStore.DeleteOlderThanAsync` — unchanged);
  2. answered popularity suggestions — new `IPopularitySignalStore.DeleteAnsweredOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken)`:
     rows with `SuggestionState != Pending` and `SuggestionComputed < cutoff`;
  3. answered synonym suggestions — new `ISynonymSuggestionStore.DeleteAnsweredOlderThanAsync(...)`:
     rows with `SynonymSuggestionState != Pending` and `SynonymSuggestionLastSeen < cutoff`.
  Pending rows are **never** touched by retention (the mining task owns them). Popularity
  *score* rows are not touched (bounded by `PopularityDocumentLimit`, replaced every run).
- The `ScheduledTaskExecutionResult` message reports all three counts and the cutoff, e.g.
  `Deleted 1200 query log rows, 3 popularity suggestions, 1 synonym suggestion older than 2025-09-02 00:00:00Z.`
  The *Last result* column in Scheduled tasks is the only feedback an admin gets.

Behavioural note for the guide + KNOWN-LIMITATIONS: deleting an answered suggestion means the
same pair *can* be re-suggested if it is mined again after the retention window. Acceptable —
the query log that produced it is gone by then too, so a re-mined suggestion is a new signal.
Record this in `docs/internal/KNOWN-LIMITATIONS.md` (symbol/file, simplified, ceiling, upgrade
path = a tombstone flag instead of deletion).

## 3. Removed option (breaking, pre-1.0)

- Delete `XpSearchAnalyticsOptions.RetentionDays`. Fix every reference (task, tests, guide
  sample). `RetentionBatchSize`, `QuerySuggestionDays`, `Popularity*`, `Synonym*` stay.
- CHANGELOG `[Unreleased]`: `**Breaking (core):**` entry for the removal (what replaces it, where
  to set it) and an `**Added (core):**` entry for the setting + broader prune. The
  breaking-changes page is generated from those entries — write the breaking one so it stands
  alone.

## 4. Tests (Core suite, NUnit, existing files — no new fixtures frameworks)

In `tests/XpSearch.Core.Tests/QueryLogTests.cs` (or a sibling `RetentionTests.cs` if cleaner):

- Rewrite `RetentionTask_DeletesOnlyRowsOlderThanTheRetentionWindow` to stub `ISettingsService`
  (a tiny fake returning "30") and `IConversionService` (use Kentico's real one if constructible
  without a DB, else a fake) — AC-2 boundaries: 10/29 kept, 31/400 deleted.
- AC-3: in-memory fakes of the two suggestion stores recording the `DeleteAnsweredOlderThanAsync`
  calls (the task test only needs to prove it calls them with the same cutoff/batch size); plus
  one test per Info store is NOT possible without a DB — instead a pure helper that decides
  "is this row prunable" (state ≠ pending ∧ date < cutoff) extracted and unit-tested for both
  row shapes, so the predicate is checked even though the `IInfoProvider` query is not.
- AC-4: setting absent → 365 used (assert via which rows survive) and a warning logged (use a
  list-capturing `ILogger`, or `NullLogger` + a spy — smallest thing that proves the warning).
- AC-5 (seeding idempotency) has no DB seam in the suite; it is verified on the host (§5). If you
  find a cheap seam (e.g. the installer taking `IInfoProvider<SettingsKeyInfo>` you can fake),
  add the test; otherwise say so in the report.
- Keep `tests/XpSearch.Core.Tests/InfoCreationSiteTests.cs` green: any `new SettingsKeyInfo {…}`
  / `new SettingsCategoryInfo {…}` site must set every non-nullable column (RK-2 lesson) — those
  are Kentico's own types, so check their required columns via the docs MCP / decompiled TYPEINFO.

## 5. Host + docs

- `docs/guides/analytics.md`: the quick-start config sample no longer sets `RetentionDays`;
  rewrite **### Retention**: where the setting lives (exact click path as shipped), default 365,
  what the task deletes (three tables, pending suggestions excluded), the *Last result* message,
  the manual task-configuration step (unchanged), and a one-line pointer that Kentico's own
  activities are pruned by Kentico's *Delete inactive contacts* setting, not by this task.
- `docs/adr/0015-analytics.md`: amend the "Retention is a scheduled task with a manual
  configuration" paragraph — threshold now an admin setting (why: post-go-live knob, mirrors
  Kentico's inactive-contact setting), default 365, option removed.
- `docs/internal/KNOWN-LIMITATIONS.md`: the re-suggest entry from §2.
- `docs/internal/screenshot-manifest.md`: mark the analytics guide's retention row stale if the
  guide gains a screenshot reference; note it in the report.
- Host rebuild is the **reviewer's** job (the host `ProjectReference`s the main worktree's
  `src/`), but your report must state the exact click path you verified against the docs and the
  category/group structure you seeded, so the reviewer can confirm it live.

## 6. Constraints

- Core must not gain an Admin or Page Builder dependency. `CMS.Core` / `CMS.DataEngine` are fine
  (already referenced).
- No new NuGet dependency. No contract regeneration. Do not touch
  `src/Components/Widgets/CardWidget/`.
- Build only `libraries/**` projects from this worktree (the solution's sibling relative paths
  break here); the reviewer runs the full-solution build after merge.
- Do not add root-level props or inline package versions to "fix" a build.
- One conventional commit on `unit/ar-1`: `feat(core): admin-editable analytics retention setting (AR-1)`.

## 7. Verification expected in your report

- `dotnet test tests/XpSearch.Core.Tests/XpSearch.Core.Tests.csproj` output: total ≥ 347 + your
  new cases, 0 failed.
- Settings-app proof (§1a) **or** the STOP report (§1b) — one of the two, explicitly.
- The exact seeded category/group/key structure (code names, parent chain).
- Which AC-5 route you took (test or host-only).
- Files changed list; no diffs pasted.
