# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public behaviour API (spec §5.7) or the JSON contract
(spec §4.2, as amended by ADR-0010) are always major-version events.

Entries lead with the change type and affected project(s), e.g. `**Added (widgets):**`.
Anything source- or behaviour-breaking leads with `**Breaking (scope):**` — the
`docs/guides/breaking-changes.md` page is generated from those entries.

## [Unreleased]

- **Fixed (themes, widgets):** the autocomplete rows have their spacing back. With one source in the
  panel the options are the listbox's own `<li>` children, and the theme's element reset flattens an
  `li` — so the row's `9px 14px` box, the panel's `8px` offset from the field, the filter card's
  padding, the link-button's open box and the screen-reader-only clip were all being wiped when
  `default.css` loaded. Every one of them now comes from `themes/src/scss/_boxes.scss` and is stated
  by **both** stylesheets. New `themes/scripts/check-layout.mjs` (wired into `npm run check`) is what
  keeps it that way: it renders every fixture with the shell alone and with shell + palette, both
  palettes, and fails if any layout property the shell declares computes differently once the theme
  is loaded — the theme may only add visuals. A `suggestions` widget in `mode: "mixed"` now also
  labels a lone group (a single Pages group says **Pages**), since the answer could have come from
  either source; the single-source modes keep their header-less list.

- **Changed (themes):** the default theme's selectors say what they paint. Every design rule is now
  scoped to its widget block — `.xps.xps-pagination .xps-pagination__link`,
  `.xps.xps-results .xps-results__empty` — or, for the elements several widgets render, to the
  element's own class doubled (`.xps .xps-button.xps-button`, `.xps .xps-chip.xps-chip`). The
  `.xps.xps.xps` triple survives only on the element reset, which has no class of its own to carry
  the weight. Nothing about the boundary changed: every rule still lands at (0,3,0), still beats a
  host selector at (0,2,1), still uses no `!important`, and `themes/scripts/check-isolation.mjs`
  still reports zero leaks across every fixture and both palettes. **If your site wrote overrides
  against `.xps.xps.xps.xps …`, update them** — they still win by specificity, but the theme's own
  rules are the ones documented in `docs/guides/theming.md`, "Specificity and host styles", and this
  is the release to follow them (pre-1.0, no deprecation window). Three rules that were written as
  descendants of the root but target the widget root itself never matched and now do, as the design
  always said: the Page Builder editor preview gets its dashed frame and its skeletons stop pulsing,
  the results list dims while a refinement is in flight, and a disabled toggle filter dims.

- **Added (widgets, themes):** the filter column is a card. `xps-sidebar` is a documented
  composition class the host puts on the element holding its refinement mounts — the shell stacks
  them, the theme draws the design's card around them (surface, 1px border, `6px` radius,
  `1.25rem` padding, soft shadow), so put `xps` on that element too. And when nothing matches the
  typed text the autocomplete panel now shows the design's empty state — a muted
  magnifier-with-minus glyph, "No suggestions for “{query}”" and "Press Enter to search anyway, or
  try a different spelling." — over a footer reduced to the two actions that still apply
  (`&crarr;` search, `esc` close), for both the standalone widget and the search box's integrated
  popup. `xps-suggestions__empty` is now a `<div role="status">` holding `__empty-icon`,
  `__empty-title` and `__empty-hint`.

- **Fixed (themes):** the mobile Filter & sort sheet is sized against the *visible* viewport
  (`100dvh` for the sheet, `92dvh` for the panel, each with the `vh` line kept as a fallback), so on
  a phone its footer no longer sits under the browser's URL bar with the page locked behind it — the
  symptom being a pane that cannot be scrolled to the Apply button. The scrolling body also states
  `min-height: 0` explicitly instead of relying on being a scroll container for its flex minimum.
  New `themes/scripts/check-sheet.mjs` (wired into `npm run check`) opens the sheet at 390×600 with
  more facet content than fits and fails if the panel leaves the viewport, if the body stops
  scrolling, or if the header or the Clear all / Show results footer is pushed out of view.

- **Breaking (core):** the root values of `XpSearchOptions` (cache lifetime, query length, page sizes,
  facet and result-window ceilings, suggestion limits, and every `Analytics` number) are now the
  **defaults for every index**, and the administration overrides them **per index** on *Lucene Search
  → index → Search settings*. `services.AddXpSearch(o => …)` keeps configuring the defaults, and an
  index nobody saved settings for uses them unchanged; a saved index uses its own row from then on.
  Code that read one of those values off `IOptions<XpSearchOptions>` should read
  `IOptionsMonitor<XpSearchIndexSettings>.Get(indexCodeName)` per operation instead — the root
  properties are still there, but they are no longer what a given index answers with. Per-index
  code-only settings (`o.Indexes["…"]`: sort keys, suggest field and mode, did-you-mean, popular
  searches) and the indexing options are unaffected. `Analytics.RetentionDays` also changed default,
  180 → **365** days. **Fourteen** of those values are per index: *Default page size* and *Default
  suggestion count* are not, because widgets carry their own sizes — `XpSearchOptions.DefaultPageSize`
  and `DefaultSuggestLimit` remain code-only, for API callers that omit `pageSize` / `limit`, and
  `XpSearchIndexSettings` no longer has them. The Results widget's *Results per page* and the two
  suggestion counts (*Maximum suggestions*, *Maximum items*) are now **required, one or greater**
  (Results defaults to 20); `ServerResultsOptions.ResultsPerPage` no longer treats 0 as "use the
  index's default". Widgets own their sizes; the index owns the caps. On an upgraded installation the
  two retired columns are dropped from `XpSearch_Settings` on the next start: the installer now
  removes the fields its form no longer declares, as well as adding the ones it gained.
- **Added (themes, widgets):** the design now ships in **two palettes** built from one source:
  `kentico-violet` (unchanged — `default.css` is that build, byte for byte, so nothing existing
  moves) and `kentico-orange`, whose accent is Kentico's brand Heritage Orange `#f05a22`. The
  neutrals and every derived tint are shared; the two stylesheets differ in three declarations.
  Pick one with the tag helper's new `theme` attribute
  (`<xps-search-assets theme="kentico-orange" />`), with the new
  `.../themes/kentico-orange.css` npm export, or in SCSS with `@use ".../scss/kentico-orange"` —
  or `".../scss/palettes/kentico-orange"` on the first line of an à la carte stylesheet. Copy a
  file in `scss/tokens/` to make a third.

  The accent is now **three tokens by role**, because a brand colour is rarely legible in all three
  places: `--xps-color-accent` paints fills and decoration (3:1, WCAG 1.4.11),
  **`--xps-color-accent-ink`** is the accent used as text on the surface (4.5:1, AA), and
  **`--xps-color-on-accent`** is the text placed on an accent fill. Both new tokens default to an
  indirection (`var(--xps-color-accent)`, `var(--xps-color-surface)`), so the documented one-token
  re-skin is unchanged and kentico-violet renders exactly as before. kentico-orange pins
  `--xps-color-accent-ink: #c64300` (`5.00:1`, Kentico's darker orange) so links and result titles
  stay AA while the fills carry the brand. Its white primary-button label on `#f05a22` is `3.39:1`
  — an accepted trade recorded in `docs/internal/KNOWN-LIMITATIONS.md`; set
  `--xps-color-on-accent: #1f2430` for AA. `themes/npm run check` now proves every contrast pair of
  **both** palettes in light and dark, the single-token re-skin, and the hostile-CSS isolation.
- **Added (core, admin):** a **Search settings** page per index (*Lucene Search → index → Search
  settings*) edits all fourteen values, headed by *"Retention: remove search analytics older than X
  days"* (default 365). Each index's row is loaded over the code defaults through
  `IConfigureNamedOptions<XpSearchIndexSettings>`, and a save is in effect on the next search without
  an application restart — only the saved index's settings are rebuilt, and that index's cached
  responses are dropped with them, so the very next request reflects the change. The
  `XpSearch.QueryLogRetention` task now prunes **each index with its own window**, deletes *answered*
  popularity and synonym suggestions along with the query log rows — pending suggestions are never
  touched — prunes rows left behind by indexes that are no longer registered with the code defaults,
  and reports one line per index as its *Last result*.

- **Changed (admin, widgets):** every setting and widget property now explains what it does and what
  it interacts with — a tooltip and an explanation line on each field of the per-index *Search
  settings* page and on each Page Builder widget property, saying the one rule: widgets own their
  sizes, the index owns the caps, and API callers that send no size get the code default.

- **Fixed (themes, widgets):** with `default.css` loaded, a widget is now a **closed styling
  boundary** — no site stylesheet can change anything inside one. The theme states its root class
  three times (`.xps.xps.xps`), which puts its element reset at (0,3,0) and its component rules at
  (0,4,0)+, above the (0,2,1) a site reaches without knowing our class names (`button`,
  `.section h3`, `.landing-page ul li`, `.product-filter input[type="checkbox"]`); no `!important`
  is involved, and the `--xps-*` tokens stay on the plain `.xps` so overriding them still works.
  Two new tokens, `--xps-font-size` (`1rem`) and `--xps-line-height` (`1.5`), stop a host `body`
  rule rescaling the design. `themes/npm run check` gained `check-isolation.mjs`, which renders
  every fixture twice in a real browser — with and without Dancing Goat's own CSS re-pointed at our
  markup — and compares 40 computed properties of every element. With it: the mobile Filter & sort
  sheet keeps its own buttons and heading colour, the facet and sheet checkboxes are themed
  (`accent-color`, 16px), the suggestion rows read from the start edge whatever the page centres,
  the empty state's Clear-filters button keeps its size and contrast, and the did-you-mean
  correction is the accent link the board draws. `shell.css` is structure only again: the skeleton
  paint and pulse, the disabled/preview opacities and three `text-align`s moved into the theme.
- **Fixed (widgets):** a refinement that returns nothing no longer takes the filter controls with
  it. `facetList` keeps a selected value the response no longer carries (checked, count 0, new
  `xps-facet-list__item--empty` modifier, never disabled) and `categoryTree` keeps the selected
  category as a real link instead of hiding the whole tree — so the visitor can untick what they
  chose instead of only "Clear all". `loadMore` now runs the same unfiltered probe as `results`, so
  the counted empty state ("There are 7 results without them" + the primary button) also appears in
  Load more mode.
- **Fixed (widgets, themes):** the autocomplete panel and the empty states now match the design
  artboards. Panel: a clock glyph on recent rows and a magnifier on suggestion rows, per-source
  option modifiers (`--recent` / `--query` / `--document`), page rows in accent semibold over their
  muted meta line, an 11px uppercase group heading with a rule between groups, 20px footer keycaps
  and a tinted footer, and an **X on each recent row** that drops that one entry (pointer
  affordance; `Delete` on the active row is the keyboard path). The recents' heading row — and with
  it the **Clear** button — moved *above* the listbox: a `<button>` inside `role="listbox"` was an
  unallowed child (axe `aria-required-children`, now covered by the a11y gate with recents seeded).
  Empty states: a bordered card with the copy centred, a 36px glyph, an `xps-results__empty-title`
  headline reading `No results for “{query}”`, popular searches as outline pills, and the
  "Try fewer words…" line dropped. `loadMore` renders that same empty state and hides its
  "No more results" control while it shows — under 1024px a fruitless search was a dead end. The
  server-rendered first paint renders the glyph and the same headline, pinned against the client's
  by `card-parity.test.ts`.
- **Fixed (widgets):** the `./widgets` barrel now tree-shakes. `DEFAULT_WIDGETS` references every
  widget as a value, so having it in the barrel dragged all fourteen widgets into any bundle that
  imported one of them (60 KB where 24 KB was asked for); it moved to a chunk of its own and is
  still re-exported from `./widgets`, so nothing changes for callers. The package check gained a
  barrel tree-shake fixture, and the bundler guide now leads with the one-line
  `import { searchBox, results } from '@xperience-community/xperience-search/widgets'` form.
- **Fixed (core):** highlighting a search with typo tolerance on is no longer ~50× the cost of the
  search itself. `HighlightStage` scored every result against the unrewritten query, so the fuzzy
  terms FZ-1 builds were re-expanded once per document and per highlighted field; the query is now
  rewritten against the index reader **once per request**, inside the searcher lease
  (`ExecuteSearchStage`), and carried to the highlighter on `SearchContext.HighlightQuery`. Measured
  by the same `XpSearch.Bench` workload as PF-1 at 10,000 documents: single term with typo tolerance
  and highlighting **135.2 ms → 8.2 ms p50** (204.3 ms → 12.1 ms p95); the exact-query and
  no-highlight rows are unchanged. Snippets are unchanged — a rewritten query holds the concrete
  terms that matched, which is what should be marked. No API and no contract change.
- **Fixed (admin):** the admin's page commands are now guarded against the "command not found" answer
  a click can get. Every command name the admin client sends is asserted against Kentico's own
  `UITree` for the page that has to answer it, for the whole Admin assembly, so a page whose command
  cannot be reached fails a test instead of a click. No command name, payload or permission changed —
  the reported failures were already fixed in source and the discovery rule they were blamed on turned
  out not to exist (ADR-0027 records what Xperience actually does).
- **Added (docs, tests):** the spec §12 performance pass. A new console tool `tests/XpSearch.Bench`
  builds deterministic synthetic corpora of 10k / 100k / 1,000,000 documents and measures the **real
  query pipeline** — the stage chain `AddXpSearch` composes, with light-but-present synonyms, one
  boost rule, non-default field weights, facets on four dimensions and highlighting — writing
  `docs/internal/perf-results-<date>.md` with an environment table, p50/p95 as median-of-runs with
  `[min-max]`, and nearest-rank percentiles. Headline latencies are uncached (a different query text
  per iteration); one cache-hit row is there for contrast. No new dependencies (Stopwatch and maths,
  like the SP-1 spike), and no library code changed. A new guide
  `docs/guides/performance-and-sizing.md` publishes the measured numbers and — the part spec §12 asks
  for — says plainly when a local Lucene index stops being the right answer: one writer, one process,
  no replication, a rebuild window that grows with the corpus, and the index sitting on one disk.
  The run also **measured a real cost that was not previously visible**: with typo tolerance on,
  highlighting dominates a search. Not fixed here, on purpose — see the results doc.
- **Fixed (widgets):** a disposed `SearchClient` no longer keeps retrying a failed request.
  Probes and analytics events carry no abort signal, so a failing count probe from a Filter & Sort
  sheet could still fetch on its retry backoff (up to ~600 ms) after the search instance was
  disposed; `dispose()` now stops the retry loop.
- **Docs:** spec §10.7's second worked extensibility example — a computed relevance field — is built
  for real in the sample project and walked in `docs/guides/indexing-strategy.md`: click counts from
  the query log written onto each document at index time (`AddField` + `ContributeAsync`, computed
  once per mapping scope), published as a `popular` sort key, and fed back into ranking by a
  host-owned `ISearchStage` registered with `AddXpSearchStage`. Opens by pointing at the shipped
  no-code popularity toggle (RK-1) — the example is about the pattern, not about clicks — states the
  staleness ceiling (the value is as fresh as the document), and `popularity-boosts.md` links to it.
  No library code changed.
- **Added (client, widgets):** typed ingestion clients for both languages, so a sync job stops
  hand-rolling HTTP (CL-1). **C#:** a new Kentico-free package `XperienceCommunity.Search.Client`
  (`XpSearchIngestionClient`, BCL only — it is for the console importer or PIM job pushing documents
  *in*, which must not drag Xperience and Lucene along). **Node:** a new npm subpath
  `@xperience-community/xperience-search/ingestion` (`createIngestionClient`), deliberately not
  re-exported from the package root because the API key is a server-side secret. Both speak the
  frozen ingestion contract with verbs named after its endpoints — `upsert`, `patch`, `delete`,
  `deleteMany`, `clear`, `rebuild`, `status`, `listIndexes` — split an upsert under both server caps
  (1000 documents / 10 MB) and `deleteMany` by id count, retry `408`/`429`/`5xx`/transport failures
  with jittered exponential backoff honouring `Retry-After` (never another `4xx`, which would be the
  same failure slower), and report a partial failure two ways: rejected documents in the result's
  `errors`, and a failed batch as a typed exception carrying the Problem Details plus the aggregate
  of everything the earlier batches already wrote. No new dependencies anywhere; the C# DTOs are a
  second emission of the same contract generator.
- **Added (core):** `indexing.AddField(contentTypeName, field)` declares a field auto-detection
  cannot know about, so a value written from `ContributeAsync` is part of the index schema and
  therefore reaches `result.attributes`, the ingestion schema endpoint and the admin attribute
  selectors (IX-1). Until now a contributed field landed in the Lucene document and nowhere else —
  indexed, searchable, and silently never returned — which the indexing guide's own example fell
  into. Declaring is now half of a documented two-step (declare once at startup, write per document),
  and writing an undeclared field logs one warning per field name naming the `AddField` call that
  fixes it. Contributed fields are appended after a content type's detected and flattened fields, so
  a name clash keeps the detected definition, and they are exempt from `Configure`/`Exclude` — you
  authored the definition, so you edit the registration. Registering the same content type and field
  name twice throws at startup.
- **Added (core):** `/suggest` logs one warning per index when document suggestions are served from
  an unconfigured `SuggestField` (IX-1). The default, `title`, is the item display name, which on
  most Kentico sites is the web page item name — a slug with a generated suffix
  (`CoffeePlunger-p2e57tss`). No smarter default is guessed: the warning names the setting and the
  guide section, and the quick start and search API guides now say to set it before shipping.
- **Added (contract, core, widgets, themes):** three additive contract members and the three
  recovery features they carry (SG-1). `Suggestion.group` says where an autocomplete entry came from
  (`"query"`, `"document"`, or `"recent"` for the client-side entries the widgets add themselves —
  the server never sends that one); `SearchResponse.didYouMean` and `SearchResponse.popularSearches`
  are the two ways out of a dead end. All three are omitted when they do not apply, so an existing
  caller sends and reads exactly what it did and the contract version is unchanged.
  **`SuggestMode.Mixed`** answers `/suggest` with query suggestions *and* documents in one list,
  queries first: they take half of `limit` (at least one), documents fill the rest, and either
  source's unused share goes to the other. `group` is emitted in every mode, so the shipped panel
  groups a mixed response without inferring the source from `result`. **Did-you-mean**
  (`Indexes[...].DidYouMean`, **on** by default) spells a zero-hit query against the live index terms
  of the same fields the query searched, then *runs the correction* before offering it — the member
  is present only when that verification found something. The verification search carries
  `probe: true`, so it is never journaled: one dead end still leaves exactly one query-log row.
  **Popular searches** (`Indexes[...].PopularSearchesOnNoResults`, a count, **off** by default
  because it exposes the query log to anonymous visitors) fills `popularSearches` from the same
  popularity count autocomplete uses. Both run only on `total == 0`, never on a probe, at most one
  extra search per request, and the enriched response is what the 60-second cache stores. In the
  widgets, the results empty state renders "Did you mean **espresso**?" and a row of popular-search
  chips, each running its query (`templates.empty` receives `didYouMean` and `popularSearches`; any
  element with `data-xps-recover` runs its query), and the autocomplete panel gains a third group:
  **recent searches**, on by default in both consumers, kept in the visitor's own `localStorage`
  (`xps-recent:<index>`, five entries, never sent anywhere), prefix-filtered, opened on focusing an
  empty field, cleared from the group heading, and turned off with `recentSearches: false` or the new
  **Offer recent searches** checkbox on both Page Builder widgets. Server-rendered empty states stay
  the plain block, like ES-1's. New dependency: `Lucene.Net.Suggest` (`DirectSpellChecker` reads the
  live index, so there is no spell index to build or maintain).

- **Added (contract, core, widgets, themes):** `SearchRequest.probe`, an optional boolean, and the
  counted recovery states it unblocks (ES-1). A probe request is answered exactly like any other —
  same pipeline, same rules, same response cache, so a probe and the search it previews share a
  cache entry — but it is **never journaled**: no search activity, no query-log row, and therefore
  nothing in any report, in the suggestion miner or in the popularity signal. The skip lives at the
  single journal call site in `CachedSearchPipeline`, so it covers the cached and the uncached path
  alike. Additive, like `queryId` before it: an existing caller sends nothing and behaves exactly as
  it did, and the contract version is unchanged. On the JavaScript side `SearchInstance` gains
  `probe(overrides?)`, which runs one such request over the instance's own index and transport and
  resolves with `{ total }` — committed state plus the caller's overrides, no state touched, nothing
  rendered, not debounced. Two widgets use it: the **filtered empty state** now says "There are
  **7 results** without them." with a **Clear filters and show 7 results** button (`templates.empty`
  receives the count as `unfilteredCount`), and the **filter sheet's Apply button** previews the
  pending selection as "Show 12 results" — `applyLabel` regains its `{count}` placeholder and now
  defaults to `'Show {count} results'`. Both debounce 250ms, discard a stale or post-Apply answer,
  and fall back to their countless label on failure or a zero count; a visitor never sees "show 0
  results" or an error. Both empty variants also gain the muted magnifier glyph
  (`xps-results__empty-icon`), and the first-search skeleton's media square is now squared off the
  media width rather than by a coincidence of two lengths. Server-rendered empty states stay the
  plain block: the recovery states are client-side by design.

- **Added (core, admin):** typo tolerance, one per-index toggle on the **Synonyms** page (FZ-1). With
  it on, every free-text term also matches near-spellings on both query paths, the plain one and the
  synonym expansion: 1–2 letters stay exact, 3–5 allow one edit, 6 and up allow two, the first letter
  always has to match, and an all-digit term is never fuzzy. Exact hits still rank first, every query
  position is still required, and `explain` reports `fuzzy:on`. **Off by default** — turning it on
  changes recall and ranking, so it is opt-in like the popularity boost. The setting is stored in the
  new `XpSearch.FuzzyIndex` class (installed automatically), read through `ITypoToleranceSource`
  behind one cache entry per index, and folded into the response cache key, so flipping it never
  serves a stale page. Rule conditions, suggestions and the JSON contract are unchanged.

- **Fixed (themes, widgets):** the range filter is one slider again, as the design has it. Both
  native range inputs are overlaid on the single `xps-range-filter__track` rail instead of stacking
  as two, and the segment between the handles is filled with the accent while the rest stays muted.
  `rangeFilter` sets `--xps-range-from` / `--xps-range-to` (the two ends as percentages of the
  bounds) on the track every render, which is what the default theme paints with — a page composing
  the markup by hand has to set them too. Keyboard, screen-reader and number-input behaviour are
  unchanged: only the thumbs take pointer events, so each handle stays draggable.

- **Added (widgets):** `XpSearch.ActiveFilters` and `XpSearch.ClearFilters`, the Page Builder
  counterparts of the two chips/clear widgets that until now could only be mounted by hand. Active
  filters takes *Screen-reader heading* and *Keep the chips on one scrolling row* (`title`,
  `scroll`); Clear filters takes *Button text* (`label`). Both render the usual `.xps-mount` and a
  static editor preview. `activeFilters` gained the `scroll` param behind that checkbox, so the
  widget itself can set `xps-active-filters--scroll` instead of the page having to.

- **Changed (widgets, themes):** the shipped defaults now *are* the design. `facetList` and
  `categoryTree` are **collapsible by default** — the title is a `<button aria-expanded>` with a
  chevron controlling a new `__body` element (`collapsible: false`, or the *Title folds the group*
  checkbox, opts out); the fold is per-render view state and never touches the URL or the search.
  `clearFilters` reads **"Clear all"** and renders `xps-button xps-button--link`, the new muted-link
  button of the default theme. `resultStats` emphasises the count with
  `<strong class="xps-result-stats__total">` — in the built-in text, which now reads
  `46 results for “espresso” (14 ms)`, and around `{total}` in a `textTemplate`; the Page Builder
  widget's **Text template** property is pre-filled with `{total} results for “{query}” ({tookMs} ms)`.
  `rangeFilter` gained a `unit` option (Page Builder: *Unit*) on the one input row, and its
  `__values` line now states the control's bounds rather than the pending selection.

- **Added (themes):** composition helpers and the mockup polish that goes with them —
  `.xps-toolbar` (stats left, sort right, wrapping) and `.xps-sidebar__header` /
  `.xps-sidebar__title` in shell, `.xps-button--link` in the default theme; the range slider becomes
  a 2px rail with round accent handles, the sort select keeps its native `<select>` but drops the
  platform arrow for an `xps-select__chevron` inside a new `xps-select__field` wrapper (a bare
  `xps-select` still gets the platform arrow), and `.xps-result__snippet` is clamped to three lines.
  New guide section [Composing the results page](docs/guides/widget-reference.md), a walkthrough of
  the whole default with zero overrides, backed by the runnable
  `src/XpSearch.Widgets/Client/demo/results-page.html` the e2e suite boots against the mock server.

- **Added (widgets):** the search box matches the mockup's single field — a decorative magnifier
  (`xps-search-box__icon`, always rendered) sits inside the input at the start, and the loading
  indicator and the reset button sit inside it at the end, so the clear control reads as part of
  the field rather than next to it. New `searchBox({ suggestions: { … } })` param group
  (`debounceMs`, `minQueryLength`, `limit`, `language`, `groupLabels`) turns the widget's own input
  into the autocomplete combobox: `withSuggestions` drives it, the panel is rendered by the module
  the standalone `suggestions` widget now also uses — keyboard-hints footer included, without the
  "See all results" link an in-place search has nowhere to point at — and picking a suggestion
  searches in place
  while `Enter` with no active option still goes through the search box's submit, redirect rules
  included. In the Page Builder: **Suggest as the visitor types** and **Maximum suggestions**,
  emitted as a nested `suggestions` group in `data-xps-config` only when enabled. One page, one
  search field: the standalone `suggestions` widget — unchanged — stays the right choice for a
  landing page or a header that navigates to a results page.

- **Added (widgets):** `filterSort`, the mobile Filter & Sort bottom sheet, and the
  `XpSearch.FilterSort` Page Builder widget that mounts it. A toolbar button with a badge counting
  the active refinements opens a `role="dialog"` sheet holding one section per configured facet
  group plus a "Sort by" pill row — composed from the same facet-list and sort behaviours the
  `facetList` and `sortSelect` widgets use. Selections inside the sheet are pending: **Apply**
  replays them through the public actions in one chain and runs a single search, while the
  backdrop, `Esc` and the close button discard them. Focus moves into the sheet, is trapped, and
  returns to the trigger; the page behind is scroll-locked and the slide-up respects
  `prefers-reduced-motion`. New styles `themes/src/scss/{shell,default}/_filter-sort.scss` and the
  `styles/widgets/filter-sort.css` bundle, new subpath `./widgets/filter-sort`, plus an opt-in
  `xps-active-filters--scroll` modifier for the chips row under the toolbar.

- **Changed (themes):** the default theme ships a Kentico-violet palette instead of the placeholder
  blue — `--xps-color-accent` `#af00fa` (5.00:1 on white), `--xps-color-text` `#1f2430`,
  `--xps-color-muted` `#5c6370`, `--xps-color-border` `#e3e5ea`, and a re-derived dark-mode block
  (accent `#c983f7`, 6.91:1 on `#17161d`). Token names, count and mechanism are unchanged, every
  derived colour is still a `color-mix` of them, and `themes/scripts/check.mjs` now recomputes the
  contrast ratios and proves a single `$color-accent` override leaves no shipped violet behind.
  Along with it: the `<mark>` highlight is a highlighter band rather than a tinted box, facet group
  titles get a rule, facet counts and pagination lose their boxes (current page is accent text with
  an underline), inputs get a subtle inset shadow, and a refining search dims the stale list instead
  of blanking it.

- **Added (widgets, core):** the default result card gained three optional, attribute-driven parts —
  a `xps-result__path` breadcrumb line under the title (client option `pathAttribute`, default
  `path`), a `xps-result__type` class on the content-type meta item, and an inline document glyph
  (`xps-result__icon`) in the media slot for a result with a `fileType` but no `image`. All three
  land identically in the client's `defaultResultItem`, the widgets' `_Result.cshtml` and
  `ServerRenderedResults.DefaultCard`; `SearchResultViewModel` gained `Path` and `FileType`, and
  `Client/src/widgets/card-parity.test.ts` now fails when one of the three renderers moves alone.

- **Added (widgets):** the results empty state knows whether filters are narrowing the search.
  `templates.empty` receives `hasRefinements` and `clearRefinements` beside `query`, and the default
  copy becomes "No results for … with these filters" plus a primary **Clear filters** button. Clicks
  on `xps-results__clear` are delegated by the widget, so a custom template can reuse the class
  without wiring a handler. The suggestions footer gained a decorative, `aria-hidden` keyboard-hint
  cluster (`xps-suggestions__hints` / `__key`), hidden on coarse pointers.

- **Fixed (admin):** an experiment's breadcrumb and sidebar section header showed its GUID instead of
  its name on the experiment detail page and every variant-scoped tuning page. `XpSearchExperimentInfo`
  declared no display name column, so Xperience resolved the object's display name through its code
  name column (absent too) down to the GUID column; the type now names itself by
  `ExperimentDisplayName`. Code-side type metadata over an existing column — no schema change.

- **Added (docs):** a screenshot-led tour of the administration surface,
  [Administration UI tour](docs/guides/admin-ui-tour.md) — how the per-index tuning pages are grafted
  onto the Lucene integration's index listing (**Lucene Search → List of registered Lucene indices →
  a row → the *Edit index (xps)* sidebar**), one section per page (Settings, Rules and the rule
  builder, Suggestions, Synonyms and Synonym suggestions, Stopwords, Field weights and the
  popularity toggle, Query tester, Analytics, Experiments with its variant-scoped copies of the four
  tuning listings, Status) and the two pages of the **Search ingestion** application (API keys,
  Ingestion log). The same captures are now embedded in the admin sections of the analytics,
  relevance-tuning, popularity-boosts, experiments and ingestion guides. Images live in
  `docs/guides/images/` and are referenced relatively, so the pages render both in the repository and
  in the GitHub wiki; every capture is scripted in `tools/screenshots/` (`npm run capture
  [-- shotName]`, routes and interaction steps in `routes.json`) and listed with its route, data
  prerequisites and staleness triggers in `docs/internal/screenshot-manifest.md`.

- **Added (core):** server-rendered first paint is a `XpSearch.Core` capability, not a Page Builder one.
  `AddXpSearch()` registers `ServerRenderedResults` and the result template registry, so a host that
  builds its search UI in plain JavaScript against the npm package can inject `ServerRenderedResults`
  into its own Razor page, render the visitor's first search into its own element and hand the
  returned `QueryId` to `createSearch` (`initialQueryId`, unchanged since it shipped). Without a
  result partial, Core emits a built-in card whose markup matches the client's default item template
  (`themes/MARKUP.md`); the Results widget still renders through the `_Result.cshtml` partial of
  `XpSearch.Widgets`, which it now passes as `ServerResultsOptions.DefaultViewPath`. New guide:
  [Server rendering and the mount contract](docs/guides/server-rendering.md).

- **Added (docs):** the widget **mount contract** — `class="xps-mount"`, `data-xps-widget`,
  `data-xps-instance`, `data-xps-instance-config`, `data-xps-config` — is documented as **stable**: a
  Page Builder widget mount hydrates from either the tag-helper bundle or a host's own bundle
  importing `@xperience-community/xperience-search`. From this release on, a change to the mount
  markup or to any config key is a breaking change and is recorded here as one.

- **Breaking (core, widgets):** the server-rendering primitives moved from `XpSearch.Widgets` to
  `XpSearch.Core`, namespace `XpSearch.Core.Rendering`. Type and member names are unchanged; update
  the `using` (there are no type forwarders). `AddXpSearchWidgets()` no longer registers these
  services itself — call it next to `AddXpSearch()`, as it always documented.

  | Type | Was | Now |
  |---|---|---|
  | `SearchQueryState` | `XpSearch.Widgets.Rendering` | `XpSearch.Core.Rendering` |
  | `ServerRenderedResults` | `XpSearch.Widgets.Rendering` | `XpSearch.Core.Rendering` |
  | `ServerResultsOptions` | `XpSearch.Widgets.Rendering` | `XpSearch.Core.Rendering` |
  | `ServerResultsRender` | `XpSearch.Widgets.Rendering` | `XpSearch.Core.Rendering` |
  | `ISearchResultTemplateRegistry` | `XpSearch.Widgets.Templates` | `XpSearch.Core.Rendering` |
  | `SearchResultTemplateRegistry` | `XpSearch.Widgets.Templates` | `XpSearch.Core.Rendering` |
  | `RegisterSearchResultTemplateAttribute` | `XpSearch.Widgets.Templates` | `XpSearch.Core.Rendering` |
  | `SearchResultTemplate` | `XpSearch.Widgets.Templates` | `XpSearch.Core.Rendering` |
  | `SearchResultViewModel` | `XpSearch.Widgets.Templates` | `XpSearch.Core.Rendering` |

  `ServerResultsOptions` also takes a new optional last parameter, `DefaultViewPath`.

- **Added (js):** per-widget subpath exports —
  `@xperience-community/xperience-search/widgets/<kebab-name>` for each of the twelve widget modules
  (`clearFilters` ships with `active-filters`), plus `.../widgets` for the barrel. Every subpath
  resolves for `import` and for `types`; `npm run build` fails if a target in the `exports` map is
  missing from the tarball.

- **Added (js):** explicit widget registration for bundler consumers.
  `mountAll(root, { widgets })` takes the widget factories a page bundled, and `bootstrap.ts` no
  longer imports the widget implementations, so importing `createSearch` plus one widget no longer
  drags in all thirteen (a bundler pass in `npm run package:check` asserts it: 15.9 KB instead of
  45.4 KB for `createSearch` + `searchBox`). `registerWidgetType` still overrides a widget of the
  same name. **The UMD bundle is unchanged** — it registers all first-party widgets on load and
  auto-mounts as before, so the `<xps-search-assets />` path is unaffected. `getWidgetType(id)` now
  answers only for what was registered, which under the UMD bundle is still every built-in.

- **Added (js, themes):** the SCSS sources ship in the package. `scss/shell`, `scss/default`,
  `scss/base` and `scss/widgets/<kebab-name>` are exports, so a page can `@use` the base once and
  then only the widgets it mounted, and `@use … with (…)` sets the default values of the `--xps-*`
  custom properties at build time. The custom properties themselves are emitted under the same
  names, so runtime theming is unchanged. For pipelines without sass the same layer ships compiled
  as `styles/base.css` and `styles/widgets/<kebab-name>.css`. `themes/shell.css` and
  `themes/default.css` keep their paths and their rules — the build fails if they stop matching the
  CSS the NuGet package serves.

- **Added (docs):** [JavaScript bundler setup](docs/guides/javascript-bundler-setup.md) — npm-first
  install, Vite example, SCSS configuration, Page Builder mounts from a bundle, and the version
  pairing table (the npm package version must match the installed `XperienceCommunity.Search.Core`).
  The tag helper is now documented as the quick start for a site with no build pipeline, with the
  rule that one page runs one runtime, never both.

- **Fixed (admin):** in the rule builder's action side panel, **Add row** in the attribute + value
  editor did nothing, and picking an attribute erased the row again (Filter results, and the
  "matching" variant of Boost and Bury). The panel kept its rows only as the stored
  `attribute:value` expression, which by design cannot hold a blank or half-filled row, so every
  unfinished row was thrown away as it was typed. The panel now keeps the rows it is editing and
  composes the expression from them. The condition panel's Filters rows were unaffected.

- **Fixed (admin):** the Analytics chart's **Show the numbers** table now pages like the report tables
  below it (same pager, same *Rows per page* selector) instead of listing every day of the range in
  one 90-row table.

- **Fixed (core, admin):** the field drop-downs built from an index schema — **Field weights**' Field,
  the facet and range-filter attribute properties of the widgets, and the rule builder's attribute
  picker — list the fields alphabetically (case-insensitive) instead of in index-discovery order.

- **Changed (widgets):** the Results widget's **Fields to show**, **Title attribute** and **Link
  attribute** are selectors filled from the index schema instead of free-text fields — an editor picks
  stored field names rather than remembering them. *Snippet attributes* stays a text area, because the
  order of its values is what decides which one wins. Widgets saved before this keep rendering
  unchanged: the old newline-separated field list is still read when the new selector is empty.

- **Fixed (widgets, core):** a page whose Results widget renders its first paint on the server no
  longer counts as two searches. The widget emits the `queryId` of the search it rendered as
  `initialQueryId` in `data-xps-instance-config`, the client sends it with its first query only, and
  the journal ignores a `queryId` it has already recorded — one query log row and one search activity
  per page load, with clicks attributed to it. The widget also emits the page size that search
  actually used, so the hydration query asks for the same page the visitor is already looking at
  instead of falling back to a different default (HW-11 saw a 6-card first paint replaced by 12).

- **Fixed (core, admin):** the `XpSearch.PopularitySignal` scheduled task failed with *Cannot insert
  the value NULL into column 'PopularityIndexEnabled'* the first time it ran for an index. A Kentico
  Info object only writes the fields that were set, so the settings row it creates now sets the
  opt-in explicitly (off). The same omission is fixed at two more creation sites: approving a
  popularity suggestion left `RuleMigrated` unset, and a query log row left `LogGuid` unset.

- **Added (widgets):** two **Page Builder personalization condition types**, so a marketer can
  personalize *any* widget by search behaviour. **Search - searched for** shows a variant to visitors
  whose logged search text contained a term within the last N days (default 30) — consent-gated like
  the activities themselves, and therefore false for crawlers, which keeps the original content
  indexable. **Search - A/B bucket** puts a visitor in bucket A or B of a named percentage split,
  reusing the experiments' `xpsearch_bucket` cookie: sticky per visitor, works without tracking
  consent, and two widgets given the same split name flip together, which turns ordinary widget
  variants into one page-level A/B test measured through Analytics. Deliberately no experiment entity
  and no report — for search-tuning A/B use Experiments. Requires the Xperience **Advanced** license
  tier (a platform gate on widget personalization). New guide:
  `docs/guides/search-personalization.md`.

- **Added (core, admin):** **popularity boosts** — the results your visitors click can raise their own
  ranking, bounded and opt-in (ADR-0025). A new scheduled task (`XpSearch.PopularitySignal`) aggregates
  the last 30 days of query log clicks into a per-document signal per index, weighting each click by
  `log2(position + 1)` so a click eight rows down counts for more than a click on the result the
  ranking already put first. A pipeline stage after the rules (order 750) applies it as a bounded
  boost: 2x for the most clicked document, scaling linearly down to no change, and nothing at all for
  an index that has not opted in or has no evidence yet. The opt-in is per index, on the **Field
  weights** page header, and is deliberately not part of an experiment's variant tuning — both
  variants see the same boost. While the boost is on, the signal version joins the response cache key,
  so a task run invalidates exactly the responses it changed and nothing else. New guide:
  `docs/guides/popularity-boosts.md`.

- **Added (admin):** **suggested boost rules**. The same task lists the frequent queries where one
  document clearly wins the clicks (at least 5 clicks and at least 50 % of the query's click weight)
  on a **Suggestions** page next to *Rules*, which links to it when any are waiting. **Approve**
  creates an ordinary rule — query condition plus a 2x boost — that you can then edit or delete like
  any other; **Dismiss** turns it down. Either answer is remembered, so a recomputation never brings
  the same query and document back. No rule is ever created without a person approving it.

- **Added (core, admin):** **mined synonyms** (ADR-0026). The same task run also reads the window in
  timestamp order and looks for reformulations — a search that got no click, followed within 60
  seconds by a different search that did — and lists the repeated ones on a **Synonym suggestions**
  page next to *Synonyms*, which links to it when any are waiting. **Approve** creates an ordinary
  two-way synonym group you can edit or delete like any other; **Dismiss** turns it down, and either
  answer is remembered for good. Pairs where one text contains the other (`coff` → `coffee`,
  `sofa` → `red sofa`) are never suggested, and a pair has to occur 3 times before it surfaces. The
  query log still holds no visitor or session identifier: the pairing is by timing, which is why the
  occurrence threshold and the human approval both exist. Configurable through
  `Analytics.SynonymWindowSeconds` and `Analytics.SynonymMinimumOccurrences`.

- **Added (core) — storage:** the query log gains `LogClickedResultID`, the result id of the clicked
  document, written by the click event that already recorded the position. It is nullable, so an
  upgraded installation needs no backfill, and it is as anonymous as the rest of the row. Four module
  classes join the analytics module: `XpSearch.PopularityIndex`, `XpSearch.PopularityScore`,
  `XpSearch.PopularitySuggestion` and `XpSearch.SynonymSuggestion`.

- **Breaking (core)** — source-breaking for custom implementations: `IQueryLogStore.SetClickedPositionAsync`
  is now `SetClickAsync(queryId, position, resultId, cancellationToken)`, `CachedSearchPipeline`
  takes an `IPopularitySignalStore`, and `XpSearchPopularityTask` takes an `ISynonymSuggestionStore`. Hosts that only call `AddXpSearch()` are unaffected; the JSON
  contract is unchanged.

- **Fixed (widgets, js) — behaviour change:** URL routing no longer treats every query parameter as a
  filter. A page carrying a foreign parameter — Kentico's own `uh` preview parameter, `utm_*`, `gclid` —
  made every search fail with `400 'uh' is not an attribute of index '…'`. The client now adopts a facet
  param only when a widget on the page declares that attribute (`facetList`, `categoryTree`,
  `toggleFilter`), and a numeric one only for a `rangeFilter`; everything else is ignored and left
  untouched in the URL. The server-side mapping behind the results widget's first paint restricts
  itself the same way, to the attributes the index's schema actually has. **This changes what a shared
  URL does:** a filter for an attribute that no widget on the target page declares is now ignored,
  where before it applied invisibly. A custom `routing: { routeToState }` is unaffected and remains the
  way to adopt everything. A custom widget that filters on an attribute makes it routable by declaring
  `$$routable: { attribute, kind }`. Widgets added after `start()` are not routable — the URL is read
  once, when the instance starts.

- **Added (core, admin):** an index's relevance tuning can be **A/B tested** against real traffic
  (ADR-0024). An experiment holds a traffic split and a variant-B draft of the index's rules, synonyms,
  weights and stopwords, cloned from live when it is created; one experiment per index at a time, and
  a draft can never leak into live results. Visitors are bucketed stickily by a first-party
  `xpsearch_bucket` cookie, registered at the **Essential** cookie level, so anonymous visitors who
  have not consented to tracking are tested too. Variant B swaps the tuning the pipeline reads, joins
  the response cache key while the experiment runs, and stamps the query log with the experiment and
  variant — which splits every existing metric (CTR, zero-result rate, clicked position, volume) by
  variant. The JSON contract and `SearchResponse` are unchanged; the JS client knows nothing about it.
  Concluding an experiment either promotes variant B to live or discards it, and applies without a
  restart.

- **Added (admin):** the **Experiments** page of that A/B testing, in the index tuning sidebar after
  *Analytics*. Create an experiment (name + traffic split), edit its variant B in the **same** Rules,
  Synonyms, Field weights and Stopwords editors the live tuning uses — reached inside the experiment,
  with a *Variant B draft — <experiment>* banner on every page — then start it and read the live
  comparison: searches, zero-result rate, click-through rate and average clicked position per variant,
  with the sample sizes shown next to them. The report states observed rates only: **no p-values, no
  winner badge, no significance claim**, because two rates over a few hundred searches usually differ
  by chance. Ending the experiment is **Promote B to live** or **Discard B**, each behind a
  confirmation that spells out what happens to the tuning rows; both apply immediately. A started
  experiment's variant B is read-only, and a save or delete aimed at the other variant's rows is
  refused. The **Query tester** grows a **Variant** select while the index has an unfinished
  experiment, so variant B can be tried before any visitor is bucketed. See the new
  [Experiments guide](docs/guides/experiments.md).

- **Added (widgets):** the **Search - Results** widget renders the visitor's first page of results on
  the server, inside its own mount element. A shared result URL (`?q=espresso&page=2&tags=coffee`) now
  arrives with its results in the HTML — read with the same parameter mapping the client writes — so the
  first paint needs no JavaScript and a visitor without it still sees results. The search runs through
  the same pipeline as `/api/xpsearch/query`, so rules, personalization and analytics apply; a failing
  search logs a warning and leaves the mount empty rather than breaking the page. The client replaces
  the block on its first render and owns every render after that. This also makes the **Result
  template** property do something: the selected `RegisterSearchResultTemplate` view renders the first
  paint, scoped by its content types, with the built-in card for everything else. A Razor template does
  not apply to client re-renders — `templates.item` does. Nothing renders server-side inside the Page
  Builder, where the widget still shows its static preview.

- **Added (widgets):** the **Search - Results** widget has **Title attribute**, **Link attribute** and
  **Snippet attributes** properties, which point the default card at fields other than `title`, `url`
  and `summary`/`content`/`excerpt`. They are emitted as `titleAttribute`, `urlAttribute` and
  `snippetAttributes` in `data-xps-config` — the client already accepted all three, no widget ever sent
  them — and apply to the server-rendered first paint too. If **Fields to show** is restricted, it has
  to include whatever these name, or the cards render empty.

- **Added (widgets) — behaviour change:** the **Search - Search box** widget has a **Sync search state
  to the URL** property, on by default, which emits `"routing":true` into `data-xps-instance-config`.
  The query, filters, sort and page then live in the address bar: result pages are shareable and the
  back button walks the search back. **This changes existing pages**: a search box saved before this
  release deserializes the missing property to its default, so searches composed in the Page Builder
  start syncing to the URL without being re-saved — which is the point, the JavaScript client has
  supported routing all along and no widget ever asked for it. Untick the property to keep a search's
  state in memory. The value is emitted either way (`"routing":false` when unticked). At most one
  search instance per page may sync: the parameters are not namespaced per instance ID, so two syncing
  searches overwrite each other.

- **Fixed (admin):** **Delete** now works on all five listings — field weights, synonyms, stopwords,
  rules and API keys. Each listing offered the action but none registered a `Delete` page command
  behind it, so every click failed with *command not found*. Deleting a tuning row needs *Delete* on
  **Lucene Search**, an API key *Delete* on **Search ingestion**. A delete request naming a row of
  another index — the row action only carries a row id — is refused with *This record belongs to a
  different search index and was not deleted*, matching how editing such a row is already refused.

- **Changed (admin):** an action row is reordered by dragging its **grip** — the six dots at its left
  edge — instead of the **Move up** / **Move down** buttons, which are gone. The grip is not
  mouse-only: `Space` or `Enter` lifts the row, `↑`/`↓` move it, `Space`/`Enter` drop it and `Esc`
  puts it back, with every step announced through a live region and focus staying on the grip. Touch
  reorder is not supported — the browser drag events most touch browsers do not raise are what the
  pointer path is built on. Nothing about the storage changed.

- **Changed (admin):** a rule's actions are edited in a side panel, and its items and attribute
  values are picked instead of typed. Each action is a numbered read-only row — `1 · Pin an item ·
  Pin Hario Skerton Plus to position 1` — with a reorder grip, **Edit** and **Remove**; the order is
  behaviour (rewrites chain, custom data merges in order). **Add action** keeps the same ten-type menu and opens the panel on a
  blank action of the chosen type; an action discarded before anything is filled in is not added.
  **Pin**, **Hide**, **Boost** and **Bury** get an item picker that searches the index as you stop
  typing (`↑`/`↓` walk the results, `Enter` picks, the stored result id is behind **Details**).
  **Filter results**, the matching halves of **Boost** and **Bury**, and a condition's **Filters**
  rows all get one attribute+value row set: **Attribute** from the index's facetable fields,
  **Value** from a live facet query with document counts, and **Edit as text** for the raw
  expression. **Nothing about the storage changed** — the same `FilterExpression` strings and
  `AttributeIs` lists, so no migration and no rule needs re-saving.

- **Added (admin):** an action pointing at an item the index no longer holds is marked *no longer in
  the index* and keeps its stored id, instead of showing a bare id or being silently dropped.

- **Added:** `RuleFilterExpression.Compose`, the inverse of `Parse`, and
  `IIndexDocumentLookup`/`IndexDocumentLookup` in `XpSearch.Core.Search`, which resolves stored result
  ids into their title and URL. Both are public: a custom admin screen holding rule data can use them.
  A submitted filter expression is now stored in the canonical form `Compose(Parse(x))` gives —
  spacing settled, half-filled pairs dropped — whichever editor wrote it.

- **Changed (admin, minor):** the rule listing reads a bury by expression as `Bury Category:tea`
  rather than `Bury `, matching how it already reads a boost by one.

- **Changed — BREAKING for custom `IRelevanceTuningSource` implementations and for anything reading
  `RuleConsequences`:** a rule's `then` is called an **action** everywhere, not a *consequence*.
  `RuleConsequence` is now `RuleAction` (with the same ten nested records), `TuningRule.Consequences`
  is `TuningRule.Actions`, `RuleJson.ReadConsequences` is `RuleJson.ReadActions`, and the admin
  template props, validation field names (`action:{index}`) and UI labels (**Action(s)**, **Add
  action**) follow. **The stored JSON is unchanged**: the same array, the same `type` discriminators
  (`pin`, `hide`, `boost`, …) and the same member names — only the column it lives in was renamed,
  `RuleConsequences` → `RuleActions`. Upgrading is automatic: `RuleStorageMigration` copies a row's
  `RuleConsequences` verbatim into an empty `RuleActions` on the same start-up pass that converts the
  flat columns, then retires the old column with them. The pass stays idempotent and crash-safe — the
  marker is still the row itself — and `InfoRelevanceTuningSource.Read` falls back to the old column
  for a row inserted by a script after start-up. Conditions are untouched, including the
  `RuleConditions` column.

- **Changed (admin):** the **Field** input of a field weight is a drop-down of the scoped index's
  searchable fields instead of a text box. A stored weight naming a field the index no longer has
  keeps that value as an option, so opening the row and saving it cannot silently retarget the weight.

- **Changed (admin):** the *Show the numbers* collapse under the analytics **Searches over time**
  chart renders the per-day figures in the platform's stock table.

- **Added:** the admin client module compiles Sass - `*.module.scss` (scoped) and `*.scss` (global) alongside the existing CSS support.

- **Changed (themes, no visual change):** `shell.css` and `default.css` are now **authored in Sass**
  — `themes/src/scss/shell.scss` and `themes/src/scss/default.scss`. The shipped files stay exactly
  where they were, `themes/src/shell.css` and `themes/src/default.css`, compiled (`npm run build` in
  `themes/`, dart-sass, expanded, no source maps) and committed, so the RCL's static web assets, the
  npm tarball's `themes/*.css` exports, the class contract and every `--xps-*` variable are
  unchanged. `npm run check` gained a drift check: it recompiles the Sass to a temporary folder and
  fails if the committed CSS no longer matches. Every rule of the hand-written files carried over
  identically (proven by a normalised rule-by-rule diff); only formatting moved. Theming your own
  site still needs no build step and no Sass — the Sass is our authoring convenience, `themes/` now
  needs `npm install` before its scripts run. See
  [Theming → Working on the stylesheets](docs/guides/theming.md#working-on-the-stylesheets).

- **Added (admin):** the four tables of the **analytics dashboard** are **paged**. The **Rows**
  control is now **Rows per page** — 10, 25 (the default), 50 or 100 — it applies immediately without
  a reload, and a table with more rows than one page grows a pager underneath it (*Page 2 of 4 · 87
  rows*, **Previous page** / **Next page**, both real keyboard buttons with real accessible names, the
  count announced politely). Paging happens in the browser over the rows the report returned, so
  turning a page issues no request; a fresh **Load** or a new page size sends every table back to page
  one. The server no longer sizes a report to the row count the client asked for: each report is
  filled to `AnalyticsReportDto.MaxReportRows` (200) instead, which is what a visitor can page
  through. `AnalyticsRequest.Limit` and `AnalyticsDashboardPage.MaxLimit` are **removed** — the row
  count is a client-side concern now.

- **Changed (branding) — BREAKING for package ids, package names and admin URLs:** the placeholder
  `YourCo` branding is gone, and the .NET packages have settled on **one** scheme
  ([ADR-0023](docs/adr/0023-branding.md), [spec amendment](docs/spec/amendments/2026-08-25-branding.md)).
  The NuGet ids are now `XperienceCommunity.Search.Core`, `XperienceCommunity.Search.Ingestion`,
  `XperienceCommunity.Search.Widgets` and `XperienceCommunity.Search.Admin`. Note this is the
  **second** rename in this unreleased entry: `Core`, `Ingestion` and `Widgets` went
  `YourCo.Xperience.Search.*` → `xperience-community.Xperience.Search.*` → `XperienceCommunity.Search.*`,
  while `Admin` went straight to `XperienceCommunity.Search.Admin` and never moved again. Nothing was
  published under either earlier name, so no one has an upgrade to perform — but the interim id is in
  the repository's history and may be in your working tree.
  The npm packages are renamed too: `@yourco/xperience-search` → **`@xperience-community/xperience-search`**
  and the private themes package `@yourco/xperience-search-themes` →
  `@xperience-community/xperience-search-themes`. Every entry point is unchanged, so only the
  specifier in your `package.json` and your imports move. `npm pack` now produces
  `xperience-community-xperience-search-0.1.0.tgz`.
  The widgets' static web assets move with the package id, to
  `/_content/XperienceCommunity.Search.Widgets/xpsearch/{shell.css,default.css,xpsearch.umd.js}` —
  use the `XpSearchAssets` constants or `<xps-search-assets />` and the path is never typed by hand.
  A `packageSourceMapping` for a local feed is one glob again, `XperienceCommunity.Search.*`.
  The admin client module is now organization `xperience-community`, project `xperience-search`, so
  the client templates are `@xperience-community/xperience-search/<Name>`, and a `Proxy`-mode host
  keys `CMSAdminClientModuleSettings` on `xperience-community-xperience-search` (port 3010). The
  per-index section's slug changed from `tuning` to `edit` and its name from *Tuning* to *Edit index*:
  every page moves from `/admin/lucene/indexes/tuning/{id}/…` to `/admin/lucene/indexes/edit/{id}/…`.
  **Update your bookmarks**, and any link into the administration interface from your own tooling.
  The UMD global (`xpsearch`), the assembly and namespace prefix (`XpSearch.*`), the widget
  identifiers (`XpSearch.SearchBox` and friends) and the ingestion application's slug
  (`xpsearch-tuning`) are unchanged.
- **Changed (relevance tuning) — BREAKING for the rule storage schema:** a rule's `if` and `then` are
  now stored as two JSON columns on `XpSearch_Rule`, `RuleConditions` and `RuleConsequences`, and the
  nine flat columns of [ADR-0014](docs/adr/0014-relevance-tuning.md) are **retired** —
  `RuleConditionType`, `RulePattern`, `RuleConsequenceType`, `RuleTargetObjectID`,
  `RuleTargetPosition`, `RuleBoostValue`, `RuleFilterExpression`, `RuleRedirectUrl` and
  `RuleContactGroup` are gone from the class and from the table, and so are the matching properties
  on `XpSearchRuleInfo`. **The migration is automatic and lossless**: every existing rule is converted
  in place the first time the application starts, through the same mapper the CR-4a shim used, so no
  rule changes meaning — including the two edges of the old model (*is anything at all* still fires on
  every query; a blank pattern under any other operator comes back disabled, as dead as it already
  was). It needs no flag to be safe: an empty `RuleConditions` column *is* the "not converted yet"
  marker, which makes the pass idempotent and safe to interrupt, and the flat columns are only dropped
  once nothing is left to convert. Anything reading the flat columns directly — a report, a custom
  `IRelevanceTuningSource`, an import script — reads the JSON now; the shape is specified in the
  [ADR-0022 addendum](docs/adr/0022-if-then-rule-engine.md#addendum--storage-and-migration-unit-cr-4b-2026-08-24)
  and in the guide's [storage appendix](docs/guides/relevance-tuning.md#appendix-how-a-rule-is-stored).
  `TuningRuleCompat`, `FlatCondition` and `FlatConsequence` are **removed from `XpSearch.Core`**; the
  mapper lives on as `XpSearch.Admin.Persistence.RuleStorageMigration.FromFlat` with
  `LegacyCondition`/`LegacyConsequence`.

- **Added (admin):** the **rule builder** — one screen that finally reaches the whole if/then model
  ([ADR-0022](docs/adr/0022-if-then-rule-engine.md)), replacing the single-condition,
  single-consequence form at the same URLs. Conditions are read-only summary rows edited in a
  right-hand **side panel** (Query / Filters / Context switches; *Apply* is local, `Esc` discards,
  nothing persists until *Save rule*); consequences are cards added from a menu of all ten kinds,
  including the five that had no editor before — **hide an item**, **remove a word**, **replace a
  word**, **replace the query** and **return custom data** (a JSON object, validated on save). Save is
  refused, with the message on the field that has to change, when a rule has no condition at all, when
  the Query switch is on with no words, when a pin has no item or a position below 1, when custom data
  is not a JSON object, and so on. The Rules listing swaps *Words to look for* for a **Conditions**
  summary column and keeps **Contact group** (*Everyone* when unscoped). A converted rule shows a
  one-time note saying so. See [Relevance tuning](docs/guides/relevance-tuning.md#the-rule-builder-region-by-region).

- **Fixed (admin):** the **Create rule** link on an analytics zero-result row rendered the analytics
  dashboard instead of the seeded rule form (HW-10 defect 3). A UI page renders inside the nearest
  `RoutingContentPlaceholder` of *its parent's* client template, and the dashboard's custom React
  template renders none, so the child page had nowhere to appear; the seeded page now hangs under the
  Rules listing, whose LISTING template does provide one. It seeds the new builder with a
  *query contains …* condition and no consequences.

- **Fixed (admin, accessibility):** the **Create rule** buttons on the analytics zero-result table
  announced only as "button" (HW-10 defect 5). The stock `ActionCell` renders icon-only buttons and
  the components library falls back to that literal string when a button has no label, so the cell is
  now a component cell whose button reads *Create rule for {query}*.

- **Changed (relevance tuning) — BREAKING for custom `IRelevanceTuningSource` implementations:** a
  relevance rule is now **if this, then that** — a list of conditions that must all hold and a list of
  consequences applied in order ([ADR-0022](docs/adr/0022-if-then-rule-engine.md)). `TuningRule` is
  reshaped to `(Id, Name, Enabled, Priority, ValidFrom, ValidTo, Conditions, Consequences)`, with
  `RuleConditions(Query, Filters, ContactGroup, Language)` and one record per consequence (`Pin`,
  `Hide`, `Boost`, `Bury`, `FilterResults`, `RemoveWord`, `ReplaceWord`, `ReplaceQuery`, `Redirect`,
  `CustomData`); the `RuleCondition` and `RuleConsequence` **enums are gone**, and
  `SynonymExpansionStage` no longer takes a `TimeProvider`. A source that reads flat columns can keep
  them: `XpSearch.Admin.Persistence.RuleStorageMigration.FromFlat` maps a flat row onto the new model,
  and is exactly what the storage migration below uses, so **no stored rule changes meaning**. New in the model: conditions can require a selected facet value (`attribute is
  value`) or a language, can be combined, and can be matched against the **analyzed** query so plurals,
  stems and synonyms count — with no typo tolerance in either mode. New consequences: **hide** a result
  (out of every page and out of the total, unlike bury, which only drops it from the page that came
  back), **remove/replace a word** and **search for something else**, and **return custom data**. A rule
  with no conditions at all never fires. Query rewrites run in a new pipeline stage,
  `SearchStageOrder.QueryRewrite` (175), before synonym expansion, so the rewritten wording is what the
  search, the facet counts and the snippets follow — while the search activity and the query log keep
  recording what the visitor typed. See
  [Relevance tuning](docs/guides/relevance-tuning.md#what-a-rule-is-if-this-then-that).

- **Added (contract):** `SearchResponse.ruleData`, an optional open object carrying the data
  matching rules attached (the *Return custom data* consequence), shallow-merged in rule order so a
  later rule wins a key, and **absent** when no rule returned any. Additive and optional — a minor
  version, `X-XpSearch-Api-Version` stays at `1`. Algolia's `userData` maps onto it
  (`contract/algolia-map.json`). The JavaScript client exposes it as `results.ruleData` wherever
  results are handed to a widget; see
  [JavaScript client](docs/guides/js-client.md#data-attached-by-a-rule) and the
  [spec amendment](docs/spec/amendments/2026-08-24-rule-data.md).

- **Fixed (analytics):** searches were invisible to the analytics as soon as the response cache
  answered them, and every `xpsearch_click` activity carried an **empty value** instead of the query
  the visitor had searched for. Activity logging, the query log row and the `queryId` → query mapping
  used to be the last pipeline stage, which a cache hit never reaches, and which in any case wrote the
  *inner* `queryId` — never the re-issued one the caller receives. They now live in
  `ISearchRequestJournal`, called once per request by the caching decorator, so each search — hit or
  miss — produces exactly one `xpsearch_query`/`xpsearch_noresults` activity and one query log row
  under the `queryId` the caller got back, and a click after a cached search resolves its query text
  again. **Breaking for a consumer who registered or replaced `LogActivityStage`:** the stage is
  removed (slot `SearchStageOrder.LogActivity` stays declared and is now free); replace
  `ISearchRequestJournal` instead. The processing time on a query log row is now the decorator's own
  elapsed time — near zero for a cache hit, which is what a hit costs — and the journal records the normalized query text (the text the cache key is built from) rather than the
  post-stopword one. Query tester runs are unaffected: they bypass the decorator and still write
  nothing to the analytics.

- **Changed (Page Builder):** a configured search widget now renders a **static server-side preview**
  of itself in the Page Builder (edit and read-only mode) instead of the bare `.xps-mount` div, which
  looked like an empty shell to editors. The preview mirrors the widget's live markup with disabled
  controls, no links and `xps-skeleton` bars for result data, under a badge naming the widget and
  saying the content is not live, and it reflects the widget's own properties (placeholder, label and
  attribute, page size and template, pagination style, sort options, range bounds). Extends the
  unconfigured-instruction-block precedent of spec §7.5 to the configured state. **Preview and the
  live site are unchanged** — both still render the mount element and the working widget. Custom
  widgets get a labelled default preview from `XpSearchMountWidgetViewComponent<T>` and can own it by
  overriding the new `BuildEditorPreview(properties)`; `XpSearchMountViewModel` gains `Preview`, and
  `shell.css` / `default.css` gain the `xps-editor-preview` block (`themes/MARKUP.md`). See
  [What editors see in the Page Builder](docs/guides/page-builder-widgets.md#what-editors-see-in-the-page-builder).

- **Added (contact groups):** three search conditions are installed on start and appear in the
  contact group condition picker under **Web activity** — *Contact has searched for text containing
  {text}*, *Contact has searched without results for text containing {text}* and *Contact has clicked
  a search result after searching for text containing {text}*. The `{text}` parameter is an optional
  case-insensitive *contains* match against the searched text of the contact's `xpsearch_query`,
  `xpsearch_noresults` and `xpsearch_click` activities, so a marketer can finally segment on **what**
  a visitor searched for, not only that they searched. They are ordinary `cms.macrorule` rows
  (`XpSearch.Core/ContactGroups/`); the installer never overwrites the *Enabled* flag of a rule that
  already exists, so hiding one survives a restart. This supersedes the AN-2 note that the platform
  had no seam for this — see the ADR-0015 addendum (AN-3) for the mechanism and its risks. Groups are
  recalculated by evaluating the macro per contact (no SQL translation, see KNOWN-LIMITATIONS), and
  the conditions carry no *in the last X days* parameter.

- **Added (personalisation):** a relevance rule can be scoped to a **contact group**, so the same
  query ranks differently for a segment (boost, pin, bury, filter and redirect all honour it). The
  rule form gains an object selector over `om.contactgroup` right after **Enabled**, the Rules
  listing a **Contact group** column showing *Everyone* for an unscoped rule, and the **Query
  tester** a **Contact group** drop-down that simulates a group so an admin can see the effect
  without being a member. The visitor's groups are resolved once per request by a new pipeline stage
  (`SearchStageOrder.ResolveContactGroups`, 150) behind the same consent gate the activity logger
  uses — cookie level *Visitor* or higher, `ICurrentContactProvider.GetExistingContact`, never
  creating a contact — and no consent means no group-scoped rule applies. A group-scoped rule shows
  as `rule:<name> (contact group <code name>)` in `ranking.boosts`. See ADR-0021 and
  [Personalise rules by contact group](docs/guides/relevance-tuning.md#personalise-rules-by-contact-group).
- **Changed (schema):** `XpSearch.Rule` gains a nullable `RuleContactGroup` column (text, 100). The
  tuning module installer adds it to an existing installation on the next application start, merging
  it into the class without touching existing rows; an empty value means "everyone", so every rule
  that exists today keeps behaving exactly as it does.
- **Changed (breaking, extenders):** the `TuningRule` record gains a trailing `ContactGroup` member.
  Custom `IRelevanceTuningSource` implementations and any code constructing `TuningRule`
  positionally must pass it; `string.Empty` reproduces today's behaviour. `CachedSearchPipeline` also
  takes an `IContactGroupResolver`, and the response cache key now includes the visitor's contact
  groups so a personalised response is never served to a visitor in different groups.
- **Changed (breaking, activities):** `xpsearch_click` and `xpsearch_conversion` now log the
  **searched text** as their `ActivityValue`, the same as `xpsearch_query` and `xpsearch_noresults`,
  instead of the pipe-joined `query | resultId | position` / `query | resultId`. The result id moved
  to `CustomActivityData.ActivityComment` and the one-based click position to `ActivityItemDetailID`
  (`0` when there is none). The value is the only field a contact group condition can reach, so it now
  holds the one thing worth segmenting on. **Anything built on the old format needs updating** — a
  contact group, customer journey or automation condition matching the old value, and any report or
  export that split the value on `|`. The `ISearchActivityLogger` method signatures are unchanged.
  `XpSearchActivityTypeInstaller` refreshes the *Description* of the four activity types on start so
  an upgraded project does not keep the old wording; the *Enabled* flag and the name are still never
  overwritten. See
  [Search activities in contact groups](docs/guides/analytics.md#search-activities-in-contact-groups)
  and the ADR-0015 addendum (AN-2).

- **Changed (admin UI):** the **Analytics** and **Query tester** pages were rebuilt to the owner's
  design spec and are now assembled only from `@kentico/xperience-admin-components` — `Card`, `Table`
  with `ActionCell`, `Tag`, `Callout`, `NameToggleButtons`, `DateTimeInput`, `Select` and the layout
  primitives — with no hand-rolled tables, no inline-styled hit rows and no stylesheet. Analytics
  gains four KPI tiles (total searches, zero-result rate, click-through rate, average clicked
  position), a two-series chart, an empty-range state and a friendly-warning error state; the query
  tester gains a quick tip before the first run, a required-query helper text that disables **Run**,
  a language drop-down filled from the index's own languages, per-side *N results · N ms · N changed*
  strips, and an error callout that links to the index's Status page. Both pages go to a single
  column below 1366 px. The index is no longer a selector on either page, so the
  `indexNames`/`indexLocked` client properties are gone. See ADR-0020.
- **Added:** `SearchAnalyticsReport.ZeroResultSearches` and `.Clicks`,
  `SearchVolumePoint.ZeroResultVolume` and `QueryVolume.P95ProcessingTimeMs` — the four figures the
  new dashboard shows, all computed in the single log read the service already did. Code
  constructing these records positionally must add the arguments.
- **Changed (admin UI):** the per-index **Status** page is now a custom React template
  (`@yourco/xperience-search-admin/IndexStatus`) instead of an edit form whose only field was a
  read-only text area. It shows the health tag with its explanation, the document/source/last-write
  figures, a stacked *Documents by source* bar with a share table, and the last ten ingestion log
  entries — failed ones first, with the invalid-row treatment **and** a *Failed* tag while the index
  is degraded. **Rebuild index** is a destructive page action behind a confirmation dialog and the
  Lucene integration's *Rebuild* permission, and reports back with a success notification and a
  *Rebuild in progress* tag. Built only from `@kentico/xperience-admin-components`; follows ADR-0020
  (AD-4a). See [Reading the Status page](docs/guides/relevance-tuning.md#reading-the-status-page).
- **Added:** `IIngestionLog.ReadRecentAsync(indexName, count, cancellationToken)` — the read side of
  the ingestion log, so a page can show the recent entries of one index without going through the
  listing. Breaking for anyone who implemented `IIngestionLog` themselves; the HTTP contract
  (`contract/xpsearch-ingestion.schema.json`) is untouched, and the status page's `failedWrites`
  comes from the ingestion queue rather than from the wire `IndexStatus`.
- **Changed (repository layout):** the JavaScript client moved from `src/XpSearch.Client` to
  `src/XpSearch.Widgets/Client`, so it sits inside the package that ships it, the same way
  `src/XpSearch.Admin/Client` holds the admin UI module. Nothing about the product changed: the npm
  package is still `@yourco/xperience-search` with the same entry points, the bundle is still
  `dist/xpsearch.umd.js`, and the widgets are still served from
  `/_content/YourCo.Xperience.Search.Widgets/xpsearch/`. Contributors run `npm ci` in the new path.
  See ADR-0019.
- **Added:** a **Search - Range filter** Page Builder widget (`XpSearch.RangeFilter`), which emits a
  `rangeFilter` mount. Its **Attribute** drop-down is filled from the selected index's numeric and date
  fields by a new form component configurator (`XpSearchConstants.NumericAttributeConfiguratorIdentifier`);
  **Minimum**, **Maximum**, **Step** and the two input labels are editor properties, because the search
  response carries no statistics the bounds could be derived from. A widget without usable bounds shows
  the unconfigured instruction block. See
  [Page Builder widgets](docs/guides/page-builder-widgets.md#the-range-filters-bounds-are-hand-configured).
- **Added (contract, minor):** `FacetValue.path` — the code names of a taxonomy value's ancestors,
  root first, excluding the value itself, absent for a root-level value and for every non-taxonomy
  attribute. Every ancestor a `path` names is itself present among the same facet's values with its
  own count, so a client can build the whole tree from one facet. Additive and optional, so a client
  that ignores it is unaffected and `X-XpSearch-Api-Version` (the semver **major**) stays at `1`.
  See ADR-0018 and the [spec amendment](docs/spec/amendments/2026-08-23-facet-value-path.md).
- **Added:** hierarchical taxonomy facets. `XpSearchIndexingStrategy` writes every **ancestor** of a
  tag as a value of the same, still-flat dimension, so facet counts roll up (*Coffee* counts every
  *Espresso* document) and `filters.facets` on a parent matches the documents tagged with its
  descendants — with no change to the request shape and none to the query pipeline.
- **Added:** the `categoryTree` widget and the `withCategoryTree` behaviour, and the
  **Search - Category tree** Page Builder widget (`XpSearch.CategoryTree`). The widget renders the
  `themes/MARKUP.md` category-tree contract: nested lists with a depth modifier per level, real
  crawlable links, `aria-current="true"` on every node of the open path, and a disabled `<span>` at
  count 0. Selection is one value at a time, because a parent's count already includes its
  descendants. `categoryTree` is no longer a reserved name with nothing behind it.
- **Changed (breaking, indexing):** **rebuild your indexes.** A tag's ancestors and its ancestry are
  written into the document, and the `<dimension>_label` term went from `code name ␟ title` to
  `code name ␟ path ␟ title`. An index written by an earlier version still searches, filters and
  labels correctly — the two-part term is still read — but its counts do not roll up and its facet
  values carry no `path` until it is rebuilt.
- **Changed (breaking, API):** `XpSearchIndexingStrategy` takes a new constructor parameter,
  `ITagAncestrySource`, after `ITaxonomyRetriever`. A derived strategy must add it to its own
  constructor and pass it through; the guide's sample shows the new signature. The default
  implementation, `TagAncestrySource`, reads the tag table once through `IInfoProvider<TagInfo>` and
  caches it on `cms.tag|all` — `ITaxonomyRetriever` exposes a tag's `ParentID` but nothing that
  resolves it to a tag.
- **Changed:** `LuceneFieldNames.ComposeLabel` takes an optional `path`, and `SplitLabel` returns a
  third element. Both still read the two-part form.

- **Changed (breaking):** the relevance-tuning admin pages moved inside the search index. They are now
  reached at **Lucene Search → indexes → click an index → the *Tuning* sidebar**: *Settings*, *Rules*,
  *Synonyms*, *Stopwords*, *Field weights*, *Query tester*, *Analytics* and *Status*. Clicking an index
  row in the Lucene index listing opens the sidebar instead of the integration's bare edit form; that
  form is the sidebar's *Settings* page. See ADR-0017.
- **Changed (breaking):** the old URLs are gone. `/admin/xpsearch-tuning/rules`, `/synonyms`,
  `/stopwords`, `/field-weights`, `/query-tester`, `/analytics` and `/index-status` are now
  `/admin/lucene/indexes/tuning/{id}/rules`, `/synonyms`, `/stopwords`, `/weights`, `/query-tester`,
  `/analytics` and `/status`. Re-point any bookmarks.
- **Changed (breaking):** permissions for those pages are now assigned on the **Lucene Search**
  application in *Role management*, not on *Search tuning*. A UI page is governed by the nearest
  ancestor application, and the moved pages sit under the Lucene integration. Grant *View*,
  *Create*, *Update*, *Delete* and *Rebuild* there. `XpSearch.SearchTuning` grants now cover only the
  two pages that stayed.
- **Changed (breaking):** the *Search tuning* application is renamed **Search ingestion** and holds
  only **API keys** and **Ingestion log**. Its identifier (`XpSearch.SearchTuning`) and slug
  (`xpsearch-tuning`) are unchanged, and it no longer declares the `UPDATE` permission, which none of
  its remaining pages evaluate.
- **Changed (breaking):** the index is no longer a field. Every tuning listing is filtered to the index
  in the URL and has lost its Index column; every tuning form shows the index read-only and takes its
  value from the URL. A row whose stored index differs from the URL's is refused on save rather than
  moved. The query tester's index selector and the analytics dashboard's "every index" option are gone
  — analytics now reports the index you are in.

- **Changed:** the search start time lives on `SearchContext` (`StartedTimestamp`, `Elapsed`) instead of
  in the internal `SearchTimingStage`, which is gone. The stage was never part of the documented
  stage-order table and nothing outside the library could reference it; the logged processing time and
  the response's `tookMs` now come from the same clock.
- **Fixed:** a redirect no longer disappears when the response comes from the cache. The default
  response cache re-issues `queryId` on every hit by copying the cached response, and that copy
  dropped `redirect`, so a host with caching enabled always saw `"redirect": null`. The copy is
  now a clone, so any future contract member is carried over too.
- **Fixed:** `_source` is facetable in practice, not only in the schema. Every document - Xperience
  content and externally pushed alike - now carries a `FacetField` for it alongside the term, so
  `facets: ["_source"]` returns counts and `filters.facets` can drill down to one provenance. The term
  is unchanged, so the ingestion status counts and `clear?source=` behave exactly as before.
- **Fixed:** `GET …/status` no longer reports `health: "degraded"` while a queued write is merely
  waiting to be indexed. `degraded` now means work failed to reach the index and nothing has succeeded
  since. `clear` and `delete` are asynchronous like `rebuild`, and the guide says so.

- **Added:** the Phase 2.5 JavaScript widgets (spec §5.3): `rangeFilter`, `loadMore` and
  `suggestions`, each a behaviour plus a default renderer emitting the markup contract in
  `themes/MARKUP.md`, each in `DEFAULT_WIDGETS` so a `.xps-mount` resolves it by name. `rangeFilter`
  renders `withRange` as two native `<input type="range">` sliders and two number inputs, and renders
  itself disabled when it has no bounds to offer. `loadMore` accumulates the pages of one search into
  one `<ol>` that is appended to and never rebuilt, with a live-region counter, a real button and an
  `IntersectionObserver` sentinel for the scroll path; place it *or* `pagination`, never both.
  `suggestions` implements the WAI-ARIA APG combobox-with-listbox pattern over `POST /suggest`, with
  a debounce, a minimum query length, latest-response-wins, and full keyboard support. Two new
  behaviours, `withLoadMore` and `withSuggestions`, are exported from
  `@yourco/xperience-search/behaviors`. See `docs/guides/widget-reference.md`.
- **Added:** `SearchInstance.suggest({ query, limit?, language? })` — autocomplete over the
  instance's own index and transport (endpoint, headers, `fetchFn`, contract-version check).
  Neither debounced nor cancelled: `withSuggestions` owns that policy.
- **Changed:** the Page Builder "Search - Suggestions" widget and the pagination "Load more" style no
  longer say their JavaScript widget ships later — both work now. No new Page Builder widget for
  `rangeFilter`: its bounds are a property of the corpus that an editor cannot know, and the contract
  does not report them (`docs/internal/KNOWN-LIMITATIONS.md`).
- **Added:** relevance tuning (spec §8). A new **Search tuning** administration application, under
  *Development*, with listings and editing pages for rules, synonyms, stopwords and field weights,
  built entirely on the built-in listing and edit UI page templates — no custom React. Four custom
  module classes (`XpSearch.Rule`, `XpSearch.Synonym`, `XpSearch.FieldWeight`,
  `XpSearch.StopwordList`) are installed on startup under their own module,
  `CMS.Integration.XpSearchTuning`. Rules pin, bury, boost or filter results, with a
  `Contains`/`Exact`/`StartsWith`/`Always` condition, an optional `Runs from`/`Runs until` schedule and
  a priority; conflicts resolve by priority then rule id, and for pin and bury the first rule to name
  a result wins. A pinned result the query did not match is injected only if it still matches the
  active filters. `explain=true` now fills `ranking.boosts` with `rule:<name>`, `weight:<field>×<w>`
  and `synonym:<term>` entries. See `docs/guides/relevance-tuning.md` and ADR-0014.
- **Added:** `XpSearch.Core.Tuning.IRelevanceTuningSource` and the four query-pipeline stages that
  read it — `SynonymExpansionStage` (200), `StopwordRemovalStage` (300), `BoostRulesStage` (700) and
  `PinnedAndBuriedStage` (900). Core registers an empty source, so search behaves exactly as before
  without `XpSearch.Admin`; `services.AddXpSearchAdmin()` swaps in the database-backed one, cached per
  index through `IProgressiveCache` and invalidated by the object types' own cache dependencies
  (spec §8.5).
- **Added:** the ingestion admin surface (spec §10.8) inside the same application — an **API keys**
  listing with a create page that shows the plaintext key exactly once, an **Index status** page with
  document counts by source, the last external write and a rebuild trigger, and an **Ingestion log**
  listing filtered by index and ordered newest first. `XpSearch.Admin` now references
  `XpSearch.Ingestion`; the reasoning is in ADR-0014.
- **Added:** redirect rules act (spec §8.2). `SearchResponse` gains a required, nullable
  `redirect: { url, rule }` — always present, `null` when no rule matched. `BoostRulesStage` sets it
  from the first matching redirect rule in the existing precedence order (priority, then id); a rule
  with an empty URL is skipped. The search is not short-circuited: the response carries its results
  next to the destination, and `explain=true` lists the rule as `rule:<name>` like any other. On the
  client, `withSearchBox` gains `submit(query)` beside `apply(query)` and the `searchBox` widget
  wires it to the form's submit event: a redirect is followed **only** for a query the visitor
  submitted, never on a keystroke and never on the search a restored URL runs at page load.
  `followRedirects: false` opts out and `withResults` exposes `redirect` so a template can render
  "Redirecting…" instead. This supersedes the "stored but not applied" note of ADR-0014.

- **Added:** the **Query tester** page (spec §8.4), under *Search tuning*. Pick an index, type a query
  and an optional language, and see the ranking twice side by side: **with rules** (what a visitor
  gets) and **without rules** (no rules, synonyms, stopwords or field weights at all). Each result
  shows its position, final score, base score and the boosts and rules that applied to it, and each
  side lists how the query itself was rewritten. Results that differ between the two sides are marked
  moved up, moved down, added or removed. Both sides run with `explain=true`; neither is served from
  the search cache, and neither is written to the analytics query log. See
  `docs/guides/relevance-tuning.md` and ADR-0016.
- **Added:** the **Analytics** dashboard (spec §9.3), under *Search tuning*: index selector, 7/30/90-day
  presets or a custom `yyyy-mm-dd` range, and all six reports from `ISearchAnalyticsService` — search
  volume over time (bar chart with a table fallback), zero-result queries, top queries, click-through
  rate and mean clicked position by query, and slowest queries. Every zero-result row has a **Create
  rule** button that opens the rule form with the index and the query pre-filled. See
  `docs/guides/analytics.md`.
- **Added:** an admin client module in `src/XpSearch.Admin/Client` (organization `yourco`, project
  `xperience-search-admin`), built with webpack and embedded into `XpSearch.Admin.dll`, so a consumer
  gets both pages from the NuGet package with no `appsettings.json` change and no dev server.
  **Building `XpSearch.Admin` now requires `npm ci && npm run build` in `src/XpSearch.Admin/Client`
  first** — the build fails with instructions when the bundle is missing. See
  `docs/guides/admin-client-development.md`.
- **Changed:** the *Search tuning* application now declares the `VIEW`, `CREATE`, `UPDATE` and `DELETE`
  permissions, so they can be assigned to roles in **Role management**; the two new pages and their
  page commands evaluate them (`VIEW` to read, `CREATE` for the *Create rule* deep link).

- **Added:** search analytics (spec §9). Four custom activity types (`xpsearch_query`,
  `xpsearch_noresults`, `xpsearch_click`, `xpsearch_conversion`) are created on startup and logged
  through `ICustomActivityLogger` for visitors whose cookie level is *Visitor* or higher — below that
  nothing is logged and nothing is thrown. Independently of consent, every search is written to the
  new `XpSearch.QueryLog` module class through a `ThreadQueueWorker`, a click event records the
  clicked position on its row, and `XpSearchQueryLogRetentionTask` (identifier
  `XpSearch.QueryLogRetention`, default 180 days) prunes it — create its configuration once in the
  *Scheduled tasks* application. `ISearchAnalyticsService` returns top queries, zero-result queries,
  click-through rate, average clicked position, daily volume and slowest queries (p95); the
  dashboard page itself is still pending (KNOWN-LIMITATIONS). See `docs/guides/analytics.md` and
  ADR-0015.
- **Added:** `SuggestMode.QuerySuggestions` now works: `/api/xpsearch/suggest` answers from the
  logged popular queries of the last `Analytics.QuerySuggestionDays` days (spec §4.3, §13.6). It
  previously returned an empty list with a warning.

- **Fixed:** the Page Builder widgets and the admin facet-attribute selector were invisible to
  Xperience. `XpSearch.Widgets`, `XpSearch.Admin` and `XpSearch.Core` did not carry
  `CMS.AssemblyDiscoverableAttribute`, so the system never scanned their `RegisterWidget`,
  `RegisterFormComponentConfigurator`, `RegisterModule`, `RegisterObjectType` and
  `RegisterScheduledTask` attributes and none of the seven widgets appeared in the Page Builder. A
  test now asserts the attribute on every shipped assembly that registers something by attribute.
- **Fixed:** the ingestion object types are registered. `XpSearchApiKeyInfo`,
  `XpSearchExternalDocumentInfo` and `XpSearchIngestionLogInfo` defined an `ObjectTypeInfo` but no
  `[assembly: RegisterObjectType]`, so no `IInfoProvider<T>` reached the container and a host failed
  DI validation on startup. Hosts that declared them as a workaround can delete that file.
- **Fixed:** a pushed document is searchable in the same process, without a restart. The Lucene
  integration caches one searcher per index and its client only invalidates it on rebuild and index
  deletion, never after an in-place upsert or delete, so `waitForIndex: true` returned before the
  document could be found and `GET …/status` reported a stale total. Every write through
  `ILuceneClient` now drops the integration's cached searcher as well as this library's response
  cache.
- **Fixed:** results render their title and link again. The default JavaScript template read
  `attributes.title`, `url` and `contentType` while the server projected the integration's `Title`,
  `Url` and `ContentTypeName`. The server owns the wire names of the base fields now — `title`,
  `url`, `contentType`, `language`, with the document id staying the result's own `id` — and maps
  them onto the Lucene fields; a field detected from a content type keeps its Xperience name on both
  sides. The `results` widget gained `titleAttribute`, `urlAttribute` and `snippetAttributes`
  params. A tuning rule's `field:value` filter expression resolves its field through the schema too,
  so `contentType:Article` reaches the `ContentTypeName` field the documents carry. **Breaking** for
  any client that read `attributes.Title`, `attributes.Url` or filtered on `ContentTypeName` /
  `LanguageName`.
- **Fixed:** the facet configuration is derived from the detected schema before anything is mapped.
  The strategy used to register a dimension the first time it mapped a document carrying one, so a
  fresh index handed the Lucene client an empty `FacetsConfig` and a document with two tags in one
  dimension failed the whole batch with *dimension "X" is not multiValued*. A host no longer needs a
  `FacetsConfigFactory` override. `XpSearchIndexingStrategy`'s constructor takes two more services
  (`ILuceneIndexAccessor`, `IIndexSchemaProvider`), which is **breaking** for derived strategies.
- **Fixed:** `GET …/status` counted replaced documents twice — `bySource.xperience` read 64 on an
  index of 32 documents — because a term's document frequency includes documents deleted from a
  segment that has not been merged away. Every figure counts live documents now, so the per-source
  counts add up to the total.
- **Fixed:** the worked custom-widget example in `docs/guides/custom-widgets.md`. The previous
  "dropdown facet in 40 lines" did not typecheck under `strict`, rendered `xps-dropdown__*` class
  names that exist in neither `MARKUP.md` nor either stylesheet and no `xps` class on its root,
  derived element ids from `container.id` — empty on a Page Builder mount, so every instance emitted
  `id="-select"` — interpolated editor text and taxonomy labels into HTML without `escapeHtml`, and
  broke single-select when two selections happened without an intervening render. The example is now
  `samples/CustomWidget.Dropdown/src/dropdownFacet.ts` reproduced verbatim, and
  `samples/pack-and-build.mjs` fails if the guide and the file diverge (ADR-0013).
- **Fixed:** the npm package now ships what the guides promise a JavaScript-only consumer.
  `themes/shell.css` and `themes/default.css` are package exports, the dependency-free mock server
  ships as `mock/server.mjs` with an `xpsearch-mock` bin entry, and `README.md` is in the tarball.
  `theming.md`'s stylesheet snippet and `js-client.md`'s mock-server instructions match reality;
  repository-only scripts are prefixed `repo:` so an installed package has no broken `npm run mock`.
  `"private": true` still blocks publishing (Phase 8) — see KNOWN-LIMITATIONS.
- **Fixed:** `docs/guides/custom-widgets.md` and `js-client.md` claimed a state change re-renders
  "the moment they are clicked". It renders on a **microtask**, so several mutations in one handler
  coalesce into one render and the DOM is current one microtask after `actions` returns, not
  synchronously. Behaviour unchanged; `src/behaviors/facet-apply.test.ts` pins it, along with what
  `apply()` does (`toggleFacet(...).search()` — toggle **and** search, two applies to one request).
- **Added:** `widgetId(container, widget, part)` and `readMountConfig(config, spec)`, exported from
  the package root. `widgetId` is the single implementation of `MARKUP.md` rule 4 — the instance
  segment falls back to `data-xps-instance`, then the container id, then `default` — and the four
  built-ins that hand-rolled an id base now use it. `readMountConfig` narrows the untyped
  `data-xps-config` values a widget factory receives, which is a trust boundary; a missing or
  wrong-typed required key throws naming the key and the bootstrap logs it once and skips the mount.
- **Added:** the shared `xps-select` utility block (`xps-select__label`, `xps-select__control`,
  `xps-select--disabled`), so a custom widget can render a themed `<select>` without inventing class
  names or borrowing another widget's. `sortSelect` renders the same block; its
  `xps-sort-select__label`/`__select` classes are **removed** — a markup-contract change, and
  semver-major per `MARKUP.md`.
- **Added:** `samples/CustomWidget.Dropdown` and `samples/pack-and-build.mjs` (`.ps1` entry point),
  which packs Core/Widgets/Admin and `npm pack`s the client into `samples/.feed/` and then restores,
  builds, typechecks and tests the sample from that feed alone — so a packaging regression fails a
  build instead of reaching a customer. `samples/README.md` explains why it is not a project
  reference.
- **Added:** `escapeHtml` is documented (`custom-widgets.md` → *Escaping*, `widget-reference.md` →
  *Templating helpers* and *The XSS model*), and `quick-start.md` gains "Installing from a private
  feed", including the `packageSourceMapping` entry that is mandatory, not optional, on a machine
  with source mapping enabled.
- **Fixed:** taxonomy fields no longer break indexing. The untyped content query result hands a Taxonomy
  column back as the JSON it is stored as, so `GetValue<IEnumerable<TagReference>>` threw
  `InvalidCastException` and took the whole rebuild batch with it — on a Dancing Goat host that meant an
  empty index and no facets at all. Values are now converted through the data type registered for the
  field data type (`DataTypeManager.ConvertToSystemType`), which works for any content type without a
  generated class, and every field is read as `object` and converted rather than cast. An item that
  still cannot be mapped is logged as an error naming the item and the field, and skipped, instead of
  escaping `MapToLuceneDocumentOrNull`.
- **Fixed:** `AddXpSearch` no longer requires `IInfoProvider<DataClassInfo>`, which Xperience 31.8.0 does
  not register (`DataClassInfoProvider` is `INotManagedByContainer`), so an application with the default
  DI validation failed to start. Class form definitions are read through the new
  `IDataClassDefinitionSource`, whose default implementation calls the documented static
  `DataClassInfoProvider.GetDataClassInfo(className)`.
- **Added:** linked reusable content items can be flattened into the parent document (spec §10.7) —
  `indexing.FlattenLinkedItems(contentTypeName, linkedFieldName, linkedContentTypeNames, depth)` indexes
  every field of each linked item onto the linking type's document under its own name, facets included,
  and reports those fields as part of the parent type's schema. Underneath it is a new
  `protected virtual XpSearchIndexingStrategy.ContributeAsync(IndexingContext, Document, CancellationToken)`
  hook whose context exposes the item, its data, its schema and `AddFieldAsync` / `AddTaxonomyAsync`, so a
  subclass adds fields with the base mapping's encoding instead of copying it. `XpSearchIndexingStrategy`
  takes `XpSearchIndexingOptions` as a new constructor parameter — update derived strategies.
- **Added:** the Page Builder widgets (spec §7) in `YourCo.Xperience.Search.Widgets` — seven
  view-component widgets (`XpSearch.SearchBox`, `.Results`, `.FacetList`, `.Pagination`, `.ResultStats`,
  `.SortSelect`, `.Suggestions`) that each render one configured `.xps-mount` element and self-assemble
  by `data-xps-instance`, the `XpSearchMountWidgetViewComponent<TProperties>` base class and
  `XpSearchMountWidgetProperties` (`Index`, `InstanceId` = `"default"`) third parties subclass, the
  `IXpSearchMountRenderer` seam, the editor-only unconfigured instruction block (invisible on a live
  page), `[RegisterSearchResultTemplate]` with `ISearchResultTemplateRegistry` behind the Results
  widget's template drop-down, sort-option validation against `XpSearchIndexOptions.SortKeys` and the
  `_asc`/`_desc` convention, `services.AddXpSearchWidgets()`, and `<xps-search-assets />` /
  `@Html.XpSearchAssets()` serving the bundle and both stylesheets as Razor Class Library static web
  assets under `_content/YourCo.Xperience.Search.Widgets/xpsearch/` (ADR-0012).
- **Added:** `FacetAttributeConfigurator` in `YourCo.Xperience.Search.Admin`, registered as
  `xpsearch.facetAttribute`, which fills a facet widget's attribute drop-down from the selected index's
  facetable schema fields and hides it until an index is chosen (spec §7.4). It is referenced by string
  identifier, so widget properties stay free of a dependency on `Kentico.Xperience.Admin`, and the pieces
  it shares with the widgets — `XpSearch.Core.XpSearchConstants` and
  `XpSearch.Core.Facets.FacetAttributeOptions` — live in Core, so `Admin` and `Widgets` stay independent
  of each other (spec §2.2).
- **Changed:** `mountAll()` now **merges** the `data-xps-instance-config` objects of every mount in a
  `data-xps-instance` group instead of using the first one that names an `index`. The first definition of
  a key wins and a mount that disagrees logs one `console.warn` naming the key and the instance, so the
  results widget's page size and retrieved fields apply wherever an editor placed it. Markup whose mounts
  already agreed is unaffected.
- **Added:** `resultStats` takes a string `textTemplate` with `{total}`, `{tookMs}`, `{query}`, `{page}`
  and `{totalPages}` placeholders (template and values escaped; `templates.text` still wins), which is
  what the Page Builder stats widget's **Text template** property emits.
- **Added:** `docs/guides/page-builder-widgets.md`, the C# half of `docs/guides/custom-widgets.md`, and
  ADR-0012.
- **Added:** `XpSearch.Ingestion` (spec §10) — push arbitrary documents into a Lucene index and search
  them alongside Xperience content. HTTP endpoints under `/api/xpsearch/admin/` (upsert, patch, delete,
  batch delete, scoped clear, rebuild, status, index list), the in-process `IXpSearchIndexer`, code-declared
  schemas (`[XpSearchSchema]` / `[XpSearchField]`) with narrow, explicit coercion and field-type-change
  detection, bearer API keys scoped per index and per operation (PBKDF2-hashed, shown once) with a
  per-key rate limit, and an ingestion audit log. Register with `AddXpSearchIngestion()` after
  `AddXpSearch()` and map with `MapXpSearchIngestion()`.
- **Added:** durable ingestion (ADR-0005, accepted). Pushed documents are persisted in the
  `XpSearch.ExternalDocument` custom module class before they are queued to Lucene through a
  `ThreadQueueWorker`; unprocessed rows are re-queued on startup, and a rebuild of Xperience content
  replays them instead of losing them. `waitForIndex: true` writes inline and is documented as a
  foot-gun for bulk imports.
- **Added:** provenance isolation. Every document carries the reserved `_source` attribute
  (`"xperience"` for content the Lucene integration indexes, the caller's own value for pushed
  documents); `clear` is scopeable to one source and can never reach Xperience content, and
  `GET .../status` reports document counts per source.
- **Added:** a second wire contract, `contract/xpsearch-ingestion.schema.json`, generated the same way
  as the query contract into `XpSearch.Ingestion.Contract` and
  `@yourco/xperience-search`'s `contract/ingestion-generated.ts`; `npm run contract:check` covers both.
- **Added:** `SearchFieldKind.Boolean` and the reserved `_source` schema field in `XpSearch.Core`, and
  `IServiceCollection.DecorateLuceneClient<TDecorator>(…)`, which the core package now uses for its own
  cache-evicting decorator and ingestion uses for the rebuild replay.

- **Changed (breaking):** the wire contract and the JavaScript API are owned by this product rather
  than modelled on Algolia and InstantSearch (ADR-0010). `SearchRequest` takes a one-based `page`,
  `pageSize`, `fields` and structured `filters` (`{ facets: [{ attribute, values, operator }],
  numeric: [{ attribute, operator, value }] }`); `SearchResponse` carries `results[]` of closed
  `Result { id, score, attributes, highlights, ranking }`, `facets` as ordered `FacetValue[]` arrays
  that include the taxonomy tag title as `label`, and `total` / `totalPages` / `tookMs`. `/suggest`
  takes `limit` and answers with `Suggestion.result`; `/events` takes `{ type, resultId, queryId,
  position }`. Validation errors are keyed by JSON path. In JavaScript, `createSearch()` replaces
  `xpsearch()`, `SearchActions` replaces the helper, widget hooks are `prepareState`/`prepareRequest`,
  render options are `{ params, results, state, actions, search }`, the second entry point is
  `./behaviors` with `with*` factories and the verbs `apply` / `urlFor` / `isActive` / `canApply` /
  `isStalled`, and the widgets and their classes are `results`, `facetList`, `resultStats`,
  `sortSelect`, `clearFilters`, `activeFilters` and `toggleFilter`. The routes, the
  `X-XpSearch-Api-Version: 1` header and the Problem Details error shape are unchanged.
- **Added:** `XpSearchIndexOptions.SortKeys` — a per-index map from a request's `sort` value to a field
  and a direction, alongside the existing `_asc` / `_desc` suffix convention.
- **Added:** `docs/guides/migrating-from-algolia.md`, generated from `contract/algolia-map.json` by
  `npm run docs:migration` and kept honest by `npm run docs:check`.

- Fixed: fields a content type inherits from a reusable field schema are detected, so a taxonomy that
  reaches a type only through a schema (`ProductFieldTags` and `ProductFieldCategory` on Dancing Goat's
  products) becomes a facet like any other. A name defined by both the content type and one of its
  schemas keeps the content type's field and logs a warning.

- Added: the nine default widgets (spec §5.3) — `searchBox`, `results`, `facetList`, `pagination`,
  `resultStats`, `sortSelect`, `clearFilters`, `activeFilters`, `toggleFilter` — each a behaviour
  plus a renderer over the public API, the escaping-by-default `html`/`highlight`/`formatNumber`
  template helpers, click tracking in `results`, an axe-core gate in CI, and a demo page on
  `npm run demo`; core plus all nine is 12.3 KB gzip against the 20 KB budget (ADR-0009).
- Added: the theme layer (spec §6) — `themes/src/shell.css` (structure only) and
  `themes/src/default.css` (opt-in, CSS-variable driven), the frozen `xps-` markup contract in
  `themes/MARKUP.md` with a fixture per widget, the three-way verification page in `themes/test/`,
  and `npm run check` to keep the stylesheets, fixtures and contract honest.
- Added: the JavaScript client core — `createSearch()` with the widget lifecycle and per-widget error
  isolation, `SearchState`, `SearchClient` (debounce, cancellation, retry, analytics), `SearchActions`,
  URL routing, the `render`/`error`/`stateChange` event bus, eight behaviours, the `.xps-mount`
  bootstrap with `registerWidgetType`, and ESM/UMD bundles under a gzip budget (ADR-0007).

- Added: the JSON search contract is owned and frozen — `contract/xpsearch-api.schema.json` generates the C#
  (`XpSearch.Core.Contract`) and TypeScript (`@yourco/xperience-search`) types for `/api/xpsearch/query`,
  `/suggest` and `/events`, versioned by the `X-XpSearch-Api-Version` response header (ADR-0006).
- Added: `XpSearch.Core` serves the contract — an ordered, injectable query pipeline behind
  `POST /api/xpsearch/query`, `/suggest` and `/events`, with taxonomy facets and drill-sideways counts,
  structured facet and numeric filters, sorting, XSS-safe highlighting, a short-TTL response cache invalidated on
  index writes, and `XpSearchIndexingStrategy`, which binds Xperience taxonomies as facets with no
  per-content-type code (spec §4, ADR-0008).
