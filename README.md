> [!WARNING] 
> Preview feature - this is currently in active development expect changes in the functionality, potentially including breaking changes. Feel free to try out the features and leave feedback.
> [View Docs](https://jkerb135.github.io/xperience-community-lucene-instant-search/)


# Xperience Search - Currently In Development

A search experience layer for Xperience by Kentico: JSON search API over Lucene,
a widget-based JS library, Page Builder widgets, admin relevance tuning,
activity tracking, and an ingestion API for external data.

- **Spec:** `docs/spec/xperience-search-spec.md` — source of truth
- **Build orchestration prompt:** `docs/internal/build-prompt.md`
- **Decisions:** `docs/adr/`
- **Start here:** `docs/guides/quick-start.md`

## Repository layout

| Path | Contents |
|---|---|
| `src/XpSearch.Core` | Query pipeline, endpoint, facets, highlighting (NuGet) |
| `src/XpSearch.Admin` | Relevance tuning + analytics admin UI (NuGet) |
| `src/XpSearch.Widgets` | Page Builder widgets (NuGet) |
| `src/XpSearch.Ingestion` | Push API, schema, API keys (NuGet) |
| `src/XpSearch.Client` | Typed ingestion client for apps outside Xperience — no Kentico dependency (NuGet) |
| `src/XpSearch.Widgets/Client` | JS library — behaviours, widgets, routing (npm) |
| `clients/` | Thin .NET and Node ingestion clients |
| `themes/` | shell.css (structural) + default.css (opt-in theme), authored in Sass under `themes/src/scss/` |
| `samples/` | Dancing Goat reference build, custom widget example |
| `tests/` | NUnit suites per package, plus two console tools: the SP-1 faceting spike and the PF-1 performance bench |
| `build/` | Packaging, licensing key tooling |
