# myCompany.dropdownFacet

A single-select drop-down facet for [Xperience Search](../../docs/guides/custom-widgets.md): a
labelled `<select>` with an "All" option and one option per facet value, `label (count)`. Picking a
value clears the previously picked one, so the control filters on at most one value at a time.

`src/dropdownFacet.ts` **is** the worked example in
[Custom widgets](../../docs/guides/custom-widgets.md) — the guide shows this file, and CI builds it
against the packed packages, so the example cannot rot.

Built entirely on the published packages — `@yourco/xperience-search` (`withFacetList`) and
`xperience-community.Xperience.Search.Widgets` (`XpSearchMountWidgetViewComponent<T>`). No forking, no internal
imports, no `any`, TypeScript strict.

```
src/dropdownFacet.ts                          the JavaScript widget + registerWidgetType
test/dropdownFacet.test.ts                    jsdom tests (vitest)
dotnet/CustomWidget.Dropdown.Widget/          the Page Builder widget (net8.0)
dotnet/CustomWidget.Dropdown.Tests/           NUnit tests over the emitted mount
nuget.config                                  points at ../.feed, the local package feed
```

Build and test it with `node samples/pack-and-build.mjs` from the repository root — see
[samples/README.md](../README.md) for why it packs first.

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

`data-xps-config` is editor input and is validated with `readMountConfig`: `attribute` must be a
non-empty string, `label` and `allLabel` are optional strings. A missing `attribute` throws, which
the bootstrap turns into one `console.error` and a skipped widget — the rest of the page still works.

### Markup and styling

```html
<div class="xps xps-stack xps-select">
  <label class="xps-select__label" for="xps-{instance}-dropdown-facet-control">Brand</label>
  <select class="xps-select__control" id="xps-{instance}-dropdown-facet-control">…</select>
</div>
```

Every class is a documented utility from
[`themes/MARKUP.md`](../../themes/MARKUP.md): `xps` for the scoped reset and the focus ring,
`xps-stack` for the spacing rhythm, and the shared `xps-select` block — the same one `sortSelect`
renders — for a themed `<select>` with no CSS of your own. The id follows MARKUP.md rule 4 and comes
from the exported `widgetId(container, widget, part)`. When the behaviour reports `canApply: false`
the control gets the real `disabled` attribute *and* the `xps-select--disabled` modifier.

## C#

```xml
<PackageReference Include="xperience-community.Xperience.Search.Widgets" Version="0.1.0" />
```

Copy `dotnet/CustomWidget.Dropdown.Widget/DropdownFacetWidget.cs` into your web project (or
reference the project). It registers itself through `[assembly: RegisterWidget]`, so
**Search - Dropdown filter** appears in the Page Builder widget list as soon as the assembly is
loaded. The host application needs the standard Xperience Search setup —
`services.AddXpSearch()`, `services.AddXpSearchWidgets()`, `app.UseXpSearch()`,
`<xps-search-assets />` — plus a reference to `XperienceCommunity.Search.Admin` in the administration
project, without which the **Attribute** drop-down stays hidden.

Editor properties: **Search index** and **Instance ID** (from the base class), then **Attribute**
(a drop-down of the selected index's facetable fields), **Label** and **"All" option text**.
Without an attribute the widget renders an instruction block for editors and nothing at all on the
live site.

The identifiers must match, dot and all: `registerWidgetType('myCompany.dropdownFacet')` and
`WidgetType => "myCompany.dropdownFacet"` are the same string in two languages, while
`[RegisterWidget(identifier: "MyCompany.DropdownFacet")]` is the Xperience-side, Pascal-cased one.

## Tests

```bash
npm test                                              # 6 vitest tests
cd dotnet && dotnet test                              # 3 NUnit tests
```

The vitest suite stubs `fetch` with a documented-shape `SearchResponse`, asserts the rendered
options, the markup contract and the escaping, and asserts through `search.actions.getState()` that
changing the selection replaces the active facet value rather than adding to it — including two
changes in a row with no render in between. The NUnit suite calls the base class's public
`BuildModel(properties)` and asserts the emitted mount element.
