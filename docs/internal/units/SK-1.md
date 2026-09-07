# SK-1 — Skeleton first paint + crawler-graceful server rendering

**Design (the spec of the look):** `docs/internal/design/Skeleton.dc.html` — the desktop page as it
exists before JavaScript runs. `States.dc.html` (left card) remains the spec of the results-list
skeleton. Per [[feedback-default-is-the-design]] the shipped default must match these boards.

**Why.** Today a live Page Builder search page is: one real thing (the Results widget's first page,
server-rendered by `ServerRenderedResults`) surrounded by empty `<div class="xps-mount">` elements.
A crawler or a no-JS visitor gets result cards but no way to search or page; a JS visitor sees empty
gaps, then — worse — the client's first render **replaces the server cards with a skeleton** until
the hydration query answers (`createRoot` empties the container; `results` paints its skeleton when
`results === null`). This unit makes the pre-JS page complete and makes hydration a no-flash handover.

## 0. Governing rules (binding)

- **Layer order per [[feedback-bottom-up-layering]]:** JS library → tag helpers → Page Builder
  widgets. Slice J touches only `src/XpSearch.Widgets/Client` + `themes/`. Slice S touches only the
  tag-helper layer that RZ-1 introduces (`src/XpSearch.Widgets/TagHelpers/`) plus one additive Core
  change (§2.1); the Page Builder layer inherits everything through `BuildAsync` and changes nothing.
- **Slice S starts only after RZ-1 is merged to `main`.** It is written against RZ-1's
  `XpSearchMountTagHelper<TOptions>.BuildContentAsync`. If RZ-1's final shape differs from its spec,
  follow the merged code and amend §2 here in the same commit.
- **The mount contract does not change.** `data-xps-*` attributes stay as they are. The one marker
  already in the contract, `data-xps-server-rendered` on the element inside the mount, becomes the
  handshake for every widget, not just results (§1.1).
- **Parity is the acceptance test.** Server skeleton markup for a widget = the fixture
  `themes/fixtures/skeleton.html` block for that widget, byte for byte (test-pinned, same pattern as
  `card-parity.test.ts`). Server GET form = client `searchBox` first render for the same state.
- **Skeletons are decoration.** Every skeleton root carries `aria-hidden="true"`; it contains no text
  a crawler could index and no focusable element. Live server markup (form, cards, chips, page
  links) carries none of that.
- **No new dependency, no new abstraction beyond what is named here.** Reuse `EditorPreview`'s
  builders (`El`, `Skeleton`, `Decorative`, …) for server skeletons; if that class needs a
  non-editor name, rename it once (`MountMarkup`) — do not duplicate it.

## 1. Slice J — JS library + themes (from `main`, independent of RZ-1)

### 1.1 Adoption instead of replacement (`widgets/dom.ts`)
`createRoot(container, tagName, className)`: when `container.firstElementChild` carries
`data-xps-server-rendered` **and** has the requested `tagName`, adopt it — set `className`, leave its
children in place, return it — instead of emptying the container. Any other content is discarded as
today. Export `isServerRendered(root)` (true until the widget's first real paint removes the
attribute) so renderers can ask.

Renderer rule, every shipped widget: on a render where `results === null` (nothing has answered yet)
and the root is server-rendered, **leave the adopted children untouched** — the server content
(skeleton or live markup) stays on screen. On the first render with `results !== null` the widget
paints as today and removes `data-xps-server-rendered`. Wiring (event listeners, the `results`
live-region `<p>`) still happens on `isFirstRender`; the `results` renderer's "remove everything
after the status element" loop must become "remove every child except the status element", since the
adopted children precede the appended status.

`searchBox` is the exception: it is not driven by results. It replaces the server form on its first
render (same markup, so nothing moves), and if the server input **had focus or a value that differs
from the state's query**, the client input receives that value and focus. A visitor who started
typing before the bundle ran loses nothing.

### 1.2 `rel` on pagination links (`widgets/pagination.ts`)
The previous/next links carry `rel="prev"` / `rel="next"`. Fixture `pagination.html` and `MARKUP.md`
updated. (Needed for byte parity with the server links in §2.3; useful to crawlers on its own.)

### 1.3 Skeleton vocabulary (themes)
Extend `xps-skeleton` with the geometry the Skeleton board draws — modifiers, not new blocks:

| modifier | stands in for | board geometry |
|---|---|---|
| `--title` (exists) | result title | 16px high, 55–70% wide |
| `--text` (exists) | body line | 11px, 60–100% |
| `--block` (exists) | media slot | 72×72, radius 6 |
| `--heading` | facet/tree/range heading | 12px, 30–40%, sits on the 2px rule |
| `--box` | checkbox / toggle square | 16×16, radius 4 |
| `--count` | facet count | 11px × 20px, pushed right |
| `--control` | select / number input body | 11px inside a bordered 32px box (`--control` is the bar; the box is the widget's own control class with no text) |
| `--track` | range track | 4px, full width, pill |
| `--line` | stats sentence / label | 11–12px, fixed widths (220px stats, 44–64px labels) |

Sizes live in `shell` (structure), colour/opacity/pulse in the default theme (as today; TH-7
boundary). Every class appears in `themes/fixtures/skeleton.html`, one block per widget root
(`xps-facet-list`, `xps-category-tree`, `xps-range-filter`, `xps-toggle-filter`,
`xps-result-stats`, `xps-sort-select`, `xps-filter-sort`, `xps-pagination`, `xps-active-filters`,
`xps-clear-filters`, `xps-load-more`, `xps-suggestions`; the results skeleton already exists in
`results.html` and stays the normative one). `npm run check` in `themes/` stays green.
Row counts: facet list 4 rows, tree 1 + 3 indented, pagination 5 pills (36×36), chips 1.

### 1.4 Tests (vitest, `widgets/ssr-adoption.test.ts`)
1. Results mount pre-filled with the server list → after `start()`, before the fetch resolves, the
   DOM still holds the server cards and **no** `xps-result--skeleton`; after it resolves the client
   list is there and the marker attribute is gone.
2. Facet mount pre-filled with the skeleton block → unchanged until the response; replaced after.
3. Empty results mount → skeleton on first render, as today (regression guard).
4. Search box: server form with a typed value and focus → after hydration the client input has that
   value and focus.
5. Pagination links carry `rel` prev/next; `card-parity`-style check that each `skeleton.html` block
   is what the server emits is in Slice S (the fixture is authored here, checked there).

## 2. Slice S — tag helpers (after RZ-1 merge)

### 2.0 RZ-1 as merged (main 92e0984) — the shapes to build on
`src/XpSearch.Widgets/TagHelpers/*.cs`, one file per widget (options record + tag helper).
`XpSearchMountTagHelper<TOptions>`: `protected virtual Task<IHtmlContent?> BuildContentAsync(options, ct)`
returns null by default; `CurrentIndex`, `MountLabels`, `ViewContext` available; `BuildAsync` is the
single path the Page Builder layer and `Html.XpSearchAsync` call. `ResultsTagHelper` is unsealed with
`protected ServerRenderedResults? ServerResults` and `protected ServerResultsRender? FirstPaint`.
Tag helpers are transient (per render), so the shared render (§2.4) lives on `HttpContext.Items`,
not on the helper. `LoadMoreTagHelper` has no content override: the skeleton default covers it.
`tests/XpSearch.Widgets.Tests/PageBuilderParityTests.cs` already pins PB output == tag output per
widget; the skeleton default must therefore come out identical both ways (it will, if it depends on
options only). The host has `/search-razor` built purely from tag helpers — a second surface to check
the first paint on.

### 2.1 Core, additive only
`ServerResultsRender` gains `Total`, `Page` (the applied page) and `Query` (the applied query text)
so the widgets that follow the results on the page can render from the same search. No new search
is ever run for them. `ServerRenderedResultsTests` pin the three values.

### 2.2 Default content = skeleton
`XpSearchMountTagHelper<TOptions>.BuildContentAsync` default returns the widget's skeleton block
(from §1.3, built with the shared builders) wrapped as
`<div data-xps-server-rendered aria-hidden="true" class="xps xps-<block> xps-<block>--skeleton">…</div>`.
The root tag name matches what the client's `createRoot` asks for (e.g. `nav` for pagination, `form`
for search box) or adoption fails silently. A test per widget compares the emitted bytes to the
fixture block. The existing editor previews are **not** changed (they keep their note + badge).

### 2.3 Live server markup where the server knows enough
- **searchBox** — a real `<form class="xps xps-search-box" role="search" method="get">` with the
  client's label, `<input type="search" name="q" value="<current q>">` and submit button, ids per the
  `widgetId` rule for the first occurrence. Submitting without JavaScript reloads the page with `?q=`,
  which `ServerRenderedResults` already honours. Parity test against the client's first render.
- **activeFilters** — when the request filters by something and the labels are known, real chips
  (each chip an `<a>` to the URL without that value; "Clear all" to the URL without filters). Labels
  come from the shared render (§2.4) if the results rendered first, else from
  `SearchQueryState` + the same label lookup `ServerRenderedResults` uses. Skeleton when nothing is
  filtered.
- **pagination** — when the shared render exists: real `<a href="?…&page=N">` links from
  `SearchQueryState` (same URL mapping the client writes), `rel="prev"/"next"`, current page marked as
  the client marks it. Else skeleton.
- **resultStats** — when the shared render exists: the client's sentence
  (`<strong>N results</strong> for “q”`). Else skeleton.
- Everything else (facets, tree, range, toggle, sort, filterSort, clearFilters, loadMore,
  suggestions) — skeleton; facet counts need a request nobody has declared yet on the server.

### 2.4 The shared render
The results tag helper stashes its `ServerResultsRender` in a per-request store keyed by instance id
(`HttpContext.Items`, one small internal class in `TagHelpers/`). The three widgets in §2.3 read it.
It is **document-order dependent**: a widget rendered before the results sees nothing and paints a
skeleton. Record in `KNOWN-LIMITATIONS.md` (symbol, ceiling, upgrade path = the `<xps-search>` scope
declaring `results-per-page` + facets and running the one search when the scope opens).

### 2.5 Tests
Per-widget skeleton parity; searchBox form parity; pagination/resultStats/activeFilters render live
when results came first and skeleton when they did not (two `_Mount`-order tests through the real
view engine, `MountViewRenderingTests` pattern); the Page Builder parity test RZ-1 introduced still
passes unchanged — that is what proves the PB layer inherited all of this for free.

## 3. Slice D — docs (doc-specialist, after S is approved)
- `docs/guides/server-rendering.md`: new section "What a crawler sees" — the pre-JS page, the GET
  form, page links, chips, which widgets stay skeletons and why (order), and one Razor line for
  `<meta name="robots" content="noindex,follow">` on `?q=` pages (internal result pages should not be
  indexed; the *links out of them* should be followed). Verified sample = the demo host's `/search`
  fetched with JavaScript disabled.
- `docs/guides/theming.md`: the skeleton modifiers table (§1.3) and how a theme restyles them.
- `themes/MARKUP.md`: `data-xps-server-rendered` handshake, `rel` on pagination, skeleton fixture.
- CHANGELOG: `**Breaking (widgets):** pagination links carry rel` is not breaking; the empty-mount →
  skeleton change is behavioural, list under Changed. ADR-0030 "Server first paint: adopt, never
  replace" (short).

## 4. Not in this unit
Facet counts on the server (needs declared facets: RZ-1 `<xps-search>` follow-up); `<link rel=next>`
in `<head>` (host layout concern, documented only); language/channel on mounts; a `<noscript>`
block (nothing needs one once the form and links are real).

## 5. Slices
- **J** — §1, worktree `.claude/worktrees/sk-1` from `main`, branch `unit/sk-1`. Dispatched
  2026-09-06.
- **S** — §2, same branch rebased on `main` after RZ-1 merges.
- **D** — §3, after S is approved.
