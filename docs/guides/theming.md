## Theming

Two stylesheets, strictly separated. `shell.css` is structure — layout, spacing, focus rings,
screen-reader utilities, loading skeletons. `default.css` is an opt-in visual theme driven
entirely by CSS custom properties. Load both and override a couple of variables, and the whole
widget set matches your site:

```html
<link rel="stylesheet" href="/node_modules/@yourco/xperience-search/themes/shell.css">
<link rel="stylesheet" href="/node_modules/@yourco/xperience-search/themes/default.css">
<style>
  .xps {
    --xps-color-accent: #b8005c;
    --xps-radius: 0;
  }
</style>
```

That is the whole re-skin: no build step, no Sass, no fork. Every visual value in `default.css`
goes through one of the variables below, so overriding one changes every widget that uses it.

Already have a design system? Load `shell.css` alone and write your own rules against the
documented class names — see [using shell on its own](#using-shell-on-its-own).

### Where the files are

| Consumer | Path |
|---|---|
| npm | `@yourco/xperience-search/themes/shell.css`, `.../themes/default.css` |
| In this repository | `libraries/xperience-search/themes/src/shell.css`, `themes/src/default.css` — `src/` is the source location; the packages ship the same two files at the root of their theme folder. |

Load `shell.css` first. `default.css` assumes it.

### The `xps` class

`default.css` declares its variables on `.xps`, and `shell.css` scopes its reset and focus ring
to `.xps`. Every widget root carries both `xps` and its own class, so widgets are self-theming
wherever they land:

```html
<div class="xps xps-hits">…</div>
```

Wrapping your whole search page in a single `.xps` element also works and is the cheaper place to
override variables:

```html
<div class="xps" style="--xps-color-accent: #b8005c">
  <div id="search-box"></div>
  <div id="search-results"></div>
</div>
```

Nothing outside an element with an `xps-` class is ever selected by either stylesheet, so neither
file can leak into the rest of your page.

### Variable reference

Every one of these is declared on `.xps` in `default.css` with the default shown.

| Variable | Default | Drives |
|---|---|---|
| `--xps-color-accent` | `#0b5fff` | Links and hit titles, the current pagination page, the primary button, chip tint, the active autocomplete option, checkbox and range `accent-color`, the `<mark>` highlight, the focus ring colour. |
| `--xps-color-text` | `#111` | Body text inside widgets, pagination links, hierarchical-menu links, the autocomplete panel's shadow tint. |
| `--xps-color-muted` | `#666` | Facet counts, hit metadata, stats, placeholders, group titles, empty states, the skeleton tint. |
| `--xps-color-surface` | `#fff` | Input, button and autocomplete-panel backgrounds; the text colour on accent-filled elements. |
| `--xps-color-border` | `#e2e2e2` | Every border and the autocomplete footer rule. |
| `--xps-radius` | `6px` | Corner radius on inputs, buttons, chips, the panel and hit images. Derived values (`calc(--xps-radius / 2)`) round the skeletons and highlights. |
| `--xps-space` | `0.75rem` | The whole spacing rhythm — gaps and padding are `var(--xps-space)`, `calc(var(--xps-space) / 2)` or `calc(var(--xps-space) * 2)`. **Also declared by `shell.css`**, so structure keeps its rhythm when the theme is not loaded. |
| `--xps-font` | `inherit` | `font-family` on `.xps`, and nothing else. The widgets never set a font size in absolute units; sizes are `em`-relative to your text. |

Override them anywhere the cascade reaches — a stylesheet rule on `.xps`, an inline `style`, or a
scoped rule for one widget:

```css
/* one widget, different accent */
.xps-pagination { --xps-color-accent: #007a5e; }
```

### Dark mode

`default.css` ships a `prefers-color-scheme: dark` block that changes variable values only — no
new rules. It is **opt-in**, because the widgets sit inside a page whose background the theme does
not control; following the OS unasked would put light text on a site that stayed light. Add
`data-xps-theme="auto"` to the element that carries `xps`:

```html
<div class="xps" data-xps-theme="auto"> … </div>
```

If your site has its own dark mode switch, ignore the attribute and set the variables from your
own selector instead — that is the same thing with your trigger:

```css
[data-theme="dark"] .xps {
  --xps-color-text: #f2f2f2;
  --xps-color-muted: #a9a9a9;
  --xps-color-surface: #16181d;
  --xps-color-border: #33363d;
  --xps-color-accent: #6f9dff;
}
```

### What shell gives you

`shell.css` never sets a colour other than `currentColor`, never sets a font, and never draws a
border. It is safe to load on any site. It provides:

- **Layout** for every widget: flex rows and columns, wrapping clusters, the facet-count column,
  the absolutely-positioned autocomplete panel, the nested hierarchical-menu indentation.
- **Spacing rhythm** from `--xps-space` (which it declares itself, defaulting to `0.75rem`).
- **A visible focus ring** — `2px solid currentColor` with a `2px` offset on `:focus-visible`.
  Nothing in either stylesheet ever sets `outline: none`.
- **A scoped reset**: `box-sizing: border-box`, list markers off, `font: inherit` on form
  controls, `max-width: 100%` on images, and `[hidden] { display: none !important }` so a host
  stylesheet cannot reveal the parts a widget has hidden. Every one of those rules is scoped
  inside `.xps`.
- **Loading skeletons** — `currentColor` at 12% opacity with a pulse that stops under
  `prefers-reduced-motion: reduce`.
- **Screen-reader and layout utilities** for custom widgets (below).
- Minimum `2.25rem` hit targets on buttons, inputs and pagination links.

Gzipped: `shell.css` ~2.2 KB, `default.css` ~1.5 KB.

### Using shell on its own

Load `shell.css`, skip `default.css`, and style the documented classes from your own design
system. The full class list, with the ARIA attributes and state modifiers each widget emits, is
`themes/MARKUP.md` in the library repository; the hand-written markup for every widget and every
state is `themes/fixtures/`, which is what to copy from when writing your rules.

Two things shell deliberately leaves to you, because both require a colour:

- `.xps-autocomplete__panel` has position but no surface. Give it a `background-color` (and
  usually a border or shadow), or the popup will be see-through over the page behind it.
- `mark.xps-highlight` keeps the browser's default yellow. Restyle it to match your palette.

State is always in the DOM, so you can style it without JavaScript: `--selected`, `--disabled`,
`--stalled`, `--open`, `--exhausted`, `--empty`, `--loading` modifiers, plus the real attributes
(`disabled`, `aria-current`, `aria-expanded`, `aria-disabled`).

### Utilities for custom widgets

A custom widget built on a connector (see [custom-widgets](custom-widgets.md)) inherits the reset
and the focus ring as soon as its root carries `xps`. These classes are part of the supported
surface:

| Class | Use |
|---|---|
| `xps-sr-only` | Visually hidden, still announced — labels, live-region status text. |
| `xps-stack` | Vertical flex, `gap: var(--xps-space)`. |
| `xps-cluster` | Horizontal flex that wraps, `gap: var(--xps-space)`. |
| `xps-button` | Button box with a `2.25rem` minimum height. `xps-button--primary` for the accent fill. |
| `xps-chip` | Removable token: `xps-chip__label`, `xps-chip__attribute`, `xps-chip__remove`. |
| `xps-skeleton` | Loading placeholder, with `--title`, `--text` and `--block` sizes. |
| `xps-highlight` | The class on `<mark>` elements emitted by the `highlight` template helper. |

```js
widgetParams.container.innerHTML = `
  <div class="xps xps-stack">
    <div class="xps-cluster">
      <button class="xps-button" type="button">Refine</button>
      <span class="xps-chip">
        <span class="xps-chip__label">Espresso</span>
        <button class="xps-chip__remove" type="button" aria-label="Remove filter Espresso">×</button>
      </span>
    </div>
  </div>`;
```

### The verification page

`themes/test/index.html` opens straight from disk — no server, no build step. It renders every
widget fixture three times: with shell only, with shell + default, and with shell plus a
deliberately hostile host stylesheet (`!important` colours, global `button`/`input`/`ul`/`a`/`mark`
rules, `* { box-sizing: content-box }`). Each section repeats a host-page sample block outside
every `.xps` element: if a widget stylesheet ever leaks, that block changes first. The page also
carries the keyboard walk-through to run against each section.

From `themes/`:

```
npm run check      # stylesheet rules + the fixture/CSS/MARKUP.md contract
npm run build:test # regenerate test/section-*.html after editing a fixture
npm run size       # raw and gzipped bytes
```

`npm run check` fails if `shell.css` grows a colour or a font, if `default.css` hard-codes a colour
outside its variable block, if either file grows a selector that is not scoped to `xps-`, if an
outline is removed without a replacement, or if the fixtures, the CSS and the markup contract stop
agreeing about a class name.

### Browser support

`default.css` uses `color-mix()` for its tints and hover states (Chrome/Edge 111+, Safari 16.2+,
Firefox 113+ — all shipped in 2023). Because those functions wrap a `var()`, a browser older than
that computes the declaration to `unset` rather than falling back to a previous value: the tint
simply does not paint. Nothing becomes unreadable — text, borders and layout are unaffected — but
in a pre-2023 browser the `<mark>` highlight, the chip fill, the active autocomplete option and
button hovers lose their background. If you must support one, add the flat colours yourself:

```css
.xps-highlight { background-color: #cfe0ff; }
.xps-chip { background-color: #f2f6ff; }
.xps-autocomplete__option--active { background-color: #eaf1ff; }
```

`shell.css` needs nothing newer than `:focus-visible`. Custom properties and logical properties
(`margin-inline-start`, `inset-inline`) are used unconditionally by both files.
