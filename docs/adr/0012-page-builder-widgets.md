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
| Configurator | Put it in `XpSearch.Admin`, referenced by string identifier | Live-site code stays free of the administration package; this is the documented pattern for [deploying without the administration](https://docs.kentico.com/documentation/developers-and-admins/deployment/deploy-to-private-cloud/deploy-without-the-administration) | Two packages must be installed for the drop-down to populate |
| Instance config | Only the Results widget emits it | One source of truth, no agreement needed | An instance without a Results widget never starts |
| Instance config | Every mount emits it; all must agree on the index | Any single widget is enough to start the instance; order-independent for the index | Options only one widget knows (page size, fields) depend on that widget being first |

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
`[FormComponentConfiguration(XpSearchWidgetConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]`
— without referencing the administration at all, and `XpSearch.Admin` registers
`FacetAttributeConfigurator` under that identifier with `[RegisterFormComponentConfigurator]`. The logic
that turns a schema into option lines is a plain static method, `FacetAttributeOptions.BuildOptionsAsync`,
in `XpSearch.Widgets`, so it is unit-tested against a substituted `IIndexSchemaProvider` without any
administration types; the configurator is a fifteen-line shell that hides the field
(`VisibilityConditions.Add(new AlwaysInvisible())`) when the method returns `null`.

**Every mount emits `data-xps-instance-config` with its index, and all widgets of one instance must
select the same index.** That is the documented rule, and it is what makes drag-and-drop placement work:
any one widget is enough to start the instance, so an editor can build a search out of a search box and
a facet list with no results widget at all and still get a running instance. Two options that are
genuinely instance-wide — *Results per page* and *Fields to show* — are contributed by the Results widget
into the same object, and therefore apply when the Results widget is the first widget of its instance in
page order. Both are optional and both have index-level defaults, so the ordering only matters to a
project that sets them.

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
- Instance options that only one widget can know are order-dependent. The upgrade path is a small change
  in `mountAll()` — merge the instance configs of a group instead of taking the first — recorded in
  `KNOWN-LIMITATIONS.md`.
- The mount markup is asserted through `IXpSearchMountRenderer` and `BuildModel()` without Razor, and the
  view itself is rendered once through a minimal MVC host, which is what proves it compiled into the RCL
  at the path the base class returns.
