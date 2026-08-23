# ADR-0019: the JavaScript client lives under the project that ships it

- **Status:** accepted — owner decision 2026-08-23 (unit MV-1)
- **Date:** 2026-08-23
- **Amends:** ADR-0011 (single repository), ADR-0007 (JS client architecture)

## Context

`src/XpSearch.Client` sat beside the four .NET projects as if it were a fifth one, but it is not a
project the solution builds — it is the npm package `@yourco/xperience-search`, whose only .NET
consumer is `XpSearch.Widgets`. `XpSearch.Widgets.csproj` reached sideways into
`..\XpSearch.Client\dist\xpsearch.umd.js` to copy the bundle into `wwwroot/xpsearch/`, and the
build error told a new contributor to go build a sibling directory. The admin UI already has the
opposite arrangement: `src/XpSearch.Admin/Client` is the webpack module `XpSearch.Admin` embeds,
and it lives inside the project that ships it.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Leave `src/XpSearch.Client` where it is | No churn | Two conventions for the same relationship; the sibling reach in the csproj; the tree suggests a fifth .NET project |
| Move it to a top-level `client/` | Signals "not a .NET project" | Still detached from the one thing that consumes it, and still a second convention next to `Admin/Client` |
| **Move it to `src/XpSearch.Widgets/Client`** | Mirrors `Admin/Client`; the bundle path becomes `$(MSBuildThisFileDirectory)Client\dist\...`; the package and the assembly that serves it are one unit | The npm package is published from a nested path; the SDK's default globs now see the folder |

## Decision

`src/XpSearch.Client` becomes `src/XpSearch.Widgets/Client`. Nothing else changes: the npm package
is still `@yourco/xperience-search`, still published from that folder, the bundle is still
`dist/xpsearch.umd.js`, and the served URLs are still
`/_content/YourCo.Xperience.Search.Widgets/xpsearch/…`.

## Consequences

- `XpSearch.Widgets.csproj` removes `Client\**` from `Compile`, `EmbeddedResource`, `None` and
  `Content`. `Content` is the one that matters: the Razor SDK's default globs take `package.json`,
  `package-lock.json`, `tsconfig*.json` and `size-limit.json` as `Content`, which a Razor class
  library packs. (`node_modules\` is already covered by `$(DefaultItemExcludes)`, so the removes are
  wider than `src/XpSearch.Admin`'s.)
- The generator scripts under `Client/scripts/` are one directory deeper, so their relative paths to
  `contract/`, `docs/` and the two `Contract/Generated` folders gained a `../`. The generated-file
  headers now say `npm run contract:gen (in src/XpSearch.Widgets/Client)`.
- The npm package's own layout, `files`, `exports` and `bin` are untouched; `samples/pack-and-build.mjs`
  packs from the new path.
