# Samples

Worked examples that a third party could have written, built the way a third party builds them.

| Sample | What it shows |
|---|---|
| [CustomWidget.Dropdown](CustomWidget.Dropdown/) | `myCompany.dropdownFacet`: a single-select drop-down facet on `withFacetList`, its Page Builder widget on `XpSearchMountWidgetViewComponent<T>`, and tests for both. `src/dropdownFacet.ts` **is** the worked example in [Custom widgets](../docs/guides/custom-widgets.md). |

## Build them

```bash
node samples/pack-and-build.mjs        # or: samples/pack-and-build.ps1
```

The script packs `XpSearch.Core`, `XpSearch.Widgets` and `XpSearch.Admin` with `dotnet pack` and the
client with `npm pack` into `samples/.feed/` (gitignored), then restores, builds and tests every
sample from that feed alone.

## Why packages and not project references

A project reference would compile against source that is not in any package, so the sample could
keep working while the published surface was broken — exactly the failure this repository already
had: the npm tarball shipped no stylesheets and no mock server while both were documented, and
nothing noticed, because nothing consumed the tarball. Restoring from a feed means the sample fails
the moment a type, an export, a static web asset or a `files` entry stops shipping.

It also makes the samples honest about the consumer experience:

- `CustomWidget.Dropdown/nuget.config` adds `..\.feed` as a source **and** maps
  `xperience-community.Xperience.Search.*` and `XperienceCommunity.Search.*` to it. On a machine with package source mapping enabled globally the
  source alone is not enough — restore fails with `NU1101` and lists the source as "not considered".
  See [Quick start → Installing from a private feed](../docs/guides/quick-start.md).
- `CustomWidget.Dropdown/package.json` depends on `file:../.feed/yourco-xperience-search-0.1.0.tgz`,
  so the sample resolves the same `dist/`, `themes/` and `mock/` a customer gets. The script deletes
  the sample's `node_modules` and `package-lock.json` first, because npm otherwise reuses the
  previous resolution of a tarball at that path.
- `samples/Directory.Build.props` and `samples/Directory.Packages.props` are deliberately near-empty:
  they stop MSBuild inheriting the library's shared build settings and central package management, so
  a sample builds with a customer's defaults and declares its package versions inline, like a
  customer's project does.
