## Page Builder widgets

Xperience Search ships nine Page Builder widgets. An editor drags them onto a page in any section, in
any order; each one renders a single configured mount element, and the JavaScript bundle assembles them
into one working search. Nothing about the page layout has to be decided by a developer.

### Install

```csharp
// Program.cs
using Kentico.Xperience.Lucene.Core;

builder.Services.AddKenticoLucene();
builder.Services.AddXpSearch();          // the search API (see the Quick start guide)
builder.Services.AddXpSearchWidgets();   // the Page Builder widgets

var app = builder.Build();

app.UseStaticFiles();                    // serves the widget's script and stylesheets
app.UseXpSearch();                       // maps /api/xpsearch/*
```

```cshtml
@* _Layout.cshtml, once per page *@
@addTagHelper *, XpSearch.Widgets

<xps-search-assets />
```

That is the whole developer-side setup. The widgets register themselves through assembly attributes, so
they appear in the Page Builder widget list as soon as the package is referenced.

Reference `XperienceCommunity.Search.Admin` as well. It carries the form component configurator behind the
facet list's and the range filter's attribute drop-downs; without it those fields stay hidden. Nothing else needs it, and no
live-site code takes a dependency on `Kentico.Xperience.Admin` either way.

### The widgets

| Widget in the Page Builder | Identifier | Emits `data-xps-widget` |
|---|---|---|
| Search - Search box | `XpSearch.SearchBox` | `searchBox` |
| Search - Results | `XpSearch.Results` | `results` |
| Search - Facet list | `XpSearch.FacetList` | `facetList` |
| Search - Category tree | `XpSearch.CategoryTree` | `categoryTree` |
| Search - Pagination | `XpSearch.Pagination` | `pagination` or `loadMore` |
| Search - Result stats | `XpSearch.ResultStats` | `resultStats` |
| Search - Sort selector | `XpSearch.SortSelect` | `sortSelect` |
| Search - Suggestions | `XpSearch.Suggestions` | `suggestions` |
| Search - Range filter | `XpSearch.RangeFilter` | `rangeFilter` |

Every widget renders exactly this, and nothing else:

```html
<div class="xps-mount"
     data-xps-widget="facetList"
     data-xps-instance="search-1"
     data-xps-config="{&quot;attribute&quot;:&quot;contentType&quot;,&quot;label&quot;:&quot;Content type&quot;,&quot;operator&quot;:&quot;or&quot;,&quot;limit&quot;:10,&quot;showMore&quot;:true}"
     data-xps-instance-config="{&quot;index&quot;:&quot;site-content&quot;}"></div>
```

The JSON is HTML-attribute-encoded, so a quote or an angle bracket an editor types into a label cannot
break out of the attribute.

### Editor properties

Every widget starts with the same two properties.

- **Search index** — which Lucene index the search queries. If the project has exactly one index, an
  editor who leaves this empty gets that index.
- **Instance ID** — the coupling mechanism, defaulting to `default`. Widgets that share an instance ID
  form one search.

Then, per widget:

| Widget | Properties |
|---|---|
| Search box | Placeholder · Show reset button · Focus on page load · Sync search state to the URL — see [Shareable result URLs](#shareable-result-urls) |
| Results | Results per page · Result template · Fields to show (a selector over the index fields — `title`, `url`, `contentType`, `language` or any stored field of your content types) · Title attribute · Link attribute · Snippet attributes — see [Pointing a card at other attributes](#pointing-a-card-at-other-attributes) |
| Facet list | Attribute · Label · Operator (any / all of the selected values) · Values shown · Show a "show more" button |
| Category tree | Attribute · Label · Nodes per level. Pick a **taxonomy** attribute: the tree comes from the tag hierarchy, and a flat attribute renders as one level. Selection is one value at a time, because a parent's count already includes its children |
| Pagination | Style (numbered pages / load more button) — "load more" emits a `loadMore` mount instead of a `pagination` one, so place one or the other, never both |
| Result stats | Text template (`{total}`, `{tookMs}`, `{query}`, `{page}`, `{totalPages}`) · Text before the first search |
| Sort selector | Sort options (one `key;Label` per line) · Label · Hide the label visually |
| Range filter | Attribute (numeric or date) · Label · Minimum · Maximum · Step · "From" label · "To" label — see [The range filter's bounds are hand-configured](#the-range-filters-bounds-are-hand-configured) |
| Suggestions | Mode (matching documents / popular queries) · Maximum items. Whether an index answers with documents or with query suggestions is server-side configuration; the property records the editor's intent and does not change the request |

A blank text property is left out of `data-xps-config` entirely, so the JavaScript widget's own default
applies rather than an empty string overriding it.

#### The attribute drop-down is filled from the index

The facet list's and the category tree's **Attribute** property is not a free-text field. It is a
drop-down populated from the selected index's actual schema, listing only fields that are facetable,
in alphabetical order. Pick the index first: until you
do, the attribute field is hidden, and changing the index repopulates it.

The range filter's **Attribute** property works the same way, but lists the index's numeric and date
fields instead — the ones a `filters.numeric` comparison can be built from. An index with no numeric
or date field leaves the drop-down hidden, which is the sign that a range filter has nothing to filter
on there.

#### The Results widget's field selectors are filled from the index too

**Fields to show**, **Title attribute** and **Link attribute** are selectors, not text fields: they
offer the stored (retrievable) fields of the registered indexes, so an editor picks a field name
instead of remembering one. *Fields to show* takes several, the other two take one each and leave
their default (`title`, `url`) in place while empty.

They differ from the facet **Attribute** drop-down in one way: a selector's option list cannot depend
on the index chosen in the same dialog, so with more than one index registered it lists the fields of
all of them. Picking a field that the selected index does not have simply returns nothing for that
field — the same outcome as typing it did before.

A Results widget saved before these selectors existed keeps working: its typed field list is still
read and still reaches the search. It shows up as an empty selector in the dialog, and the moment you
pick anything there, the selection replaces the old list.

#### Pointing a card at other attributes

The default result card reads `title`, links to `url`, and takes its snippet from the first of
`summary`, `content`, `excerpt` that has a value. Three Results properties re-point it without any
code, for an index whose fields are named differently:

| Property | Default | What it does |
|---|---|---|
| Title attribute | `title` | Attribute the heading of the card comes from |
| Link attribute | `url` | Attribute the card links to. A result without it links to `#` |
| Snippet attributes | `summary`, `content`, `excerpt` | One attribute name per line, tried in order; the first with a value is shown |

They are display options, so they end up in `data-xps-config` (`titleAttribute`, `urlAttribute`,
`snippetAttributes`) and apply to the server-rendered first paint and to the client's rendering alike.

**They interact with Fields to show.** *Fields to show* is what the search asks the index to return: an
attribute that is not in that list does not arrive, so a card that is told to read `heading` while
*Fields to show* lists only `title` and `url` renders empty. If you restrict the fields, list every
attribute the card needs — the title, the link, the snippet candidates, plus `contentType` and `image`
if you want the meta line and the thumbnail. Leaving *Fields to show* empty avoids the problem
entirely.

#### The range filter's bounds are hand-configured

| Property | Required | What it does |
|---|---|---|
| Attribute | yes | The numeric or date field to filter, picked from the index schema |
| Label | no | Heading above the control. Empty falls back to the attribute name |
| Minimum / Maximum | yes | The two ends of the control. The maximum must be greater than the minimum |
| Step | no | Step of the sliders and the number inputs. Defaults to `1` |
| "From" / "To" label | no | Visible labels of the two number inputs. Empty leaves `From` and `To` |

**The editor has to know the bounds.** The search response carries no statistics about the corpus, so
there is nowhere for a server-computed minimum and maximum to arrive — the widget cannot discover that
prices in the index run from 3 to 480. Until a widget has both bounds, and a maximum above its minimum,
it shows the unconfigured instruction block rather than a slider that filters nothing.

A configured range filter renders exactly this:

```html
<div class="xps-mount" data-xps-config="{&quot;attribute&quot;:&quot;price&quot;,&quot;min&quot;:0,&quot;max&quot;:500,&quot;step&quot;:5,&quot;label&quot;:&quot;Price&quot;,&quot;labels&quot;:{&quot;from&quot;:&quot;Cheapest&quot;,&quot;to&quot;:&quot;Dearest&quot;}}" data-xps-instance="search-1" data-xps-instance-config="{&quot;index&quot;:&quot;site-content&quot;}" data-xps-widget="rangeFilter"></div>
```

The bounds and the step stay JSON numbers, and the two input labels are left out of the JSON entirely
when the editor did not set them, so the JavaScript widget's own `From` / `To` apply.

A date attribute is indexed as Unix epoch seconds, so its bounds are entered as epoch seconds too —
`1704067200` is 2024-01-01. Give such a filter a **Label** and "From" / "To" labels that say so.

#### Shareable result URLs

The search box's **Sync search state to the URL** property, on by default, keeps the query, the
selected filters, the sort key and the page number in the address bar. A visitor can then bookmark or
send a result page, and the browser's back button walks the search back instead of leaving the page.
Typing replaces the current history entry rather than pushing one per keystroke; a filter or a page
change pushes.

The search box owns the option because exactly one of them exists per search, but it applies to the
whole instance — it is written to `data-xps-instance-config`, which the bootstrap merges into the
`routing` option of `createSearch()`:

```html
<div class="xps-mount" data-xps-config="{&quot;showReset&quot;:true,&quot;autofocus&quot;:false}" data-xps-instance="default" data-xps-instance-config="{&quot;index&quot;:&quot;site-content&quot;,&quot;routing&quot;:true}" data-xps-widget="searchBox"></div>
```

A synced search reads those parameters on load, so `?q=coffee&contentType=Article&page=2` renders that
result page directly. It reads back only the filters the page has a widget for: `contentType=Article`
applies when the page carries a facet list, category tree or toggle on `contentType`, and a numeric
parameter such as `price_lte=50` when it carries a range filter on `price`. Any other parameter —
Kentico's own `uh`, campaign parameters, or a filter for an attribute this page does not show — is
left alone and stays in the URL. So a link shared from a page with more filters than the target page
has applies only the filters the target page can display.

**At most one search instance per page may sync.** The parameter names (`q`, `page`, `sort`, and each
facet's attribute name) are not namespaced by instance ID, so two syncing searches on one page write
over each other's parameters and both restore from the same `q`. Untick the property on the secondary
search — a product finder embedded in an article, say — and it keeps its state in memory only. The
property is emitted either way, so `"routing":false` in the markup is the sign of a deliberate choice.

#### Sort options are validated

A sort option's key must be one the search API will accept: `relevance`, a key configured for the index
in `XpSearchIndexOptions.SortKeys`, or a sortable field with an `_asc` / `_desc` suffix.

```text
relevance;Most relevant
publishedAt_desc;Newest first
price_asc;Price, low to high
```

Keys the API would reject are dropped from the rendered mount. If none of them are usable, the widget
shows the unconfigured instruction block instead of rendering a selector with no options.

### Instance IDs and two searches on one page

Widgets are grouped by `data-xps-instance`; each group becomes one `createSearch()` instance with its
own state and its own requests — but *not* its own URL parameters, which is why only one instance per
page should sync to the URL (see [Shareable result URLs](#shareable-result-urls)). Leaving **Instance ID** at `default` is right for
the common case — a search page with one search on it.

To put two independent searches on one page, give each set of widgets its own instance ID:

```text
Section "Articles"      →  Instance ID: articles   (search box, results, pagination)
Section "Products"      →  Instance ID: products   (search box, facet list, results)
```

**The rule to remember: every widget of one instance must select the same index.** The bootstrap merges
the `data-xps-instance-config` of every mount in a group into one options object, so placement order does
not matter — two properties of the **Results** widget that are instance-wide rather than per-widget,
*Results per page* and *Fields to show*, apply wherever the editor dropped the Results widget. Where two
mounts define the same key with different values the first one in page order wins and the browser console
carries one warning naming the key and the instance; pointing two widgets of one instance at different
indexes is exactly that case.

### An unconfigured widget never breaks a live page

A widget that has been placed but not configured — no index selected, no facet attribute, no usable sort
key — renders an instruction block that only editors see:

> This search widget is not configured yet. Click the Configure widget (gear) icon in the widget's
> toolbar and fill in its properties. Select a search index.

The wording adapts to whether the page is in Page Builder edit mode, Page Builder read-only mode, or
preview. On the live site the same widget renders **nothing at all** — no empty container, no broken
control, no console noise.

### What editors see in the Page Builder

A configured widget does **not** mount inside the Page Builder. It renders a server-side static preview
of itself instead — the widget's own markup with every control disabled, no links, and placeholder bars
where the search results would be — under a badge that names the widget and says the content is not
live:

```html
<div class="xps xps-editor-preview xps-editor-preview--facet-list" data-xps-widget="facetList">
  <span class="xps-editor-preview__badge">Search widget: facetList — preview, not live results</span>
  <div class="xps-editor-preview__body" aria-hidden="true">
    <div class="xps-facet-list">…the facet, disabled…</div>
    <p class="xps-editor-preview__note">Attribute: contentType</p>
  </div>
</div>
```

The preview reflects the widget's own properties — the search box's placeholder, the facet's label and
attribute, the results widget's page size and template, the pagination style, the sort options — so
configuring a widget visibly changes what the builder shows.

Why not the real thing: the Page Builder re-renders widget markup over AJAX on every add, move and
configure, so client-side hydration there is unreliable by construction, and no search should run from
the editor. **Preview and the live site are unaffected** — both render the mount element and the working
widget, so preview remains the way to see real results.

`xps-editor-preview` is styled by `shell.css`, which `<xps-search-assets />` loads in the layout the
Page Builder renders too. The classes are part of the markup contract (`themes/MARKUP.md`).

### Server-rendered result templates

On a live or previewed page the Results widget runs the visitor's first search on the server and renders
the cards inside its own mount element, wrapped in `<div data-xps-server-rendered>`. The first paint
therefore needs no JavaScript: a shared result URL — `?q=espresso&page=2&tags=coffee` — arrives with its
results in the HTML, and a visitor without JavaScript still sees them. The search runs through the same
pipeline as `/api/xpsearch/query`, so rules, personalization and analytics apply. If it fails, the
warning goes to the event log and the mount is left empty; a broken search never breaks the page.

**One page load is one search in the analytics.** The widget hands the client the `queryId` of the
search it rendered (`initialQueryId` in `data-xps-instance-config`) and the page size that search
actually used, so the hydration query repeats the same search under the same id: the query log gets
one row per page load, not two, and a click is attributed to it. Only the first query after hydration
carries the id; everything the visitor does afterwards is a search of its own.

**The client takes over on its first render.** The server block is replaced the moment the bundle
hydrates the widget, and every later render — a new query, a facet, page 2 — is the JavaScript client's.
A Razor template controls the first paint and the no-JavaScript visitor, nothing else. To style the list
after hydration, use the `results` widget's `templates.item` option, described in
[Custom widgets](custom-widgets.md), and keep the two in step.

A developer registers a result template with an assembly attribute; editors then pick it in the Results
widget's **Result template** drop-down:

```csharp
using XpSearch.Core.Rendering;

[assembly: RegisterSearchResultTemplate(
    identifier: "MyCompany.ProductCard",
    name: "Product card",
    viewName: "~/Components/Search/_ProductCard.cshtml",
    contentTypes: ["MyCompany.Product"])]
```

`contentTypes` scopes the template: a result whose `contentType` is not listed falls back to the built-in
card, so one selection can cover a mixed result list. Pass an empty array to apply it to everything. The
identifier is also written into `data-xps-config` as `template`.

The view is a partial over `SearchResultViewModel`:

```cshtml
@using XpSearch.Core.Rendering
@model SearchResultViewModel

<article class="xps-result xps-result--product">
    <div class="xps-result__body">
        <h3 class="xps-result__title">
            <a class="xps-result__link" href="@Model.Url">@Model.Title</a>
        </h3>
        @if (Model.Attribute("price") is { Length: > 0 } price)
        {
            <p class="xps-result__price">@price</p>
        }
        @if (Model.Snippet is not null)
        {
            <p class="xps-result__snippet">@Model.Snippet</p>
        }
    </div>
</article>
```

| Member | Type | What it gives you |
|---|---|---|
| `Result` | `XpSearch.Core.Contract.Result` | The result exactly as the search API returned it |
| `Title` | `IHtmlContent` | The title attribute, highlighted when the response highlighted it |
| `Url` | `string` | The link attribute, or `#` |
| `Snippet` | `IHtmlContent?` | The first snippet attribute with a value, highlighted; `null` when there is none |
| `ContentType` / `Image` | `string?` | `contentType` and `image`, or `null` |
| `Path` / `FileType` | `string?` | `path` (the breadcrumb line the built-in card shows under the title) and `fileType` (which makes the built-in card draw a document glyph when there is no image), or `null` |
| `Attribute(name)` | `string?` | Any attribute as text |
| `Highlight(name)` | `IHtmlContent` | Any attribute as HTML: the highlighted form if there is one, else the encoded value |

`Title`, `Snippet` and `Highlight` honour the **Title attribute**, **Link attribute** and **Snippet
attributes** properties, and the same *Fields to show* caveat applies — an attribute your view reads has
to be one the search returns.

An identifier that is not registered, or a view name that does not resolve, logs a warning and renders
the built-in card instead.

The same first paint is available without the widgets: `ServerRenderedResults` lives in
`XpSearch.Core`, so a page that builds its search UI in plain JavaScript can render one too — see
[Server rendering and the mount contract](server-rendering.md).

### Static assets

The client bundle and the two stylesheets ship as Razor Class Library static web assets of the
`XperienceCommunity.Search.Widgets` package:

```text
/_content/XperienceCommunity.Search.Widgets/xpsearch/shell.css        structure only
/_content/XperienceCommunity.Search.Widgets/xpsearch/default.css      the opt-in visual theme
/_content/XperienceCommunity.Search.Widgets/xpsearch/xpsearch.umd.js  the UMD bundle, global `xpsearch`
```

`<xps-search-assets />` emits all three. **This is the quick start: no npm, no build pipeline.** If the
site already has a JavaScript build, the recommended setup is the npm package instead —
[JavaScript bundler setup](javascript-bundler-setup.md).

**One page, one runtime.** A page runs either the tag helper's bundle or your own — never both, or
`.xps-mount` elements get hydrated twice and every keystroke searches twice. If you bundle the npm
package, do not emit `<xps-search-assets />` in that layout (import the stylesheets through your build
instead). Page Builder mounts hydrate from whichever runtime is present, so the editors' experience is
the same either way.

If your site has its own design system, load only the structural stylesheet:

```cshtml
<xps-search-assets default-theme="false" />
```

`@Html.XpSearchAssets()` and `@Html.XpSearchAssets(defaultTheme: false)` do the same from a view that does
not register the tag helper. Both honour the application's path base, so a site hosted in a virtual
directory gets the right URLs.

A project that prefers Kentico's Page Builder bundling can ignore the tag helper, copy the three files
into `~/wwwroot/PageBuilder/Public/Widgets/XpSearch/` and let the bundler emit the tags. Nothing in the
widgets depends on where the files are served from.

### Custom widgets in the Page Builder

A control you wrote yourself can be placed by editors too — subclass
`XpSearchMountWidgetViewComponent<T>` and you get property serialization, instance grouping and the
unconfigured state for free. See [Custom widgets](custom-widgets.md).

### Related pages

- [Quick start](quick-start.md) — indexing, `AddXpSearch`, the first search.
- [Custom widgets](custom-widgets.md) — build your own control and make it placeable.
- [Widget reference](widget-reference.md) — every option of every JavaScript widget.
- [Theming](theming.md) — what `shell.css` and `default.css` each cover.
- [JavaScript client](js-client.md) — the options the mount configuration maps onto.
