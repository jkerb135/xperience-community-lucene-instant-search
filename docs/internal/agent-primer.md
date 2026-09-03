# Agent primer — read this before exploring

Standing facts for implementation agents. Trust this file over re-discovery; if reality
contradicts it, fix the file in your commit.

## Layout

- `src/XpSearch.Core` — contract, pipeline stages, tuning rules, analytics, contact groups, the
  popularity signal (`Popularity/`: module classes, aggregation task, store) the Admin UI only
  toggles and lists, and the server-rendered first paint (`Rendering/`: `ServerRenderedResults`,
  `SearchQueryState`, the result-template surface — PK-2 lifted these out of Widgets).
- `src/XpSearch.Widgets` — Page Builder widgets (`Components/Widgets/XpSearch/`), mount
  infrastructure (`Mounting/`), and the **JS client at `src/XpSearch.Widgets/Client`**
  (vitest, strict TS, UMD bundle).
- `src/XpSearch.Admin` — admin UI; React client at `src/XpSearch.Admin/Client` (webpack,
  `@kentico/xperience-admin-*` packages).
- `src/XpSearch.Ingestion` — external-document push API.
- `src/XpSearch.Client` — the typed ingestion client for apps OUTSIDE Xperience (CL-1). BCL only:
  it must never reference Kentico or Lucene (`tests/XpSearch.Client.Tests/KenticoFreeTests.cs` pins
  that). Its contract DTOs are a second emission of the ingestion schema, own namespace.
- Tests: `tests/XpSearch.{Core,Admin,Ingestion,Widgets,Client}.Tests` (NUnit), plus two console tools,
  `tests/XpSearch.FacetSpike` (SP-1, frozen) and `tests/XpSearch.Bench` (PF-1).
  (There is no `tests/a11y`, `tests/performance` or `XpSearch.Integration.Tests` project — checked
  2026-09-01; the a11y checks live in the widgets JS client.)
- Docs: guides in `docs/guides/` (wiki-ready, verified samples), ADRs in `docs/adr/`,
  spec + amendments in `docs/spec/`, shortcuts in `docs/internal/KNOWN-LIMITATIONS.md`,
  unit specs in `docs/internal/units/`.

## Build & test commands (run from the worktree root)

```bash
# Widgets JS client MUST be built before the Widgets C# tests (csproj checks dist/):
cd src/XpSearch.Widgets/Client && npm ci && npm run build && npm test

dotnet test tests/XpSearch.Core.Tests/XpSearch.Core.Tests.csproj
dotnet test tests/XpSearch.Admin.Tests/XpSearch.Admin.Tests.csproj
dotnet test tests/XpSearch.Ingestion.Tests/XpSearch.Ingestion.Tests.csproj
dotnet test tests/XpSearch.Widgets.Tests/XpSearch.Widgets.Tests.csproj
dotnet test tests/XpSearch.Client.Tests/XpSearch.Client.Tests.csproj

# Admin React client:
cd src/XpSearch.Admin/Client && npm ci && npm run build

# Performance bench (PF-1). NOT part of any suite: Release-only, minutes long, writes
# docs/internal/perf-results-<date>.md. A full 10k/100k/1M run takes ~15 minutes and needs ~1.5 GB
# of temp disk (cleaned up at the end). Smoke it with `--sizes 10k --runs 1 --iterations 20`:
dotnet run --project tests/XpSearch.Bench/XpSearch.Bench.csproj -c Release -- --sizes 10k,100k,1m --runs 3 --iterations 100

# Contract codegen lives in the WIDGETS client; regen + drift check after contract changes:
cd src/XpSearch.Widgets/Client && npm run contract:gen && npm run contract:check
```

Suite sizes (2026-09-01, after HL-1): Core 347, Admin 260, Ingestion 47, Widgets 78, Client 16,
JS 286 (the former `widgets.test.ts` facet-count flake is fixed — a disposed `SearchClient` no
longer retries a failed probe into the next test's fetch log) — if
your run shows fewer, you ran the wrong project. There is no solution file in the repo root; run each
test project by path. The Admin C# suite needs `src/XpSearch.Admin/Client` built first, like the
Widgets one.

## Patterns to copy (don't invent parallel ones)

- New Page Builder widget / widget property: `Components/Widgets/XpSearch/SearchBoxWidget.cs`;
  mount base `Mounting/XpSearchMountWidgetViewComponent.cs` (`BuildConfig` reflects ALL public
  properties — override and `Remove(...)` to keep a property out of `data-xps-config`);
  markup tests in `tests/XpSearch.Widgets.Tests/MountMarkupTests.cs`.
- JS widget: `Client/src/widgets/`, registry + trust-boundary config parsing in
  `Client/src/bootstrap.ts` (`readMountConfig`).
- New JS widget entry point: add it to `Client/scripts/widget-entries.mjs` (rollup input, the
  `exports` walk and the per-widget CSS all read that map) and add its `./widgets/<kebab>` export.
- Stylesheets: authored in `themes/src/scss/{shell,default}/_<widget>.scss`, bundled by
  `scss/{shell,default}.scss`, à la carte via `scss/widgets/_<widget>.scss`. `themes/src/*.css` is
  generated **and committed** (`cd themes && npm run build`); the widgets client recompiles the same
  sources and fails if the rules differ. `themes/npm run check` also recomputes the palette's
  contrast ratios and the single-token re-skin, so a token value change has to stay AA.
- Default result card: it exists THREE times (client `defaultResultItem`, `_Result.cshtml`,
  `ServerRenderedResults.DefaultCard`). Change all three together;
  `Client/src/widgets/card-parity.test.ts` compares them and `ServerRenderedResultsTests` in both
  the Core and Widgets suites pin the strings.
- Autocomplete popup: `Client/src/widgets/suggestionsPanel.ts` renders it for BOTH the standalone
  `suggestions` widget and `searchBox`'s `suggestions` param group; a change there has to keep
  `themes/fixtures/{suggestions,search-box}.html` true, and `widgets.test.ts` compares the two
  consumers' panels directly. Recent searches (SG-1) live beside it in `recentSearches.ts` and are
  composed into the render state at the WIDGET layer (`recents.wrap(options, pick)`), never inside
  `behaviors/suggestions.ts` — that behaviour's transport and state machine stay untouched.
- Enriching a response after the pipeline but inside the cache: decorate `ISearchPipeline` between
  `CachedSearchPipeline` and `SearchPipeline`, like `Recovery/RecoverySearchPipeline` (SG-1). A
  decorator there can also re-enter the inner pipeline (did-you-mean verifies its correction) without
  recursion, and its enrichment lands in the cached entry.
- Pipeline stage: implement + register like `ResolveContactGroupsStage` (order constants matter;
  anything affecting results must join the response cache key).
- Per-index opt-in setting (popularity RK-1, typo tolerance FZ-1): Info class in Core + form in
  `XpSearchAnalyticsModuleInstaller`, a seam interface read behind `IProgressiveCache` with an
  `ForInfoObjects<T>().All()` dependency (`TouchCacheDependencies = true` on the TYPEINFO), the flag
  folded into `SearchCacheKey.Compute`, and a header command + callout on the listing it belongs to.
- Admin page: custom templates need `RoutingContentPlaceholder` in parent templates; ActionCell
  buttons need real aria-labels (use labelled stock Buttons); `[PageCommand]` works on plain methods,
  on abstract bases and on re-annotated overrides alike — the only rule is that the command NAME is
  unique on the page (a collision refuses to build the UI tree) and that `ListingPage.Delete` carries
  no attribute of its own, so a listing must supply one (ADR-0027, decompiled). New commands are
  guarded by `tests/XpSearch.Admin.Tests/PageCommandDiscoveryTests.cs`, which asks Kentico's real
  `UITree`; add the client's command name there. A host "command not found" is usually a stale host
  build — the host `ProjectReference`s the MAIN worktree's `src/`.
- Per-index admin setting (AR-2): add a property to `XpSearchIndexSettings` (default read off
  `XpSearchOptions`) + a column on `XpSearchSettingsInfo` + a field in
  `XpSearchAnalyticsModuleInstaller.SettingsForm()`/`SettingsColumns`, a property on
  `SearchSettingsValues` (with its `From`/`ApplyTo`/`StoredSearchSettings` lines), and a field on
  `SearchSettingsModel` in `XpSearch.Admin`. Consumers read
  `IOptionsMonitor<XpSearchIndexSettings>.Get(index)` **per operation**, with the index name resolved
  through `ILuceneIndexAccessor.ResolveName` (named options compare ordinally) — never `IOptions<>`,
  or a saved setting needs a restart. `XpSearchOptions.Indexes[...]` stays for code-only per-index
  members.
- Removing a column from a Core Info class: drop it from the Info, from `SettingsForm()`/its form
  method, and from every mapping. `XpSearchAnalyticsModuleInstaller.InstallClass` then drops it from
  an installed class too (`RemoveUndeclaredFields` after `CombineWithForm`, which only ever adds), so
  no `NOT NULL` leftover meets the next insert. The Admin and Ingestion installers still only add.
- Scheduled/background work: follow the analytics retention task registration.
- Creating an Info object (`new XpSearch…Info { … }`): set EVERY field its installer form declares
  without `allowEmpty`, including booleans and the GUID. Kentico serializes only the fields that were
  SET - a property default never reaches the INSERT, so an omitted field is a NOT NULL crash on the
  host (RK-2). `tests/XpSearch.Core.Tests/InfoCreationSiteTests.cs` checks this for `XpSearch.Core`.

## Conventions

- Lazy senior dev (global CLAUDE.md): minimum code, no unrequested abstractions; intentional
  shortcuts go in `docs/internal/KNOWN-LIMITATIONS.md` (symbol+file / simplified / ceiling /
  upgrade path), never as code comments.
- Every unit: CHANGELOG.md `[Unreleased]` entry (breaking entries lead with `**Breaking (scope):**`);
  guide page updated with a verified sample if editor/dev-facing; non-trivial logic leaves one
  runnable check (test in the existing suite).
- Units that change admin UI, widget properties, or JS client options: update the affected guide
  page AND mark the affected rows in `docs/internal/screenshot-manifest.md` stale (note it in your
  report) — the lead recaptures screenshots at the host pass / next `/docs-ship`.
- Kentico/Lucene API questions: use the Kentico docs MCP; Lucene-integration APIs are on GitHub
  (`Kentico/xperience-by-kentico-lucene`), not docs.kentico.com. `Lucene.Net*` pinned to
  `4.8.0-beta00017`.
- Conventional commits (`feat(widgets): …`), one commit per unit on `unit/<name>`.

## Token discipline

- Read this primer and your unit spec first; explore only what they don't cover.
- Read files in targeted ranges; don't re-read files you just edited.
- Your report: findings and verification output only — never paste whole files or diffs the
  reviewer can get from git.
