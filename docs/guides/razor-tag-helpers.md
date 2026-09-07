## Razor tag helpers

Write a search page as Razor markup instead of hand-written mount divs. Every JavaScript widget has a
tag element, the tag element renders the mount the bundle hydrates, and the same element is what the
Page Builder widgets render underneath — one implementation, three ways to reach it.

```cshtml
@* Views/Search/Index.cshtml *@
<xps-search-styles />

<xps-search index="site-content">
    <xps-search-box placeholder="Search…" enable-suggestions="true" />

    <xps-facet-list attribute="contentType" label="Content type" limit="10" />
    <xps-result-stats />
    <xps-results results-per-page="10" />
    <xps-pagination />
</xps-search>

<xps-search-scripts />
```

That page renders one `<div class="xps-mount" …>` per tag, all of them in the `default` search
instance on the `site-content` index, and the first page of results is already in the HTML before the
bundle runs. Nothing else is needed: no `@section scripts`, no JavaScript of your own.

### Setup

```csharp
// Program.cs
builder.Services.AddKenticoLucene();
builder.Services.AddXpSearch();          // the search API and the server-rendered first paint
builder.Services.AddXpSearchWidgets();   // the tag helpers (and the Page Builder widgets)

var app = builder.Build();
app.UseStaticFiles();                    // serves the stylesheets and the bundle
app.UseXpSearch();                       // maps /api/xpsearch/*
```

```cshtml
@* Views/_ViewImports.cshtml — once per application *@
@addTagHelper *, XpSearch.Widgets
```

`XpSearch.Widgets` is the **assembly** name; the NuGet package is
`XperienceCommunity.Search.Widgets`. Without that line Razor treats `<xps-search-box />` as an unknown
HTML element and emits it verbatim, which is the usual cause of "my tag renders as literal markup".

### Assets: `<xps-search-styles>`, `<xps-search-scripts>`, `<xps-search-assets>`

The client is two stylesheets and one script, shipped as static web assets of the package
(`/_content/XperienceCommunity.Search.Widgets/xpsearch/…`) and served by `app.UseStaticFiles()`.
Emit them once per page. Use the split pair when you want the styles in `<head>` and the bundle at the
end of the body, and `<xps-search-assets />` — the shorthand for both, in load order — when you do not
care.

```cshtml
<head>
    <xps-search-styles theme="kentico-orange" />
</head>
<body>
    @RenderBody()
    <xps-search-scripts />
</body>
```

| Tag | Attribute | Type | Default | Emits |
|---|---|---|---|---|
| `<xps-search-styles />` | `default-theme` | bool | `true` | `shell.css`, plus the palette unless `default-theme="false"` |
| `<xps-search-styles />` | `theme` | string | `default` | one of `default` (= `kentico-violet`), `kentico-violet`, `kentico-orange` |
| `<xps-search-scripts />` | — | — | — | `<script src="…/xpsearch.umd.js" defer>` |
| `<xps-search-assets />` | `default-theme`, `theme` | as above | as above | the links followed by the script |

An unknown `theme` throws `ArgumentException` naming the shipped palettes — the name never becomes
part of a URL. All three honour the application's path base, so they work under a virtual directory.
For views that prefer C#, `@Html.XpSearchStyles()`, `@Html.XpSearchScripts()` and
`@Html.XpSearchAssets()` emit exactly the same bytes and take the same two arguments
(`defaultTheme`, `theme`).

Building the theme yourself instead? See [Theming](theming.md) and
[JavaScript bundler setup](javascript-bundler-setup.md) — a page built from your own bundle uses the
same tags for everything except the script.

### `<xps-search>` — the scope

`<xps-search>` renders no element of its own. It publishes the index, the instance id and the
instance-wide search options to every Xperience Search tag inside it, so they are written once instead
of on every widget.

```cshtml
<xps-search index="site-content" instance="products" routing="true" fields="title,url,summary">
    <xps-search-box />
    <xps-load-more />
</xps-search>
```

| Attribute | Type | Default | Meaning |
|---|---|---|---|
| `index` | string | – | Code name of the index the widgets inside search |
| `instance` | string | `default` | The search the widgets inside join; two scopes with different ids are two independent searches on one page |
| `routing` | bool | – | Whether the search keeps its query, filters and page in the address bar |
| `page-size` | int | – | How many results one page holds |
| `fields` | string list | – | The index fields retrieved for each result |

`routing`, `page-size` and `fields` are instance-wide: the bootstrap merges them across the mounts of
the instance. The scope never overwrites what a widget itself said — `<xps-results results-per-page="6">`
inside a scope with `page-size="24"` searches with 6 — and because `<xps-results>` always states a page
size, `page-size` on the scope is for pages whose result list is `<xps-load-more>`.

**Precedence, for `index` and `instance`:** the tag's own attribute wins, then the options record
handed to `options="@model"`, then the enclosing `<xps-search>`, and finally — for `index` only — the
project's sole index if it has exactly one. An `index` that resolves to nothing is a render-time
failure, not an empty div.

### Every widget tag takes these three

| Attribute | Type | Default | Meaning |
|---|---|---|---|
| `index` | string | – | The index to search; see the precedence rule above |
| `instance` | string | `default` | The search this widget joins |
| `options` | the widget's options record | – | The whole configuration as one object, `options="@model"` |

`options` is the C# vocabulary of the same widget: `SearchBoxOptions`, `ResultsOptions`,
`FacetListOptions` and so on, one sealed record per widget in `XpSearch.Widgets.Options`, free of
Kentico and Page Builder types. Individual attributes win over the record, so a view can carry a
configured record from its model and still override one value in the markup:

```cshtml
@model SearchPageModel
<xps-facet-list options="@Model.ContentTypeFacet" limit="25" />
```

### Failure semantics

A tag that cannot render **throws** `InvalidOperationException` at render time, with the same message
the Page Builder shows an editor:

| Message | Raised by |
|---|---|
| Select a search index. | any tag, when no `index` is named and the project has more than one |
| Select the attribute this facet filters on. | `<xps-facet-list>`, `<xps-category-tree>`, `<xps-toggle-filter>`, `<xps-range-filter>` |
| Enter a Minimum and a Maximum for the range, with the maximum greater than the minimum. | `<xps-range-filter>` |
| Enter at least one valid sort option, one per line, in the form key;Label. | `<xps-sort-select>` |
| Enter at least one facet group, one per line, in the form attribute;Label. | `<xps-filter-sort>` |
| Name the JavaScript widget in the type attribute. | `<xps-widget>` |

A Razor developer gets the failure where the mistake is,
rather than an empty div and a silent page. (The Page Builder widgets over the same tag helpers treat
the identical message differently — an editor sees an instruction block and a visitor sees nothing,
because a half-configured widget must never break a live page.)

### The server-rendered first paint

`<xps-results>` runs the visitor's search in the request that renders the page and writes the cards
inside its mount, wrapped in `<div data-xps-server-rendered>` — so a shared result URL
(`?q=espresso&page=2&tags=coffee`) arrives with its results in the HTML and a visitor without
JavaScript still sees them. It hands the client the page size and the `queryId` the server used, so
the page load is journaled once. Nothing is needed to switch it on beyond `AddXpSearch()`; the search
that could not run is a logged warning and an empty mount, never a broken page. See
[Server rendering and the mount contract](server-rendering.md) for the details, the result templates
and the plain-`XpSearch.Core` path.

### `<xps-search-box>`

The query input, with an optional suggestions combobox.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `placeholder` | string | – | Empty keeps the JavaScript default |
| `show-reset` | bool | `true` | The clear button, offered once the visitor has typed |
| `autofocus` | bool | `false` | Takes focus on page load |
| `enable-suggestions` | bool | `false` | Turns the input into a combobox with a suggestions panel |
| `suggestion-limit` | int | `5` | Only used when suggestions are on |
| `recent-searches` | bool | `true` | Offers this visitor's own recent searches in the panel |
| `sync-state-to-url` | bool | `true` | Instance-wide: writes `routing` for the whole search |

`sync-state-to-url` is emitted whether on or off, so a second, deliberately non-syncing search on the
page reads as configured rather than forgotten. `enable-suggestions`, `suggestion-limit` and
`recent-searches` become one nested `suggestions` group in the config — absent means off.

### `<xps-results>`

The result list, and the page's server-rendered first paint.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `results-per-page` | int | `20` | Instance-wide: pagination steps in it. The index's maximum page size may clamp it |
| `template` | string | – | Identifier of a registered server-side result template |
| `fields` | string list | – | Instance-wide: the document fields retrieved. Empty retrieves the index defaults |
| `title-attribute` | string | – | What the default card reads the title from. Empty keeps `title` |
| `url-attribute` | string | – | What the default card links to. Empty keeps `url` |
| `snippet-attributes` | string list | – | Tried in order for the snippet. Empty keeps summary, content, excerpt |

### `<xps-load-more>`

The result list that appends the next page instead of replacing it. It renders the cards itself and
owns the page, so it **stands in for** `<xps-results>` and `<xps-pagination>` rather than joining
them — a page with both runs two lists fighting over the same page number.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `auto-load` | bool | `true` | Loads the next page when the end of the list scrolls into view |
| `title-attribute` | string | – | What the default card reads the title from. Empty keeps `title` |
| `url-attribute` | string | – | What the default card links to. Empty keeps `url` |
| `snippet-attributes` | string list | – | Tried in order for the snippet. Empty keeps summary, content, excerpt |
| `more-label` | string | – | Button text while there is more. Empty keeps "Load more results" |
| `exhausted-label` | string | – | Button text once everything is loaded. Empty keeps "No more results" |

### `<xps-pagination>`

Moving between the pages of an `<xps-results>` list.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `style` | string | `numbered` | `numbered` mounts the `pagination` widget; `loadMore` mounts the `loadMore` widget instead |

The style picks the JavaScript widget rather than becoming an option of one, so
`<xps-pagination style="loadMore" />` emits `data-xps-widget="loadMore"`. Reach for `<xps-load-more>`
instead when you want the endless list's own options.

### `<xps-facet-list>`

Filter checkboxes for one attribute, with counts.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `attribute` | string | – | Required. The index attribute the facet filters on |
| `label` | string | – | The heading shown above the values |
| `operator` | string | `or` | How several selected values combine: `or` or `and` |
| `limit` | int | `10` | How many values are listed before "show more" |
| `show-more` | bool | `false` | Offers a "show more" button for the remaining values |
| `collapsible` | bool | `true` | The group's title folds the values away |

### `<xps-category-tree>`

A taxonomy attribute as a drill-down tree.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `attribute` | string | – | Required. The index attribute the tree navigates |
| `label` | string | – | The heading shown above the tree |
| `limit` | int | `10` | How many nodes are listed at each level |
| `collapsible` | bool | `true` | The tree's title folds it away |

### `<xps-toggle-filter>`

One checkbox for a single value of a facet attribute — "In stock", "Free shipping".

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `attribute` | string | – | Required. The index attribute the checkbox filters on |
| `value` | string | `true` | The single value it filters on |
| `label` | string | – | The visible text. Empty leaves the label the server gave the value |
| `show-count` | bool | `true` | Shows the number of matching documents beside the label |

### `<xps-range-filter>`

A numeric or date range, as two sliders and two number inputs.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `attribute` | string | – | Required. The numeric or date index attribute the range narrows |
| `label` | string | – | The heading shown above the control |
| `minimum` | decimal | – | Required. The response carries no corpus statistics, so the bounds are yours |
| `maximum` | decimal | – | Required, and greater than `minimum` |
| `step` | decimal | `1` | Step of the sliders and the number inputs |
| `from-label` | string | – | Visible label of the lower input. Empty leaves "From" |
| `to-label` | string | – | Visible label of the upper input. Empty leaves "To" |
| `unit` | string | – | Shown after the two inputs, such as "USD" or "kg" |

### `<xps-active-filters>`

The visitor's refinements as removable chips.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `title` | string | – | The heading screen readers announce for the chip list. Never shown on screen |
| `scroll` | bool | `false` | Keeps the chips on one scrolling row instead of wrapping |

### `<xps-clear-filters>`

The button that removes every refinement.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `label` | string | – | The button text. Empty keeps "Clear all" |

### `<xps-result-stats>`

The "N results for …" line.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `text-template` | string | `{total} results for “{query}” ({tookMs} ms)` | Placeholders: `{total}`, `{tookMs}`, `{query}`, `{page}`, `{totalPages}` |
| `empty-text` | string | – | Shown before the first search runs |

### `<xps-sort-select>`

The order of the results.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `sort-options` | string | `relevance;Most relevant` | One order per line, `key;Label` |
| `label` | string | – | Label of the selector. Empty keeps the JavaScript default |
| `hide-label` | bool | `false` | Hides the label from sighted users; screen readers keep it |

A key is `relevance`, a sort key configured for the index, or a sortable field with an `_asc` /
`_desc` suffix. Keys the index does not publish are dropped, and a selector whose every key was
dropped is a render-time failure rather than an empty control.

### `<xps-filter-sort>`

The mobile filter-and-sort sheet: one trigger button that opens every facet and the sort options in a
full-height sheet.

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `facets` | string | – | Required. The facet groups the sheet shows, one per line as `attribute;Label` |
| `sort-options` | string | – | The offered orders, one per line as `key;Label`. Empty hides the sort section |
| `label` | string | – | Trigger and sheet heading. Empty keeps the JavaScript default |
| `apply-label` | string | – | Primary button text; `{count}` becomes the pending result count. Empty keeps "Show {count} results" |

### `<xps-suggestions>`

Type-ahead suggestions as a standalone combobox, for a page whose search box is elsewhere (a header).

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `mode` | string | `documents` | `documents`, `querySuggestions` or `mixed`. Records the intent; what an index answers with is configured per index in code |
| `limit` | int | `5` | Capped by the index's maximum suggestion count |
| `recent-searches` | bool | `true` | Offers this visitor's own recent searches in the panel |

### `<xps-widget>` — any widget registered in JavaScript

A widget you registered with `registerWidgetType()` needs no C# of its own: `<xps-widget>` mounts any
type by name, and serializes an anonymous object into `data-xps-config`.

```cshtml
<xps-widget type="myCompany.dropdownFacet"
            config='@(new { attribute = "brand", label = "Brand", allLabel = "All" })' />
```

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `type` | string | – | Required. The `registerWidgetType()` identifier, which must contain a dot |
| `config` | object | – | The widget's own options. A POCO or anonymous object is camel-cased property by property; a dictionary is copied key for key |
| `instance-config` | object | – | Instance options beyond `index`, read the same way |

Give the widget a tag of its own instead — `<my-dropdown-facet attribute="brand" />` — by deriving
`XpSearchMountTagHelper<TOptions>` and registering the pair with
`services.AddXpSearchWidget<TTagHelper, TOptions>()`. See
[Custom widgets](custom-widgets.md#a-tag-helper-of-your-own) for the worked example.

### `Html.XpSearchAsync`

One generic extension renders any widget from its options record — the same bytes the tag element
emits, useful when the widget is chosen at runtime:

```cshtml
@using XpSearch.Widgets.Options
@using XpSearch.Widgets.Rendering

@foreach (var facet in Model.Facets)
{
    @await Html.XpSearchAsync(new FacetListOptions
    {
        Index = "site-content",
        Attribute = facet.Attribute,
        Label = facet.Label,
        Limit = 10
    })
}
```

It resolves the widget's tag helper from DI by the options type, so a widget that was never registered
with `AddXpSearchWidget<TTagHelper, TOptions>()` throws `InvalidOperationException`, and so does an
options record that fails validation. There are no per-widget `Html.*` methods: the tag element is the
Razor surface, this is the escape hatch.

### Related pages

- [Building a search page](building-a-search-page.md) — the same results page in plain HTML, in Razor and in the Page Builder.
- [Widget reference](widget-reference.md) — what each widget renders, its JavaScript params and the XSS model.
- [Page Builder widgets](page-builder-widgets.md) — the editor-facing layer over these same tag helpers.
- [Server rendering and the mount contract](server-rendering.md) — the first paint and the stable mount attributes.
- [Custom widgets](custom-widgets.md) — a widget of your own in JavaScript, as a tag helper, and in the Page Builder.
- [Theming](theming.md) — the stylesheets the asset tags load, and how to replace them.
