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
- Tests: `tests/XpSearch.{Core,Admin,Ingestion,Widgets}.Tests` (NUnit), plus `tests/XpSearch.FacetSpike`.
  (There is no `tests/a11y`, `tests/performance` or `XpSearch.Integration.Tests` project — checked
  2026-09-01; the a11y and performance checks live in the widgets JS client.)
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

# Admin React client:
cd src/XpSearch.Admin/Client && npm ci && npm run build

# Contract codegen lives in the WIDGETS client; regen + drift check after contract changes:
cd src/XpSearch.Widgets/Client && npm run contract:gen && npm run contract:check
```

Suite sizes (2026-09-01, after TH-1): Core 286, Admin 187, Ingestion 47, Widgets 72, JS 208 — if
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
- Pipeline stage: implement + register like `ResolveContactGroupsStage` (order constants matter;
  anything affecting results must join the response cache key).
- Admin page: custom templates need `RoutingContentPlaceholder` in parent templates; ActionCell
  buttons need real aria-labels (use labelled stock Buttons); `[PageCommand]` on abstract bases /
  re-annotated overrides is SUSPECT on the host — declare commands as plain methods on the final
  page class.
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
