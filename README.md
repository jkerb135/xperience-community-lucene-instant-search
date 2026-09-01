# Xperience Search - Currently In Development
- not intended for production sites

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
| `src/XpSearch.Widgets/Client` | JS library — behaviours, widgets, routing (npm) |
| `clients/` | Thin .NET and Node ingestion clients |
| `themes/` | shell.css (structural) + default.css (opt-in theme), authored in Sass under `themes/src/scss/` |
| `samples/` | Dancing Goat reference build, custom widget example |
| `tests/` | Unit, integration, a11y, performance |
| `build/` | Packaging, licensing key tooling |
