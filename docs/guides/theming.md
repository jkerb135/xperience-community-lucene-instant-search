## Theming

Two stylesheets, strictly separated. `shell.css` is **structure only** — layout, box model,
positioning, sizing, visibility and state mechanics, focus rings and screen-reader utilities; it
carries no colour, font, border, shadow or radius at all. `default.css` is **the design**, driven
entirely by CSS custom properties, and it is also what makes a widget a closed styling boundary
(see [Specificity and host styles](#specificity-and-host-styles)). A custom theme replaces
`default.css` and starts from the bare structure. Load both and override a couple of variables, and
the whole widget set matches your site:

```html
<link rel="stylesheet" href="/css/xpsearch/shell.css">
<link rel="stylesheet" href="/css/xpsearch/default.css">
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
| Xperience site | `<xps-search-assets />` emits both `<link>` tags; the `XperienceCommunity.Search.Widgets` package serves them as static web assets from `/_content/XperienceCommunity.Search.Widgets/xpsearch/`. |
| npm | `@xperience-community/xperience-search/themes/shell.css` and `.../themes/default.css` are package exports: `import '@xperience-community/xperience-search/themes/shell.css'` from a bundler, or copy the two files into the folder your site serves CSS from. The package also ships the SCSS sources (`.../scss/shell`, `.../scss/default`, `.../scss/widgets/<name>`) and per-widget compiled CSS (`.../styles/base.css`, `.../styles/widgets/<name>.css`) — see [JavaScript bundler setup](javascript-bundler-setup.md#build-time-theming-with-scss). |
| In this repository | `themes/src/shell.css` and `themes/src/default.css` — the shipped files. Both packages ship those exact two files; they are compiled from `themes/src/scss/` and committed, see [Working on the stylesheets](#working-on-the-stylesheets). |

`node_modules` is not usually web-served, so the `<link>` snippet above points at wherever your build
put them rather than at the package folder.

Load `shell.css` first. `default.css` assumes it.

### The `xps` class

`default.css` declares its variables on `.xps`, and `shell.css` scopes its reset and focus ring
to `.xps`. Every widget root carries both `xps` and its own class, so widgets are self-theming
wherever they land:

```html
<div class="xps xps-results">…</div>
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
| `--xps-color-accent` | `#af00fa` | Links and result titles, the current pagination page and its underline, the result type label, the primary button, chip tint, the active suggestions option, checkbox and range `accent-color`, the `<mark>` highlighter band, the focus ring colour. |
| `--xps-color-text` | `#1f2430` | Body text inside widgets, category-tree links, the facet group-title rule, the input inset shadow, the suggestions panel's shadow tint. |
| `--xps-color-muted` | `#5c6370` | Facet counts, result metadata, the result path line, result stats, pagination links, placeholders, group titles, empty states, keycaps, the skeleton tint. |
| `--xps-color-surface` | `#fff` | Input, button, keycap and suggestions-panel backgrounds; the text colour on accent-filled elements. |
| `--xps-color-border` | `#e3e5ea` | Every border, the keycaps, and the suggestions footer rule. |
| `--xps-radius` | `6px` | Corner radius on inputs, buttons, chips, the panel and hit images. A derived value (`calc(--xps-radius / 2)`) rounds the skeletons. |
| `--xps-space` | `0.75rem` | The whole spacing rhythm — gaps and padding are `var(--xps-space)`, `calc(var(--xps-space) / 2)` or `calc(var(--xps-space) * 2)`. **Also declared by `shell.css`**, so structure keeps its rhythm when the theme is not loaded. |
| `--xps-font` | `inherit` | `font-family` on `.xps`. Inherited by default, so the widgets read as part of your page — but stated from the token, so a host `button { font-family: … }` cannot reach inside one. |
| `--xps-font-size` | `1rem` | `font-size` on `.xps`, which every size inside a widget is `em`-relative to. Stated rather than inherited so a host `body { font-size: 20px }` cannot rescale the design; set it to `inherit` to go back to following your page. |
| `--xps-line-height` | `1.5` | `line-height` on `.xps`, same reasoning. |

Override them anywhere the cascade reaches — a stylesheet rule on `.xps`, an inline `style`, or a
scoped rule for one widget:

```css
/* one widget, different accent */
.xps-pagination { --xps-color-accent: #007a5e; }
```

The variables are declared on the plain `.xps` class on purpose: they are the override surface, so
a one-class rule of yours beats them. The rules they feed are stated more specifically — that is
the next section.

### Specificity and host styles

With `default.css` loaded, **every visual property of every element inside a widget is decided by
the theme**: colour, background, border, radius, font, line height, letter spacing, text transform
and decoration, alignment, margins, padding, shadow, `appearance`, `box-sizing`, list style,
outline and cursor. A site's own `button { … }`, `.section h3 { … }`, `.landing-page ul li { … }` or
`.product-filter input[type="checkbox"] { … }` does not reach inside one.

It works by specificity alone — there is no `!important` in either stylesheet. `default.css` states
the root class three times on every rule it owns:

```css
.xps            { --xps-color-accent: … }   /* (0,1,0) tokens: your override wins */
.xps.xps.xps *  { … }                       /* (0,3,0) the element reset          */
.xps.xps.xps h3 { … }                        /* (0,3,1) element defaults           */
.xps.xps.xps .xps-button { … }               /* (0,4,0) the components             */
```

A stylesheet that does not know our class names tops out at two classes plus an element, which the
reset outranks. `themes/scripts/check-isolation.mjs` proves it on every build: each fixture is
rendered twice in a real browser — plain, and under Dancing Goat's own CSS re-pointed at our
markup — and every computed property of every element is compared.

**Deliberately overriding the theme** is still one rule away: match the theme's specificity or
beat it. The theme's component rules are three classes plus the component's own, so this wins:

```css
/* your button style, inside the search widgets */
.xps.xps.xps.xps .xps-button {
  background-color: #272219;
  color: #fff;
  border-radius: 0;
}
```

`.xps` repeated four times is not elegant, and it is not the usual answer — overriding
`--xps-color-*`, `--xps-radius` and `--xps-font*` re-skins the whole set without any of this, and a
site with its own design system loads `shell.css` alone
(see [using shell on its own](#using-shell-on-its-own)). It is here for the one control your brand
insists on.

#### One token re-skins the theme

Every derived colour in `default.css` is a `color-mix` of the variables above — the hover fills,
the chip tints, the highlighter band, the input shadow. Nothing hard-codes the shipped violet, so
setting the accent alone is a complete re-skin:

```css
/* Kentico Heritage Orange */
.xps { --xps-color-accent: #f05a22; }
```

One caveat with that particular swap, and with any light accent: `#f05a22` is `3.39:1` on
white, which is fine for a button fill or a border but **fails WCAG AA for link text**. If you use
a light accent, either keep links on the text colour, or pick a darker shade of your brand colour
for the accent and use the light one elsewhere. The shipped `#af00fa` is `5.00:1` on white, and
the dark-mode `#c983f7` is `6.91:1` on `#17161d`; `themes/scripts/check.mjs` recomputes both on
every build.

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
  --xps-color-text: #f2f0f5;
  --xps-color-muted: #aaa6b4;
  --xps-color-surface: #17161d;
  --xps-color-border: #35323d;
  --xps-color-accent: #c983f7;
}
```

Those are the shipped dark values. The accent is a lighter violet than the light-mode one on
purpose: `#af00fa` is only `3.60:1` on `#17161d`, short of AA for link text.

### What shell gives you

`shell.css` never sets a colour other than `currentColor`, never sets a font, and never draws a
border. It is safe to load on any site. It provides:

- **Layout** for every widget: flex rows and columns, wrapping clusters, the facet-count column,
  the absolutely-positioned suggestions panel, the nested category-tree indentation.
- **Spacing rhythm** from `--xps-space` (which it declares itself, defaulting to `0.75rem`).
- **A visible focus ring** — `2px solid currentColor` with a `2px` offset on `:focus-visible`.
  Nothing in either stylesheet ever sets `outline: none`.
- **A scoped reset**: `box-sizing: border-box`, list markers off, `font: inherit` on form
  controls, `max-width: 100%` on images, and `[hidden] { display: none !important }` so a host
  stylesheet cannot reveal the parts a widget has hidden. Every one of those rules is scoped
  inside `.xps`.
- **The box of every control** — the `2.25rem` minimum hit target and the padding of buttons,
  inputs, selects, pills and pagination links (`themes/src/scss/_boxes.scss`, which `default.css`
  states again at its own specificity so a host rule cannot resize them).
- **Loading skeleton boxes** — their size and position. What they are painted with, and whether
  they pulse, is the theme's.
- **Screen-reader and layout utilities** for custom widgets (below).

Gzipped: `shell.css` ~2.2 KB, `default.css` ~1.5 KB.

### Using shell on its own

Load `shell.css`, skip `default.css`, and style the documented classes from your own design
system. The full class list, with the ARIA attributes and state modifiers each widget emits, is
`themes/MARKUP.md` in the library repository; the hand-written markup for every widget and every
state is `themes/fixtures/`, which is what to copy from when writing your rules.

Three things shell deliberately leaves to you, because all three require a colour:

- `.xps-suggestions__panel` has position but no surface. Give it a `background-color` (and
  usually a border or shadow), or the popup will be see-through over the page behind it.
- `mark.xps-highlight` keeps the browser's default yellow. Restyle it to match your palette.
- `.xps-skeleton` has the right box but no fill. Give it a `background-color` (and a pulse, if you
  want one — remember `prefers-reduced-motion`).

Without `default.css` there is no isolation either: your own site rules and the widgets' classes
share one cascade, which is the point of running shell alone.

State is always in the DOM, so you can style it without JavaScript: `--selected`, `--disabled`,
`--stalled`, `--open`, `--exhausted`, `--empty`, `--loading` modifiers, plus the real attributes
(`disabled`, `aria-current`, `aria-expanded`, `aria-disabled`).

### Utilities for custom widgets

A custom widget built on a behaviour (see [custom-widgets](custom-widgets.md)) inherits the reset
and the focus ring as soon as its root carries `xps`. These classes are part of the supported
surface:

| Class | Use |
|---|---|
| `xps-sr-only` | Visually hidden, still announced — labels, live-region status text. |
| `xps-stack` | Vertical flex, `gap: var(--xps-space)`. |
| `xps-cluster` | Horizontal flex that wraps, `gap: var(--xps-space)`. |
| `xps-button` | Button box with a `2.25rem` minimum height. `xps-button--primary` for the accent fill, `xps-button--link` for the muted-link look (what `clearFilters` renders). |
| `xps-chip` | Removable token: `xps-chip__label`, `xps-chip__attribute`, `xps-chip__remove`. |
| `xps-select` | Labelled select: `xps-select__label` (a real `<label for>`) and `xps-select__control` (the native `<select>`), plus `xps-select--disabled`. Wrap the control in `xps-select__field` with an `xps-select__chevron` `<svg>` for the design's own arrow; without that wrapper the platform arrow stays. The only themed `<select>` in the product — `sortSelect` renders this same block, and so should your drop-down. |
| `xps-toolbar` | One row, first child left, last child right, wrapping when narrow — the stats/sort row above the results. |
| `xps-sidebar__header` | The filter column's heading row: `xps-sidebar__title` plus a trailing clear-all, under the same rule a facet-group title carries. |
| `xps-skeleton` | Loading placeholder, with `--title`, `--text` and `--block` sizes. |
| `xps-highlight` | The class on `<mark>` elements emitted by the `highlight` template helper. |

```js
params.container.innerHTML = `
  <div class="xps xps-stack">
    <div class="xps-cluster">
      <button class="xps-button" type="button">Apply</button>
      <span class="xps-chip">
        <span class="xps-chip__label">Espresso</span>
        <button class="xps-chip__remove" type="button" aria-label="Remove filter Espresso">×</button>
      </span>
    </div>
  </div>`;
```

Anything outside this table is a *widget's own* contract (`xps-facet-list__*`, `xps-results__*`) or
does not exist. `themes/scripts/check.mjs` enforces a three-way agreement between `MARKUP.md`, the
CSS and the fixtures, and your class names are not in it: use the utilities above, or your own block
that you style yourself.

### Working on the stylesheets

*Only relevant if you are changing the shipped stylesheets in this repository. Theming your own
site needs no build step and no Sass — you write plain CSS against the class names above, and the
markup contract is exactly the same either way.*

The two stylesheets are authored in Sass and compiled into the CSS the packages ship:

| File | Role |
|---|---|
| `themes/src/scss/shell/*.scss`, `themes/src/scss/default/*.scss` | Authoring source, one partial per widget per layer (`shell/_results.scss` is the structure of the results widget, `default/_results.scss` its theme). Edit these. |
| `themes/src/scss/shell.scss`, `themes/src/scss/default.scss` | The two bundles: a `@forward` of the layer's `!default` variables and the partials in cascade order. Adding a widget means adding a partial and a `@use` line here. |
| `themes/src/scss/widgets/_<name>.scss`, `themes/src/scss/base.scss` | The à la carte entries the npm package exposes as `scss/widgets/<name>` and `scss/base`. |
| `themes/src/shell.css`, `themes/src/default.css` | Generated **and committed** — the files the RCL and the npm tarball copy. Do not edit by hand. |

```
cd themes
npm install        # the theme scripts now need dart-sass
npm run build      # src/scss/*.scss -> src/{shell,default}.css
```

`npm run check` recompiles to a temporary folder and fails if the committed CSS has drifted from the
Sass, so a forgotten `npm run build` cannot ship. The Sass is a convenience for us — nesting for
state rules, `$half`/`$quarter` for the `--xps-space` fractions, `!default` variables that only
supply the default *values* of the custom properties — and deliberately nothing more: the compiled
output is still plain, readable CSS whose custom properties are the theming API.

`src/XpSearch.Widgets/Client`'s build compiles the same sources again for the npm package
(`themes/*.css`, `styles/base.css`, `styles/widgets/*.css`) and fails if its output does not match
the committed CSS rule for rule, so the two packages can never ship different rules.

### The verification page

`themes/test/index.html` opens straight from disk — no server, no build step. It renders every
widget fixture three times: with shell only, with shell + default, and with shell plus a
deliberately hostile host stylesheet (`!important` colours, global `button`/`input`/`ul`/`a`/`mark`
rules, `* { box-sizing: content-box }`). Each section repeats a host-page sample block outside
every `.xps` element: if a widget stylesheet ever leaks, that block changes first. The page also
carries the keyboard walk-through to run against each section.

From `themes/` (run `npm install` first — the scripts need dart-sass):

```
npm run build      # compile src/scss/*.scss into the committed src/*.css
npm run check      # CSS/Sass drift + stylesheet rules + the fixture/CSS/MARKUP.md contract
npm run build:test # regenerate test/section-*.html after editing a fixture
npm run size       # raw and gzipped bytes
```

`npm run check` fails if the committed CSS no longer matches `src/scss/`, if `shell.css` grows a colour or a font, if `default.css` hard-codes a colour
outside its variable block, if either file grows a selector that is not scoped to `xps-`, if an
outline is removed without a replacement, or if the fixtures, the CSS and the markup contract stop
agreeing about a class name.

### Browser support

`default.css` uses `color-mix()` for its tints and hover states (Chrome/Edge 111+, Safari 16.2+,
Firefox 113+ — all shipped in 2023). Because those functions wrap a `var()`, a browser older than
that computes the declaration to `unset` rather than falling back to a previous value: the tint
simply does not paint. Nothing becomes unreadable — text, borders and layout are unaffected — but
in a pre-2023 browser the `<mark>` highlight, the chip fill, the active suggestions option and
button hovers lose their background. If you must support one, add the flat colours yourself:

```css
.xps-highlight { background-color: #cfe0ff; }
.xps-chip { background-color: #f2f6ff; }
.xps-suggestions__option--active { background-color: #eaf1ff; }
```

`shell.css` needs nothing newer than `:focus-visible`. Custom properties and logical properties
(`margin-inline-start`, `inset-inline`) are used unconditionally by both files.
