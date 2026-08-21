# ADR-0012: Page Builder widgets — asset distribution, configurator placement and the instance-config rule

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §5.7, §5.8, §7, §12

## Context

Spec §7 asks for view-component Page Builder widgets that render nothing but a configured `.xps-mount`
element, a facet attribute drop-down populated from the selected index's real schema, and an
editor-only instruction block for an unconfigured widget. Three decisions had no obvious answer.

1. **Where the JavaScript bundle and the stylesheets live.** Kentico's convention for builder
   components is `~/wwwroot/PageBuilder/Public/Widgets/<Identifier>/`
   ([distribute builder components](https://docs.kentico.com/documentation/developers-and-admins/development/builders/distribute-builder-components),
   [bundle static assets](https://docs.kentico.com/documentation/developers-and-admins/development/builders/bundle-static-assets-of-builder-components)).
   That is a path inside the *host application*, and a NuGet package cannot write into it.
2. **Where the dependent-drop-down configurator lives.** §7.4's attribute drop-down needs a
   `FormComponentConfigurator<DropDownComponent>`
   ([configure editing component state](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/editing-components/configure-editing-component-state)).
   The widget properties themselves are live-site code.
3. **How a widget tells the JavaScript bootstrap which index to query.** `mountAll()` reads the
   instance options from `data-xps-instance-config` on the *first* mount of a `data-xps-instance` group
   that parses and names an `index`. The widgets are placed independently, in any order.

## Options considered

| Decision | Option | Pros | Cons |
|---|---|---|---|
| Assets | Ask the host to copy the files into `wwwroot/PageBuilder/Public/Widgets/XpSearch/` | Kentico's own convention; the host's bundler picks them up | Manual, and silently stale after a package upgrade — the failure mode is "the search stopped working after an update" |
| Assets | Serve them from the assembly through an embedded-file provider | Nothing to copy | Needs startup wiring the host must not forget, and a non-standard URL space |
| Assets | Ship them as Razor Class Library static web assets (`Microsoft.NET.Sdk.Razor`, `wwwroot/`) | Versioned with the package, served by `UseStaticFiles()` alone, standard `_content/<PackageId>/` URLs, the views compile into the same RCL | Not the documented Page Builder bundling path; a project using that bundling has to copy the files itself |
| Configurator | Put it in `XpSearch.Widgets`, referenced by type | One package | `FormComponentConfigurator<T>` and `DropDownComponent` live in `Kentico.Xperience.Admin.Base.dll` (the `Kentico.Xperience.Admin` package), so every live-site project would take a dependency on the administration |
| Configurator | Put it in `XpSearch.Admin`, sharing the option-building code through `XpSearch.Widgets` | Live-site code stays free of the administration package | `XpSearch.Admin` would depend on `XpSearch.Widgets`, which spec §2.2 forbids - a headless site installing Admin for relevance tuning would pull the Page Builder package and its static assets |
| Configurator | Put it in `XpSearch.Admin`, sharing the option-building code through `XpSearch.Core` | Live-site code stays free of the administration package ([deploying without the administration](https://docs.kentico.com/documentation/developers-and-admins/deployment/deploy-to-private-cloud/deploy-without-the-administration)), and the package graph stays the flat `Core` &larr; `Widgets`, `Core` &larr; `Admin` of spec §2.2 | Two packages must be installed for the drop-down to populate |
| Instance config | Only the Results widget emits it | One source of truth, no agreement needed | An instance without a Results widget never starts |
| Instance config | Every mount emits it; the bootstrap takes the first that names an index | No JavaScript change | Options only one widget knows (page size, fields) depend on that widget being first - an editor property that silently does nothing |
| Instance config | Every mount emits it; the bootstrap **merges** the group's objects | Placement-independent for every option, not just the index; a disagreement becomes a warning instead of a silent override | A behaviour change in `mountAll()` |

## Decision

**Assets ship as Razor Class Library static web assets.** `XpSearch.Widgets` uses
`Microsoft.NET.Sdk.Razor` with an explicit `StaticWebAssetBasePath` of
`_content/YourCo.Xperience.Search.Widgets`, so the three files are served at a stable path whether the
library is consumed as a project reference or as the package. An MSBuild target copies
`src/XpSearch.Client/dist/xpsearch.umd.js` and `themes/src/{shell,default}.css` into `wwwroot/xpsearch/`
before static web asset discovery and fails with "run `npm ci && npm run build` in `src/XpSearch.Client`"
when the bundle is missing; the copies are gitignored. An `<xps-search-assets />` tag helper and an
`Html.XpSearchAssets()` extension emit the `<link>` and `<script>` tags, both honouring the application
path base. A project that prefers Kentico's bundling copies the three files into
`~/wwwroot/PageBuilder/Public/Widgets/XpSearch/` and skips the tag helper; nothing else changes.

**The configurator lives in `XpSearch.Admin` and is referenced by identifier.** The checked fact is that
`Kentico.Xperience.Admin.Base.Forms.FormComponentConfigurator<T>` ships in the `Kentico.Xperience.Admin`
package, while the *annotations* (`DropDownComponentAttribute`, `IDropDownOptionsProvider`,
`FormComponentConfigurationAttribute`) ship in `Kentico.Xperience.Admin.Base.Shared.dll`, which comes
with `Kentico.Xperience.WebApp`. So `XpSearch.Widgets` can declare the drop-down and its dependency —
`[FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]` —
without referencing the administration at all, and `XpSearch.Admin` registers
`FacetAttributeConfigurator` under that identifier with `[RegisterFormComponentConfigurator]`.

**Everything the two packages share sits in `XpSearch.Core`.** The configurator identifier and the name
of the index property are `XpSearch.Core.XpSearchConstants`; the logic that turns a schema into
`value;label` option lines is `XpSearch.Core.Facets.FacetAttributeOptions.BuildOptionsAsync`. That keeps
the package graph the one spec §2.2 states — `Widgets` depends on `Core`, `Admin` depends on `Core`, and
neither on the other — so a headless site that installs `XpSearch.Admin` for relevance tuning never pulls
the Page Builder package or its static assets. The option builder is unit-tested in `XpSearch.Core.Tests`
against a substituted `IIndexSchemaProvider`; the configurator is a fifteen-line shell that hides the
field (`VisibilityConditions.Add(new AlwaysInvisible())`) when the method returns `null`.

**Every mount emits `data-xps-instance-config` with its index, and `mountAll()` merges the group's
objects into one.** Emitting from every mount is what makes drag-and-drop placement work: any one widget
is enough to start the instance, so an editor can build a search out of a search box and a facet list
with no results widget at all and still get a running instance. Merging is what makes the two genuinely
instance-wide options — *Results per page* and *Fields to show*, which only the Results widget knows —
placement-independent, as §7.3 editor properties have to be. The first definition of a key wins and a
disagreement is one `console.warn` naming the key and the instance, so the rule editors are given is
unchanged: all widgets of one instance must select the same index.

**The index falls back to the project's only index.** `IXpSearchIndexCatalog` (over
`ILuceneIndexManager.GetAllIndices()`) supplies both the drop-down and the fallback: a project with one
index never forces an editor to pick it, and a project with several makes the widget unconfigured until
one is chosen.

## Consequences

- A host needs `UseStaticFiles()` and one `<xps-search-assets />` in its layout. That is the whole
  developer-side setup; everything else is attribute-driven.
- Building `XpSearch.Widgets` requires the JavaScript bundle to have been built. This is a real coupling
  and the error message says exactly what to run.
- Installing only `YourCo.Xperience.Search.Widgets` gives working widgets with a *hidden* facet attribute
  field, because nothing is registered under `xpsearch.facetAttribute`. Adding
  `YourCo.Xperience.Search.Admin` populates it. This is stated in the guide.
- `XpSearch.Admin` now references `XpSearch.Widgets` (for the identifier constant and `nameof(Index)`),
  which makes the package graph Core ← Widgets ← Admin.
- `readInstanceOptions` in `src/XpSearch.Client/src/bootstrap.ts` changed behaviour: it merges the
  group's instance configs rather than taking the first usable one. A group whose mounts already agreed
  produces exactly the same options object, so existing markup is unaffected.
- `XpSearch.Core` gained two Page-Builder-facing members it does not itself use — `XpSearchConstants` and
  `Facets.FacetAttributeOptions`. That is the price of the flat package graph: they are the contract
  between `Widgets` and `Admin`, and Core is the only place both can see.
- The mount markup is asserted through `IXpSearchMountRenderer` and `BuildModel()` without Razor, and the
  view itself is rendered once through a minimal MVC host, which is what proves it compiled into the RCL
  at the path the base class returns.
