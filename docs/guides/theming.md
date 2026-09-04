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

### Two shipped palettes

The design ships in two colourways, built from the same source and differing only in the accent:

| Palette | Stylesheet | Light accent | Dark accent |
|---|---|---|---|
| **kentico-violet** — the default | `kentico-violet.css`, and `default.css`, which is the same file under its original name | `#af00fa` | `#c983f7` |
| **kentico-orange** | `kentico-orange.css` | `#f05a22` | `#ff8852` |

Both are Kentico's own brand colours — the Xperience violet from the admin's own tag token
(`--color-background-tag-xperience-violet`), and Heritage Orange from
[the brand palette](https://brand.kentico.com/What-do-we-look-like/Colors). Everything the palette
drives beyond the accent — button hovers, chip and band tints, the focus ring — is a `color-mix` of
it, so the two stylesheets differ in three declarations.

#### The accent has three roles

A brand colour rarely does every job at once, so the accent is three tokens:

| Token | Where it lands | What it owes |
|---|---|---|
| `--xps-color-accent` | fills and decoration: the primary button's background, the selected sort pill, the slider, the current-page underline, the focus ring, every `color-mix` tint | 3:1 on the surface (WCAG 1.4.11, non-text UI) |
| `--xps-color-accent-ink` | the accent used **as text**: result titles and links, the did-you-mean correction, *Show more*, *See all*, the current page number, the type label | 4.5:1 on the surface (AA body text) |
| `--xps-color-on-accent` | the text placed **on** an accent fill: the primary button's label, the selected pill, the filter-count badge | 4.5:1 on the accent |

kentico-violet is dark enough to do all three: it declares `--xps-color-accent-ink:
var(--xps-color-accent)` and `--xps-color-on-accent: var(--xps-color-surface)`, so overriding the
accent alone still re-skins everything coherently. **kentico-orange is the worked example of the
split**: the brand `#f05a22` is `3.39:1` on white — right for a fill, short for text — so the
palette pins `--xps-color-accent-ink: #c64300` (`5.00:1`, Kentico's own darker orange from
`--color-background-tag-kentico-orange`) for everything that is read as text, and leaves the brand
on the fills. In dark mode the fill lightens to `#ff8852` (`7.61:1` on `#17161d`), which is legible
as text too, so the ink goes back to following the accent.

One number in kentico-orange is deliberately below AA: the **white label on the `#f05a22` primary
button is `3.39:1`**, an accepted trade for the brand button (recorded in
`docs/internal/KNOWN-LIMITATIONS.md`, and printed by `npm run check` on every build). If your site
needs AA there, one declaration fixes it without a fork:

```css
.xps { --xps-color-on-accent: #1f2430; } /* 4.57:1 on #f05a22 */
```

Pick one instead of `default.css`; never load both.

```html
<!-- no-build: swap the second <link> -->
<link rel="stylesheet" href="/css/xpsearch/shell.css">
<link rel="stylesheet" href="/css/xpsearch/kentico-orange.css">
```

```cshtml
@* Xperience site: the tag helper takes the palette by name.
   theme="default" (the default) and theme="kentico-violet" load the same stylesheet. *@
<xps-search-assets theme="kentico-orange" />
```

```js
// bundler, plain CSS
import '@xperience-community/xperience-search/themes/shell.css';
import '@xperience-community/xperience-search/themes/kentico-orange.css';
```

```scss
// bundler, SCSS — one entry point carries structure-free design plus the palette
@use "@xperience-community/xperience-search/scss/kentico-orange";
```

Compiling the widget partials à la carte? Select the palette on the **first** line — sass fixes a
module's configuration on its first load, so anything that reads the tokens has to come after it:

```scss
@use "@xperience-community/xperience-search/scss/palettes/kentico-orange";
@use "@xperience-community/xperience-search/scss/base";
@use "@xperience-community/xperience-search/scss/widgets/results";
```

**Your own palette.** A palette file is nothing but the ten colour values — copy
`themes/src/scss/tokens/_kentico-violet.scss` (shipped as `scss/tokens/_kentico-violet.scss`),
change what you like, and point a copy of `palettes/_kentico-orange.scss` at it. Without a build
step, the same thing is a variable block: load `default.css` and override
[the variables below](#variable-reference), which is what the rest of this page is about.

### Where the files are

| Consumer | Path |
|---|---|
| Xperience site | `<xps-search-assets />` emits both `<link>` tags (`theme="kentico-orange"` for the other palette); the `XperienceCommunity.Search.Widgets` package serves them as static web assets from `/_content/XperienceCommunity.Search.Widgets/xpsearch/`. |
| npm | `@xperience-community/xperience-search/themes/shell.css`, `.../themes/default.css`, `.../themes/kentico-violet.css` and `.../themes/kentico-orange.css` are package exports: `import '@xperience-community/xperience-search/themes/shell.css'` from a bundler, or copy the files into the folder your site serves CSS from. The package also ships the SCSS sources (`.../scss/shell`, `.../scss/default`, `.../scss/kentico-orange`, `.../scss/palettes/<name>`, `.../scss/tokens/<name>`, `.../scss/widgets/<name>`) and per-widget compiled CSS (`.../styles/base.css`, `.../styles/widgets/<name>.css`) — see [JavaScript bundler setup](javascript-bundler-setup.md#build-time-theming-with-scss). |
| In this repository | `themes/src/shell.css`, `themes/src/default.css`, `themes/src/kentico-violet.css` and `themes/src/kentico-orange.css` — the shipped files. Both packages ship those exact files; they are compiled from `themes/src/scss/` and committed, see [Working on the stylesheets](#working-on-the-stylesheets). |

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

Every one of these is declared on `.xps` in the theme stylesheet with the value shown. The two
palettes differ in the accent only; everything else is shared.

| Variable | kentico-violet (`default.css`) | kentico-orange | Drives |
|---|---|---|---|
| `--xps-color-accent` | `#af00fa` | `#f05a22` | **Fills and decoration only**: the primary button and selected pill backgrounds, the filter badge, chip tint, the active suggestions option, the current page's underline, checkbox and range `accent-color`, the range track and thumbs, the `<mark>` highlighter band, the focus ring. Accent-coloured *text* is `--xps-color-accent-ink`. |
| `--xps-color-accent-ink` | `var(--xps-color-accent)` | `#c64300` | The accent used **as text on the surface**: result titles and links, the did-you-mean correction, *Show more*, *See all*, the suggestions page title, the current page number, the result type label, the selected category. Defaults to the accent, so a dark-enough accent needs nothing else. |
| `--xps-color-on-accent` | `var(--xps-color-surface)` | same (`#fff` light, `#17161d` dark) | The text placed **on an accent fill**: the primary button's label, the selected sort pill, the filter-count badge. |
| `--xps-color-text` | `#1f2430` | same | Body text inside widgets, category-tree links, the facet group-title rule, the input inset shadow, the suggestions panel's shadow tint. |
| `--xps-color-muted` | `#5c6370` | same | Facet counts, result metadata, the result path line, result stats, pagination links, placeholders, group titles, empty states, keycaps, the skeleton tint. |
| `--xps-color-surface` | `#fff` | same | Input, button, keycap and suggestions-panel backgrounds; the text colour on accent-filled elements. |
| `--xps-color-border` | `#e3e5ea` | same | Every border, the keycaps, and the suggestions footer rule. |
| `--xps-radius` | `6px` | same | Corner radius on inputs, buttons, chips, the panel and hit images. A derived value (`calc(--xps-radius / 2)`) rounds the skeletons. |
| `--xps-space` | `0.75rem` | same | The whole spacing rhythm — gaps and padding are `var(--xps-space)`, `calc(var(--xps-space) / 2)` or `calc(var(--xps-space) * 2)`. **Also declared by `shell.css`**, so structure keeps its rhythm when the theme is not loaded. |
| `--xps-font` | `inherit` | same | `font-family` on `.xps`. Inherited by default, so the widgets read as part of your page — but stated from the token, so a host `button { font-family: … }` cannot reach inside one. |
| `--xps-font-size` | `1rem` | same | `font-size` on `.xps`, which every size inside a widget is `em`-relative to. Stated rather than inherited so a host `body { font-size: 20px }` cannot rescale the design; set it to `inherit` to go back to following your page. |
| `--xps-line-height` | `1.5` | same | `line-height` on `.xps`, same reasoning. |

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

That works because `--xps-color-accent-ink` and `--xps-color-on-accent` follow the accent by
default — one token really does move everything.

One caveat with that particular swap, and with any light accent: `#f05a22` is `3.39:1` on white,
which is fine for a button fill or a border but **fails WCAG AA for link text**, and the ink
followed the accent down with it. A light accent therefore takes two more tokens — which is exactly
what the shipped kentico-orange does:

```css
.xps {
  --xps-color-accent: #f05a22;      /* fills, 3.39:1 — over the 3:1 non-text bar */
  --xps-color-accent-ink: #c64300;  /* accent text, 5.00:1 — AA */
  --xps-color-on-accent: #1f2430;   /* label on the fill, 4.57:1 — AA */
}
```

The shipped `#af00fa` is `5.00:1` on white and the dark-mode `#c983f7` is `6.91:1` on `#17161d`;
`themes/scripts/check.mjs` recomputes every pair of both palettes on every build, and prints what a
one-token re-skin to `#f05a22` would yield so this caveat cannot go stale.

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

Those are the shipped dark values of kentico-violet. The accent is a lighter violet than the
light-mode one on purpose: `#af00fa` is only `3.60:1` on `#17161d`, short of AA for link text.
kentico-orange lightens its accent for the same reason — its dark value is `#ff8852` (`7.61:1`),
light enough to be the accent ink as well, so the ink token goes back to following the accent in
dark mode. `--xps-color-on-accent` is `var(--xps-color-surface)` in both palettes, which means a
dark-mode fill carries the dark surface colour as its label (`6.91:1` violet, `7.61:1` orange) —
white on either dark accent would be under `3:1`. The four neutrals are the same in both palettes.

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
| `xps-chip` | Removable token: `xps-chip__label` holding `xps-chip__attribute` and `xps-chip__value`, plus `xps-chip__remove`. |
| `xps-select` | Labelled select: `xps-select__label` (a real `<label for>`) and `xps-select__control` (the native `<select>`), plus `xps-select--disabled`. Wrap the control in `xps-select__field` with an `xps-select__chevron` `<svg>` for the design's own arrow; without that wrapper the platform arrow stays. The only themed `<select>` in the product — `sortSelect` renders this same block, and so should your drop-down. |
| `xps-toolbar` | One row, first child left, last child right, wrapping when narrow — the stats/sort row above the results. |
| `xps-sidebar` | The filter column. The one composition class the theme paints: a card (surface, border, radius, `1.25rem` padding, soft shadow) around the refinement widgets. Put `xps` on the same element — the theme's rule for it is `.xps.xps.xps.xps-sidebar`. |
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
| `themes/src/scss/tokens/_<palette>.scss` | A palette: the ten colour values, and the only place in the whole tree that holds a colour literal. |
| `themes/src/scss/_theme-variables.scss` | The one indirection every design partial reads — the palette's colours plus the shape/typography knobs. Defaults to `tokens/kentico-violet`. |
| `themes/src/scss/palettes/_<palette>.scss`, `themes/src/scss/<palette>.scss` | Selecting a palette: the `palettes/` partial configures `_theme-variables.scss`, the top-level entry adds the whole design on top and compiles to `src/<palette>.css`. |
| `themes/src/scss/widgets/_<name>.scss`, `themes/src/scss/base.scss` | The à la carte entries the npm package exposes as `scss/widgets/<name>` and `scss/base`. |
| `themes/src/shell.css`, `themes/src/default.css`, `themes/src/kentico-violet.css`, `themes/src/kentico-orange.css` | Generated **and committed** — the files the RCL and the npm tarball copy. Do not edit by hand. |

```
cd themes
npm install        # the theme scripts now need dart-sass
npm run build      # src/scss/*.scss -> src/{shell,default,kentico-violet,kentico-orange}.css
```

`default.css` and `kentico-violet.css` are built from separate entry points and the build fails if
they are not byte-identical: `default` is violet, and the older name has to keep meaning that.

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
widget fixture four times: with shell only, with shell + default, with shell + kentico-orange (the
two palettes side by side), and with shell plus a deliberately hostile host stylesheet (`!important` colours, global `button`/`input`/`ul`/`a`/`mark`
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

`npm run check` fails if the committed CSS no longer matches `src/scss/`, if `default.css` and
`kentico-violet.css` are not the same bytes, if `shell.css` grows a colour or a font, if a theme
stylesheet hard-codes a colour outside its variable block, if either palette drops below AA in
light or dark mode, if overriding `$color-accent` leaves that palette's accent behind anywhere, if
either file grows a selector that is not scoped to `xps-`, if an outline is removed without a
replacement, if the fixtures, the CSS and the markup contract stop agreeing about a class name, or
if a host stylesheet reaches inside a widget in either palette.

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
