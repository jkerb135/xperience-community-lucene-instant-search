# ADR-0020: The custom admin pages follow the owner's design spec, built only from the stock component library

- **Status:** accepted
- **Date:** 2026-08-23
- **Spec reference:** §8.1, §8.4, §9.3
- **Supersedes (in part):** ADR-0016, "Design-system components for chrome, hand-written markup for the data"

## Context

ADR-0016 shipped the two custom pages as working but plain: hand-written `<table>` markup, inline
styles on every hit row, a one-series SVG chart, and a layout that was a flex wrap with hard-coded
pixel gaps. It read as a developer's page, not as part of the administration.

The owner then produced a design spec for the three custom pages (Analytics, Query tester, Status):
<https://claude.ai/design/p/d9cffec1-046f-46e2-b611-d162418351f9>. Every region of every artboard
carries a badge naming the `@kentico/xperience-admin-components` component it is built from, so the
spec is an implementation instruction rather than a picture. This ADR covers Analytics (artboards
1a–1d) and Query tester (2a–2d).

## Decision

**The design spec is the specification for these two pages.** Region by region, the badge names the
component. Nothing is invented and nothing is approximated with markup where a component exists:
`Card` (with its `headline`, `description` and `footer` slots) for every panel, `Row`/`Column`/
`Stack`/`Inline`/`Box` for every layout, `Table` with `StringCell`/`ActionCell` for every report,
`Tag` for every marker, `Callout` for every empty and error state, `NameToggleButtons`,
`DateTimeInput`, `Select`, `Input`, `Button`, `Headline`, `Icon` and `Spinner` for the controls.
The hand-rolled `<table>`, `HitRow` and `ChangeMark` markup of ADR-0016 is gone.

**Only the stock package, and no CSS pipeline.** `@kentico/xperience-admin-components` 31.8.0 is the
whole vocabulary; its `.d.ts` is the authority on what exists. No UI or CSS library was added, and no
style loader was added to webpack, because the layout components' spacing props express the design's
spacing and the responsive behaviour is a `useMediaBreakpoints()` branch. The three text treatments no
component exposes — low-emphasis prose, monospace query panels, the KPI figure — live in
`src/theme.ts` as inline style objects built from the package's own `Colors` tokens, so they follow a
theme change like everything else.

**Two badges could not be honoured literally.**

- *Tabs* (artboard 2d). The package exports no `Tabs`; the only tab-shaped export is `VerticalTab`,
  a single left-rail item. The narrow query tester therefore uses `NameToggleButtons`, the package's
  horizontal segmented control, which is what the artboard's pill row actually looks like.
- *Tag with an icon*. `TagProps` has `label`, `background`, `tooltipText` and a `leadingButton`, but
  no icon. A change marker is an `Inline` of a stock `Icon` and a `Tag`, which keeps the rule that a
  marker is never colour alone.

Both are recorded here rather than as code comments.

**Responsive is a hook, not a media query.** `useMediaBreakpoints().sm` is true at or below 1365.98
px, which is the artboards' 1024 board. At `sm` the KPI tiles go from `Col3` to `Col6`, the two table
columns become `Col12` (zero-result queries first, because it is the actionable one), and the query
tester's two columns become one toggled list with the pipeline stages collapsed.

**Four figures the design needs were added to the report, not faked.** The KPI tiles need a
zero-result rate and a click-through rate, the chart needs a second series, and the top-queries table
shows p95. Summing the top-N rows would have been wrong — a top-N list is not a total — so
`SearchAnalyticsReport` gained `ZeroResultSearches` and `Clicks`, `SearchVolumePoint` gained
`ZeroResultVolume`, and `QueryVolume` gained `P95ProcessingTimeMs`. All four come out of the single
read `SearchAnalyticsService` already does; nothing new is queried.

**The index is never a selector.** Both pages are index-scoped by URL (ADR-0017), so
`IndexNames` and `IndexLocked` were dead weight on both client property classes and are gone. The
index is a name under the headline.

**The query tester's language is a list, not free text.** The options are the index's own
`LuceneIndexModel.LanguageNames` plus *Any language*; `IndexScope.ResolveModel` exposes the stored
model the code names come from. Display names come from the browser's `Intl.DisplayNames`, which
falls back to the bare code, so no language table travels to the client.

**The error callout's actions are page commands.** *Open status* is a new `OpenStatus` command on
`QueryTesterPage` returning `NavigateTo(IPageLinkGenerator.GetPath<IndexStatusPage>(…))`, mirroring
the dashboard's `CreateRule`. The client never builds an admin URL.

## Consequences

- The two pages now depend on the layout and table components, so a breaking change in
  `@kentico/xperience-admin-components` is felt more widely than before. The package is pinned.
- `SearchAnalyticsReport`, `SearchVolumePoint` and `QueryVolume` are positional records that gained
  members: a consumer constructing them by hand must add the arguments. They are report DTOs produced
  by the service, so the practical blast radius is test fixtures.
- There is still no JavaScript test harness (KNOWN-LIMITATIONS). The runnable check for these pages
  remains `npm run build` with `strict` TypeScript, plus the C# command tests. Whether the painted
  page matches the artboard can only be judged signed in to a host.
- The design spec covers a Status page too (artboards 3a–3c). It is built separately.
- The pages that grew out of this decision — analytics, index status, the rule builder and the
  experiment detail — share one set of layout guidelines with the query tester: stock components
  everywhere, layout in per-page flex/grid wrappers, a 24px rhythm between cards and 16px inside
  one, no page-level padding, and colours named only through the package's `--color-*` tokens. They
  are written down in *Layout guidelines for custom pages* in
  `docs/guides/admin-client-development.md` and checked statically by
  `src/XpSearch.Admin/Client/src/layout.test.ts`. The regions that are our own markup because the
  package has no component for them are: the analytics volume chart (inline SVG, its legend and its
  axis labels), the index status page's stacked “documents by source” bar and its source
  swatches, and the rule builder's drag grip, drop-insertion line, dashed add area and item-picker
  list. Everything else on those pages is a stock component.
