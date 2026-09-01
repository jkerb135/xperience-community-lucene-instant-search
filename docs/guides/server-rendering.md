## Server rendering and the mount contract

The first paint of a search page can come from the server: the visitor's search runs in the request
that renders the page, so a shared result URL — `?q=espresso&page=2&tags=coffee` — arrives with its
results in the HTML, and a visitor without JavaScript still sees them (spec §5.8). The JavaScript
client takes over on its first render.

Two ways to get it:

- **Page Builder widgets** — the **Search - Results** widget does it for you, nothing to write.
- **Your own markup** — inject `ServerRenderedResults` from `XpSearch.Core` into a Razor view or page
  and render into your own element. This needs `XpSearch.Core` only: no Page Builder, no
  `XpSearch.Widgets`, no tag helper.

### The widgets path

On a live or previewed page the Results widget runs the first search itself and writes the cards
inside its mount element, wrapped in `<div data-xps-server-rendered>`; it hands the client the
`queryId` and the page size the server used so the page load is journaled once. See
[Page Builder widgets → Server-rendered result templates](page-builder-widgets.md#server-rendered-result-templates)
for the details and for registering a Razor result template.

### The plain-JavaScript path

`AddXpSearch()` registers `XpSearch.Core.Rendering.ServerRenderedResults` (scoped). Inject it,
call `RenderAsync`, write the result inside the element your client bundle mounts on, and pass the
returned `QueryId` to `createSearch` so the hydration query is journaled as the same search.

`RenderAsync` returns `null` when the search could not run: the warning goes to the log and the
element stays empty for the client to fill. It never throws through into the page.

A server-rendered search that found nothing paints the plain empty block. The recovery states the
client adds — the unfiltered count behind "Clear filters and show N results" — are client-side by
design: each needs a second request, and the first paint is one query or it is not a first paint.
They appear as soon as the client hydrates.

```cshtml
@page
@using Microsoft.AspNetCore.Html
@using XpSearch.Core.Rendering
@inject ServerRenderedResults SearchResults

@{
    // Index, page size, projected fields, an optional registered template, and the attributes the
    // built-in card reads. The search state (q, page, sort, facet and range filters) is read from
    // the request's query string with the same mapping the client writes it with.
    var render = await SearchResults.RenderAsync(
        ViewContext,
        new ServerResultsOptions(
            Index: "site-content",
            ResultsPerPage: 10,
            Fields: ["title", "url", "summary", "image", "contentType"],
            TemplateIdentifier: null,
            TitleAttribute: null,
            UrlAttribute: null,
            SnippetAttributes: []),
        Context.RequestAborted);
}

<input id="search-box" type="search" aria-label="Search">
<div id="search-results">@(render?.Content ?? HtmlString.Empty)</div>

<script type="module">
  import createSearch, { searchBox, results } from '/js/xpsearch.mjs';

  const search = createSearch({
    index: 'site-content',
    routing: true,
    // The queryId of the search the server rendered. The first query after hydration reuses it, so
    // the page load is one row in the query log instead of two; later queries get their own id.
    initialQueryId: '@(render?.QueryId)',
    // The page size the pipeline actually applied, so the client asks for the page the visitor is
    // already looking at rather than falling back to its own default.
    initialState: { pageSize: @(render?.PageSize ?? 10) },
  });

  search.addWidgets([
    searchBox({ container: '#search-box' }),
    results({ container: '#search-results' }),
  ]);
  search.start();
</script>
```

The `results` widget empties its container on its first render, so the `[data-xps-server-rendered]`
block never coexists with the client's list. If you render the results yourself instead of using the
`results` widget, remove the block on your first render.

**Which markup you get.** With no `DefaultViewPath` in `ServerResultsOptions`, Core emits its
built-in card — the same markup as the client's default item template and as the widgets'
`_Result.cshtml` (`themes/MARKUP.md`), so a page looks the same before and after hydration:

```html
<div data-xps-server-rendered class="xps xps-results">
  <ol class="xps-results__list">
    <li class="xps-results__item">
      <article class="xps-result">
        <div class="xps-result__media"><img class="xps-result__image" src="…" alt="" width="96" height="96"></div>
        <div class="xps-result__body">
          <h3 class="xps-result__title"><a class="xps-result__link" href="…">Title</a></h3>
          <p class="xps-result__path">Home / Blog / Coffee</p>
          <p class="xps-result__snippet">Snippet</p>
          <ul class="xps-result__meta"><li class="xps-result__meta-item xps-result__type">Article</li></ul>
        </div>
      </article>
    </li>
  </ol>
</div>
```

The path line and the media slot are drawn only when the result carries `path` / `image`; a result
with a `fileType` and no `image` gets an inline `xps-result__icon` document glyph in the slot
instead. Ask for those fields in `Fields` if you want them.

For your own card, either pass `DefaultViewPath` — a partial over
`XpSearch.Core.Rendering.SearchResultViewModel`, see the member table in
[Page Builder widgets](page-builder-widgets.md#server-rendered-result-templates) — or register a
template with `[assembly: RegisterSearchResultTemplate(…)]` and name it in `TemplateIdentifier`. An
unresolvable view is a logged warning and the built-in card, never a broken page. Keep the Razor card
and the client's `templates.item` in step: the Razor one only ever renders the first paint.

### The mount contract (stable)

A Page Builder widget renders one element per widget, and the bundle mounts on it:

```html
<div class="xps-mount"
     data-xps-widget="facetList"
     data-xps-instance="search-1"
     data-xps-instance-config='{"index":"site-content","routing":true}'
     data-xps-config='{"attribute":"contentType","limit":10}'></div>
```

What `mountAll()` reads (`Client/src/bootstrap.ts`):

| Attribute | Read as | Meaning |
|---|---|---|
| `class="xps-mount"` | selector | What the bootstrap scans for. An element already mounted (`data-xps-mounted="true"`) is skipped, so `mountAll()` can be called again after inserting markup. |
| `data-xps-widget` | string | Widget type, resolved against the registry then the built-ins. Unknown type: one `console.error`, that widget skipped. |
| `data-xps-instance` | string | Mount group; defaults to `default`. One `createSearch()` per group. |
| `data-xps-instance-config` | JSON object | `createSearch` options. Merged across every mount of the group, first definition of a key wins; a conflicting later value is a `console.warn`. A group without an `index` is skipped with a `console.error`. |
| `data-xps-config` | JSON object | The widget's own params, passed to its factory together with `container` (the mount element). Values are untrusted: widgets narrow them with `readMountConfig`. |
| `data-xps-mounted` | `"true"` | Written by the bootstrap after a mount succeeds. |

**This is a stable contract.** A Page Builder widget mount hydrates from either bundle — the one
`<xps-search-assets />` loads, or your own bundle importing
`@xperience-community/xperience-search` and calling `mountAll()`. Nothing in the mount element is
tag-helper specific.

From this release on, a change to the mount markup or to any `data-xps-config` /
`data-xps-instance-config` key is a **breaking change** and is recorded as one in the
[changelog](../../CHANGELOG.md).

### Related pages

- [Page Builder widgets](page-builder-widgets.md) — the widgets, their properties and result templates
- [JavaScript client](js-client.md) — `createSearch` and its options
- [Custom widgets](custom-widgets.md) — writing widgets against a mount element
