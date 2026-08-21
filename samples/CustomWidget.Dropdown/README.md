# myCompany.dropdownFacet

A single-select drop-down facet for [Xperience Search](../docs/custom-widgets.md): a labelled
`<select>` with an "All" option and one option per facet value, `label (count)`. Picking a value
clears the previously picked one, so the control filters on at most one value at a time.

Built entirely on the published packages — `@yourco/xperience-search` (`withFacetList`) and
`YourCo.Xperience.Search.Widgets` (`XpSearchMountWidgetViewComponent<T>`). No forking, no internal
imports.

```
src/dropdownFacet.ts                          the JavaScript widget + registerWidgetType
test/dropdownFacet.test.ts                    jsdom tests (vitest)
dotnet/CustomWidget.Dropdown.Widget/          the Page Builder widget (net8.0)
dotnet/CustomWidget.Dropdown.Tests/           NUnit tests over the emitted mount
nuget.config                                  points at the local package feed
FRICTION.md                                   what the docs made us guess
```

## JavaScript

```bash
npm install @yourco/xperience-search
```

Copy `src/dropdownFacet.ts` into your front-end project and add the widget to a search instance:

```ts
import createSearch from '@yourco/xperience-search';
import { dropdownFacet } from './dropdownFacet';

const search = createSearch({ index: 'site-content', routing: true });

search.addWidgets([
  dropdownFacet({
    container: document.querySelector<HTMLElement>('#facet-brand')!,
    attribute: 'brand',
    label: 'Brand',
    allLabel: 'Any brand',
    limit: 50,
    sortBy: ['name:asc'],
  }),
]);

search.start();
```

`container` must be an `HTMLElement` (the built-in widgets also accept a selector string; this one
does not). Everything `withFacetList` accepts — `attribute`, `operator`, `limit`, `showMore`,
`showMoreLimit`, `sortBy`, `transformItems` — is accepted here too.

### Page Builder

Register the factory once, before `mountAll()` runs, so that
`data-xps-widget="myCompany.dropdownFacet"` resolves:

```ts
import { mountAll } from '@yourco/xperience-search';
import { registerDropdownFacet } from './dropdownFacet';

registerDropdownFacet();
mountAll();
```

From the UMD bundle, `mountAll()` runs itself on `DOMContentLoaded`, so the registration script must
execute before that event — load it synchronously in `<head>` or before the bundle's own script tag.

`data-xps-config` is validated: `attribute` must be a non-empty string, `label` and `allLabel` are
optional strings and anything else falls back to the widget's defaults (`Filter`, `All`).

### Markup and styling

```html
<div class="xps xps-dropdown-facet xps-stack">
  <label class="xps-dropdown-facet__label" for="xps-{instance}-dropdown-facet-{n}-select">Brand</label>
  <select class="xps-dropdown-facet__select" id="xps-{instance}-dropdown-facet-{n}-select">…</select>
</div>
```

The root carries `xps`, so `shell.css`'s reset and focus ring apply, and `xps-stack` for the
spacing rhythm. `xps-dropdown-facet*` is this widget's own block: neither `shell.css` nor
`default.css` styles a `<select>` outside `xps-sort-select__select`, so give it your own rule if you
want it to match the shipped sort selector (see FRICTION.md #3). The `<select>` gets the `disabled`
attribute and a `--disabled` modifier whenever the behaviour reports `canApply: false`.

## C#

```xml
<PackageReference Include="YourCo.Xperience.Search.Widgets" Version="0.1.0" />
```

Copy `dotnet/CustomWidget.Dropdown.Widget/DropdownFacetWidget.cs` into your web project (or
reference the project). It registers itself through `[assembly: RegisterWidget]`, so
**Search - Dropdown filter** appears in the Page Builder widget list as soon as the assembly is
loaded. The host application needs the standard Xperience Search setup —
`services.AddXpSearch()`, `services.AddXpSearchWidgets()`, `app.UseXpSearch()`,
`<xps-search-assets />` — plus a reference to `YourCo.Xperience.Search.Admin` in the administration
project, without which the **Attribute** drop-down stays hidden.

Editor properties: **Search index** and **Instance ID** (from the base class), then **Attribute**
(a drop-down of the selected index's facetable fields), **Label** and **"All" option text**.
Without an attribute the widget renders an instruction block for editors and nothing at all on the
live site.

## Tests

```bash
npm install && npx tsc --noEmit && npx vitest run     # 3 tests
cd dotnet && dotnet build && dotnet test               # 3 tests
```

The vitest suite stubs `fetch` with a documented-shape `SearchResponse`, asserts the rendered
options and asserts through `search.actions.getState()` that changing the selection replaces the
active facet value rather than adding to it. The NUnit suite calls the base class's public
`BuildModel(properties)` and asserts the emitted mount element.
