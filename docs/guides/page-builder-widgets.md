## Page Builder widgets

Xperience Search ships seven Page Builder widgets. An editor drags them onto a page in any section, in
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

Reference `YourCo.Xperience.Search.Admin` as well. It carries the form component configurator behind the
facet list's attribute drop-down; without it that one field stays hidden. Nothing else needs it, and no
live-site code takes a dependency on `Kentico.Xperience.Admin` either way.

### The seven widgets

| Widget in the Page Builder | Identifier | Emits `data-xps-widget` |
|---|---|---|
| Search - Search box | `XpSearch.SearchBox` | `searchBox` |
| Search - Results | `XpSearch.Results` | `results` |
| Search - Facet list | `XpSearch.FacetList` | `facetList` |
| Search - Pagination | `XpSearch.Pagination` | `pagination` or `loadMore` |
| Search - Result stats | `XpSearch.ResultStats` | `resultStats` |
| Search - Sort selector | `XpSearch.SortSelect` | `sortSelect` |
| Search - Suggestions | `XpSearch.Suggestions` | `suggestions` |

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
| Search box | Placeholder · Show reset button · Focus on page load |
| Results | Results per page · Result template · Fields to show (one attribute name per line — `title`, `url`, `contentType`, `language` or any field of your content types) |
| Facet list | Attribute · Label · Operator (any / all of the selected values) · Values shown · Show a "show more" button |
| Pagination | Style (numbered pages / load more button) |
| Result stats | Text template (`{total}`, `{tookMs}`, `{query}`, `{page}`, `{totalPages}`) · Text before the first search |
| Sort selector | Sort options (one `key;Label` per line) · Label · Hide the label visually |
| Suggestions | Mode (matching documents / popular queries) · Maximum items |

A blank text property is left out of `data-xps-config` entirely, so the JavaScript widget's own default
applies rather than an empty string overriding it.

#### The attribute drop-down is filled from the index

The facet list's **Attribute** property is not a free-text field. It is a drop-down populated from the
selected index's actual schema, listing only fields that are facetable. Pick the index first: until you
do, the attribute field is hidden, and changing the index repopulates it.

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
own state, its own requests and its own URL parameters. Leaving **Instance ID** at `default` is right for
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

### Result templates

A developer registers a result template with an assembly attribute; editors then pick it in the Results
widget's **Result template** drop-down:

```csharp
[assembly: RegisterSearchResultTemplate(
    identifier: "MyCompany.ProductCard",
    name: "Product card",
    viewName: "~/Components/Search/_ProductCard.cshtml",
    contentTypes: ["MyCompany.Product"])]
```

The chosen identifier is written into `data-xps-config` as `template`. Registration and selection are all
that is implemented today — the server does not yet render the view for the initial page load. Until it
does, style the result item on the JavaScript side with the `results` widget's `templates.item` option,
described in [Custom widgets](custom-widgets.md).

### Static assets

The client bundle and the two stylesheets ship as Razor Class Library static web assets of the
`YourCo.Xperience.Search.Widgets` package:

```text
/_content/YourCo.Xperience.Search.Widgets/xpsearch/shell.css        structure only
/_content/YourCo.Xperience.Search.Widgets/xpsearch/default.css      the opt-in visual theme
/_content/YourCo.Xperience.Search.Widgets/xpsearch/xpsearch.umd.js  the UMD bundle, global `xpsearch`
```

`<xps-search-assets />` emits all three. If your site has its own design system, load only the structural
stylesheet:

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
