# Friction log — building `myCompany.dropdownFacet` from the published docs and packages

Third-party developer, no access to the product's source. Inputs: `docs/*.md`, the four published
packages, and the Kentico documentation for the platform parts.

---

### 1. The flagship 40-line example does not compile under TypeScript strict — blocker (for the example), workaround (for me)

**Trying to do:** copy `custom-widgets.md` → "A dropdown facet in 40 lines" into a `.ts` file.

**Docs say:** the example is JavaScript, but the very next section promises
> "Behaviours are generic over your widget params …, so nothing in a custom widget needs an `any` or
> an import from an internal path"

and the task spec ("TypeScript strict, no `any`") is the normal way an agency would consume this.

**Missing/wrong:** three lines of the example are type errors:
* `event.target.value` — `EventTarget` has no `value`; needs a cast or a different source.
* `container.querySelector('select').addEventListener(...)` — possibly `null`.
* `container: document.querySelector('#facet-brand')` in the usage snippet — `Element | null` against
  the `HTMLElement` the TS snippet below declares.

The TypeScript section of the same page shows a *different*, trivial example
(`params.container.textContent = …`) that sidesteps all three, so nothing on the page shows how the
real one is meant to look in TS.

**Did instead:** read `select.value` inside the handler instead of `event.target`, `if (!select)
return` instead of a non-null assertion, and typed `DropdownFacetParams extends Record<string,
unknown>` so it satisfies `withFacetList`'s constraint. No `any`, no internal imports — that part of
the promise holds.

---

### 2. `registerWidgetType(...)` as documented does not typecheck either — workaround

**Trying to do:** make the widget placeable, exactly as the guide shows:

> ```js
> import { registerWidgetType } from '@yourco/xperience-search';
> registerWidgetType('myCompany.dropdownFacet', (config) => dropdownFacet(config));
> ```

**Types say** (`dist/types/bootstrap.d.ts`):

```ts
export type MountConfig = Record<string, unknown> & { container: HTMLElement };
export type MountWidgetFactory = (config: MountConfig) => Widget;
```

**Missing/wrong:** `config.attribute` is `unknown`, and `withFacetList` requires `attribute: string`,
so the documented one-liner is a compile error in TS. Nothing in `custom-widgets.md` mentions that a
mount config is untyped editor input or suggests validating it — and this *is* a trust boundary:
the JSON comes from whatever an editor typed into the widget dialog.

**Did instead:** a `text(value: unknown): string | undefined` narrower per key, and a thrown error
when `attribute` is missing (the bootstrap is documented to swallow it into one `console.error`, so
the rest of the page still works). Good behaviour, but the docs sell a one-liner and deliver a
20-line function.

---

### 3. The example's CSS classes do not exist anywhere in the product — workaround

**Trying to do:** "Use the product's documented shell/utility classes where the docs say custom
widgets should."

**Docs say:** the example emits `class="xps-dropdown__label"` and `class="xps-dropdown__select"`, and
`custom-widgets.md` guarantees
> "**The shell CSS is available to you.** Layout primitives, focus rings and skeleton classes are
> documented utilities"

while `MARKUP.md` states
> "Changing a class name or an ARIA attribute here is a breaking change … `themes/scripts/check.mjs`
> enforces the three-way agreement: every class in a fixture is styled or documented here, every
> class styled in CSS appears in a fixture, and every class named here appears in a fixture."

**Missing/wrong:** `xps-dropdown` appears in neither `MARKUP.md` nor the shipped
`shell.css`/`default.css` (grep for `dropdown` in both files: 0 hits). So the flagship example
invents two class names outside the very contract the product treats as semver-major, and they are
unstyled. Worse, the example's root element carries **no `xps` class**, so — contrary to
"Accessibility is yours, but scaffolded" — it inherits neither the scoped reset nor the focus ring.
There is also no documented way for a custom widget to get a *themed* `<select>`: the only styled
select in the product is `xps-sort-select__select`, which `MARKUP.md` declares to be the built-in
sortSelect's contract, so borrowing it would be exactly the breakage the same file warns about.

**Did instead:** own BEM block `xps-dropdown-facet__{label,select}` on a root carrying the documented
utilities `xps` and `xps-stack`, plus the documented `--disabled` modifier convention, and told
consumers in the README that they must style the select themselves.

---

### 4. The example produces duplicate, malformed element ids in Page Builder — blocker (for the example)

**Trying to do:** give the `<select>` an id so `<label for>` works.

**Docs say:** the example uses `for="${container.id}-select"`, while `MARKUP.md` rule 4 says
> "**`id` pattern**: `id="xps-{instance}-{widget}-{part}"` … The instance id is `data-xps-instance`
> from the Page Builder mount, or the widget's container id. Ids must be unique across the page".

**Missing/wrong:** a Page Builder mount element has no `id` — both `page-builder-widgets.md` and
`custom-widgets.md` show the mount as class + `data-xps-*` only. So the moment the example is placed
by an editor it renders `id="-select"` on every instance: invalid, duplicated, and the `<label for>`
points at the first one. Nothing tells a custom-widget author how to derive the id, and nothing says
whether the `container` handed to a mount factory is the mount element (it is — `MountConfig` is
documented as "the parsed `data-xps-config` plus `container`, the mount element itself" — which is
what makes `data-xps-instance` readable at all).

**Did instead:** `xps-${container.id || container.getAttribute('data-xps-instance') || 'default'}-dropdown-facet-${n}-select`
with a module-level counter for uniqueness. The counter is a deviation from the documented pattern
that nothing in the docs sanctions.

---

### 5. "Capture the actions, not the data" — and then the example captures the data. It is wrong. — blocker

**Trying to do:** implement single-select (clear the previous value, apply the new one).

**Docs say**, immediately under the example:
> "The event handler is registered once and reads `renderOptions.items` at click time, because
> `renderOptions` is rebuilt on every render — capture the behaviour's actions, not its data."

and in the lifecycle section:
> "`render` runs after every response **and again on every state change** with the previous
> `results`, so controls update the moment they are clicked rather than when the network answers."

**Missing/wrong:** those two statements are in tension and the example follows the losing side. Its
listener closes over the **first** `renderOptions` and reads `.items` from it; if the object really is
"rebuilt on every render", that snapshot is stale. Empirically it is: my first implementation derived
the previous value from the last render, and a second change before a re-render made the widget
*add* the old value back instead of clearing it. The failing assertion, from the jsdom test:

```
expected [ 'Product', 'Article' ] to deeply equal []
```

i.e. after choosing Article then Product, both were active — the shipped example's single-select
semantics break on a fast second selection. The related claim that a state change re-renders
"the moment they are clicked" also did not hold in the test: no synchronous re-render happened
between two changes.

**Did instead:** keep the applied value in `select.dataset.xpsActive`, written both by `render`
(state is authoritative when something else — routing, a clear-filters widget — changes it) and by
the change handler itself (so two changes in a row are correct regardless of render timing). Three
lines, but I had to find the bug with a test the docs never suggest writing.

---

### 6. It is not documented what a behaviour's `apply()` actually does — nit

`custom-widgets.md` lists `apply(value)` in the `withFacetList` row and says a behaviour supplies
"filter dispatch"; `js-client.md` says of `actions` that "Mutators are chainable and none of them
searches; `search()` executes". Whether `apply()` is `toggleFacet` alone or `toggleFacet` +
`search()` is never stated. It matters here: the single-select idiom the docs themselves show calls
`apply` twice in one handler, so a reader cannot tell whether that is one request or two (the
`debounceMs` default presumably coalesces them, which is also not stated). I copied the documented
idiom and moved on. `actions.clearFilters(attribute)` would express single-select in one call, but
it clears numeric filters on the attribute too and — per the quote above — would not search.

---

### 7. `escapeHtml` exists, is exported, and is documented nowhere — workaround

The example interpolates `item.label`, `item.value` and the editor-supplied `label`/`allLabel`
straight into an HTML string, including inside `value="${item.value}"`. A quote in a taxonomy title
or in a label an editor types breaks out of the attribute. `page-builder-widgets.md` is proud of the
C# side doing exactly this correctly —
> "The JSON is HTML-attribute-encoded, so a quote or an angle bracket an editor types into a label
> cannot break out of the attribute."

— and then the JS example drops the value straight back into unescaped markup. `escapeHtml(value)`
*is* exported from the package root (`index.d.ts`), but `grep -n escapeHtml docs/*.md` returns
nothing: no guide mentions it. Found it by reading the `.d.ts`.

**Did instead:** every interpolation goes through `escapeHtml`.

---

### 8. The npm package cannot be installed the documented way, and ships no stylesheets — blocker for a JS-only consumer

* `package.json` in the tarball has `"private": true`, which `npm publish` refuses; a real consumer
  cannot `npm install @yourco/xperience-search` from a registry at all. I installed the tarball by
  path, as instructed for this exercise.
* `"files": ["dist"]` and the `exports` map (`.`, `./behaviors`, `./package.json`) mean
  `theming.md`'s
  > `<link rel="stylesheet" href="/node_modules/@yourco/xperience-search/themes/shell.css">`

  is a 404 — there is no `themes/` folder in the package and no `./themes/*` export. The two
  stylesheets exist only as static web assets of the .NET `YourCo.Xperience.Search.Widgets` package.
  A Kentico site is fine (`<xps-search-assets />`); a front-end build is not.
* No `README.md` in the package, so `npm` shows nothing on install.

---

### 9. The mock server the docs sell cannot be reached from the package — workaround

`js-client.md` → "Run it against the mock server" promises "a dependency-free mock of the search API,
so you can build UI before the endpoint exists", then gives

> ```bash
> cd libraries/xperience-search/src/XpSearch.Client
> npm ci
> npm run mock
> ```

That is a path inside the vendor's repository. The installed package keeps the `mock`/`demo` scripts
in its `package.json` but ships neither `mock/` nor `scripts/`, so `npm run mock` cannot work for a
third party — which is precisely the audience the paragraph addresses.

**Did instead:** stubbed `fetchFn` with a `SearchResponse` literal copied from `search-api.md`. That
worked well; the `SearchResponse` type accepted the documented payload verbatim, which is a genuinely
good property.

---

### 10. Small gaps in the C# unit-testing path — nit

`custom-widgets.md` is unusually good here:
> "`BuildModel(properties)` is public, so a widget's markup can be asserted in a unit test without an
> Xperience application: substitute `IXpSearchEditorContext` and `IXpSearchIndexCatalog`, use the real
> `XpSearchMountRenderer`, and read `model.Mount`."

Three things it does not say, each costing a compile error or a failed assertion:
* `IXpSearchIndexCatalog.GetIndexNames()` returns `IReadOnlyList<string>` (I guessed
  `IEnumerable<string>` → `CS0738`).
* `XpSearchMountViewModel.Mount` is an `IHtmlContent`, so the test needs
  `WriteTo(writer, HtmlEncoder.Default)` to get a string.
* the config JSON in the mount is HTML-attribute-encoded, so assertions need `HtmlDecode` first
  (documented for the markup, not for the test recipe).

All three fell out of the compiler and the XML doc comments within a couple of minutes.

---

### 11. The JS and C# identifiers are two hand-copied magic strings — nit

> "Two registrations make it work end to end … The identifiers must match, dot and all."

They must match *between* `registerWidgetType('myCompany.dropdownFacet')` and
`protected override string WidgetType => "myCompany.dropdownFacet"`, while the `[RegisterWidget]`
identifier is the differently-cased `MyCompany.DropdownFacet`. Three strings, two casings, no shared
constant, and nothing validates the pair: a typo is a `console.error` on a live page, not a build
error. I exported a `WIDGET_TYPE` constant on the JS side to at least keep my own two uses in sync.

---

### 12. Installing from a private feed needs `packageSourceMapping`, which no guide mentions — nit

`dotnet restore` failed with

```
error NU1101: Unable to find package YourCo.Xperience.Search.Widgets … PackageSourceMapping is
enabled, the following source(s) were not considered: xps-local.
```

because this machine enables package source mapping globally. Adding a `<packageSource>` is not
enough; a `<packageSourceMapping>` entry is required too. There is no "install from a private feed"
page, and any customer on a mapped feed will hit this.

---

### 13. What was good (relevant to the verdict)

* **The C# example is correct verbatim.** `DropdownFacetWidgetProperties` /
  `DropdownFacetWidgetViewComponent` compiled first try, 0 warnings with `TreatWarningsAsErrors`, and
  the `[FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier,
  nameof(Index))]` + `Order` pattern matches Kentico's own requirement that a dependency "can only be
  established with properties of a lower order"
  ([Configure editing component state](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/editing-components/configure-editing-component-state))
  and the `[assembly: RegisterWidget(viewComponentType: …)]` form from
  ([Widgets for Page Builder](https://docs.kentico.com/documentation/developers-and-admins/development/builders/page-builder/widgets-for-page-builder)).
  The base class's `OrderFirstWidgetProperty` constant removed the one thing agencies usually get
  wrong. `Kentico.Xperience.Admin.Base.FormAnnotations` arrived transitively through
  `Kentico.Xperience.WebApp`, so the live-site widget assembly needed no admin reference, exactly as
  claimed.
* **The type surface is honest.** `RenderOptions`, `FacetListRenderState`, `SearchActions` and
  `SearchResponse` are fully typed with doc comments; the documented state shape
  (`filters.facets[].values`) is exactly what `getState()` returns, so the test could assert through
  the public API with no internals.
* **The mount bootstrap behaved as documented**: registration by dotted id, grouping by
  `data-xps-instance`, merged `data-xps-instance-config` (including `searchOnInitialLoad: false`),
  and a returned instance per group.

---

## Verdict

**Yes — a competent Kentico agency developer can build this from the docs alone, but not by
following the worked example.** Everything structural is right: the behaviour API is well typed, the
C# base class does what it promises, the contract fixtures are accurate, and the two-registration
model is explained clearly enough that the Page Builder half took one compile. What is not
trustworthy is the flagship JavaScript example itself — it does not typecheck, it emits class names
that exist nowhere in the product, it emits `id="-select"` in the exact environment (Page Builder)
that the same page tells you to place it in, it interpolates editor input into HTML without the
`escapeHtml` the package exports, and its single-select logic is provably wrong when two selections
happen without an intervening render. A developer who copies it ships a broken control; a developer
who tests it (nothing in the docs suggests testing a widget) finds four bugs and ends at ~95 lines,
not "40 lines of JS". **The single most useful change: replace that example with the real
strict-TypeScript version — id derivation, escaping, mount-config narrowing, render-safe state — and
add its classes to `MARKUP.md`, `shell.css` and the fixtures, so the product's own three-way class
contract covers the control it tells third parties to build.**
