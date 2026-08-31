# ADR-0023: branding — `XperienceCommunity.Search.*` on NuGet, `@xperience-community` on npm

- **Status:** accepted — lead decision 2026-08-25 under owner delegation (units BR-1, BR-2)
- **Date:** 2026-08-25
- **Amends:** ADR-0011 (which deferred branding to Phase 8)

## Context

The spec shipped a placeholder, `YourCo`, and told us to replace it before implementation; ADR-0011
deferred that to Phase 8. BR-1 removed `YourCo` but landed the .NET packages on
`xperience-community.Xperience.Search.*` — lowercase, dotted, and five segments long — while the
admin package it wrote in the same pass used `XperienceCommunity.Search.Admin`. The repository
therefore shipped two naming schemes for four sibling packages, and every `nuget.config` in the docs
carried two `<package pattern>` lines to cover both. Nothing had been published under either name.

The remaining question was one scheme for both registries, or one per registry.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| `xperience-community.Xperience.Search.*` everywhere | Matches the GitHub org string and the admin module's `orgName` character for character | Not a .NET package ID convention: NuGet IDs are PascalCase, and the community's own packages are `XperienceCommunity.*`. Also redundant — `Xperience` twice. And npm forbids it anyway (see below) |
| `XperienceCommunity.Search.*` everywhere | One string to remember | npm package names **must be lowercase** — the registry rejects an uppercase name outright. Not available |
| **`XperienceCommunity.Search.*` on NuGet, `@xperience-community/xperience-search` on npm** | Each registry gets the name its ecosystem expects; one source-mapping glob; matches the Admin package that already existed | Two strings, so a reader has to know which registry they are in |

## Decision

Split by registry, because the registries disagree and one of them is not negotiable:

- **NuGet: `XperienceCommunity.Search.{Core,Ingestion,Widgets,Admin}`.** PascalCase is the NuGet
  convention, it is what the wider Xperience community publishes under, and `XpSearch.Admin` had
  already used it. Dropping the second `Xperience` shortens a five-segment ID to three.
- **npm: `@xperience-community/xperience-search`** (themes: `@xperience-community/xperience-search-themes`).
  Lowercase is not a preference — npm rejects a name with an uppercase letter — so the scope carries
  the branding in the only form npm allows. It also matches the GitHub organization and the admin
  module's `orgName`, which are lowercase for the same class of reason.

The internal `XpSearch` prefix is untouched everywhere (see the
[branding amendment](../spec/amendments/2026-08-25-branding.md) for the full "not branded" list).

## Consequences

- The widgets' `StaticWebAssetBasePath` follows the package ID to
  `_content/XperienceCommunity.Search.Widgets`, so the three served URLs move a second time. Hosts
  that use `XpSearchAssets`' constants or `<xps-search-assets />` never see it; a host with the path
  typed into a `.cshtml` gets a 404 and must be updated.
- Every documented `packageSourceMapping` collapses from two patterns to the single glob
  `XperienceCommunity.Search.*`.
- `npm pack` now emits `xperience-community-xperience-search-0.1.0.tgz`, which is the `file:`
  dependency `samples/CustomWidget.Dropdown` installs; `samples/pack-and-build.mjs` proves the
  renamed nupkgs and tarball resolve end to end.
- Nothing was published between the two schemes, so there is no migration to document — but the
  CHANGELOG records both hops, because the interim name is in the repository's history and in a
  reader's working tree.
