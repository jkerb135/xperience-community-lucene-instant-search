# Markup contract

This file, together with `themes/fixtures/*.html`, is the contract between the stylesheets
(`themes/src/shell.css`, `themes/src/default.css`) and the default widget renderers in
`XpSearch.Client`. The fixtures are the normative form: they are the exact DOM a widget must
produce with no custom templates. This file explains what each class and attribute means.

Changing a class name or an ARIA attribute here is a breaking change for anyone who has
written CSS against the widgets, and is treated as a semver-major event.

`themes/scripts/check.mjs` (`npm run check`) enforces the three-way agreement: every class in a
fixture is styled or documented here, every class styled in CSS appears in a fixture, and every
class named here appears in a fixture.

## Rules that apply to every widget

1. **Every widget root carries two classes: `xps` and its own root class** — for example
   `<div class="xps xps-hits">`. `default.css` declares its custom properties on `.xps`, so a
   widget is self-theming wherever it lands; `shell.css` scopes its reset and focus ring to
   `.xps` for the same reason. Wrapping a whole search page in one `.xps` element also works —
   the nested re-declaration is a no-op — and is the cheaper option when you override variables.
2. **Prefix everything `xps-`, BEM-ish**: `block__element--modifier`. Modifiers live on the
   element they modify, and the element keeps its base class
   (`class="xps-refinement-list__item xps-refinement-list__item--selected"`).
3. **Optional parts are toggled with the `hidden` attribute**, not by removing them from the DOM
   and not by a `--hidden` modifier. Shell has `.xps [hidden] { display: none !important }`, which
   also keeps a host stylesheet from revealing them.
4. **`id` pattern**: `id="xps-{instance}-{widget}-{part}"` (`id="xps-search-1-search-box-input"`). The
   instance id is `data-xps-instance` from the Page Builder mount, or the widget's container id.
   Ids must be unique across the page — several instances can coexist.
5. **Disabled controls use the real attribute** (`disabled` on form controls, `aria-disabled="true"`
   on links and on elements that stay focusable), *plus* the `--disabled` modifier for styling.
6. **Never remove focus.** Shell draws the ring with `:focus-visible` in `currentColor`; renderers
   must not set `tabindex="-1"` on anything a keyboard user needs to reach.
7. **Text is never a `div` that acts like a control.** Buttons are `<button>`, checkboxes are
   `<input type="checkbox">`, links that navigate are `<a href>`.

## Theming hooks

- `data-xps-theme="auto"` on the element carrying `xps` opts that subtree into the
  `prefers-color-scheme: dark` variable set in `default.css`. It is opt-in because the theme does
  not control the host page's background. Renderers should pass the attribute through from
  configuration, never set it themselves.
- Shell gives `xps-autocomplete__panel` position but no surface, and leaves `xps-highlight` at the
  browser's default `<mark>` styling — both need a colour, which shell does not have. A site
  running shell alone supplies them.

## Utilities (available to custom widgets — spec §5.7)

Fixture: `fixtures/utilities.html`. These are the only classes a custom connector renderer needs
to know to inherit accessible defaults.

| Class | What it does |
|---|---|
| `xps` | Theme scope. Put it on your widget root. Declares `--xps-space` (shell) and the full variable set (default theme). |
| `xps-sr-only` | Visually hidden, still announced. Use for labels and status text. |
| `xps-stack` | Vertical flex, `gap: var(--xps-space)`. |
| `xps-cluster` | Horizontal flex that wraps, `gap: var(--xps-space)`. |
| `xps-button` | Button box: inline-flex, `2.25rem` minimum height, symmetric padding. Shell adds no colour; the default theme gives it a surface, border and radius. |
| `xps-button--primary` | Accent-filled button (default theme only). |
| `xps-chip` | Removable-token box. Children: `xps-chip__label`, optional `xps-chip__attribute` (the facet name inside the label), `xps-chip__remove` (a `<button>` with an `aria-label` naming what is removed). |
| `xps-skeleton` | Loading placeholder: `currentColor` at low opacity with a pulse animation, suppressed under `prefers-reduced-motion`. Modifiers `--title`, `--text`, `--block`. |
| `xps-highlight` | The class on the `<mark>` element produced by the `highlight` template helper. |

The focus ring (`.xps :focus-visible`) and the scoped reset apply to anything inside an element
with the `xps` class, so custom markup gets them for free.

## Page Builder mount

Fixture: `fixtures/mount.html`. Spec §7.1.

```html
<div class="xps-mount" data-xps-widget="refinementList" data-xps-instance="search-1"
     data-xps-config='{"attribute":"contentType"}'></div>
```

`xps-mount` is deliberately **unstyled** — no display, no spacing — so an empty mount (before
hydration, or when a widget fails to construct) cannot disturb the page layout. The widget root
the bootstrap renders inside it carries `xps` itself.

---

## searchBox

Fixture: `fixtures/search-box.html`. Root `<form class="xps xps-search-box" role="search">`.

| Class | Element | Notes |
|---|---|---|
| `xps-search-box` | `<form role="search">` | `novalidate`; submitting runs the search. |
| `xps-search-box--stalled` | root modifier | A request has outlived the stall threshold; reveals the loading indicator. |
| `xps-search-box__label` | `<label for>` | Always rendered and always associated. Add `xps-sr-only` when the label is not shown. |
| `xps-search-box__field` | `<div>` | Flex row: input, loading indicator, reset, submit. |
| `xps-search-box__input` | `<input type="search" name="q">` | `autocomplete="off"`; `placeholder` from options. |
| `xps-search-box__loading` | `<span class="xps-skeleton" aria-hidden="true">` | Only visible under `--stalled`. |
| `xps-search-box__reset` | `<button type="reset">` | `xps-button`; `aria-label="Clear the search query"`; `hidden` while the query is empty. |
| `xps-search-box__submit` | `<button type="submit">` | `xps-button`; `aria-label="Submit the search query"`; omitted entirely when `showSubmit: false`. |

Accessibility: `role="search"` on the form, label associated by `for`/`id`, both icon buttons
labelled by `aria-label` with their glyph `aria-hidden="true"`.

## hits

Fixture: `fixtures/hits.html`. Root `<div class="xps xps-hits">`.

| Class | Element | Notes |
|---|---|---|
| `xps-hits` | `<div>` | |
| `xps-hits--empty` | root modifier | No results for the current state. |
| `xps-hits--loading` | root modifier | Results in flight; root also gets `aria-busy="true"`. |
| `xps-hits__status` | `<p role="status" class="xps-sr-only">` | **The live region** (§5.6). Text changes to `"{n} results for “{query}”"`, `"No results…"`, `"Searching…"`. `role="status"` implies `aria-live="polite"`. |
| `xps-hits__list` | `<ol>` | Ordered — result rank is meaningful. |
| `xps-hits__item` | `<li>` | One per hit; wraps the item template output. |
| `xps-hits__empty` | `<div>` | The `templates.empty` output. |

The default item template (`templates.item`) produces:

| Class | Element | Notes |
|---|---|---|
| `xps-hit` | `<article>` | |
| `xps-hit--skeleton` | modifier | Placeholder row during loading; also `aria-hidden="true"`. |
| `xps-hit__media` | `<div>` | Image slot. Omitted when the hit has no image. |
| `xps-hit__image` | `<img alt="" width height>` | Decorative: the title link carries the accessible name. |
| `xps-hit__body` | `<div>` | |
| `xps-hit__title` | `<h3>` | Heading level is configurable; the class does not change. |
| `xps-hit__link` | `<a href>` | Wraps the highlighted title. |
| `xps-hit__snippet` | `<p>` | Highlighted excerpt; contains `<mark class="xps-highlight">`. |
| `xps-hit__meta` | `<ul>` | Content type, date, and any configured attributes. |
| `xps-hit__meta-item` | `<li>` | |

Only `xps-hits__status` announces counts. `stats` renders the same number visually but is not a
live region, so a page with both widgets announces the change once.

## refinementList

Fixture: `fixtures/refinement-list.html`. Root `<div class="xps xps-refinement-list">`.

| Class | Element | Notes |
|---|---|---|
| `xps-refinement-list` | `<div>` | |
| `xps-refinement-list--searchable` | root modifier | `searchable: true`; the search sub-block is rendered. |
| `xps-refinement-list__title` | `<h3 id>` | The attribute's display name; `<ul>` references it via `aria-labelledby`. |
| `xps-refinement-list__search` | `<div>` | Holds an `xps-sr-only` label and the input. |
| `xps-refinement-list__search-input` | `<input type="search">` | Facet value search. |
| `xps-refinement-list__list` | `<ul aria-labelledby>` | |
| `xps-refinement-list__item` | `<li>` | |
| `xps-refinement-list__item--selected` | modifier | `isRefined` — the checkbox is `checked`. |
| `xps-refinement-list__item--disabled` | modifier | `canRefine === false` — the checkbox is `disabled`. |
| `xps-refinement-list__label` | `<label>` | Wraps the input, so the whole row is the hit area; no `for` needed. |
| `xps-refinement-list__checkbox` | `<input type="checkbox">` | A real checkbox (§5.6), never a styled div. |
| `xps-refinement-list__value` | `<span>` | May contain `<mark class="xps-highlight">` in the searchable variant. |
| `xps-refinement-list__count` | `<span>` | Facet count. |
| `xps-refinement-list__show-more` | `<button aria-expanded>` | `xps-button`. Label toggles Show more / Show less. |
| `xps-refinement-list__show-more--disabled` | modifier | Nothing more to show; the button is `disabled` and stays in the DOM so focus survives. |
| `xps-refinement-list__no-results` | `<p role="status">` | Facet search matched nothing. |

## toggleRefinement

Fixture: `fixtures/toggle-refinement.html`. Root `<div class="xps xps-toggle-refinement">`.

`xps-toggle-refinement__label` (`<label>`) wraps `xps-toggle-refinement__checkbox`
(`<input type="checkbox">`), `xps-toggle-refinement__value` and `xps-toggle-refinement__count`.
`xps-toggle-refinement--disabled` is the `canRefine === false` state and pairs with the
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

## stats

Fixture: `fixtures/stats.html`. Root `<div class="xps xps-stats">` containing
`xps-stats__text` (`<span>`) and, inside it, `xps-stats__time` (`<span>`) for the timing.
`xps-stats--empty` is the no-query-yet state. Not a live region — see `hits`.

## sortBy

Fixture: `fixtures/sort-by.html`. Root `<div class="xps xps-sort-by">` with
`xps-sort-by__label` (`<label for>`, add `xps-sr-only` to hide it) and `xps-sort-by__select`
(a native `<select>`, one `<option>` per `items` entry, `selected` on the current sort).

## clearRefinements

Fixture: `fixtures/clear-refinements.html`. Root `<div class="xps xps-clear-refinements">` with
`xps-clear-refinements__button` (`<button type="button" class="xps-button">`).
`xps-clear-refinements--disabled` + the `disabled` attribute when there is nothing to clear —
the button is never removed, so focus is not destroyed mid-interaction.

## currentRefinements

Fixture: `fixtures/current-refinements.html`. Root `<div class="xps xps-current-refinements">`.

| Class | Element | Notes |
|---|---|---|
| `xps-current-refinements--empty` | root modifier | No active refinements; the empty `<ul>` still renders so layout does not jump. |
| `xps-current-refinements__title` | `<h3 id>` | "Active filters"; usually `xps-sr-only`. |
| `xps-current-refinements__list` | `<ul aria-labelledby>` | |
| `xps-current-refinements__item` | `<li>` | Contains one `xps-chip`. |

The chip's remove button must carry a full `aria-label` naming the attribute and value
("Remove filter Content type: Article") — "×" alone is not a name.

## rangeSlider

Fixture: `fixtures/range-slider.html`. Root `<div class="xps xps-range-slider">`.

Two native `<input type="range">` controls, not a custom drag widget: they are keyboard-operable
and announced by screen readers with no extra code.

| Class | Element | Notes |
|---|---|---|
| `xps-range-slider__title` | `<h3 id>` | Referenced by the track's `aria-labelledby`. |
| `xps-range-slider__track` | `<div role="group" aria-labelledby>` | |
| `xps-range-slider__range` | `<input type="range">` | Plus `xps-range-slider__range--min` / `xps-range-slider__range--max`. `aria-describedby` points at the values line. |
| `xps-range-slider__inputs` | `<div>` | Numeric entry row. |
| `xps-range-slider__input-label` | `<label for>` | Visible "From" / "To". |
| `xps-range-slider__input` | `<input type="number" inputmode="numeric">` | |
| `xps-range-slider__separator` | `<span aria-hidden="true">` | |
| `xps-range-slider__values` | `<p id>` | Human-readable current range. |
| `xps-range-slider--disabled` | root modifier | No refinable range; all four inputs are `disabled`. |

## hierarchicalMenu

Fixture: `fixtures/hierarchical-menu.html`. Root
`<nav class="xps xps-hierarchical-menu" aria-label="…">`, with `xps-hierarchical-menu__title` (`<h3>`).

| Class | Element | Notes |
|---|---|---|
| `xps-hierarchical-menu__list` | `<ul>` | One depth modifier per level: `xps-hierarchical-menu__list--lvl0`, `xps-hierarchical-menu__list--lvl1`, `xps-hierarchical-menu__list--lvl2`, and so on for deeper trees. A child list nests inside its parent `<li>`. |
| `xps-hierarchical-menu__item` | `<li>` | |
| `xps-hierarchical-menu__item--selected` | modifier | On every node of the open path; the link gets `aria-current="true"`. |
| `xps-hierarchical-menu__item--parent` | modifier | Has a nested list. |
| `xps-hierarchical-menu__item--disabled` | modifier | Count 0; rendered as a `<span aria-disabled="true">` instead of a link. |
| `xps-hierarchical-menu__link` | `<a href>` (or the disabled `<span>`) | href is a real, crawlable refined URL from `createURL`. |
| `xps-hierarchical-menu__value` | `<span>` | |
| `xps-hierarchical-menu__count` | `<span>` | |

## infiniteHits

Fixture: `fixtures/infinite-hits.html`. Root `<div class="xps xps-infinite-hits">`. Items reuse
the `xps-hit` template exactly as `hits` does.

| Class | Element | Notes |
|---|---|---|
| `xps-infinite-hits--exhausted` | root modifier | Every result loaded. |
| `xps-infinite-hits__status` | `<p role="status" class="xps-sr-only">` | Live region: "Showing {n} of {m} results". |
| `xps-infinite-hits__list` | `<ol>` | Appended to, never rebuilt — appending keeps scroll position and focus. |
| `xps-infinite-hits__item` | `<li>` | |
| `xps-infinite-hits__sentinel` | `<div aria-hidden="true">` | 1px intersection-observer target for the scroll path. |
| `xps-infinite-hits__load-more` | `<button type="button" class="xps-button">` | The keyboard path. Always present; `disabled` when exhausted. |

## autocomplete

Fixture: `fixtures/autocomplete.html`. Root `<div class="xps xps-autocomplete">`.
Implements the WAI-ARIA APG combobox-with-listbox pattern:
<https://www.w3.org/WAI/ARIA/apg/patterns/combobox/>.

| Class | Element | Notes |
|---|---|---|
| `xps-autocomplete--open` | root modifier | Popup shown; mirrors `aria-expanded="true"`. |
| `xps-autocomplete__form` | `<form role="search">` | Submitting goes to the full results page. |
| `xps-autocomplete__label` | `<label for>` | Usually `xps-sr-only`. |
| `xps-autocomplete__field` | `<div>` | |
| `xps-autocomplete__input` | `<input type="text" role="combobox">` | `aria-expanded`, `aria-controls` → the listbox id, `aria-activedescendant` → the active option id (empty when none), `aria-autocomplete="list"`, `autocomplete="off"`. |
| `xps-autocomplete__reset` | `<button type="reset" class="xps-button">` | `aria-label`; `hidden` while empty. |
| `xps-autocomplete__panel` | `<div>` | Absolutely positioned; `hidden` when closed. |
| `xps-autocomplete__list` | `<ul role="listbox" id aria-label>` | Always present, even when empty, so `aria-controls` never dangles. |
| `xps-autocomplete__group` | `<li role="group" aria-labelledby>` | One per source (suggestions, federated hits). Omit the group wrapper when there is only one ungrouped source and put `role="option"` on `<li>` directly. |
| `xps-autocomplete__group-title` | `<div id>` | Labels the group. Not an option. |
| `xps-autocomplete__option` | `<div role="option" id aria-selected>` | Ids follow `…-option-{index}` in visual order. |
| `xps-autocomplete__option--active` | modifier | The `aria-activedescendant` target; also `aria-selected="true"`. Exactly one at a time, or none. |
| `xps-autocomplete__option-title` | `<span>` | May contain `<mark class="xps-highlight">`. |
| `xps-autocomplete__option-meta` | `<span>` | Secondary line for federated hits. |
| `xps-autocomplete__empty` | `<p role="status">` | Open with no suggestions. |
| `xps-autocomplete__footer` | `<div>` | Holds `xps-autocomplete__see-all` (`<a href>`). |
| `xps-autocomplete__see-all` | `<a href>` | Link to the full results page. |

Keyboard, per the APG: `Down`/`Up` move `aria-activedescendant` (DOM focus stays in the input),
`Home`/`End` jump to the first/last option, `Enter` activates the active option, `Escape` closes
the popup and, pressed again, clears the input; `Tab` closes the popup and moves on.
