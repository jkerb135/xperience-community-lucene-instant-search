# Host pass HW-9 — 2026-08-23

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, Xperience by Kentico 31.8.0, DB `comm_projects`,
http://localhost:27340), rebuilt by the owner against library **`main` 1778a4c** (PB-2 range-filter
widget, CR-3 hierarchical facets / `categoryTree`, MV-1 client move). Run by the lead with the
Management API MCP (taxonomy/Page Builder reads and writes), curl, `sqlcmd` and the in-app browser.
`src\Components\Widgets\CardWidget\` untouched.

Scope: the three merged units plus the Phase 6 open item (activity log).

## Verdict: **PASS** — one cosmetic defect found in passing (fixed), one pre-existing gap logged (PB-3)

## 1. Host changes made during the pass

- `src/Search/DancingGoatSearchIndexingStrategy.cs` — the CR-3 breaking constructor: `ITagAncestrySource`
  added after `ITaxonomyRetriever` and passed to `base(...)` (done before the owner's rebuild).
- `XpSearch_ApiKey`: the `dev-sample` row was deleted (`sqlcmd`) and the host restarted so
  `DevIngestionKeySeeder` issued a fresh key — the README's documented procedure; the old plaintext was
  never recorded. The key was read from the startup log into a shell variable only.
- Page `/Landing_pages/Search` (`101389f6-…`): two widgets added by the Management API to the
  `side-panel` zone of the 25/75 section, after the two facet lists — **left in place** for the owner:
  - `XpSearch.CategoryTree` `369660bf-9465-4900-a91f-c5b97df44c47` — Attribute `CoffeeTastes`, Label "Taste", Limit 10
  - `XpSearch.RangeFilter` `74c99f0c-b830-45f2-ac4e-ee9f9810db9c` — Attribute `ProductFieldPrice`, Label "Price", 0–500 step 5
- Index `DancingGoatSample` rebuilt via `POST /api/xpsearch/admin/indexes/DancingGoatSample/rebuild`
  (202; status `{"documents":{"bySource":{"xperience":32},"total":32},"health":"healthy"}`).

## 2. CR-3 — hierarchical facets

Dancing Goat already ships a two-level taxonomy, **Coffee tastes**: `Sweet` → `Acidy`, `Mellow`;
`Sour` → `Soury`, `Winey` (`list_tags`: the four children carry `parentTagId`).

**Before the rebuild** (index written by the previous version) — labels still resolve through the
two-part label term, no `path`, no roll-up:

```
CoffeeTastes: Acidy 2, Mellow 2, Soury 1, Winey 1          (no Sweet/Sour values)
ProductFieldTags: Bestsellers 4, "Hot tips"/HotTips 3      (title still resolved)
```

**After the rebuild** — `POST /api/xpsearch/query {"facets":["CoffeeTastes"]}`:

```
{count: 3, label: Sweet,  value: Sweet}
{count: 2, label: Acidy,  value: Acidy,  path: [Sweet]}
{count: 2, label: Mellow, value: Mellow, path: [Sweet]}
{count: 2, label: Sour,   value: Sour}
{count: 1, label: Soury,  value: Soury,  path: [Sour]}
{count: 1, label: Winey,  value: Winey,  path: [Sour]}
```

- Roll-up: Sweet = 3 (Acidy 2 ∪ Mellow 2, one product carries both).
- `filters.facets` on the **parent** `Sweet` → `total 3`: Brazil Natural Barra Grande, El Salvador
  Finca San Jose, Ethiopia Yirgacheffe (decaf). On the child `Acidy` → `total 2`.
- Non-taxonomy dimension (`contentType`) carries no `path` member at all (omitted, not null).
- `TagAncestrySource` against the real `CMS_Tag` table therefore works (the unit's open item 2).

## 3. Page Builder — PB-2 range filter, CR-3 category tree

`get_page_builder_widget` resolves both: `XpSearch.RangeFilter` ("Search - Range filter"; `Minimum`,
`Maximum`, `Step` typed `number`/`decimal`, nullable) and `XpSearch.CategoryTree` ("Search - Category
tree"; `Attribute`, `Label`, `Limit` int32). Added to `/search`, the live page emits:

```
<div class="xps-mount" data-xps-config="{"attribute":"CoffeeTastes","label":"Taste","limit":10}" … data-xps-widget="categoryTree">
<div class="xps-mount" data-xps-config="{"attribute":"ProductFieldPrice","min":0,"max":500,"step":5,"label":"Price"}" … data-xps-widget="rangeFilter">
```

`/search` now mounts: searchBox, facetList ×2, categoryTree, rangeFilter, resultStats, sortSelect,
results, loadMore.

**Painted DOM (in-app browser — the first time any pass could look):**
- `categoryTree` hydrated exactly to the `themes/MARKUP.md` contract: `<nav class="xps xps-category-tree" aria-label="Taste">`,
  `__list--lvl0` with `Sweet (3)` and `Sour (2)` as `__item--parent`, nested `__list--lvl1` with the
  children and counts, hrefs `/search?CoffeeTastes=Sweet` etc.
- Clicking **Acidy**: result stats "2 products in 4 ms"; `aria-current="true"` on `Sweet` and `Acidy`;
  `--selected` on exactly those two `<li>`s (Mellow, Sour untouched).
- `rangeFilter` hydrated **enabled** (no `--disabled`): two `<input type="range">` and two `type="number"`
  all `min=0 max=500 step=5`, values 0/500. API check that the attribute filters: `ProductFieldPrice lte 50`
  → 26 of 32 products, prices from 3.5.

**Icons.** Checked every `IconClass` this package registers against Kentico's annotated list
(`github.com/kentico/xperience-by-kentico-component-icons`, `KenticoIcons.cs`): `icon-arrows-h` and
`icon-tree-structure` (the two the units could not verify offline) **exist**. **Defect:** the facet list
widget's `icon-filter` (PB-1) does **not** — it has been a blank glyph in the widget list since Phase 4.
Fixed in this pass: `icon-funnel`. The remaining seven are valid.

## 4. MV-1 — client under Widgets

`GET /_content/YourCo.Xperience.Search.Widgets/xpsearch/xpsearch.umd.js` → 200, `Content-Length: 50765`
(the same bytes MV-1 measured before the move); `shell.css` 11009. The bundle hydrates the page (§3).

## 5. Phase 6 open item — contact activity log

`OM_ActivityType` has `xpsearch_query`, `xpsearch_noresults`, `xpsearch_click`, `xpsearch_conversion`, all
enabled. `OM_Activity` holds **68 `xpsearch_query`, 19 `xpsearch_noresults`, 3 `xpsearch_click`** rows, the
latest from 2026-08-23 11:06 (the owner's browser session). Activities are written for real contacts →
**Phase 6 gate closes with no open item.**

## 6. Defect / gap log

| # | Severity | What | Where | Disposition |
|---|---|---|---|---|
| 1 | cosmetic | `IconClass = "icon-filter"` is not a Kentico icon → blank glyph in the widget list | `FacetListWidget.cs` (PB-1) | **fixed** in this pass → `icon-funnel` |
| 2 | gap | Page Builder search pages have **no URL routing**: no widget emits `routing:true` in `data-xps-instance-config` and no property exposes it, so `urlFor` hrefs (`/search?CoffeeTastes=Acidy`), deep links and the back button do nothing — loading that URL shows 32 products with nothing selected; clicking any facet leaves the URL at `?q=coffee`. Predates CR-3 (PB-1); the tree's crawlable links make it visible. | `XpSearch.Widgets` mounting | → unit **PB-3**: a "Sync search state to the URL" option on the Search box widget (the instance anchor) emitting `"routing":true`; guide + KNOWN-LIMITATIONS entry until then |

## 7. Not verified here (needs a browser with an administrator sign-in)

- The **Attribute** drop-down of the Range filter listing only numeric/date fields, and the Category tree's
  listing facetable fields, inside the widget configuration dialog (the configurators are registered —
  `XpSearch.Admin.Tests` — and the schema says `ProductFieldPrice` is `Number`; the dependent-field
  behaviour itself is editor-only).
- The two icons painted in the widget list.
