## Building a search page

Xperience Search is built bottom-up in three layers. The bottom one is a **JavaScript library** —
widgets, behaviours and a mount contract — that works on any HTML page, with or without Xperience.
Above it sit **Razor tag helpers**, one per widget, which are the single C# implementation of "options
in, mount out". Above those sit the **Page Builder widgets**, which are nothing but editor properties
mapped onto the same tag helpers, so a page an editor assembled and a page a developer wrote render
byte-identical markup.

This page builds the same results page — a search box, a filter sidebar, a toolbar and a paginated
result list — in each of the three, so you can pick the one that fits your project and recognise the
other two when you meet them.

### The same page in Razor

Everything is a tag element; `<xps-search>` names the index once for all of them.

```cshtml
@* Views/Search/Index.cshtml — needs @addTagHelper *, XpSearch.Widgets in _ViewImports.cshtml *@
<xps-search-styles />

<xps-search index="site-content" search-on-initial-load="true">

    <xps-search-box placeholder="Search…" enable-suggestions="true" suggestion-limit="5" />

    <div class="layout">                            @* your own two-column grid *@
        <aside class="xps xps-sidebar">             @* the filter column, drawn as the design's card *@

            <div class="xps-sidebar__header">
                <h2 class="xps-sidebar__title">Filters</h2>
                <xps-clear-filters />
            </div>

            <xps-facet-list attribute="contentType" label="Content type" limit="10" />
            <xps-category-tree attribute="tags" label="Categories" />
            <xps-range-filter attribute="price" label="Price" minimum="0" maximum="500" step="5" unit="USD" />
        </aside>

        <main class="xps-stack">

            <div class="xps-toolbar">
                <xps-result-stats />
                <xps-sort-select hide-label="true" sort-options="relevance;Relevance
newest;Newest first" />
            </div>

            <div class="xps-toolbar">
                <xps-active-filters scroll="true" attribute-labels="contentType;Content type" />
                <xps-clear-filters />
            </div>

            <xps-results results-per-page="6" />
            <xps-pagination padding="2" show-first="true" show-last="true" />
        </main>
    </div>
</xps-search>

<xps-search-scripts />
```

Four composition classes hold the widgets, and they are yours to place: `xps-toolbar` (one row, first
child left, last child right), `xps-sidebar` with `xps-sidebar__header`, and the `xps-stack` /
`xps-cluster` utilities. Only `xps-sidebar` is painted by the theme — it draws the card around the
filter column, which is why that element also carries `xps`.

Two things are worth reading twice. `sort-options` is one `key;Label` **per line**, so the attribute
value spans two lines above; keys the index does not publish are dropped. And the page size lives on
`<xps-results>`, not on `<xps-search>`: the result list always states the page size for the whole
search instance, so `page-size` on the scope only has an effect on a page whose result list is
`<xps-load-more>`.

The page's own language and website channel travel with every mount, so this page searches Spanish
DancingGoat content when it is viewed at `/es/search` — add `language="*"` or `channel="*"` to
`<xps-search>` to widen it. The attribute of every tag, its type and its default are in
[Razor tag helpers](razor-tag-helpers.md). A tag that cannot render — no index, a facet without an
attribute, a range without bounds — throws at render time with the message the Page Builder would show
an editor.

### The same page in plain HTML

The canonical hand-written version of exactly this page is
[Widget reference → Composing the results page](widget-reference.md#composing-the-results-page): the
same widgets in the same order as `.xps-mount` divs, one stylesheet link pair and one script tag, no
Xperience anywhere. It is a runnable file in the repository
(`src/XpSearch.Widgets/Client/demo/results-page.html`), the end-to-end tests boot that exact file, and
this page's Razor version is test-pinned against it: the tags above are read out of this guide, run,
and their mounts compared with the recipe's, widget for widget
(`tests/XpSearch.Widgets.Tests/BuildingASearchPageTests.cs`).

Reach for plain HTML when the page is not a Razor view of the Xperience application — a separate
front end, a static page, a bundler-driven SPA. From a bundler it is the same markup plus your own
`mountAll(document, { widgets })` call; see
[JavaScript bundler setup](javascript-bundler-setup.md).

Every configuration key of the recipe is a tag attribute; the parity test compares them key for key
and would name any that stopped being one. `attribute-labels` takes either the `attribute;Label`
lines above or a dictionary — `attribute-labels="@labels"` — and `search-on-initial-load` sits on the
scope because it is an option of the search, not of one widget; `<xps-search-box>` carries it too, for
a page written without a scope.

### The same page in the Page Builder

Give an editor a page with one wide section (or a two-column section for the sidebar) and let them
place these widgets, in this order — the same eleven mounts:

| Order | Widget in the Page Builder | Configure |
|---|---|---|
| 1 | Search - Search box | Index `site-content`, placeholder, suggestions on with a limit of 5 |
| 2 | Search - Clear filters | Beside your own "Filters" heading |
| 3 | Search - Facet list | Attribute `contentType`, label "Content type", 10 values |
| 4 | Search - Category tree | Attribute `tags`, label "Categories" |
| 5 | Search - Range filter | Attribute `price`, minimum 0, maximum 500, step 5, unit USD |
| 6 | Search - Result stats | Nothing; the default wording is the design's |
| 7 | Search - Sort selector | Sort options `relevance;Relevance` and `newest;Newest first`, label hidden |
| 8 | Search - Active filters | Scroll on one row |
| 9 | Search - Clear filters | The second one, at the end of the chips row |
| 10 | Search - Results | Results per page 6 |
| 11 | Search - Pagination | Style: numbered |

Every widget carries the index and the instance id as its first two properties, so the editor picks
the index on the first widget and repeats it on the rest — or the project has exactly one index and
nobody picks anything. A widget that is not configured yet shows the editor an instruction block and a
visitor nothing at all. The full property tables are in
[Page Builder widgets](page-builder-widgets.md).

The Page Builder is the right layer when the page's composition belongs to editors. It is *the same
widgets*: the Page Builder widget maps its editor properties onto the widget's options record and
hands them to the tag helper, and a test asserts the two render the same bytes for every one of the
fourteen widgets.

### Extending it

A control the shipped fourteen do not cover — a single-select dropdown facet, a map, a colour swatch
picker — is written once in JavaScript and then made available to whichever of the three layers you
need it in: `<xps-widget type="…">` for Razor with no extra C#, a tag helper of your own for a first-class
Razor tag, and a Page Builder widget over the same tag helper for editors. All three are worked
through, with the buildable sample, in [Custom widgets](custom-widgets.md).

### Related pages

- [Razor tag helpers](razor-tag-helpers.md) — every tag, its attributes and defaults, and `Html.XpSearchAsync`.
- [Widget reference](widget-reference.md) — the canonical plain-HTML recipe and what each widget renders.
- [Page Builder widgets](page-builder-widgets.md) — the editor properties of all fourteen widgets.
- [Custom widgets](custom-widgets.md) — a widget of your own, across the three layers.
- [JavaScript bundler setup](javascript-bundler-setup.md) — the npm path, per-widget imports and SCSS.
- [Server rendering and the mount contract](server-rendering.md) — the first paint and the mount attributes.
- [Quick start](quick-start.md) — indexing, querying and tuning before there is a page at all.
