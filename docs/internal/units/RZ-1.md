# RZ-1 — Bottom-up layering: JS library → tag helpers → Page Builder widgets

**Status:** SPEC READY — NOT DISPATCHED (owner directive 2026-09-06; dispatch on the owner's word).
**Origin:** owner, 2026-09-06, after the go-to-market review: the product must be built from the
ground up as (1) a JavaScript/HTML consumption library in the InstantSearch mould, (2) tag helpers
that wrap it and initialise the controls for Razor view development, (3) out-of-the-box Page Builder
widgets, shipped only in the Widgets project, that scaffold on top of the tag helpers. One library,
extensible through every avenue by a developer.

**What exists today (main 6e11f4a):** layer 1 is complete (`Client/src/**`, the `data-xps-*` mount
contract, `mountAll`, `registerWidgetType`, the behaviours API). Layer 3 exists for 12 of 14 widgets
but sits DIRECTLY on `XpSearchMount` + `IXpSearchMountRenderer` inside
`XpSearchMountWidgetViewComponent<T>`, which owns index resolution, instance defaulting, config
reflection, per-widget config mapping (5 overrides), instance config (2), server-rendered content
(results) and labels (FC-1). Layer 2 is only `<xps-search-assets />` and `Html.XpSearchAssets()`.
A Razor developer today hand-writes mount divs. This unit inserts layer 2, moves the shared logic
into it, and reduces layer 3 to editor-facing mapping over it.

## 0. Governing rules (binding)

- **Layer 1 is untouched.** No file under `src/XpSearch.Widgets/Client/src/` changes; the mount
  contract (`data-xps-widget`, `data-xps-instance`, `data-xps-config`, `data-xps-instance-config`,
  `data-xps-labels`) does not change. STOP and report if the tag-helper layer appears to need either.
- **Core is untouched.** STOP clause as usual.
- **One implementation per widget.** After this unit, "options → mount" for every widget lives in
  exactly one place, the widget's tag helper class. The Page Builder base class no longer has
  `BuildConfig`, `BuildInstanceConfig`, `GetWidgetType`, `BuildMountContentAsync`,
  `ConfigurationHint`, `ReflectConfig`, `CurrentIndex` or `MountLabels`. Delete them; do not keep
  forwarding shims (pre-1.0, source-breaking convention applies, CHANGELOG `**Breaking (widgets):**`).
- **Parity is the acceptance test.** 14 JS widgets = 14 tag helpers = 14 Page Builder widgets, and
  for every widget the Page Builder live-page output is byte-identical to the tag element's output
  for the same options (test-pinned).
- **Third parties get the same three avenues** as first-party widgets, through the same base
  classes. The dropdown sample (`samples/CustomWidget.Dropdown`, the drift-checked worked example)
  is updated in this unit and must pack-and-build green.

## 1. Layer 2 — tag helpers (`src/XpSearch.Widgets/TagHelpers/`)

### 1.0 Assets split (owner, 2026-09-06)
`<xps-search-assets />` is split into two tag helpers so a host can place styles in `<head>` and
scripts at the end of the body: `<xps-search-styles default-theme="…" theme="…" />` emits ONLY the
stylesheet links (shell + chosen palette), `<xps-search-scripts />` emits ONLY the script tag.
`XpSearchAssets.Render` splits into `RenderStyles`/`RenderScripts` (one implementation; the
existing `<xps-search-assets />` and `Html.XpSearchAssets()` stay as the shorthand that emits both,
composed from the two — no duplicated markup). `Html.XpSearchStyles()`/`Html.XpSearchScripts()`
mirror them. `AssetsTests` cover the split and the composition (assets == styles + scripts).

### 1.1 Options records — one per widget, Kentico-free
`XpSearch.Widgets.Mounting.XpSearchMountOptions` (abstract record: `Index`, `InstanceId`) and one
sealed record per JS widget (`SearchBoxOptions`, `ResultsOptions`, `FacetListOptions`,
`CategoryTreeOptions`, `RangeFilterOptions`, `ToggleFilterOptions`, `SortSelectOptions`,
`PaginationOptions`, `LoadMoreOptions`, `ResultStatsOptions`, `ActiveFiltersOptions`,
`ClearFiltersOptions`, `FilterSortOptions`, `SuggestionsOptions`). Properties mirror the JS widget's
params 1:1 with the C# defaults the Page Builder properties carry today. NO form annotations, no
Page Builder or Admin types on them: this is the surface a Razor developer and a unit test see.

### 1.2 `XpSearchMountTagHelper<TOptions>` — the single implementation
Abstract `TagHelper`. Per-widget subclasses bind attributes with `[HtmlAttributeName]` (kebab-case
of the option name) and also accept the whole record via `options="@model"`. The base owns, once:
- index resolution: attribute → enclosing `<xps-search>` (§1.3) → the project's sole index
  (`IXpSearchIndexCatalog`) → validation failure;
- instance id: attribute → enclosing `<xps-search>` → `default`;
- `Validate(options)` → `null` or a message (moved from `ConfigurationHint`, same texts from
  `WidgetResources`). On a live page the tag helper THROWS `InvalidOperationException(message)`:
  a Razor developer wants the failure at render time, not an empty div;
- `BuildConfig` (default = reflection over the record, as `ReflectConfig` does today),
  `BuildInstanceConfig`, `GetWidgetType`, `BuildContentAsync` (results first paint),
  `Labels` (FC-1) — the virtuals move here verbatim, per widget, from the view components;
- `public Task<XpSearchMount> BuildAsync(TOptions, CancellationToken)` — the reusable core, used by
  `ProcessAsync`, by the Page Builder layer (§2) and by the `IHtmlHelper` extension (§1.5);
- rendering through `IXpSearchMountRenderer` (unchanged).
Tag helpers are DI-activated; also register each as a service in `AddXpSearchWidgets()` so §2 can
resolve `XpSearchMountTagHelper<TOptions>` by options type. Third parties register theirs with
`services.AddXpSearchWidget<TTagHelper, TOptions>()`.

### 1.3 `<xps-search>` — instance scope for ease of use
`XpSearchTagHelper`: attributes `index`, `instance` (default `default`), plus the instance-wide
options the JS `createSearch` accepts (`routing`, and whatever `BuildInstanceConfig` writes today —
enumerate from `SearchBoxWidget`/`ResultsWidget`; add nothing new). Publishes them through
`TagHelperContext.Items` for descendant mounts; renders no element of its own (`TagName = null`).
Every child mount still writes its own `data-xps-instance-config` — no mount-contract change.

### 1.4 `<xps-widget>` — the generic mount for custom JS widgets
`XpSearchWidgetTagHelper`: `type` (the `registerWidgetType` id), `config` (object, serialised
camelCase), optional `instance-config`, `index`, `instance`. A custom widget author who does not
want a bespoke tag helper uses this; one who does derives `XpSearchMountTagHelper<TOptions>` exactly
as first-party widgets do.

### 1.5 `IHtmlHelper`
One generic extension: `Task<IHtmlContent> XpSearchAsync<TOptions>(this IHtmlHelper, TOptions)`
resolving the tag helper for `TOptions` and returning `renderer.Render(await BuildAsync(...))`. No
per-widget typed extensions (YAGNI; the tag element is the Razor surface).

### 1.6 The two missing widgets
`toggleFilter` and `loadMore` get options records, tag helpers AND Page Builder widgets
(`XpSearch.ToggleFilter`, `XpSearch.LoadMore`, constants, editor previews, properties following the
sibling pattern, `page-builder-widgets.md` sections). This closes 14/14 and is the natural
acceptance test of the layering. Note MB-1: `loadMore` replaces `results`+`pagination` and owns
`state.page`; the PB widget's explanation text must say it does not coexist with Pagination.

## 2. Layer 3 — Page Builder widgets scaffold on the tag helpers

`XpSearchMountWidgetViewComponent<TProperties, TOptions>` keeps ONLY editor concerns:
- `abstract TOptions ToOptions(TProperties)` — the property → options mapping (one-liners);
- unconfigured state: `tagHelper.Validate(options)` message → the existing editor instruction
  block (`Unconfigured_*` resources); live page renders nothing, as today (spec §7.5);
- Edit/ReadOnly mode → `BuildEditorPreview(TProperties)` exactly as today;
- live page → `await tagHelper.BuildAsync(options)` → `_Mount.cshtml` renders the mount via the
  renderer, as today. The tag helper is resolved from DI by `TOptions`.
Razor tag syntax cannot be selected dynamically inside the one shared view, so the Page Builder
layer calls the tag helper CLASS rather than emitting the tag element; the parity test in §4 pins
that both produce the same bytes, which is what makes "widgets scaffold on the tag helpers" true.
`XpSearchMountWidgetProperties` (with its form annotations) stays the editor-facing shape; the 14
`*WidgetProperties` classes keep their annotations and gain nothing.

## 3. Docs (wiki-ready, verified samples, per [[feedback-docs-wiki-ready]])
- NEW `docs/guides/razor-tag-helpers.md`: `<xps-search-assets>`, `<xps-search>`, every widget tag
  helper with its attribute table (generate the tables from the options records with a script under
  `Client/scripts/` or a small C# test that emits them — pick the smaller; the point is no drift),
  `<xps-widget>`, `Html.XpSearchAsync`, failure semantics (throws), SSR through `<xps-results>`.
- NEW `docs/guides/building-a-search-page.md`: the SAME results page three ways — plain HTML +
  bundle (the existing canonical recipe from `widget-reference.md`, moved or linked), Razor with
  tag helpers (verified: the view is rendered in `Widgets.Tests` and its mounts asserted equal to
  the plain-HTML recipe's), Page Builder (which widgets to place). This is the page the README's
  quick start points at for "the UI".
- `custom-widgets.md`: the three avenues for a custom widget (JS `registerWidgetType`; bespoke tag
  helper or `<xps-widget>`; Page Builder via the two-type-parameter base) with the sample embedded
  verbatim as today (drift check kept).
- `page-builder-widgets.md`, `server-rendering.md`: one paragraph each on the layering.
- ADR-0029 "Bottom-up layering of the widget surfaces" (context: this spec; decision; the
  "call the class, not the tag" consequence; alternatives: per-widget PB views, a definitions layer
  beside the tag helpers — rejected as a second layer).
- CHANGELOG: `**Breaking (widgets):**` base-class change + `**Added (widgets):**` tag helpers,
  `<xps-search>`, `<xps-widget>`, two widgets. KNOWN-LIMITATIONS for honest ceilings only.

## 4. Verification
- `Widgets.Tests` (NUnit, no host — reuse `WidgetTestContext`, `TagHelperContext`/`TagHelperOutput`
  harness): one test class per tag helper (attributes → mount JSON); attribute↔options reflection
  parity (every public option property has exactly one `[HtmlAttributeName]`, kebab-case);
  `<xps-search>` inheritance and override precedence; sole-index fallback; validation throws with
  the resource text; `<xps-widget>` generic; `<xps-results>` server content + labels;
  **PB ≡ tag helper**: for all 14 widgets, live-page view output for `ToOptions(properties)` equals
  the tag element output for the same record, byte-for-byte; `ThirdPartyWidgetTests` re-pointed at
  the new base. Existing `MountMarkupTests`, `MountViewRenderingTests`, `UnconfiguredStateTests`,
  `EditorPreviewTests`, `ServerRenderedResultsTests` adapted, none deleted.
- `git diff --stat -- src/XpSearch.Widgets/Client/src` is EMPTY at review (rule §0).
- All C# suites green; JS suite unchanged (run it anyway); `samples/pack-and-build.mjs` green;
  themes check green (no theme change expected — STOP if one is needed).
- Checklist section for the host: `docs/internal/host-pass-hw11-checklist-2026-08-26.md` gains a
  `§AB` — the demo's Razor page (§5) renders the mockup skeleton with zero overrides; PB `/search`
  unchanged pixel-for-pixel after the rebuild; the two new PB widgets placeable.

## 5. Host follow-up (lead-direct, after merge — not this unit)
Add `/search-razor` to the Dancing Goat host as a plain Razor view using `<xps-search-assets>`,
`<xps-search index="DancingGoatSample">` and the tag helpers, mirroring `/search` — so the demo
shows all three avenues on the same index. README of `src/Search` documents it.

## 6. Not in this unit
JS changes of any kind; Core; language/channel properties on mounts (separate units, see the
2026-09-06 review); typed per-widget `IHtmlHelper` methods; React/Vue adapters.

## 7. Slices (one agent, one worktree, review between slices)
- **A** — §1.1–1.5 tag helpers + §2 base refactor + sample + tests §4 (all 12 existing widgets).
- **B** — §1.6 the two widgets across all three layers + their tests and guide sections.
- **C** — §3 docs + ADR + CHANGELOG (docs agent may take this after A+B are approved).
