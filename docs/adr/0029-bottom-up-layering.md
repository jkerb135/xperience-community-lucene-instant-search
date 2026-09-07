# ADR-0029: Bottom-up layering of the widget surfaces

- **Status:** accepted
- **Date:** 2026-09-06
- **Spec reference:** §5.7, §5.8, §7; unit RZ-1

## Context

The owner's go-to-market review (2026-09-06) restated what the product is: a JavaScript consumption
library in the InstantSearch mould, wrapped by Razor tag helpers for view development, with
out-of-the-box Page Builder widgets on top — one library, extensible through every avenue.

What existed did not have that shape. Layer 1 was complete (`Client/src/**`, the `data-xps-*` mount
contract, `mountAll`, `registerWidgetType`). Layer 3 existed for 12 of the 14 JavaScript widgets, but
sat *directly* on `XpSearchMount` + `IXpSearchMountRenderer` inside
`XpSearchMountWidgetViewComponent<T>`, which owned index resolution, instance defaulting, config
reflection, per-widget config mapping (5 overrides), instance config (2), the server-rendered first
paint and the FC-1 mount labels. Layer 2 was two members: `<xps-search-assets />` and
`Html.XpSearchAssets()`. A Razor developer hand-wrote mount divs and hand-encoded their JSON, and a
third party writing a widget could only reach the Page Builder — there was no Razor surface to extend.

The forcing constraint: "options in, mount out" had to exist exactly once per widget, and the layer a
developer writes Razor against had to be the one the Page Builder widgets themselves use — otherwise
the two drift, and the second implementation is discovered by a customer.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Leave the logic in the view component; add tag helpers that call it | No change to layer 3 | The Page Builder base class stays the implementation, so the tag helpers are a wrapper over an editor-facing type — Razor developers inherit `ComponentViewModel`, `ViewComponent` and the Kentico dependency |
| Tag helper is the implementation; the Page Builder widget calls the tag helper class | One implementation; the Razor surface is Kentico-free (`XpSearchMountOptions` records); third parties extend the same base classes | Source-breaking change to the Page Builder base class; the tag helper becomes per-render state, so it must be transient |
| Per-widget Page Builder views, each emitting its own tag element | "The widget really is the tag" | 14 new `.cshtml` files, each duplicating the editor-mode branching, the preview and the unconfigured block; the shared `_Mount.cshtml` disappears |
| A definitions layer (widget metadata) beside the tag helpers, consumed by both | Neither surface calls the other | A fourth concept nobody asked for; every widget then has an options record, a definition and a tag helper, and the definition is exactly the tag helper minus its attributes |

## Decision

**Three layers, bottom-up, with the tag helper as the single implementation.**

1. **JavaScript** — untouched by this unit. The mount contract (`data-xps-widget`,
   `data-xps-instance`, `data-xps-config`, `data-xps-instance-config`, `data-xps-labels`) is unchanged
   and remains the stable interface (ADR-0009, ADR-0012).
2. **Tag helpers** — one sealed options record per widget (`XpSearch.Widgets.Options`, deriving
   `XpSearchMountOptions`, free of Kentico and Page Builder types) and one
   `XpSearchMountTagHelper<TOptions>` subclass. The base owns index resolution (attribute → options
   record → enclosing `<xps-search>` → the project's sole index), the instance id, `Validate`,
   `BuildConfig`, `BuildInstanceConfig`, `GetWidgetType`, `BuildContentAsync` and rendering through
   `IXpSearchMountRenderer`. `<xps-search>` supplies scope, `<xps-widget>` mounts any
   `registerWidgetType()` id, and `Html.XpSearchAsync(options)` resolves a tag helper by options type.
3. **Page Builder widgets** — `XpSearchMountWidgetViewComponent<TProperties, TOptions>` keeps editor
   concerns only: `ToOptions(properties)`, `BuildEditorPreview(properties)`, the unconfigured
   instruction block, and delegation to the tag helper for the live page.

**The Page Builder layer calls the tag helper class, not the tag element.** Razor cannot select a tag
name dynamically inside the one shared `_Mount.cshtml`, and 14 per-widget views would be 14 copies of
the editor-mode branching. So the widget resolves `XpSearchMountTagHelper<TOptions>` from DI and calls
`BuildAsync`/`RenderAsync` on it. What makes "the widgets scaffold on the tag helpers" true rather than
a claim is a test: `PageBuilderParityTests` renders all 14 widgets both ways from the same options and
asserts the markup is byte-identical.

**Failure differs by surface, deliberately.** `Validate(options)` returns one message from
`WidgetResources`; the tag helper throws `InvalidOperationException` with it, because a Razor developer
wants the failure at render time, while the Page Builder widget turns the same message into the
editor's instruction block and renders nothing on a live page (spec §7.5).

## Evidence

- `tests/XpSearch.Widgets.Tests/PageBuilderParityTests.cs` — 14/14 widgets, Page Builder output ≡ tag
  element output, byte for byte, for the same options.
- `tests/XpSearch.Widgets.Tests/TagHelperTests.cs` — attribute binding, `options="@model"` precedence,
  `<xps-search>` inheritance and override, sole-index fallback, validation throws.
- `tests/XpSearch.Widgets.Tests/BuildingASearchPageTests.cs` — the guide's Razor page produces the
  mounts of the canonical plain-HTML recipe, which is the layering stated as a customer-visible fact.
- `samples/CustomWidget.Dropdown` — a third-party widget built on the published base classes across all
  three layers, packed and built in CI.

## Consequences

- **Source-breaking for anyone who subclassed the Page Builder base** (pre-1.0 convention; CHANGELOG
  `**Breaking (widgets):**`). `BuildConfig`, `BuildInstanceConfig`, `GetWidgetType`,
  `BuildMountContentAsync`, `ConfigurationHint`, `ReflectConfig`, `CurrentIndex` and `MountLabels` are
  gone from it and live on the tag helper (`ConfigurationHint` is now `Validate`); the constructor takes
  the tag helper and the editor context; `BuildModel` is `BuildModelAsync`. No forwarding shims.
- **Tag helpers are transient services.** A tag helper carries the state of one render — the resolved
  `CurrentIndex`, the results widget's first paint — so `AddXpSearchWidget<TTagHelper, TOptions>()`
  registers both it and `XpSearchMountTagHelper<TOptions>` as transient. A singleton would leak one
  request's index into another's.
- **The C# vocabulary is the options records**, not the Page Builder properties: a unit test, a Razor
  view, `Html.XpSearchAsync` and `ToOptions` all speak the same Kentico-free type. The properties
  classes keep their form annotations and gain nothing.
- Rendered markup is unchanged, byte for byte, so no host page and no stylesheet moves.
- The server-rendered first paint now lives in `ResultsTagHelper.BuildContentAsync`, so a plain Razor
  page gets it for free — and SK-1's skeleton first paint has one place to replace (RZ-1 §8).
- Adding a widget is now three small pieces in a fixed order (record + tag helper, registration,
  properties + `ToOptions`), which is what closed the last two widgets, `toggleFilter` and `loadMore`,
  in the same unit.
- A widget option that only the JavaScript has (`pagination`'s `padding`, `activeFilters`'
  `attributeLabels`, the instance's `searchOnInitialLoad`) is now visibly missing from the C# surface
  rather than invisibly absent; `BuildingASearchPageTests` lists them so adding one is a deliberate act.
