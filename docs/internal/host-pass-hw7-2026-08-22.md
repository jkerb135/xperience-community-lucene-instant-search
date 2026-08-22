# Host pass HW-7 — 2026-08-22

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, Xperience by Kentico 31.8.0, DB `comm_projects`,
http://localhost:27340). Library `libraries/xperience-search`, **`main` 4f39ad4** — the pass started at
4cfd95b (the AD-3 merge) and another unit (`client-clean-dist`) was merged into `main` while it ran;
`git diff 4cfd95b 4f39ad4` is one line of `src/XpSearch.Client/package.json` (the build script now empties
`dist/` first), no source change, and the client bundle rebuilt at 4f39ad4 is byte-identical to the one
verified below. No library source was changed and nothing was committed (`git status --short` empty; this
note is untracked). `src\Components\Widgets\CardWidget\` untouched.

Scope: the two merged units on the real host.

| Unit | Verdict |
|---|---|
| **W25** — `rangeFilter` / `loadMore` / `suggestions` widgets, `SearchInstance.suggest()` | **PASS** (headless half; DOM hydration needs a browser) |
| **AD-3** — index-scoped admin | **FAIL** — §6.1: the admin UI tree does not build at all. Every UI page in the host, including pages that have nothing to do with this library, is unreachable. |

---

## 1. Builds

```
$ cd libraries/xperience-search/src/XpSearch.Client && rm -rf dist && npm ci && npm run build
found 0 vulnerabilities
> rollup -c && tsc -p tsconfig.build.json && node scripts/package-assets.mjs
src/index.ts, src/behaviors.ts → dist ... created dist in 304ms
src/umd.ts → dist/xpsearch.umd.js ... created dist/xpsearch.umd.js in 158ms
packaged themes/shell.css, themes/default.css, mock/server.mjs

$ cd libraries/xperience-search/src/XpSearch.Admin/Client && npm ci && npm run build
webpack 5.109.2 compiled successfully in 1155 ms

$ cd F:\Personal\CommunityProjects && dotnet build CommProjects.sln
    114 Warning(s)
    0 Error(s)
Time Elapsed 00:00:12.14
```

The 114 are the host's and the two sibling community projects' pre-existing warnings (CS1591, S2094,
S1144, ASPDEPR006, CS0618, NU1902). `dotnet build … | grep -i xpsearch` returns only the six
`XpSearch.* -> …dll` output lines: no warning names an `XpSearch` project, and warnings are errors there,
so 0 errors is proof.

A stale `CommunityProjects.exe` (PID 37900) from an earlier session was stopped before the build; the same
lock caused an `MSB3027` mid-pass and is not a code defect.

## 2. Host run

`dotnet run --project src --no-build`. Startup log is **39 lines**, ends with `Now listening on:
http://localhost:27340` / `Application started.`, and contains **no** error, warning, failure or exception
line — in particular nothing naming `IndexTuningSection`, `PageExtender`, `IndexListingTuningExtender`,
`IndexSettingsPage`, a duplicate slug or a permission. See §6.1: **a clean startup log is not evidence for
AD-3** — the UI tree is built lazily, so its failure never reaches the startup log.

The HW-5 index in `src/App_Data/LuceneSearch/DancingGoatSample` was still present and correct (24 hits for
`coffee`), so it was **not** rebuilt. The dev ingestion key already existed (`dev-sample`).

---

## 3. W25 on the host — PASS

### 3.1 The `suggestions` mount is still emitted

```
$ GET http://localhost:27340/search   → 200
data-xps-widget values, in document order:
  searchBox, resultStats, facetList, facetList, sortSelect, results, pagination, suggestions
```

The suggestions mount, verbatim from the page (HTML-decoded):

```html
<div class="xps-mount" data-xps-config='{"mode":"documents","limit":5}'
     data-xps-instance="default" data-xps-instance-config='{"index":"DancingGoatSample"}'
     data-xps-widget="suggestions">
```

i.e. exactly the `{"mode":"documents","limit":5}` HW-3 recorded, unchanged. The pagination mount is
`data-xps-config="{}"`, so the Page Builder page still uses the **numbered-pages** style; the *Load more*
style has to be selected by the owner in the editor (`docs/guides/page-builder-widgets.md:78` — the style
property emits a `loadMore` mount instead of a `pagination` one). Assets tag helper emits all three files:
`/_content/YourCo.Xperience.Search.Widgets/xpsearch/{shell.css,default.css,xpsearch.umd.js}`.

### 3.2 The served bundle contains the three new widgets

The file the browser would download is byte-identical to the one just built:

```
built   48253 bytes  sha256 B49C94E4D693F3C1…
wwwroot 48253 bytes  sha256 B49C94E4D693F3C1…
served  48253 bytes  sha256 B49C94E4D693F3C1…   (GET /_content/YourCo.Xperience.Search.Widgets/xpsearch/xpsearch.umd.js → 200)
```

Occurrence counts of the markup-contract class names in the **served** bytes:

```
xps-suggestions            21
xps-load-more              12
xps-range-filter           18
xps-suggestions__option     4
aria-activedescendant       1
IntersectionObserver        2
```

And the registry itself — the `DEFAULT_WIDGETS` object literal in the served bundle:

```js
xt={searchBox:O(ut),results:O(it),facetList:O(ct),pagination:O(ot),resultStats:O(ft),sortSelect:O(dt),
    clearFilters:O(st),activeFilters:O(tt),toggleFilter:O(pt),rangeFilter:O(gt),loadMore:O(ht),suggestions:O(mt)}
```

`rangeFilter`, `loadMore` and `suggestions` are registered by name, so a `.xps-mount` with
`data-xps-widget="suggestions"` resolves instead of logging *unknown widget type*. The suggestions
behaviour is present as `$$type:"xps.suggestions"` with the combobox state machine
(`activeIndex`, `sequence`, `timer`), and `IntersectionObserver` (the `loadMore` scroll sentinel) is in
the bundle.

### 3.3 `POST /suggest` in the widget's exact request shape

`SearchClient.suggest` (`src/XpSearch.Client/src/client.ts:109`) posts the `SuggestRequest` unmodified to
`SUGGEST_ROUTE`, so this is the wire the widget produces:

```
$ POST /api/xpsearch/suggest  {"index":"DancingGoatSample","query":"co","limit":5}
HTTP 200
{"suggestions":[
  {"result":{"attributes":{"title":"CoffeePlunger-p2e57tss"},"id":"d09cafef-…:en","score":1},
   "text":"CoffeePlunger-p2e57tss","url":"/products/coffee-plunger"},
  {"result":{"attributes":{"title":"ColombiaCarlosImbachi-tlu1k2is"},"id":"74a8f102-…:en","score":1},
   "text":"ColombiaCarlosImbachi-tlu1k2is","url":"/products/colombia-carlos-imbachi"}]}
```

Two of the five asked for, because only two indexed titles begin with `co` — a prefix match over the
document title, as designed. HW-3 §5.3 still applies: the suggestion text is the web page item *name*
(`CoffeePlunger-p2e57tss`), so out of the box the popup shows slugs.

### 3.4 What this does **not** prove

**DOM hydration needs a browser.** Everything above is markup on the wire, bytes in the bundle and a JSON
response. That the popup actually opens, that ↓/↑/Enter/Escape move and commit the active option, that
`aria-activedescendant` tracks it, that the *Load more* button appends a page to the existing `<ol>`
instead of rebuilding it, and that the `IntersectionObserver` sentinel fires on scroll — none of that was
observable headlessly. See the checklist in §7.

---

## 4. AD-3 on the host — FAIL

The probe (`src\Search\Hw7Probe.cs`, one file, `[assembly: RegisterModule]`, deleted afterwards) asked
`IPageLinkGenerator.GetPath(type, new PageParameterValues { { typeof(IndexEditPage), 2 } })` for every page
AD-3 registers. A returned path proves registration; an exception proves the opposite (SP-2 §Q1).

`ApplicationEvents.PostStart` did **not** fire at startup in this host — it fired on the first HTTP
request. Worth knowing for the next probe: `OnInit` ran immediately, `PostStart fired` only appeared after
`GET /`.

Verbatim probe output:

```
OnInit reached
PostStart fired
first registered Lucene index id = 2
IndexEditPage            ! InvalidOperationException: Node 'IndexTuningSection' must use either 'SidePanel' or 'Dialog' page location because its parent uses the '@kentico/xperience-admin-base/Edit' template. Update the location via 'UIPageLocationAttribute'.
IndexTuningSection       ! InvalidOperationException: Node 'IndexTuningSection' must use either 'SidePanel' or 'Dialog' page location because its parent uses the '@kentico/xperience-admin-base/Edit' template. Update the location via 'UIPageLocationAttribute'.
IndexSettingsPage        ! InvalidOperationException: … (same)
RuleListing              ! InvalidOperationException: … (same)
RuleCreate               ! InvalidOperationException: … (same)
SynonymListing           ! InvalidOperationException: … (same)
StopwordListing          ! InvalidOperationException: … (same)
FieldWeightListing       ! InvalidOperationException: … (same)
QueryTesterPage          ! InvalidOperationException: … (same)
AnalyticsDashboardPage   ! InvalidOperationException: … (same)
IndexStatusPage          ! InvalidOperationException: … (same)
SearchTuningApplication  ! InvalidOperationException: … (same)
ApiKeyListing            ! InvalidOperationException: … (same)
ApiKeyCreate             ! InvalidOperationException: … (same)
IngestionLogListing      ! InvalidOperationException: … (same)
done
```

Not one page resolved — **including `IndexEditPage`, which is the Lucene integration's own page, and
`SearchTuningApplication` / `ApiKeyListing` / `IngestionLogListing`, which AD-3 deliberately left
standalone**. The same message names `IndexTuningSection` every time, whichever page is asked for. That is
the signature of a failure while building the whole UI tree, not of one unresolvable node: the tree is
validated as a unit, the validation throws on `IndexTuningSection`, and every consumer of the tree gets
the exception. The admin application list is built from that same tree.

`GET /admin` returns **200**, and no error is written to the startup log or to `CMS_EventLog` — the admin
SPA serves its HTML shell (and, anonymous, its login page) without touching the UI tree, exactly as the
task warned. Route checks are therefore not evidence either way; the probe is.

See §6.1 for the defect, the root cause and the fix.

## 5. Regression — all green

Run on the final build, after the probe was deleted and the solution rebuilt (`0 Error(s)`, 6 warnings —
the host's own), with a freshly started host whose log is again 39 lines with no error, no exception and
no `HW7PROBE` line, i.e. the host is back exactly as it was:

```
$ POST /api/xpsearch/query {"index":"DancingGoatSample","query":"coffee","pageSize":2,
    "facets":["ProductFieldCategory","contentType","_source"],
    "highlight":{"fields":["ProductFieldName","ProductFieldDescription"]}}
HTTP 200  total=24  totalPages=12  tookMs=137
facets._source              : xperience=24            ← the CA _source facet fix, live
facets.contentType          : DancingGoat.ProductPage=24
facets.ProductFieldCategory : Coffees=8, Brewers=7, Accessories=6, Grinders=3
highlights                  : {"ProductFieldName":"<mark>Coffee</mark> Plunger",
                               "ProductFieldDescription":" Eight cups of <mark>coffee</mark> in a single plunger. …"}

$ POST /api/xpsearch/suggest {"index":"DancingGoatSample","query":"co","limit":5}   → HTTP 200, 2 suggestions
$ POST /api/xpsearch/events  {"type":"click","queryId":"1111…","resultId":"x","position":1}  → HTTP 202
$ GET  /  → 200      $ GET /search → 200      $ GET /admin → 200
```

`_source` now returns a facet count (`xperience=24`), where HW-3 §5.1 recorded `[]` — that defect is fixed
on the host.

Tuning tables, as found and as left:

```
$ sqlcmd -S localhost -d comm_projects -E -C -Q "SELECT counts…"
rules|synonyms|stopwordLists|fieldWeights|queryLog|externalDocs
0    |0       |0            |0           |56      |0
```

**All four tuning tables are empty** — no rule, synonym, stopword list or field weight survives from an
earlier pass, so nothing in this pass exercised a tuning rule end to end. `XpSearch_QueryLog` holds 56
rows accumulated over HW-3/4/5 and this pass, including the zero-result `yirgacheffe` row (LogID 5) the
analytics *Create rule* check wants.

## 6. Defects found in the library

### 6.1 Blocker — `IndexTuningSection` breaks the entire admin UI tree

`src/XpSearch.Admin/UIPages/IndexTuning.cs:13-19`

```csharp
[assembly: UIPage(
    parentType: typeof(IndexEditPage),          // ← uses TemplateNames.EDIT
    slug: "tuning",
    uiPageType: typeof(IndexTuningSection),
    name: "Tuning",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 100)]
```

Xperience refuses this: a page whose **parent** uses the `Edit` template must declare
`[UIPageLocation(PageLocationEnum.SidePanel)]` or `Dialog`. `IndexTuningSection`
(`IndexTuning.cs:44`) declares no `UIPageLocation`, so the tree validation throws — and it throws for the
whole tree, so **no** admin UI page in the host resolves any more, not the tuning pages, not the pages
AD-3 left standalone, and not the Lucene integration's or Xperience's own pages. Evidence in §4.

This is the risk `docs/internal/KNOWN-LIMITATIONS.md` ("`IndexTuningSection` in …/IndexTuning.cs") flagged
as *unverified*, and Kentico's own documentation only ever hangs a `SecondaryMenuSectionPage` under a
LISTING page (*Side navigation UI page template*: `parentType: typeof(UserList)`).

**Contradiction that needs resolving.** Spike SP-2 reported, on this same host and package versions,
`GetPath<Sp2Section> = /lucene/indexes/2/tuning` for a `SecondaryMenuSectionPage` registered under
`IndexEditPage` — i.e. the exact registration that now throws. One of the two observations is
mis-attributed. The two structural differences between SP-2's spike and AD-3's shipped code are that AD-3
adds an **EDIT-template child** to the section (`IndexSettingsPage`, `IndexTuning.cs:21-27`) and that
AD-3's pages live in `XpSearch.Admin` rather than the host assembly. Neither is named by the exception,
which blames the section↔parent relation only. Re-running SP-2's exact spike, in isolation, is the cheapest
way to settle it — and it should be done before choosing between the two fixes below.

**Proposed fix A (preserves the ADR-0017 UX, small).** Re-parent the section, as
`KNOWN-LIMITATIONS.md` already prescribes: register `IndexTuningSection` under
`Kentico.Xperience.Lucene.Admin.IndexListingPage` with `PageParameterConstants.PARAMETERIZED_SLUG`
instead of under `IndexEditPage`, which is the shape Kentico documents. Consequences, all mechanical:
`IndexScope.Route` (`IndexTuning.cs:73-74`) keys `PageParameterValues` on `typeof(IndexTuningSection)`
instead of `typeof(IndexEditPage)`; the explicit `[PageParameter(typeof(IntPageModelBinder),
typeof(IndexEditPage))]` bindings (e.g. `IndexTuning.cs:135-136`) go back to the parameterless overload;
`IndexListingTuningExtender` (`IndexTuning.cs:97-104`) keeps working unchanged, because
`AddEditRowAction<IndexTuningSection>()` substitutes the row id into the nearest parameterized slug, which
becomes the section's own. URLs change from `/lucene/indexes/{id}/tuning/…` to
`/lucene/indexes/tuning/{id}/…` (or whatever the slug order gives), so ADR-0017, the CHANGELOG entry and
`docs/guides/relevance-tuning.md` need re-pointing.

**Fix B (one line, wrong UX).** Add `[UIPageLocation(PageLocationEnum.SidePanel)]` to
`IndexTuningSection`. That satisfies the validation, but it renders the whole tuning section in the
right-hand side panel over the index edit form — not the full-width sidebar ADR-0017 describes, and the
query tester's two side-by-side columns and the analytics charts would live in a panel. Only worth it as a
stop-gap.

Either way the unit needs a **re-run of the host pass**, because nothing about the AD-3 UI has been
observed working — sidebar order, row click target, the missing Index column, the locked index in the
React pages, cross-index rejection and the role behaviour are all downstream of a tree that does not
build.

### 6.2 Note, not a defect: `docs/adr/0017-index-scoped-admin.md` §Evidence overstates what was proven

The ADR cites SP-2's in-process probe as evidence that the design "is buildable … registers and routes at
`/lucene/indexes/{id}/tuning`". §4 shows it does not, in the shipped form. Whatever the outcome of the
re-run in §6.1, the *Evidence* section should be corrected, and the *Unverified* paragraph at the end of
the ADR (which does hedge on the render) upgraded to say that registration itself is the thing that failed.

### 6.3 Note: HW-3 §5.3 still stands, and now shows in the suggestions popup

`/suggest` returns the web page item name as `text` (`CoffeePlunger-p2e57tss`). With W25 the popup is a
visible surface for it, so `docs/guides/widget-reference.md` (suggestions) is worth one sentence telling an
integrator that the suggestion label is the item name and that Dancing Goat's names are slugs.

## 7. Owner's browser checklist

Nothing below can be checked headlessly. **The AD-3 half is blocked by §6.1 — do not attempt it until the
tree builds; every one of those pages is currently unreachable.**

**AD-3 — the index-scoped admin**

1. **Sidebar order.** *Lucene Search → indexes → click an index → Tuning*: the left navigation reads, top
   to bottom, **Settings, Rules, Synonyms, Stopwords, Field weights, Query tester, Analytics, Status**.
2. **Row click.** Clicking a row in the index listing lands on `/admin/lucene/indexes/{id}/tuning/settings`
   — not the integration's bare edit form.
3. **No Index column.** None of Rules, Synonyms, Stopwords, Field weights shows an *Index* column, and each
   listing shows only the rows of the index in the URL.
4. **Index as text.** In the **Query tester** and the **Analytics** dashboard the index is rendered as
   plain text, not a `Select`; there is no "every index" option.
5. **Create rule from a zero-result row.** On the Analytics dashboard, a zero-result row's **Create rule**
   button opens the rule form with this index and that query pre-filled, and saving lands the rule in this
   index's Rules listing. `XpSearch_QueryLog` LogID 5 is a real zero-result `yirgacheffe` row to use.
6. **Cross-index edit rejected.** Open a rule that belongs to index A through index B's URL
   (`/admin/lucene/indexes/{B}/tuning/rules/{ruleId}/edit`): the save must be refused, not silently
   re-homed to B.
7. **Role tests.** With a role granted only *View* on **Lucene Search**: the tuning pages are readable, the
   row click reaches the sidebar, and Create/Update/Delete are unavailable. With *Create*/*Update*/*Delete*
   granted: the forms save. With grants only on **Search ingestion**: the tuning pages are **not**
   reachable, but API keys and the ingestion log are. Confirm a *View*-only role can still reach the
   sidebar by clicking the row (the index edit URL itself requires *Update*).
8. Confirm the standalone application appears as **Search ingestion** under *Development* and contains
   only **API keys** and **Ingestion log**.

**W25 — the JavaScript widgets**

9. **Suggestions popup, keyboard.** On `/search`, type into the search box: the popup opens after the
   debounce and the minimum query length; ↓/↑ move the active option and `aria-activedescendant` follows
   it; Enter commits the active option; Escape closes without committing; blur closes; typing fast never
   leaves a stale answer on screen (latest-response-wins). Expect the labels to be item-name slugs
   (§6.3).
10. **Load more — the button.** Switch the *Search - Pagination* widget's style to **load more** in the
    Page Builder (it emits a `loadMore` mount instead of a `pagination` one — never place both). The
    button appends the next page to the existing list: the earlier results must stay in the DOM, not be
    rebuilt, and the live-region counter must announce the new total shown. The button disappears on the
    last page.
11. **Load more — the scroll path.** Scrolling the sentinel into view loads the next page without a click.
12. **`rangeFilter`.** No Page Builder widget ships for it (by design). If mounted by hand, the two native
    sliders and the two number inputs move together, and with no bounds available the widget renders
    itself disabled.

## 8. Host changes made by this pass

| File | Change |
|---|---|
| `F:\Personal\CommunityProjects\src\Search\README.md` | The `AddXpSearchAdmin()` bullet named the old **Search tuning** application and its flat page list. Rewritten to point at *Lucene Search → indexes → click the index → Tuning* (`/admin/lucene/indexes/{id}/tuning/…`), to say that only API keys and the ingestion log remain in the renamed **Search ingestion** app, and that permissions for the per-index pages are granted on **Lucene Search**. |
| `F:\Personal\CommunityProjects\src\Search\Hw7Probe.cs` | Created and **deleted**. The solution was rebuilt after the deletion (`0 Error(s)`) and the host restarted clean, so the host is otherwise unchanged. |

## 9. Verdict

- **W25 — PASS.** The three widgets are in the shipped bundle and registered by name, the bundle the host
  serves is byte-identical to the one built from `main`, the `/search` page still emits the `suggestions`
  mount, and `POST /suggest` answers the widget's exact request with 200. The painted DOM is unverified.
- **AD-3 — FAIL.** `IndexTuningSection` cannot be registered under an EDIT-template parent without a
  `UIPageLocation`, and the resulting validation failure takes down the whole admin UI tree — including
  pages that predate this unit. §6.1 has the file:line and two fixes; the unit needs the fix and a fresh
  host pass.
