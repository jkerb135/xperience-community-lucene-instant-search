# Markup contract

This file, together with `themes/fixtures/*.html`, is the contract between the stylesheets
(`themes/src/shell.css` and one theme file — `default.css`, `kentico-violet.css` or
`kentico-orange.css`, all generated from `themes/src/scss/`, run `npm run build` in `themes/` after
editing) and the default widget renderers in
`XpSearch.Widgets/Client`. The fixtures are the normative form: they are the exact DOM a widget must
produce with no custom templates. This file explains what each class and attribute means.

Changing a class name or an ARIA attribute here is a breaking change for anyone who has
written CSS against the widgets, and is treated as a semver-major event.

`themes/scripts/check.mjs` (`npm run check`) enforces the three-way agreement: every class in a
fixture is styled or documented here, every class styled in CSS appears in a fixture, and every
class named here appears in a fixture.

## Rules that apply to every widget

1. **Every widget root carries two classes: `xps` and its own root class** — for example
   `<div class="xps xps-results">`. `default.css` declares its custom properties on `.xps`, so a
   widget is self-theming wherever it lands; `shell.css` scopes its reset and focus ring to
   `.xps` for the same reason. Wrapping a whole search page in one `.xps` element also works —
   the nested re-declaration is a no-op — and is the cheaper option when you override variables.
2. **Prefix everything `xps-`, BEM-ish**: `block__element--modifier`. Modifiers live on the
   element they modify, and the element keeps its base class
   (`class="xps-facet-list__item xps-facet-list__item--selected"`).
3. **Optional parts are toggled with the `hidden` attribute**, not by removing them from the DOM
   and not by a `--hidden` modifier. Shell has `.xps [hidden] { display: none !important }`, which
   also keeps a host stylesheet from revealing them.
4. **`id` pattern**: `id="xps-{instance}-{widget}-{part}"` (`id="xps-search-1-search-box-input"`).
   The instance segment is `data-xps-instance` from the Page Builder mount, else the widget's
   container id, else the literal `default` — a Page Builder mount element carries **no `id`**, so
   `data-xps-instance` is the only thing that separates two instances on one page.
   Ids must be unique across the page, so when the same widget is placed twice in one instance the
   second one's `{widget}` segment gets `-2` appended (`id="xps-search-1-sort-select-2-select"`), the
   third `-3`, and so on; every part of one widget shares the one prefix, and a re-render never
   renumbers it. `widgetId(container, widget, part)` is exported from
   `@xperience-community/xperience-search` and is the single implementation of this rule — the shipped
   renderers and custom widgets call the same function, so nobody has to re-derive it.
5. **Disabled controls use the real attribute** (`disabled` on form controls, `aria-disabled="true"`
   on links and on elements that stay focusable), *plus* the `--disabled` modifier for styling.
6. **Never remove focus.** Shell draws the ring with `:focus-visible` in `currentColor`; renderers
   must not set `tabindex="-1"` on anything a keyboard user needs to reach.
7. **Text is never a `div` that acts like a control.** Buttons are `<button>`, checkboxes are
   `<input type="checkbox">`, links that navigate are `<a href>`.

## The two layers, and the one root selector

**`shell.css` is structure only** — layout, box model, positioning, sizing, visibility and state
mechanics, focus and screen-reader mechanics. No colours, backgrounds, border colours or styles,
fonts, shadows or radii live in it. **`default.css` is the design** (`docs/internal/design/*.dc.html`).
A custom theme replaces `default.css` and starts from that bare structure; `themes/scripts/check.mjs`
enforces the split on every build.

**One root selector: `.xps`.** `default.css` states it three times — `.xps.xps.xps` — on every rule
it owns, so each lands at (0,3,0) or more:

| Tier | Example | What it is |
|---|---|---|
| (0,1,0) | `.xps { --xps-color-accent: … }` | the tokens, deliberately weak: they are the documented override surface |
| (0,3,0) | `.xps.xps.xps *` | the element reset — every property a host stylesheet could otherwise reach |
| (0,3,1) | `.xps.xps.xps h3` | element defaults (headings, `strong`, links, cursors) |
| (0,4,0)+ | `.xps.xps.xps .xps-button` | the component rules, which paint the design |

A site's own stylesheet reaches (0,2,1) without knowing our class names (`button`, `.section h3`,
`.landing-page ul li`, `.product-filter input[type="checkbox"]`), so with the theme loaded a widget
is a **closed styling boundary**: nothing outside it decides how anything inside it looks.
`themes/scripts/check-isolation.mjs` renders every fixture twice — with and without
`themes/test/site-hostile.css`, which is Dancing Goat's own CSS re-pointed at our markup — and
compares `getComputedStyle` of every element, property by property. No `!important` anywhere: a host
that deliberately wants its own style writes a selector at least as specific as the theme's
(see `docs/guides/theming.md`, "Specificity and host styles").

Page-level utilities the host puts on its *own* elements (`xps-toolbar`, `xps-stack`, `xps-cluster`,
`xps-mount`, `xps-sidebar__*`) are outside that boundary by definition — they are the host's boxes.

## Theming hooks

- `data-xps-theme="auto"` on the element carrying `xps` opts that subtree into the
  `prefers-color-scheme: dark` variable set in `default.css`. It is opt-in because the theme does
  not control the host page's background. Renderers should pass the attribute through from
  configuration, never set it themselves.
- The theme ships in two palettes, `kentico-violet` (which `default.css` is) and `kentico-orange`.
  They are the same rules with two token values swapped, so the markup contract is identical and a
  page loads exactly one of them. Selection is a stylesheet choice, never a class or an attribute
  on the markup.
- Shell gives `xps-suggestions__panel` position but no surface, leaves `xps-highlight` unpainted and
  the skeletons untinted — all of them need a colour, which shell does not have. A site running
  shell alone supplies them.

## Utilities (available to custom widgets — spec §5.7)

Fixture: `fixtures/utilities.html`. These are the only classes a custom behaviour renderer needs
to know to inherit accessible defaults.

| Class | What it does |
|---|---|
| `xps` | Theme scope. Put it on your widget root. Declares `--xps-space` (shell) and the full variable set (default theme). |
| `xps-sr-only` | Visually hidden, still announced. Use for labels and status text. |
| `xps-stack` | Vertical flex, `gap: var(--xps-space)`. |
| `xps-cluster` | Horizontal flex that wraps, `gap: var(--xps-space)`. |
| `xps-button` | Button box: inline-flex, `2.25rem` minimum height, symmetric padding. Shell adds no colour; the default theme gives it a surface, border and radius. |
| `xps-button--primary` | Accent-filled button (default theme only). |
| `xps-button--link` | Muted-link button (default theme only): no box, underlines on hover, still a real `<button>` at full hit size. What `clearFilters` renders. |
| `xps-select` | Labelled-select box: flex row, `gap` half `--xps-space`. Children: `xps-select__label` (a real `<label for>`; add `xps-sr-only` to hide it) and `xps-select__control` (the native `<select>`, styled like every other form control by the default theme). Wrap the control in `xps-select__field` with an `xps-select__chevron` `<svg>` beside it for the design's own arrow — that wrapper, and only that wrapper, drops the platform arrow, so a bare `xps-select` keeps it. Modifier `xps-select--disabled` pairs with the `disabled` attribute on the control (rule 5). The only themed `<select>` in the product — `sortSelect` renders this same block, and a custom widget that needs a drop-down should too. |
| `xps-toolbar` | The row above the results: stats left, sort right, wrapping onto two rows when the column is narrow. Colourless; it holds widget mounts and is not a widget. |
| `xps-sidebar__header` | The filter column's heading row: `xps-sidebar__title` (any heading element) and a trailing "Clear all". Carries the same rule under it as a facet-group title. |
| `xps-chip` | Removable-token box. Children: `xps-chip__label`, optional `xps-chip__attribute` (the facet name inside the label), `xps-chip__remove` (a `<button>` with an `aria-label` naming what is removed). |
| `xps-skeleton` | Loading placeholder: `currentColor` at low opacity with a pulse animation, suppressed under `prefers-reduced-motion`. Modifiers `--title`, `--text`, `--block`. |
| `xps-highlight` | The class on the `<mark>` element produced by the `highlight` template helper. |

The focus ring (`.xps :focus-visible`) and the scoped reset apply to anything inside an element
with the `xps` class, so custom markup gets them for free.

## Page Builder mount

Fixture: `fixtures/mount.html`. Spec §7.1.

```html
<div class="xps-mount" data-xps-widget="facetList" data-xps-instance="search-1"
     data-xps-config='{"attribute":"contentType"}'></div>
```

`xps-mount` is deliberately **unstyled** — no display, no spacing — so an empty mount (before
hydration, or when a widget fails to construct) cannot disturb the page layout. The widget root
the bootstrap renders inside it carries `xps` itself.

## Page Builder editor preview

Fixture: `fixtures/editor-preview.html`. Spec §7.5.

Inside the Page Builder there is no mount: the widget renders a server-side static picture of
itself instead, because the builder re-renders widget markup over AJAX on every add, move and
configure, and no search should run from the editor.

| Class | Element | Notes |
|---|---|---|
| `xps-editor-preview` | `<div class="xps">` | Root; also carries `data-xps-widget`, the same value the mount would. |
| `xps-editor-preview__badge` | `<span>` | Names the widget and says the content is not live. The only part announced to assistive technology. |
| `xps-editor-preview__body` | `<div aria-hidden="true">` | The mirrored widget markup: the widget's own classes, every control `disabled`, every link a `<span>`, unknown text as `xps-skeleton` bars. |
| `xps-editor-preview__note` | `<p>` | Subtle line naming configuration the mirrored markup cannot show (attribute, template, fields, limits). |

The root also carries a per-widget modifier, `xps-editor-preview--` plus the `data-xps-widget`
value in kebab-case — `xps-editor-preview--search-box`, `xps-editor-preview--facet-list`,
`xps-editor-preview--results`, `xps-editor-preview--range-filter` and so on; a third-party
`myCompany.dropdownFacet` becomes `…--my-company-dropdown-facet`. Nothing styles them;
they exist so a site can recognise one preview from another.

---

## searchBox

Fixture: `fixtures/search-box.html`. Root `<form class="xps xps-search-box" role="search">`.

| Class | Element | Notes |
|---|---|---|
| `xps-search-box` | `<form role="search">` | `novalidate`; submitting runs the search. |
| `xps-search-box--stalled` | root modifier | A request has outlived the stall threshold; reveals the loading indicator. |
| `xps-search-box__label` | `<label for>` | Always rendered and always associated. Add `xps-sr-only` when the label is not shown. |
| `xps-search-box__field` | `<div>` | Positioning context: the icon, the loading indicator and the reset sit inside the input; the submit follows it in the flex row. |
| `xps-search-box__icon` | `<svg aria-hidden="true" focusable="false">` | Decorative magnifier, `stroke="currentColor"` on the 24px grid. Always rendered. |
| `xps-search-box__input` | `<input type="search" name="q">` | `autocomplete="off"`; `placeholder` from options. Padded on both sides so text never runs under the icon or the reset. |
| `xps-search-box__loading` | `<span class="xps-skeleton" aria-hidden="true">` | Only visible under `--stalled`. |
| `xps-search-box__reset` | `<button type="reset">` | `xps-button`; `aria-label="Clear the search query"`; `hidden` while the query is empty. Hit target ≥ 2.25rem. |
| `xps-search-box__submit` | `<button type="submit">` | `xps-button`; `aria-label="Submit the search query"`; omitted entirely when `showSubmit: false`. |

Accessibility: `role="search"` on the form, label associated by `for`/`id`, both icon buttons
labelled by `aria-label` with their glyph `aria-hidden="true"`.

**Integrated suggestions** (`params.suggestions`, `EnableSuggestions` in the Page Builder): the
input gains the combobox attributes described under [suggestions](#suggestions) —
`role="combobox"`, `aria-expanded`, `aria-controls`, `aria-autocomplete="list"`,
`aria-activedescendant` — and a `xps-suggestions__panel` becomes the last child of the form,
below the field; the root takes `xps-suggestions--open` while the popup is shown. The panel is
byte-for-byte the standalone widget's (same renderer), so the same styles apply — including the
`xps-suggestions__footer` and its `xps-suggestions__hints` keycaps. Selecting searches in place,
so the footer carries the hints alone: there is no results page to put an
`xps-suggestions__see-all` link in. Use this *or* the standalone widget on a page, never both:
two fields is two search boxes.

## results

Fixture: `fixtures/results.html`. Root `<div class="xps xps-results">`.

| Class | Element | Notes |
|---|---|---|
| `xps-results` | `<div>` | |
| `xps-results--empty` | root modifier | No results for the current state. |
| `xps-results--loading` | root modifier | Results in flight; root also gets `aria-busy="true"`. |
| `xps-results__status` | `<p role="status" class="xps-sr-only">` | **The live region** (§5.6). Text changes to `"{n} results for “{query}”"`, `"No results…"`, `"Searching…"`. `role="status"` implies `aria-live="polite"`. |
| `xps-results__list` | `<ol>` | Ordered — result rank is meaningful. |
| `xps-results__item` | `<li>` | One per result; wraps the item template output. |
| `xps-results__empty` | `<div>` | The `templates.empty` output. A card of its own, with the copy centred in it; also rendered by `loadMore`, which shows the same empty state. |
| `xps-results__empty-title` | `<p>` | The headline: `No results for “{query}”`, with ` with these filters` appended while refinements narrow it. |
| `xps-results__empty-icon` | `<svg aria-hidden="true" focusable="false">` | The magnifier-with-minus above the empty-state copy, in both variants. 24px grid, `currentColor`, no external asset. |
| `xps-results__clear` | `<button type="button" class="xps-button xps-button--primary">` | Only in the empty state, and only while filters are applied: clears them. Reads "Clear filters and show N results" once an unfiltered probe has answered with a count, and "Clear filters" until then (and if it never does). Delegated from the results root, so re-rendering it is safe. |
| `xps-results__did-you-mean` | `<p>` | Only in the empty state, and only when the response carried `didYouMean`: "Did you mean **<correction>**?" around the button below. |
| `xps-results__correction` | `<button type="button" class="xps-button xps-button--link" data-xps-recover>` | The correction itself. Clicking runs it; `data-xps-recover` holds the query, and the results root delegates the click. |
| `xps-results__popular` | `<div>` | Only in the empty state, and only when the response carried `popularSearches`. |
| `xps-results__popular-title` | `<p>` | Labels the chips row. |
| `xps-results__popular-list` | `<ul>` | Unstyled list of chips, wraps. |
| `xps-results__popular-item` | `<li>` | One popular search. |
| `xps-results__popular-button` | `<button type="button" class="xps-button xps-chip" data-xps-recover>` | Runs that query, through the same delegated `data-xps-recover` handler. |

The default item template (`templates.item`) produces:

| Class | Element | Notes |
|---|---|---|
| `xps-result` | `<article>` | |
| `xps-result--skeleton` | modifier | Placeholder row during the first search only; also `aria-hidden="true"`. Mirrors the thumbnail card: an `xps-skeleton` square in `xps-result__media` (squared off the media width) beside the title and text bars. |
| `xps-result__media` | `<div>` | Image slot. Omitted when the result has neither an `image` nor a `fileType`. |
| `xps-result__image` | `<img alt="" width height>` | Decorative: the title link carries the accessible name. |
| `xps-result__icon` | `<svg aria-hidden="true" focusable="false">` | The media slot's stand-in when the result has a `fileType` but no `image`. Inline, `currentColor`, no external asset. |
| `xps-result__body` | `<div>` | |
| `xps-result__title` | `<h3>` | Heading level is configurable; the class does not change. |
| `xps-result__link` | `<a href>` | Wraps the highlighted title. |
| `xps-result__path` | `<p>` | Breadcrumb path, between title and snippet. Omitted when the result carries no `path`. Plain text, never highlighted. |
| `xps-result__snippet` | `<p>` | Highlighted excerpt; contains `<mark class="xps-highlight">`. |
| `xps-result__meta` | `<ul>` | Content type, date, and any configured attributes. |
| `xps-result__meta-item` | `<li>` | |
| `xps-result__type` | first `<li class="xps-result__meta-item">` | The content-type label, so the theme can set it apart from the rest of the meta row. |

The three default card renderers — the client's `defaultResultItem`, the widgets' `_Result.cshtml`
and `ServerRenderedResults.DefaultCard` — emit this table element for element; the widgets client's
`card-parity.test.ts` fails when one of them moves.

Only `xps-results__status` announces counts. `resultStats` renders the same number visually but is not a
live region, so a page with both widgets announces the change once.

## facetList

Fixture: `fixtures/facet-list.html`. Root `<div class="xps xps-facet-list">`.

| Class | Element | Notes |
|---|---|---|
| `xps-facet-list` | `<div>` | |
| `xps-facet-list--searchable` | root modifier | `searchable: true`; the search sub-block is rendered. |
| `xps-facet-list__title` | `<h3 id>` | The attribute's display name; `<ul>` references it via `aria-labelledby`. Holds the toggle button unless `collapsible: false`. |
| `xps-facet-list__toggle` | `<button aria-expanded aria-controls>` | The disclosure. Default (`collapsible: true`); absent when the group is always open. |
| `xps-facet-list__toggle-label` | `<span>` | The title text inside the button. |
| `xps-facet-list__chevron` | `<svg aria-hidden="true" focusable="false">` | `currentColor` chevron; points right while `aria-expanded="false"`. |
| `xps-facet-list__body` | `<div id>` | Everything below the title. Toggled with `hidden`, never removed; the toggle's `aria-controls` points at it. Always rendered, collapsible or not. |
| `xps-facet-list__search` | `<div>` | Holds an `xps-sr-only` label and the input. |
| `xps-facet-list__search-input` | `<input type="search">` | Facet value search. |
| `xps-facet-list__list` | `<ul aria-labelledby>` | |
| `xps-facet-list__item` | `<li>` | |
| `xps-facet-list__item--selected` | modifier | `isActive` — the checkbox is `checked`. |
| `xps-facet-list__item--disabled` | modifier | `canApply === false` — the checkbox is `disabled`. |
| `xps-facet-list__item--empty` | modifier | Selected, and this response carries no hit for it — a refinement that narrowed the search to nothing. The row stays, checked, with a count of `0`, and is **never** `disabled`: unticking it is the way back (TH-7). |
| `xps-facet-list__label` | `<label>` | Wraps the input, so the whole row is the click target; no `for` needed. |
| `xps-facet-list__checkbox` | `<input type="checkbox">` | A real checkbox (§5.6), never a styled div. |
| `xps-facet-list__value` | `<span>` | May contain `<mark class="xps-highlight">` in the searchable variant. |
| `xps-facet-list__count` | `<span>` | Facet count. |
| `xps-facet-list__show-more` | `<button aria-expanded>` | `xps-button`. Label toggles Show more / Show less. |
| `xps-facet-list__show-more--disabled` | modifier | Nothing more to show; the button is `disabled` and stays in the DOM so focus survives. |
| `xps-facet-list__no-results` | `<p role="status">` | Facet search matched nothing. |

## toggleFilter

Fixture: `fixtures/toggle-filter.html`. Root `<div class="xps xps-toggle-filter">`.

`xps-toggle-filter__label` (`<label>`) wraps `xps-toggle-filter__checkbox`
(`<input type="checkbox">`), `xps-toggle-filter__value` and `xps-toggle-filter__count`.
`xps-toggle-filter--disabled` is the `canApply === false` state and pairs with the
`disabled` attribute.

## pagination

Fixture: `fixtures/pagination.html`. Root `<nav class="xps xps-pagination" aria-label="Search results pages">`.

| Class | Element | Notes |
|---|---|---|
| `xps-pagination__list` | `<ul>` | |
| `xps-pagination__item` | `<li>` | Always plus exactly one *kind* modifier. |
| `xps-pagination__item--first`, `xps-pagination__item--previous`, `xps-pagination__item--next`, `xps-pagination__item--last` | modifiers | Rendered per `showFirst` / `showLast`. |
| `xps-pagination__item--page` | modifier | A numbered page inside `padding`. |
| `xps-pagination__item--current` | modifier | Sits alongside `--page`; the link gets `aria-current="page"`. |
| `xps-pagination__item--ellipsis` | modifier | Gap in the number run. |
| `xps-pagination__item--disabled` | modifier | Only on the end controls at the range boundary. |
| `xps-pagination__link` | `<a href>` — or `<span aria-disabled="true">` when disabled | A disabled control is a `<span>`: no href, not focusable, nothing for a keyboard user to land on that does nothing. |
| `xps-pagination__ellipsis` | `<span aria-hidden="true">` | |

Every control's visible content is a glyph marked `aria-hidden="true"` plus an `xps-sr-only`
name ("First page", "Page 4"), so the accessible name never reads as "«".

## resultStats

Fixture: `fixtures/result-stats.html`. Root `<div class="xps xps-result-stats">` containing
`xps-result-stats__text` (`<span>`) and, inside it, `xps-result-stats__total`
(`<strong>`, the count) and `xps-result-stats__time` (`<span>`, the timing).
`xps-result-stats--empty` is the no-query-yet state. Not a live region — see `results`.

The `<strong>` is the only markup a `textTemplate` produces: the template text and every
substituted value are escaped, and `{total}` is wrapped. A template without `{total}` renders as
plain escaped text.

## sortSelect

Fixture: `fixtures/sort-select.html`. Root `<div class="xps xps-sort-select xps-select">`: the widget
adds only its identity class and renders the shared **`xps-select`** block described under
Utilities — `xps-select__label` (`<label for>`, add `xps-sr-only` to hide it) and
`xps-select__field` (`<span>`) holding `xps-select__control` (a native `<select name="sort">`, one
`<option>` per `items` entry, `selected` on the current sort) plus `xps-select__chevron`
(`<svg aria-hidden="true" focusable="false">`). `xps-sort-select` itself carries no styling; it is
the hook a site can target.

## clearFilters

Fixture: `fixtures/clear-filters.html`. Root `<div class="xps xps-clear-filters">` with
`xps-clear-filters__button` (`<button type="button" class="xps-button xps-button--link">` —
the muted-link look of the design; drop the modifier in your own CSS for a boxed button).
`xps-clear-filters--disabled` + the `disabled` attribute when there is nothing to clear —
the button is never removed, so focus is not destroyed mid-interaction.

## activeFilters

Fixture: `fixtures/active-filters.html`. Root `<div class="xps xps-active-filters">`.

| Class | Element | Notes |
|---|---|---|
| `xps-active-filters--empty` | root modifier | No active filters; the empty `<ul>` still renders so layout does not jump. |
| `xps-active-filters__title` | `<h3 id>` | "Active filters"; usually `xps-sr-only`. |
| `xps-active-filters__list` | `<ul aria-labelledby>` | |
| `xps-active-filters__item` | `<li>` | Contains one `xps-chip`. |

The chip's remove button must carry a full `aria-label` naming the attribute and value
("Remove filter Content type: Article") — "×" alone is not a name.

## filterSort

Fixture: `fixtures/filter-sort.html`. Root `<div class="xps xps-filter-sort">` in the mount; the
sheet is a second root, `<div class="xps xps-sheet">`, appended to `document.body` while open and
removed when it closes.

| Class | Element | Notes |
|---|---|---|
| `xps-filter-sort__trigger` | `<button type="button" class="xps-button">` | `aria-haspopup="dialog"`, `aria-expanded` mirrors the sheet. |
| `xps-filter-sort__icon` | `<svg aria-hidden="true" focusable="false">` | Funnel glyph, `currentColor`, no external asset. |
| `xps-filter-sort__label` | `<span>` | The trigger's visible text. |
| `xps-filter-sort__badge` | `<span>` | Number of active refinements on the configured attributes plus a non-default sort; `hidden` at zero. |
| `xps-sheet__backdrop` | `<div>` | Dims the page; clicking it discards the pending selection and closes. |
| `xps-sheet__panel` | `<div role="dialog" aria-modal="true" aria-labelledby>` | Labelled by the title. Focus moves to the close button on open, is trapped while open, and returns to the trigger on close. |
| `xps-sheet__header` | `<header>` | |
| `xps-sheet__title` | `<h2 id>` | The dialog's accessible name. |
| `xps-sheet__close` | `<button type="button" aria-label>` | The glyph is `aria-hidden="true"`. |
| `xps-sheet__body` | `<div>` | The scrolling region; scroll is contained and the page behind is locked. |
| `xps-sheet__section` | `<section>` | One per facet group, plus the sort section when sort options are configured. Hairline rule between sections. |
| `xps-sheet__section-title` | `<h3>` (`id` on the sort section) | |
| `xps-sheet__pills` | `<div role="group" aria-labelledby>` | The "Sort by" choice row. |
| `xps-sheet__pill` | `<button type="button" aria-pressed>` | Carries `data-xps-sort` with the sort key. |
| `xps-sheet__pill--selected` | modifier | Pairs with `aria-pressed="true"`. Exactly one at a time. |
| `xps-sheet__values` | `<ul>` | |
| `xps-sheet__value` | `<li>` | |
| `xps-sheet__value-label` | `<label>` | Wraps the input, so the whole row is the target; no `for` needed. |
| `xps-sheet__checkbox` | `<input type="checkbox">` | A real checkbox. Checking it changes the **pending** selection only — nothing refines until Apply. |
| `xps-sheet__value-text` | `<span>` | |
| `xps-sheet__value-count` | `<span>` | Facet count. |
| `xps-sheet__footer` | `<footer>` | Sticky at the bottom of the panel. |
| `xps-sheet__clear` | `<button type="button" class="xps-button">` | "Clear all" — pending, like every other selection in the sheet. |
| `xps-sheet__apply` | `<button type="button" class="xps-button xps-button--primary">` | Applies the pending selection in one batch and closes. Its label previews the count the pending selection would return ("Show 12 results"); until a probe answers, and if none does, it reads "Show results". |

`xps-active-filters--scroll` keeps the chips on one row that scrolls sideways instead of wrapping.
The widget sets it from `scroll: true` (`Scroll sideways` in the Page Builder); a page composing
the markup by hand can also put it on the root itself.

The slide-up animation on `xps-sheet__panel` is dropped under `prefers-reduced-motion: reduce`.

## rangeFilter

Fixture: `fixtures/range-filter.html`. Root `<div class="xps xps-range-filter">`.

Two native `<input type="range">` controls, not a custom drag widget: they are keyboard-operable
and announced by screen readers with no extra code.

| Class | Element | Notes |
|---|---|---|
| `xps-range-filter__title` | `<h3 id>` | Referenced by the track's `aria-labelledby`. |
| `xps-range-filter__track` | `<div role="group" aria-labelledby>` | The one rail. Both range inputs are overlaid on it and carry `pointer-events: none` with their thumbs restored to `auto`, so each handle stays draggable. Styles the fill from the two custom properties below. |
| `xps-range-filter__range` | `<input type="range">` | Plus `xps-range-filter__range--min` / `xps-range-filter__range--max`. `aria-describedby` points at the values line. |
| `xps-range-filter__inputs` | `<div>` | Numeric entry row. |
| `xps-range-filter__input-label` | `<label for>` | Visible "From" / "To". |
| `xps-range-filter__input` | `<input type="number" inputmode="numeric">` | |
| `xps-range-filter__separator` | `<span aria-hidden="true">` | |
| `xps-range-filter__unit` | `<span>` | The `unit` option ("USD"), last on the input row. Omitted when unset. |
| `xps-range-filter__values` | `<p id>` | The bounds of the control ("0 to 500"), what the sliders are `aria-describedby`; carries the "no range in these results" sentence when disabled. |
| `xps-range-filter--disabled` | root modifier | No refinable range; all four inputs are `disabled`. |

The widget sets `--xps-range-from` and `--xps-range-to` on `xps-range-filter__track` on every
render — the two ends as percentages of the bounds, clamped to `0%`–`100%` (both `0%` when the
control is disabled). The default theme paints the rail with them: `--xps-color-border` outside the
pair, `--xps-color-accent` between. A page composing this markup by hand has to set them too, or
the rail reads as fully selected. Under `forced-colors: active` the two colours become
`ButtonBorder` / `Highlight`; the RTL rail flips through `--xps-range-side`.

`shell.css` alone overlays the two sliders but paints no rail, so a shell-only site sees the
platform sliders stacked on one row and styles the rail itself.

## categoryTree

Fixture: `fixtures/category-tree.html`. Root
`<nav class="xps xps-category-tree" aria-label="…">`, with `xps-category-tree__title` (`<h3>`).

| Class | Element | Notes |
|---|---|---|
| `xps-category-tree__toggle` | `<button aria-expanded aria-controls>` | The disclosure inside `xps-category-tree__title`, with `xps-category-tree__toggle-label` and `xps-category-tree__chevron` inside it. Default (`collapsible: true`); absent when the tree is always open. |
| `xps-category-tree__body` | `<div id>` | Wraps the level-0 list. Toggled with `hidden`; always rendered. |
| `xps-category-tree__list` | `<ul>` | One depth modifier per level: `xps-category-tree__list--lvl0`, `xps-category-tree__list--lvl1`, `xps-category-tree__list--lvl2`, and so on for deeper trees. A child list nests inside its parent `<li>`. |
| `xps-category-tree__item` | `<li>` | |
| `xps-category-tree__item--selected` | modifier | On every node of the open path; the link gets `aria-current="true"`. |
| `xps-category-tree__item--parent` | modifier | Has a nested list. |
| `xps-category-tree__item--disabled` | modifier | Count 0; rendered as a `<span aria-disabled="true">` instead of a link. |
| `xps-category-tree__link` | `<a href>` (or the disabled `<span>`) | href is a real, crawlable filtered URL from `urlFor`. |
| `xps-category-tree__value` | `<span>` | |
| `xps-category-tree__count` | `<span>` | |

## loadMore

Fixture: `fixtures/load-more.html`. Root `<div class="xps xps-load-more">`. Items reuse
the `xps-result` template exactly as `results` does.

| Class | Element | Notes |
|---|---|---|
| `xps-load-more--exhausted` | root modifier | Every result loaded. |
| `xps-load-more__status` | `<p role="status" class="xps-sr-only">` | Live region: "Showing {n} of {m} results". |
| `xps-load-more__list` | `<ol>` | Appended to, never rebuilt — appending keeps scroll position and focus. |
| `xps-load-more__item` | `<li>` | |
| `xps-load-more__sentinel` | `<div aria-hidden="true">` | 1px intersection-observer target for the scroll path. |
| `xps-load-more__load-more` | `<button type="button" class="xps-button">` | The keyboard path. Always present; `disabled` when exhausted, and `hidden` while the search found nothing. |

A search with no results renders the `results` widget's `xps-results__empty` block between the list
and the sentinel — one empty state for both widgets, including the `data-xps-recover` offers, but
without the unfiltered probe that counts the results behind the filters.

## suggestions

Fixture: `fixtures/suggestions.html`. Root `<div class="xps xps-suggestions">`.
Implements the WAI-ARIA APG combobox-with-listbox pattern:
<https://www.w3.org/WAI/ARIA/apg/patterns/combobox/>.

| Class | Element | Notes |
|---|---|---|
| `xps-suggestions--open` | root modifier | Popup shown; mirrors `aria-expanded="true"`. |
| `xps-suggestions__form` | `<form role="search">` | Submitting goes to the full results page. |
| `xps-suggestions__label` | `<label for>` | Usually `xps-sr-only`. |
| `xps-suggestions__field` | `<div>` | |
| `xps-suggestions__input` | `<input type="text" role="combobox">` | `aria-expanded`, `aria-controls` → the listbox id, `aria-activedescendant` → the active option id (empty when none), `aria-autocomplete="list"`, `autocomplete="off"`. |
| `xps-suggestions__reset` | `<button type="reset" class="xps-button">` | `aria-label`; `hidden` while empty. |
| `xps-suggestions__panel` | `<div>` | Absolutely positioned; `hidden` when closed. |
| `xps-suggestions__list` | `<ul role="listbox" id aria-label>` | Always present, even when empty, so `aria-controls` never dangles. |
| `xps-suggestions__group` | `<li role="group" aria-labelledby>` | One per source, in order: recent searches (client-side), query suggestions, matching documents. Omit the group wrapper when there is only one ungrouped source and put `role="option"` on `<li>` directly — except for the recents, whose group is always labelled because the label row carries the Clear control. |
| `xps-suggestions__group-title` | `<div id>` | Labels the group. Not an option. |
| `xps-suggestions__group-header` | `<div>` | Only the recents: their title row, with the Clear control at its far end. It sits **before the listbox**, not inside it — a `<button>` is not something a listbox may own — and the recents' group points at its title with `aria-labelledby`. Every other group renders the bare title inside itself. |
| `xps-suggestions__group-clear` | `<button type="button" class="xps-button xps-button--link" data-xps-recent-clear>` | Empties this visitor's recent searches and closes the group. Outside the title element, so the group name stays "Recent searches", and outside the listbox. |
| `xps-suggestions__row` | `<div>` | Only a recent row: wraps the option and its remove control. The control is a **sibling** of the option, never a child — a focusable descendant of `role="option"` is swallowed by the option's accessible name. |
| `xps-suggestions__option` | `<div role="option" id aria-selected>` | Ids follow `…-option-{index}` in visual order. |
| `xps-suggestions__option--recent` / `xps-suggestions__option--query` / `xps-suggestions__option--document` | modifier | Which source the row came from, so the theme can give each its own glyph and weight. |
| `xps-suggestions__option--active` | modifier | The `aria-activedescendant` target; also `aria-selected="true"`. Exactly one at a time, or none. |
| `xps-suggestions__option-icon` | `<svg aria-hidden="true" focusable="false">` | The row's leading glyph — a clock on a recent row, a magnifier on a suggestion, none on a document row — and the X inside the remove control. 24px grid, `currentColor`, no external asset. |
| `xps-suggestions__option-remove` | `<span data-xps-recent-remove title aria-hidden="true">` | Drops one recent search from this visitor's list without closing the panel. A pointer affordance only — a listbox owns options and groups and nothing else, so a focusable control cannot live in it; the keyboard and assistive-tech path is **Delete on the active recent row** (plus the group's Clear). |
| `xps-suggestions__option-title` | `<span>` | May contain `<mark class="xps-highlight">`. |
| `xps-suggestions__option-meta` | `<span>` | Secondary line for a matching document. |
| `xps-suggestions__empty` | `<p role="status">` | Open with no suggestions. |
| `xps-suggestions__footer` | `<div>` | Holds `xps-suggestions__hints` and `xps-suggestions__see-all` (`<a href>`), in that order. The link is dropped when the widget searches in place; the standalone widget then drops the whole footer, the integrated search box keeps it for the hints. |
| `xps-suggestions__hints` | `<span aria-hidden="true">` | Decorative keyboard hints. Hidden from assistive tech (the combobox roles already convey the model) and from coarse pointers. |
| `xps-suggestions__key` | `<kbd>` | One keycap inside the hints cluster. |
| `xps-suggestions__see-all` | `<a href>` | Link to the full results page. |

Keyboard, per the APG: `Down`/`Up` move `aria-activedescendant` (DOM focus stays in the input),
`Home`/`End` jump to the first/last option, `Enter` activates the active option, `Escape` closes
the popup and, pressed again, clears the input; `Tab` closes the popup and moves on.
