# Agent primer — read this before exploring

Standing facts for implementation agents. Trust this file over re-discovery; if reality
contradicts it, fix the file in your commit.

## Layout

- `src/XpSearch.Core` — contract, pipeline stages, tuning rules, analytics, contact groups, and the
  popularity signal (`Popularity/`: module classes, aggregation task, store) the Admin UI only
  toggles and lists.
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

Suite sizes (2026-09-01, after PS-1): Core 260, Admin 186, Ingestion 47, Widgets 84, JS 191 — if your
run shows fewer, you ran the wrong project.

## Patterns to copy (don't invent parallel ones)

- New Page Builder widget / widget property: `Components/Widgets/XpSearch/SearchBoxWidget.cs`;
  mount base `Mounting/XpSearchMountWidgetViewComponent.cs` (`BuildConfig` reflects ALL public
  properties — override and `Remove(...)` to keep a property out of `data-xps-config`);
  markup tests in `tests/XpSearch.Widgets.Tests/MountMarkupTests.cs`.
- JS widget: `Client/src/widgets/`, registry + trust-boundary config parsing in
  `Client/src/bootstrap.ts` (`readMountConfig`).
- Pipeline stage: implement + register like `ResolveContactGroupsStage` (order constants matter;
  anything affecting results must join the response cache key).
- Admin page: custom templates need `RoutingContentPlaceholder` in parent templates; ActionCell
  buttons need real aria-labels (use labelled stock Buttons); `[PageCommand]` on abstract bases /
  re-annotated overrides is SUSPECT on the host — declare commands as plain methods on the final
  page class.
- Scheduled/background work: follow the analytics retention task registration.

## Conventions

- Lazy senior dev (global CLAUDE.md): minimum code, no unrequested abstractions; intentional
  shortcuts go in `docs/internal/KNOWN-LIMITATIONS.md` (symbol+file / simplified / ceiling /
  upgrade path), never as code comments.
- Every unit: CHANGELOG.md `[Unreleased]` entry; guide page updated with a verified sample if
  editor/dev-facing; non-trivial logic leaves one runnable check (test in the existing suite).
- Kentico/Lucene API questions: use the Kentico docs MCP; Lucene-integration APIs are on GitHub
  (`Kentico/xperience-by-kentico-lucene`), not docs.kentico.com. `Lucene.Net*` pinned to
  `4.8.0-beta00017`.
- Conventional commits (`feat(widgets): …`), one commit per unit on `unit/<name>`.

## Token discipline

- Read this primer and your unit spec first; explore only what they don't cover.
- Read files in targeted ranges; don't re-read files you just edited.
- Your report: findings and verification output only — never paste whole files or diffs the
  reviewer can get from git.
