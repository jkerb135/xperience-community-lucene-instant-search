# Xperience Search — Technical Specification

**Working name:** `Xperience.Search` (replace `YourCo` / `XpSearch` prefixes with final branding before implementation)

**Target platform:** Xperience by Kentico (XbK), .NET 8+, version 31.x
**Audience for this document:** Claude Code, implementing in phases against a live XbK instance

---

## 1. Product summary

A drop-in search experience layer for Xperience by Kentico. XbK ships a Lucene integration that handles index configuration and querying, but stops at a sample `SearchController`. Every project rebuilds the results page, autocomplete, faceting, highlighting, and analytics by hand.

This product supplies that missing layer as three coordinated pieces:

1. **A JSON search API** over the existing Lucene index infrastructure — stateless, cacheable, versioned.
2. **A vanilla-JS client library** modelled on Algolia InstantSearch — independent widgets bound to a shared search state, mountable anywhere in the DOM by CSS selector.
3. **Page Builder widgets** that wrap the JS components so non-technical editors can place and configure search UI by drag and drop.

Plus two differentiators:

4. **Admin-configurable relevance tuning** (boosting, pinning, synonyms, stopwords) — the Algolia Rules/boosting equivalent, in the XbK admin UI.
5. **Search analytics** via Xperience's native activity system, so search behaviour feeds contact profiles, contact groups, and personalization.

### Design principle

The JS library is the product. The Page Builder widgets are thin server-rendered mount points that emit a configured container div; the JS library hydrates it. This means a developer using no Page Builder at all gets full functionality, and an editor using only Page Builder gets a working search page — without two implementations of anything.

---

## 2. Repository layout

This library lives inside the **CommunityProjects** monorepo, not in a repository of its own. Everything below is relative to the monorepo root.

```
~/CommunityProjects/
├── CommunityProjects.sln           # single solution, all libraries
├── Directory.Build.props           # shared: LangVersion, Nullable, warnings-as-errors
├── Directory.Packages.props        # central package version management
├── .editorconfig                   # shared style rules
├── docs/                           # monorepo-level: contributing, conventions, release process
├── build/                          # shared MSBuild targets, signing, packaging scripts
│
└── libraries/
    ├── <other-integration>/
    └── xperience-search/           # ← this project
        ├── README.md
        ├── CHANGELOG.md            # per-library, independently versioned
        ├── Directory.Build.props   # library-local overrides only
        ├── docs/
        │   ├── spec/               # this document
        │   ├── adr/                # decisions specific to this library
        │   ├── guides/             # customer-facing, ships with the package
        │   ├── api/                # generated reference
        │   └── internal/           # build prompt, phase log
        ├── src/
        │   ├── XpSearch.Core/      # NuGet: query pipeline, models, API endpoint
        │   ├── XpSearch.Admin/     # NuGet: admin UI — boosting, synonyms, analytics
        │   ├── XpSearch.Widgets/   # NuGet: Page Builder widgets
        │   ├── XpSearch.Ingestion/ # NuGet: push API, schema, API keys
        │   └── XpSearch.Client/    # npm: JS library (TypeScript, ESM + UMD)
        ├── clients/                # thin .NET + Node ingestion clients
        ├── themes/
        │   ├── shell.css           # structural only — layout, no visual opinion
        │   └── default.css         # opt-in visual theme, CSS-variable driven
        ├── samples/
        │   ├── DancingGoat.Search/ # reference implementation on the XbK sample site
        │   └── CustomWidget.Dropdown/  # must build against the published package only
        └── tests/
```

### 2.1 Monorepo conventions

These apply to every library under `libraries/`, not just this one. Where a convention doesn't exist yet, establish it here and write it up in the monorepo-level `docs/` — this is the first library to formalize it, not the only one that will use it.

- **Solution organization.** Projects are added to `CommunityProjects.sln` under a solution folder matching the library name (`libraries/xperience-search`). One solution, so cross-library refactors and shared-code changes stay compilable in one step.
- **Shared build configuration.** Root `Directory.Build.props` owns `TargetFramework`, `LangVersion`, `Nullable`, `TreatWarningsAsErrors`, and analyzer packages. A library-local `Directory.Build.props` imports the parent and overrides only what it must. Never redeclare a shared setting locally — divergence between libraries is exactly the cost a monorepo is supposed to avoid.
- **Central package management.** Package versions live in the root `Directory.Packages.props`. Project files reference packages without versions. This matters here specifically: Xperience packages move on a monthly cadence, and two libraries pinned to different XbK versions in the same solution will not restore cleanly.
- **Independent versioning and release.** Each library versions and ships on its own cadence. Tag format `xperience-search/v1.2.0`. The monorepo has no global version number.
- **CI path filtering.** Workflows trigger on `libraries/xperience-search/**` so unrelated library changes don't run this library's test suite. Shared-file changes (root props, `build/`) trigger everything.
- **Namespace and package naming.** Assembly and root namespace `XpSearch.*`; published package IDs `YourCo.Xperience.Search.*`. Keep the internal short prefix and the public branded ID distinct so renaming the product later is a packaging change, not a repo-wide refactor.
- **No cross-library project references.** If two libraries need the same code, it becomes its own library under `libraries/` with its own package. Direct `ProjectReference` across library boundaries makes independent release impossible.

### 2.2 Package split rationale

`Core` must be installable without `Admin` (headless / API-only consumers) and without `Widgets` (developers who don't use Page Builder). `Admin` depends on `Core`. `Widgets` depends on `Core`. `Ingestion` depends on `Core` only.

---

## 3. Phase plan

Implement in this order. Each phase should end in a working, demoable state on the Dancing Goat sample site.

| Phase | Scope | Exit criteria |
|---|---|---|
| 0 | Environment, sample project, index setup | Dancing Goat indexed via Lucene, sample controller returns results |
| 1 | `XpSearch.Core` — JSON API, query pipeline | `POST /api/search` returns hits, facets, highlights |
| 2 | `XpSearch.Client` — JS library core + 6 widgets | Working search page assembled from HTML + JS only |
| 3 | `themes/` — shell + default theme | Unstyled and themed renders both look deliberate |
| 4 | `XpSearch.Widgets` — Page Builder components | Editor can build a search page by drag and drop |
| 5 | `XpSearch.Admin` — boosting, pinning, synonyms | Admin can promote a result for a given query |
| 6 | Activity tracking + analytics dashboard | Searches appear in contact activity log; zero-result report works |
| 7 | Ingestion API — push external data into indexes | A third-party system can POST documents and have them searchable |
| 8 | Packaging, licensing, docs | Installable from a private NuGet feed with a getting-started guide |

**Extensibility is not a phase.** Custom widget authoring (§5.7) and custom pipeline stages (§4.4) are built into Phases 1–2 and must be dogfooded: every first-party widget is written using the same public connector API a third-party developer would use. If a first-party widget needs private internals, the public API is wrong and should be fixed rather than worked around.

---

## 4. Phase 1 — `XpSearch.Core`

### 4.1 Dependencies

Build on top of `Kentico.Xperience.Lucene` (the existing integration). Do **not** fork it. Consume:

- `ILuceneSearchService` — index querying
- `ILuceneIndexingStrategy` / `DefaultLuceneIndexingStrategy` — index population
- The existing admin Search application for index definition

The product's job is everything downstream of `ILuceneSearchService`.

### 4.2 The search endpoint

Single endpoint, POST, JSON in / JSON out. Registered via `AddXpSearch()` in `Program.cs`.

```
POST /api/xpsearch/query
```

**Request contract:**

```jsonc
{
  "index": "site-content",          // required — Lucene index code name
  "query": "espresso",              // free-text; empty string = match all
  "page": 0,                        // zero-based
  "hitsPerPage": 20,
  "facets": ["contentType", "tags", "language"],
  "facetFilters": [                 // outer array = AND, inner array = OR
    ["contentType:Article", "contentType:Product"],
    ["tags:coffee"]
  ],
  "numericFilters": ["price<=50", "publishedAt>=1700000000"],
  "sort": "relevance",              // or a configured sort key
  "highlight": {
    "fields": ["title", "content"],
    "preTag": "<mark>",
    "postTag": "</mark>",
    "snippetLength": 200
  },
  "attributesToRetrieve": ["title", "url", "summary", "image"],
  "language": "en",
  "queryId": "generated-guid"       // for analytics correlation; see §8
}
```

**Response contract:**

```jsonc
{
  "hits": [
    {
      "objectID": "web-page-42-en",
      "title": "Espresso Basics",
      "url": "/articles/espresso-basics",
      "summary": "...",
      "_score": 8.42,
      "_highlights": {
        "title": "<mark>Espresso</mark> Basics",
        "content": "...brewing <mark>espresso</mark> requires..."
      },
      "_rankingInfo": {              // only when explain=true
        "baseScore": 6.10,
        "appliedBoosts": ["freshness:+1.2", "rule:pin-espresso-guide"],
        "position": 1
      }
    }
  ],
  "facets": {
    "contentType": { "Article": 34, "Product": 12 },
    "tags": { "coffee": 40, "brewing": 18 }
  },
  "page": 0,
  "hitsPerPage": 20,
  "nbHits": 46,
  "nbPages": 3,
  "processingTimeMs": 14,
  "queryId": "generated-guid"
}
```

**Contract note:** field names deliberately mirror Algolia's response shape. This is a deliberate migration-path decision — a team moving off Algolia can swap the transport and keep most of their UI code. Document this explicitly as a feature.

### 4.3 Additional endpoints

```
POST /api/xpsearch/suggest      # autocomplete — lighter payload, prefix-matched
POST /api/xpsearch/events       # analytics events (click, conversion) — see §8
```

`/suggest` returns `{ suggestions: [{ text, hits?, url? }] }`. It should support two modes, configured per index: **query suggestions** (from logged popular queries) and **federated hits** (top N documents, for a dropdown showing actual results).

### 4.4 Query pipeline

Implement as an ordered, injectable pipeline so consumers can insert their own stages:

```
Request
  → validate & normalize (trim, lowercase, length cap)
  → synonym expansion          [Phase 5]
  → stopword removal           [Phase 5]
  → build Lucene Query
  → apply facet filters (Lucene Filter / BooleanQuery MUST clauses)
  → apply numeric filters (NumericRangeQuery)
  → apply boost rules          [Phase 5]
  → execute via ILuceneSearchService
  → apply pinned/buried results [Phase 5]
  → collect facet counts
  → generate highlights
  → project to response DTO
  → log search activity        [Phase 6]
Response
```

Expose as `ISearchPipeline` with `IEnumerable<ISearchStage>`; register stages in DI with explicit ordering. Consumers add stages via `services.AddXpSearchStage<T>(order)`.

### 4.5 Faceting

Lucene.NET 4.8 faceting requires the taxonomy index sidecar. Two viable approaches — **evaluate both in Phase 1 and pick one:**

- **A: `Lucene.Net.Facet` taxonomy writer.** Correct, fast, supports hierarchical facets. Requires a parallel taxonomy directory alongside each index — meaningful change to how indexes are built and stored.
- **B: `DocValues`-based grouping/counting.** Simpler, no sidecar index, adequate for flat facets and moderate corpus sizes.

Recommendation: start with B for time-to-first-demo, design the `IFacetProvider` abstraction so A can be swapped in for hierarchical facets later. Document the tradeoff.

**Facets must bind to Xperience Taxonomies without custom code.** This is a specific, checkable requirement — the indexing strategy should auto-detect taxonomy fields on indexed content types and register them as facetable. If a developer has to hand-write a facet mapping for every taxonomy, the product has failed its main promise.

### 4.6 Highlighting

Use `Lucene.Net.Search.Highlight` with `QueryScorer` and a `SimpleFragmenter`. Configurable pre/post tags. Guard against XSS: highlight tags are inserted server-side into content that must be HTML-encoded *before* tag insertion, never after.

### 4.7 Caching

Output-cache identical queries with a short TTL (default 60s, configurable). Cache key = hash of the normalized request. Must invalidate on index rebuild — hook the Lucene integration's rebuild event.

---

## 5. Phase 2 — `XpSearch.Client` (the JS library)

This is the piece that determines whether the product feels professional. Model it closely on InstantSearch.js's architecture, which solved these problems well.

### 5.1 Architecture

**Three layers:**

1. **`SearchClient`** — transport. Wraps `fetch` to the endpoint. Handles request debouncing, in-flight cancellation (`AbortController`), retry, and error surfacing.
2. **`SearchState`** — a small observable store holding `{ query, page, facetFilters, numericFilters, sort }`. Pure, synchronous, serializable. Emits change events.
3. **`Widget`** — independent UI units. Each widget subscribes to state, renders, and dispatches state changes. Widgets never talk to each other.

**Critical design property:** any number of widgets, mounted anywhere in the DOM, in any order, with no shared parent. A developer must be able to put the search box in the site header, facets in a left rail, results in `<main>`, and a hit counter in the footer — and have it all work. This is the flexibility requirement; treat it as non-negotiable.

### 5.2 Public API

```js
import xpsearch, { searchBox, hits, refinementList, pagination, stats, sortBy }
  from '@yourco/xperience-search';

const search = xpsearch({
  endpoint: '/api/xpsearch/query',
  index: 'site-content',
  routing: true,                  // sync state to URL query params
  initialState: { query: '' },
  searchOnInitialLoad: false,     // don't fire an empty query on page load
  debounceMs: 150
});

search.addWidgets([
  searchBox({
    container: '#search-input',
    placeholder: 'Search…',
    showReset: true,
    showSubmit: false,
    autofocus: false
  }),

  hits({
    container: '#search-results',
    templates: {
      item: (hit, { html, highlight }) => html`
        <article class="xps-hit">
          <h3><a href="${hit.url}">${highlight('title', hit)}</a></h3>
          <p>${highlight('content', hit)}</p>
        </article>`,
      empty: () => html`<p>No results.</p>`,
      loading: () => html`<div class="xps-skeleton"></div>`
    },
    transformItems: (items) => items    // escape hatch for client-side massaging
  }),

  refinementList({
    container: '#facet-content-type',
    attribute: 'contentType',
    operator: 'or',
    limit: 10,
    showMore: true,
    sortBy: ['count:desc', 'name:asc'],
    transformItems: (items) => items
  }),

  pagination({ container: '#search-pagination', padding: 2 }),
  stats({ container: '#search-stats' }),
  sortBy({
    container: '#search-sort',
    items: [
      { label: 'Relevance', value: 'relevance' },
      { label: 'Newest', value: 'date_desc' }
    ]
  })
]);

search.start();
```

### 5.3 Widget inventory (Phase 2 scope)

| Widget | Purpose | Key options |
|---|---|---|
| `searchBox` | Query input | `placeholder`, `showReset`, `showSubmit`, `autofocus`, `queryHook` |
| `hits` | Result list | `templates.item/empty/loading`, `transformItems` |
| `refinementList` | Facet checkboxes | `attribute`, `operator`, `limit`, `showMore`, `searchable` |
| `pagination` | Page controls | `padding`, `showFirst`, `showLast` |
| `stats` | "46 results in 14ms" | `templates.text` |
| `sortBy` | Sort selector | `items` |

**Phase 2.5 (stretch, same phase if time allows):**

| Widget | Purpose |
|---|---|
| `autocomplete` | Dropdown with suggestions + federated hits, full keyboard nav |
| `clearRefinements` | Reset all filters |
| `currentRefinements` | Removable filter chips |
| `rangeSlider` | Numeric range |
| `hierarchicalMenu` | Nested taxonomy navigation |
| `infiniteHits` | Load-more / infinite scroll instead of pagination |
| `toggleRefinement` | Single boolean facet |

### 5.4 Templating

Ship a tiny tagged-template `html` helper (no framework dependency, no virtual DOM). Every widget accepts a `templates` object; every template receives the relevant data plus helpers (`html`, `highlight`, `formatNumber`).

Default templates must produce semantic, accessible markup with shell classes. A developer who supplies no templates should get something that works and looks intentional — not a bare `<ul>`.

### 5.5 URL routing

When `routing: true`, sync state to URL query params (`?q=espresso&contentType=Article&page=2`). Must support browser back/forward via `popstate`, and produce shareable, crawlable URLs. Make the param mapping configurable (`routing: { stateToRoute, routeToState }`).

### 5.6 Accessibility requirements

Non-negotiable, and a genuine differentiator in this market:

- Search box: `role="search"`, associated `<label>`, `aria-label` on reset
- Results region: `aria-live="polite"` announcing result count changes
- Autocomplete: full WAI-ARIA combobox pattern — `aria-expanded`, `aria-activedescendant`, arrow/enter/escape handling
- Facets: real `<input type="checkbox">` elements with labels, not styled divs
- Pagination: `<nav aria-label="Search results pages">`, `aria-current="page"`
- Visible focus indicators inherited from shell CSS, never `outline: none`
- Full keyboard operability with no mouse

### 5.7 Custom widget authoring (connector API)

Third-party developers must be able to build arbitrary UI on the search state without forking the library. Follow InstantSearch's connector pattern: separate **behaviour** (state subscription, data shaping, refinement dispatch) from **rendering** (DOM output).

**Every first-party widget in §5.3 must be implemented as `connector + default renderer`.** This is the dogfooding rule — it guarantees the public API is sufficient, because the library itself has nothing else to use.

```js
import { connectRefinementList } from '@yourco/xperience-search/connectors';

// A connector calls the renderer on every state change.
const myFacetWidget = connectRefinementList((renderOptions, isFirstRender) => {
  const { items, refine, createURL, canToggleShowMore, toggleShowMore, widgetParams } = renderOptions;

  if (isFirstRender) {
    // one-time DOM setup, event delegation, third-party lib init
  }

  widgetParams.container.innerHTML = items.map(item => `
    <button data-value="${item.value}" aria-pressed="${item.isRefined}">
      ${item.label} (${item.count})
    </button>`).join('');
});

search.addWidgets([
  myFacetWidget({ container: document.querySelector('#my-facets'), attribute: 'tags' })
]);
```

**Connectors to expose publicly:** `connectSearchBox`, `connectHits`, `connectRefinementList`, `connectPagination`, `connectStats`, `connectSortBy`, `connectAutocomplete`, `connectRange`, `connectHierarchicalMenu`, `connectCurrentRefinements`.

#### Fully custom widgets

For UI that no connector models, expose the raw widget lifecycle interface:

```js
search.addWidgets([{
  // Contribute parameters to the outgoing search request
  getSearchParameters(state) { return { ...state, hitsPerPage: 5 }; },

  init({ state, helper, instantiate }) { /* before first search */ },
  render({ results, state, helper }) { /* after every response */ },
  dispose() { /* teardown, remove listeners */ }
}]);
```

The `helper` object is the only sanctioned way to mutate state: `helper.setQuery()`, `helper.toggleFacetRefinement()`, `helper.setPage()`, `helper.search()`. Widgets must never write to state directly.

#### Framework adapters

Ship optional thin adapters so the connectors work idiomatically in the frameworks XbK developers actually use. Keep these as separate entry points so the core bundle stays framework-free:

- `@yourco/xperience-search/react` — `useSearchBox()`, `useHits()`, `useRefinementList()` hooks over the connectors
- `@yourco/xperience-search/vue` — composables
- Alpine.js / vanilla — already covered by the base API

#### Registering a custom widget as a Page Builder widget

A developer's custom JS widget should be placeable by editors too. Provide a registration helper so custom widget types resolve through the same `.xps-mount` bootstrap:

```js
import { registerWidgetType } from '@yourco/xperience-search';

registerWidgetType('myCompany.ratingFilter', (config) => myRatingWidget(config));
```

Paired with a C# base class (`XpSearchMountWidgetViewComponent`) that a developer subclasses to emit `data-xps-widget="myCompany.ratingFilter"` with a serialized properties object. Document this end-to-end with a worked example — a developer shipping a custom control that an editor can drag onto a page is the strongest possible proof the extensibility is real.

#### Worked example: a custom dropdown facet control

This is the reference example for the docs and should be built in the sample project. It exercises every extension point in one artifact: a `<select>`-based facet control — something the built-in `refinementList` deliberately doesn't cover, and a request that will come up constantly.

**Step 1 — the widget, using only the public connector API**

```js
import { connectRefinementList } from '@yourco/xperience-search/connectors';

export const dropdownFacet = connectRefinementList((renderOptions, isFirstRender) => {
  const { items, refine, widgetParams } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = widgetParams;

  if (isFirstRender) {
    container.innerHTML = `
      <label class="xps-dropdown__label" for="${container.id}-select">${label}</label>
      <select class="xps-dropdown__select" id="${container.id}-select"></select>`;

    container.querySelector('select').addEventListener('change', (e) => {
      const current = items.find(i => i.isRefined);
      if (current) refine(current.value);          // clear previous (single-select)
      if (e.target.value) refine(e.target.value);  // apply new
    });
  }

  const select = container.querySelector('select');
  const selected = items.find(i => i.isRefined)?.value ?? '';

  select.innerHTML =
    `<option value="">${allLabel}</option>` +
    items.map(i => `
      <option value="${i.value}" ${i.isRefined ? 'selected' : ''}>
        ${i.label} (${i.count})
      </option>`).join('');

  select.value = selected;
});
```

Note what the developer did **not** have to do: no fetch, no request building, no facet-count math, no URL sync, no debouncing, no state management. The connector supplies `items` (with `label`, `value`, `count`, `isRefined`) and `refine`; everything else is inherited.

**Step 2 — use it**

```js
search.addWidgets([
  dropdownFacet({
    container: document.querySelector('#facet-brand'),
    attribute: 'brand',
    label: 'Brand',
    limit: 50,
    sortBy: ['name:asc']
  })
]);
```

**Step 3 — make it available to editors in Page Builder**

```js
import { registerWidgetType } from '@yourco/xperience-search';
registerWidgetType('myCompany.dropdownFacet', (config) => dropdownFacet(config));
```

```csharp
[assembly: RegisterWidget(
    identifier: "MyCompany.DropdownFacet",
    viewComponentType: typeof(DropdownFacetWidgetViewComponent),
    name: "Search - Dropdown filter",
    propertiesType: typeof(DropdownFacetWidgetProperties),
    IconClass = "icon-chevron-down")]

public class DropdownFacetWidgetProperties : IWidgetProperties
{
    [TextInputComponent(Label = "Search instance", Order = 1)]
    public string InstanceId { get; set; } = "default";

    // Custom form component supplying facetable fields from the selected index (§7.4)
    [FacetAttributeSelectorComponent(Label = "Attribute", Order = 2)]
    public string Attribute { get; set; } = "";

    [TextInputComponent(Label = "Label", Order = 3)]
    public string Label { get; set; } = "Filter";

    [TextInputComponent(Label = "\"All\" option text", Order = 4)]
    public string AllLabel { get; set; } = "All";
}

public class DropdownFacetWidgetViewComponent : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties>
{
    protected override string WidgetType => "myCompany.dropdownFacet";
    // Base class serializes properties to data-xps-config and emits the mount div.
}
```

The developer writes ~40 lines of JS and one properties class. They get: a working control, URL routing, editor drag-and-drop placement, analytics correlation, and admin-configured boost rules applying to its results — all for free.

**The base class must do the heavy lifting.** `XpSearchMountWidgetViewComponent<T>` handles property serialization, instance grouping, script registration, and the unconfigured-state editor message (§7.5). If a developer has to hand-write the mount div and JSON-encode config themselves, half of them will get it subtly wrong.

#### Widget SDK contract

Publish these as TypeScript types — they are the API surface third parties depend on, and breaking them is a semver-major event:

```ts
interface RenderOptions<TParams> {
  widgetParams: TParams;
  results: SearchResults | null;
  state: SearchState;
  helper: SearchHelper;
  instantSearchInstance: InstantSearch;
}

interface SearchHelper {
  setQuery(q: string): SearchHelper;
  toggleFacetRefinement(attribute: string, value: string): SearchHelper;
  clearRefinements(attribute?: string): SearchHelper;
  setPage(page: number): SearchHelper;
  addNumericRefinement(attr: string, op: '<'|'<='|'='|'>='|'>', value: number): SearchHelper;
  setSort(key: string): SearchHelper;
  search(): void;                    // chainable mutators, explicit execute
}
```

Also expose a lifecycle event bus (`search.on('render' | 'error' | 'stateChange', handler)`) so developers can hook analytics, loading indicators, or third-party integrations without wrapping a widget.

#### Guardrails

- **Namespace requirement:** custom widget type identifiers must contain a dot (`myCompany.thing`). Reserve the bare namespace for first-party widgets so a future built-in never collides with a client's control.
- **Error isolation:** a throwing custom widget must not take down the other widgets on the page. Wrap each widget's `render` in a try/catch, log to console, and keep the rest of the search functional.
- **Shell CSS available to custom widgets:** expose the layout primitives, focus-ring, and skeleton classes as documented utilities so custom controls inherit accessible defaults instead of reinventing them.
- **Accessibility is the developer's responsibility, but scaffold it:** the connectors should surface the ARIA state a control needs (`isRefined`, `canRefine`, `isSearchStalled`) rather than making developers derive it.

### 5.8 Custom result templates for editors

Editors choosing "result template" in the Page Builder Results widget (§7.3) need that dropdown populated by developer-registered templates:

```csharp
[assembly: RegisterSearchResultTemplate(
    identifier: "MyCompany.ProductCard",
    name: "Product card",
    viewName: "~/Components/Search/_ProductCard.cshtml",
    contentTypes: new[] { "MyCompany.Product" })]
```

Server-rendered templates apply to the initial page load and progressive-enhancement scenarios; the JS `templates.item` option covers client-rendered updates. Both must be supported, and a project should be able to use either without the other.

### 5.9 Build output

- ESM (`dist/xpsearch.mjs`) for bundlers
- UMD (`dist/xpsearch.umd.js`) for `<script>` tags — this matters, many XbK sites have no JS build step
- TypeScript declarations
- Target < 20KB gzipped for core + the six Phase 2 widgets. Enforce with a size-limit check in CI.

---

## 6. Phase 3 — Theming

Two stylesheets, strictly separated. This follows Kentico's own documented guidance: ship only the structural styles a component needs to render correctly, and keep site-specific design in the project's own stylesheet.

### `shell.css`

Structure only. Flex/grid layout, spacing rhythm, focus rings, screen-reader utilities, loading skeletons, sensible reset for the component tree. **No colours beyond `currentColor`, no fonts, no borders that imply a design.** A site with its own design system loads only this.

### `default.css`

An opt-in visual theme, driven entirely by CSS custom properties so it can be re-skinned without a build step:

```css
.xps {
  --xps-color-accent: #0b5fff;
  --xps-color-text: #111;
  --xps-color-muted: #666;
  --xps-color-surface: #fff;
  --xps-color-border: #e2e2e2;
  --xps-radius: 6px;
  --xps-space: 0.75rem;
  --xps-font: inherit;
}
```

### Class naming

Prefix everything `xps-` and follow a BEM-ish convention: `.xps-hit`, `.xps-hit__title`, `.xps-refinement-list__item--selected`. Kentico's widget docs explicitly recommend a unique prefix to avoid collisions with other third-party components.

### Verification

Build a test page that renders every widget with (a) shell only, (b) shell + default theme, (c) shell + a deliberately clashing host site stylesheet. All three must be usable and none must leak styles into the host page.

---

## 7. Phase 4 — `XpSearch.Widgets` (Page Builder)

### 7.1 Pattern

Each widget is a view-component-based Page Builder widget (the recommended type for anything needing business-layer interaction). It renders **only** a configured mount point:

```html
<div class="xps-mount"
     data-xps-widget="refinementList"
     data-xps-instance="search-1"
     data-xps-config='{"attribute":"contentType","limit":10,"showMore":true}'>
</div>
```

A single bootstrap script scans for `.xps-mount` elements, groups them by `data-xps-instance`, and constructs one `xpsearch()` instance per group with the discovered widgets. **This is what makes drag-and-drop placement work** — editors can place widgets in any section, any order, and they self-assemble.

### 7.2 Registration

```csharp
[assembly: RegisterWidget(
    identifier: "XpSearch.RefinementList",
    viewComponentType: typeof(RefinementListWidgetViewComponent),
    name: "Search - Facet list",
    propertiesType: typeof(RefinementListWidgetProperties),
    Description = "Displays filter checkboxes for a search attribute.",
    IconClass = "icon-filter",
    AllowCache = false)]
```

Widget properties classes give editors the config form for free. Static assets go in `~/wwwroot/PageBuilder/Public/Widgets/XpSearch/` per Kentico's convention.

### 7.3 Widget set and editor-facing properties

| Page Builder widget | Editor-configurable properties |
|---|---|
| Search box | Instance ID, placeholder, show reset, autofocus |
| Results | Instance ID, hits per page, result template (dropdown of registered templates), fields to show |
| Facet list | Instance ID, attribute (dropdown populated from index schema), label, operator, limit, show-more |
| Pagination | Instance ID, style (numbered / load-more) |
| Stats | Instance ID, text template |
| Sort selector | Instance ID, sort options |
| Autocomplete | Instance ID, mode (suggestions / hits / both), max items |

**Instance ID** is the coupling mechanism and must default sensibly (`"default"`) so a non-technical editor never has to think about it, while still supporting two independent searches on one page.

### 7.4 Attribute dropdowns

The facet widget's `attribute` property must be a **dropdown populated from the selected index's actual schema**, not a free-text field. Implement as a custom UI form component that calls back to a server endpoint listing facetable fields on the chosen index. Typo-prone free-text config is the single most common source of "your product is broken" support tickets.

### 7.5 Misconfiguration state

When a widget is placed but not configured, render an editor-only instruction block (visible in Page Builder, invisible on the live site) — following the existing XbK convention for unconfigured widgets. Never render a broken or empty component on a live page.

---

## 8. Phase 5 — `XpSearch.Admin` (relevance tuning)

This is the Algolia-parity feature and the strongest reason for an agency to pay for the product rather than wire up the free Lucene integration.

### 8.1 New admin application

Register a UI application "Search tuning" with these pages. Xperience's admin framework lets you add pages using existing React templates without writing front-end code, so **prefer built-in listing and editing templates** and only write custom React where a built-in template genuinely can't express the UI.

```
Search tuning
├── Rules            (listing + edit)
├── Synonyms         (listing + edit)
├── Stopwords        (edit)
├── Field weights    (per index)
├── Analytics        (dashboard)
└── Query tester     (custom page — needs bespoke UI)
```

### 8.2 Data model — custom module classes

Define via custom module classes so they get code generation, CI/CD support, and admin CRUD scaffolding. Follow the pattern the Lucene integration itself uses (one class for config, dependent classes for details).

**`XpSearchRule`**
| Field | Type | Notes |
|---|---|---|
| RuleID | int | PK |
| RuleIndexName | string | which index |
| RuleName | string | display |
| RuleEnabled | bool | |
| RuleConditionType | enum | `Contains` / `Exact` / `StartsWith` / `Always` |
| RulePattern | string | query pattern to match |
| RuleConsequenceType | enum | `Pin` / `Bury` / `Boost` / `Filter` / `Redirect` |
| RuleTargetObjectID | string | for pin/bury — the document objectID |
| RuleTargetPosition | int | for pin — 1-based |
| RuleBoostValue | decimal | for boost |
| RuleFilterExpression | string | for filter |
| RuleRedirectUrl | string | for redirect |
| RuleValidFrom / RuleValidTo | datetime? | scheduling — campaign support |
| RulePriority | int | conflict resolution order |

**`XpSearchSynonym`**
| Field | Type | Notes |
|---|---|---|
| SynonymID | int | PK |
| SynonymIndexName | string | |
| SynonymType | enum | `TwoWay` / `OneWay` |
| SynonymInput | string | comma-separated |
| SynonymOutput | string | for one-way |
| SynonymEnabled | bool | |

**`XpSearchFieldWeight`**
| Field | Type | Notes |
|---|---|---|
| WeightID | int | PK |
| WeightIndexName | string | |
| WeightFieldName | string | |
| WeightValue | decimal | multiplier, default 1.0 |

### 8.3 Rule application

Boost/field-weight rules apply at **query build time** (Lucene `BoostQuery` / per-field boosts on a `MultiFieldQueryParser`). Pin/bury rules apply **post-execution** as a reordering pass — a pinned document that isn't in the result set for the current query should be injected if and only if it matches the active filters.

Conflict resolution: sort by `RulePriority`, then `RuleID`. Document the precedence clearly; ambiguous rule interaction is the #1 support burden on this kind of feature.

### 8.4 Query tester page

A custom UI page where an admin types a query and sees:

- The ranked result list as it would appear live
- Per-hit ranking explanation: base score, each applied boost, each applied rule
- A toggle for "with rules" vs "without rules" side by side

Backed by the `explain=true` flag on the search endpoint (§4.2). This page is what makes relevance tuning learnable rather than superstitious, and it demos extremely well. Do not cut it.

### 8.5 Caching and invalidation

Rules, synonyms, and weights are read on every query. Cache them in memory, keyed by index name, invalidated by object change handlers on the module classes. Never hit the database per search request.

---

## 9. Phase 6 — Activity tracking and analytics

### 9.1 Xperience activity integration

Register custom activity types and log them via `ICustomActivityLogger` (server-side) so search behaviour lands in the standard contact activity log, and becomes available to contact groups and content personalization. This is the integration that makes the product a *marketing* tool, not just a dev tool — lead with it in sales material.

**Activity types to register:**

| Code name | Logged when | Value |
|---|---|---|
| `xpsearch_query` | A search returns ≥1 result | The query string |
| `xpsearch_noresults` | A search returns 0 results | The query string |
| `xpsearch_click` | User clicks a result | `query \| objectID \| position` |
| `xpsearch_conversion` | Developer-signalled goal after search | `query \| objectID` |

**Consent gate:** activities on website channels are only logged for visitors who have consented to tracking with the appropriate cookie level. The library must check consent state and degrade silently — never throw, never log without consent. Aggregate analytics (§9.2) must work independently of consent, since it stores no personal data.

**Client-side click tracking:** the `hits` widget attaches a click handler that fires the `/api/xpsearch/events` endpoint with `{ queryId, objectID, position }`. The `queryId` from the original query response is what correlates a click back to the search that produced it — this is exactly how Algolia's click analytics work and it's what makes CTR-by-query meaningful.

### 9.2 Aggregate analytics store

Separate from contact activities (which are per-person and consent-gated). A custom module class storing anonymous aggregates:

**`XpSearchQueryLog`**
| Field | Type |
|---|---|
| LogID | int |
| LogIndexName | string |
| LogQueryText | string (normalized, lowercased) |
| LogResultCount | int |
| LogTimestamp | datetime |
| LogChannelName | string |
| LogLanguage | string |
| LogClickedPosition | int? |
| LogProcessingTimeMs | int |

Write via a `ThreadQueueWorker` — the pattern XbK's own Lucene integration uses for background work — so logging never blocks a search response.

Include a retention/pruning scheduled task (default: 180 days, configurable). Unbounded log growth on a busy site is a real support liability.

### 9.3 Analytics dashboard

Admin page showing, per index and date range:

- **Top queries** by volume
- **Zero-result queries** — the highest-value report in the product; it tells a content team exactly what visitors want and can't find
- **Click-through rate by query**
- **Average position clicked**
- **Search volume over time**
- **Slowest queries** (performance)

Each zero-result query row should offer a one-click "Create rule" action that deep-links to the rule editor pre-filled with that pattern. Closing the loop from *insight* to *fix* inside one screen is the thing that makes an admin feel the product is worth its price.

---

## 10. Phase 7 — Ingestion API

Out of the box, indexes are populated by the Lucene integration's `ILuceneIndexingStrategy` reacting to Xperience content changes. That only covers content that lives in Xperience.

Real projects need more: a PIM feeding product data, a support knowledge base, a legacy system nobody is migrating, PDFs on a file share, an external blog. Today each of those means a bespoke integration. This phase makes pushing arbitrary documents into an index a documented, first-class operation.

**This meaningfully widens the product's value.** Federated search across Kentico content *and* external systems is something the free Lucene integration cannot do at all, and it's a common enterprise requirement.

### 10.1 Push endpoints

Separate controller from the query API, separate auth. All under `/api/xpsearch/admin/`.

```
POST   /api/xpsearch/admin/indexes/{index}/documents        # upsert one or many
DELETE /api/xpsearch/admin/indexes/{index}/documents/{id}   # delete one
POST   /api/xpsearch/admin/indexes/{index}/documents/delete # batch delete by id or filter
POST   /api/xpsearch/admin/indexes/{index}/clear            # drop all external docs
POST   /api/xpsearch/admin/indexes/{index}/rebuild          # trigger full rebuild
GET    /api/xpsearch/admin/indexes/{index}/status           # doc count, last update, health
GET    /api/xpsearch/admin/indexes                          # list indexes + schemas
```

**Upsert request:**

```jsonc
{
  "documents": [
    {
      "objectID": "pim-sku-88213",        // required, caller-owned, stable
      "title": "Ethiopian Yirgacheffe",
      "content": "Full text body…",
      "url": "https://shop.example.com/p/88213",
      "contentType": "Product",
      "tags": ["coffee", "single-origin"],
      "price": 18.50,
      "publishedAt": 1735689600,
      "language": "en",
      "_source": "pim"                     // reserved: provenance
    }
  ],
  "waitForIndex": false                    // true = block until searchable
}
```

**Response:**

```jsonc
{
  "indexed": 1,
  "failed": 0,
  "errors": [],
  "taskId": "b3f1…",        // poll status when waitForIndex=false
  "processingTimeMs": 22
}
```

### 10.2 Semantics

- **Upsert, not insert.** Same `objectID` replaces the existing document atomically. Callers should be able to re-push their whole catalogue safely and idempotently.
- **Partial updates:** support `PATCH` on a document for single-field updates (e.g. stock level) without re-sending the full body. Implement as read-modify-rewrite; Lucene has no true in-place update.
- **Batch limits:** cap at 1,000 documents or 10MB per request, whichever is hit first. Return `413` with a clear message rather than silently truncating.
- **Async by default.** Queue writes through a `ThreadQueueWorker` — the same pattern the Lucene integration uses for content-driven indexing — so a bulk import never blocks the request thread or degrades live search. `waitForIndex: true` is available for tests and small syncs, and should be documented as a foot-gun for bulk use.
- **Provenance isolation.** Documents carry a `_source`. Xperience-managed content is `_source: "xperience"`. A rebuild of Xperience content must never delete externally pushed documents, and `clear` must be scopeable to one source. Getting this wrong means a routine content rebuild silently wipes a client's product catalogue.

### 10.3 Schema handling

Each index declares a schema — field name, type (`string` / `text` / `number` / `date` / `boolean` / `string[]`), and flags (`searchable`, `facetable`, `sortable`, `retrievable`).

- Schema is defined in the admin UI or declaratively in code via an attribute on a strategy class.
- Pushed documents are validated against it. Unknown fields: reject by default, with an opt-in `allowDynamicFields` per index.
- Type coercion is explicit and narrow (string→number where unambiguous). Ambiguity is an error, not a guess. Silent coercion produces facets that mysteriously don't work, which is far more expensive to debug than a clear 400.
- Changing a field's type requires a rebuild. Detect and say so plainly in the response rather than corrupting the index.

### 10.4 Authentication

API keys, managed in the admin UI, scoped per index and per operation.

**`XpSearchApiKey`**
| Field | Type | Notes |
|---|---|---|
| KeyID | int | PK |
| KeyName | string | display |
| KeyHash | string | store a hash, never the key itself |
| KeyPrefix | string | first 8 chars, for identification in the UI |
| KeyScopes | string | JSON: `{"indexes":["products"],"ops":["write","delete"]}` |
| KeyEnabled | bool | |
| KeyExpiresAt | datetime? | |
| KeyLastUsedAt | datetime? | |

Present the key exactly once at creation. Bearer token in the `Authorization` header. Rate-limit per key. Log every write operation with key prefix, index, document count, and outcome — clients will ask "who deleted our catalogue" eventually, and the answer needs to exist.

The **query** endpoint (§4.2) is separately authenticated: public by default (it serves live site visitors), with an optional search-only key for headless/external consumers.

### 10.5 Client convenience layers

Ship two thin clients so integrators aren't hand-rolling HTTP:

**C#** — for other .NET apps and Xperience custom code:
```csharp
var client = new XpSearchIndexClient(baseUrl, apiKey);
await client.Index("products").UpsertAsync(documents);
await client.Index("products").DeleteAsync("pim-sku-88213");
```

**Node/JS** — for build pipelines and JAMstack sync jobs:
```js
const client = xpsearchAdmin({ endpoint, apiKey });
await client.index('products').saveObjects(documents);
```

Both handle batching, retry with exponential backoff, and partial-failure reporting automatically.

### 10.6 In-process indexing API

For code running inside the Xperience app, skip HTTP entirely:

```csharp
public interface IXpSearchIndexer
{
    Task UpsertAsync(string index, IEnumerable<SearchDocument> docs, CancellationToken ct = default);
    Task DeleteAsync(string index, IEnumerable<string> objectIds, CancellationToken ct = default);
    Task DeleteBySourceAsync(string index, string source, CancellationToken ct = default);
    Task<IndexStatus> GetStatusAsync(string index, CancellationToken ct = default);
}
```

Inject and call from scheduled tasks, custom modules, global event handlers, or automation steps. This is the API a Kentico developer reaches for first — make it the best-documented one.

### 10.7 Custom indexing strategies

Document the extension point for controlling what gets indexed from Xperience content itself. Developers subclass the Lucene integration's `DefaultLuceneIndexingStrategy` to add computed fields, flatten linked content items, pull in taxonomy tags, or crawl rendered page output.

Ship at least two worked examples in the sample project:
1. Indexing a content type with linked reusable content items flattened into the parent document
2. Adding a computed relevance field (e.g. popularity from the analytics store in §9.2, fed back into ranking)

### 10.8 Admin surface

Add to the Search tuning application (§8.1):

```
Search tuning
├── …
├── API keys          (listing + create, key shown once)
├── Index status      (doc counts by source, last write, rebuild trigger)
└── Ingestion log     (recent writes: key, source, count, outcome)
```

---

## 11. Phase 8 — Packaging and distribution

### 11.1 NuGet packages

- `YourCo.Xperience.Search.Core`
- `YourCo.Xperience.Search.Admin`
- `YourCo.Xperience.Search.Widgets`
- npm: `@yourco/xperience-search`

Target the XbK versions in active support. Declare compatibility explicitly in the readme; XbK ships monthly refreshes and package compatibility drift is a known ecosystem pain point.

**Monorepo packaging notes:**
- Pack from `libraries/xperience-search/src/*` only. The `CommunityProjects.sln` build must never produce packages for unrelated libraries.
- The XbK version constraint is declared once in the root `Directory.Packages.props`. If another library in the monorepo needs a different XbK version, that is a signal to split repositories, not to override locally.
- Release workflow triggers on the `xperience-search/v*` tag pattern, reads the version from `libraries/xperience-search/Directory.Build.props`, and publishes only this library's packages.
- The npm package versions independently of the NuGet packages but must declare a compatible-range note in its readme. A JS library newer than the API it calls is the most likely support issue you will hit.

### 11.2 Licensing

Per-site licence key, validated at application startup, verified offline via signed key (RSA public key embedded in the package; no phone-home). Grace behaviour on invalid key: log a prominent warning and add a small attribution notice to rendered widgets — **do not disable search on a production site.** Breaking a client's live site over a licence check destroys the agency relationships this product depends on.

Be realistic in planning: a NuGet package ships as readable IL and determined parties will bypass any check. The licence exists to make paying the path of least resistance for legitimate agencies, not to be uncrackable. Don't over-invest engineering time here.

### 11.3 Documentation deliverables

- Quick start: zero to working search in under 15 minutes
- Migrating from Algolia (map the API surface — this is a sales asset as much as a doc)
- Widget reference with live examples
- **Building a custom widget** — the dropdown facet walkthrough (§5.7), end to end from connector to Page Builder registration
- Theming guide
- Relevance tuning guide for non-developers (written for a marketer, not a dev)
- Sample project walkthrough on Dancing Goat

---

## 12. Testing requirements

- **Unit:** query building, filter parsing, rule application, synonym expansion, facet counting
- **Integration:** full pipeline against a real Lucene index with seeded fixture content
- **JS:** widget lifecycle, state sync, URL routing, DOM output snapshots
- **Accessibility:** automated axe-core run on the sample search page; manual keyboard-only walkthrough as a release gate
- **Performance:** benchmark 10k / 100k / 1M document corpora; document the point at which Lucene local indexes stop being the right answer and be honest about it in the docs
- **Multi-instance:** two independent search instances on one page must not interfere
- **Ingestion:** idempotent re-push, partial-batch failure, schema rejection, source isolation (an Xperience rebuild must not delete pushed docs), API key scoping and expiry
- **Extensibility:** build a custom widget using only the public connector API and a custom control using only the documented Page Builder base class — as an automated test, not a manual check
- **Widget error isolation:** a custom widget that throws in `render` must not break the other widgets on the page
- **Connector coverage:** the dropdown-facet example (§5.7) must compile against the published TypeScript types with no `any` casts and no imports from internal paths

---

## 13. Open decisions

These need resolution during implementation. Flag them rather than guessing.

1. **Faceting approach** (§4.5) — taxonomy sidecar vs DocValues. Decide in Phase 1 with a benchmark, not a coin flip.
2. **Multilingual strategy** — one index per language, or one index with a language field? Affects facet counts and analyzer selection. Per-language analyzers (stemming) argue for separate indexes.
3. **Azure AI Search adapter** — the architecture should not preclude a second backend behind `ISearchProvider`. Decide in Phase 1 whether to build the abstraction now (cheap) or retrofit later (expensive).
4. **SaaS deployment constraints** — Lucene indexes on XbK SaaS use CMS.IO storage abstraction and may map to blob storage. Validate index read/write performance and read-only-mode deployment behaviour early; this could invalidate assumptions.
5. **Ingestion durability** — if the app restarts mid-queue, are queued writes lost? Decide between accepting loss (callers retry), persisting the queue to the database, or requiring `waitForIndex` for critical syncs. Affects the SLA you can promise.
6. **Autocomplete data source** — logged popular queries require the analytics store to exist first. Either accept the Phase 6 dependency or ship prefix-matching on indexed titles in Phase 2 and upgrade later.

---

## 14. Competitive position

Worth keeping in view while building, since it should shape priorities:

- Kentico's native search covers index **configuration**; the front end is a sample controller. This product covers everything downstream.
- Algolia is the UX benchmark but is a recurring per-operation cost. The buyers for this product are teams who chose Lucene *because* of cost, data residency, or on-prem requirements, and are currently accepting a worse experience for it.
- The realistic market is the XbK installed base — on the order of 15,000 sites and growing as KX13 sunsets at the end of 2026. That is a lifestyle-business ceiling, not a venture-scale one. Price and sell accordingly: per-site agency licences, sold to the ~200 Kentico Solution Partners, not per-seat to end clients.
- **Platform risk is real.** Kentico could extend native search toward the front end. The defensible parts are relevance tuning, analytics, and the marketing-activity integration — not the widgets. Weight the roadmap toward the parts that are hard to commoditize.
