# Unit TH-7 — Theme hardening against site styles + selected refinements at zero hits

Owner walk of §Z (2026-09-03) found two defects:

- **129:** in the mobile Filter & sort sheet the buttons take Dancing Goat's site CSS (they should
  be white per the board) — the shipped theme loses on specificity. Owner: "make sure the theme
  colours are set for all items based on the design targeting CSS specificity since a lot is being
  overridden via site styles in the shipped theme scss." This is the same root cause as the deferred
  cosmetic quartet from 2026-09-01 (suggestion option text alignment, empty-state Clear-filters
  button contrast/width, sheet header inheriting the site heading colour, mobile checkboxes
  unthemed) — fold those in.
- **128:** with a filter applied that yields zero hits, the filter controls disappear ("Filters
  disappear when hits don't contain facet"): the facet lists collapse to "No matching filters"
  because the response carries no values for the filtered attribute, so the visitor cannot untick
  what they chose; only the Clear-all path remains. The board's *No results — with filters* column
  assumes the refinements stay visible.

Read `docs/internal/agent-primer.md`, `docs/internal/units/TH-1.md`, `TH-2.md`, `TH-6.md`,
`themes/MARKUP.md`, and `themes/README`/scripts. Work only in your worktree (branch `unit/th-7`).
The boards in `docs/internal/design/*.dc.html` remain the visual spec.

## 1. Theme hardening (themes/src/scss/{shell,default}, both stylesheets)

**Requirement (owner, verbatim intent):** "the style when including the theme exactly matches
each component design in the scss; no site styles should bleed into the components; they should
all be specific." So every `.xps-*` component is a **closed styling boundary**: with the theme
included, every visual property of every element inside a widget is decided by the theme (or by
the shell's structural rules), never by the host — colour, background, border (width/style/colour/
radius), font (family, size, weight, line-height, letter-spacing, text-transform, text-decoration),
text alignment, margin, padding, box-shadow, `appearance`, `box-sizing`, list-style, outline,
cursor. Where the theme deliberately inherits (e.g. body font family), it must set it explicitly
from a token (`--xps-font-family: inherit` is fine — the point is that a host `button { font-family: … }`
cannot reach in). The declared values are the boards' values (`docs/internal/design/*.dc.html`).
Concretely the theme must win against selectors up to specificity **(0,2,1)**,
e.g. `button`, `.button`, `a:hover`, `h2`, `.section h3`, `input[type=checkbox]`, `select`,
`.landing-page button.primary`, `p`, `ul li`. Dancing Goat's real stylesheets are the test
corpus: `F:/Personal/CommunityProjects/src/wwwroot/Content/Styles/*.css` (`Landing-page.css`
and whatever `/search` loads — check the page's `<link>`s) — extract the rules that hit
`button`, `a`, headings, inputs, selects, lists, paragraphs inside the search markup.

**Mechanism (decide, then apply uniformly — do not mix):** the lazy, conventional answer is a
theme root selector prefix on every default-theme rule so each lands at (0,2,0)+ and, for the
element-level rules the host is most likely to have, an explicit property reset on the widget's
interactive elements (`.xps-root :is(button, input, select, a)` → `font: inherit; color: inherit;
background: none; border: 0; …` followed by the themed declarations). The root already exists as
`data-xps-theme` on the host page (checklist D.19) — confirm what the shell/default stylesheets
key on today and keep ONE root selector documented in `themes/MARKUP.md` and `theming.md`
("the theme applies inside `[data-xps-theme]`; without it the shell is unstyled structure"). If a
rule needs (0,3,x) to beat a realistic host rule, double the block class (`.xps-x.xps-x`) — say
where and why in the report. `!important` is not acceptable.

**Scope — all of it, not just the sheet:** buttons (primary/secondary/ghost incl. the sheet's
Apply/Cancel, Clear filters, Show more, Load more, pagination), links (result titles, did-you-mean,
see-all), headings (sheet header, facet group titles, result titles), form controls (checkboxes,
range inputs, selects incl. sort select, search input), list resets (`ul/li` inside widgets),
paragraph margins inside cards, chips/pills, suggestion option text alignment, the empty-state
Clear-filters button contrast/width (quartet item), the mobile checkboxes (quartet item), the sheet
header colour (quartet item).

**Runnable check (this is the acceptance):** a Playwright-driven check in `themes/` (the docs
screenshot tooling already uses Playwright — reuse its dependency, no new package) that loads each
fixture in `themes/fixtures/*.html` with (a) the theme alone and (b) the theme plus an adversarial
`themes/test/site-hostile.css` you author from the Dancing Goat rules above (element and
utility-class selectors at up to (0,2,1)), and asserts `getComputedStyle` of every themed element
(EVERY element under each widget root × the full property set above: color, background-color,
border-*-width/style/color, border-radius, font-family, font-size, font-weight, line-height,
letter-spacing, text-transform, text-decoration-line, text-align, margin-*, padding-*, box-shadow,
appearance, box-sizing, list-style-type, outline-style, cursor) is identical in (a) and (b) — walk
the DOM, do not hand-pick selectors. Wire it into `npm run check`. It must fail
on today's stylesheets (prove it: run it before your changes and paste the failing lines) and pass
after. Contrast + re-skin checks stay green; `themes/src/*.css` regenerated and committed; the
widgets client recompiles the same sources (`npm run build` there must still pass).

## 2. Selected refinements stay visible at zero hits (widgets client)

- `facetList`, `categoryTree`: when the current state holds selected values for the attribute that
  the response's facet list does not carry (zero hits, or a value filtered out), render those
  selected values anyway, checked, with count 0, ahead of the response values — so they can be
  unticked. The "No matching filters" empty text shows only when there are neither response values
  nor selections. `rangeFilter`: keeps the state's from/to (verify it already does).
- `activeFilters` chips and `clearFilters` derive from state — verify they persist at zero hits
  (they should) and pin it.
- The counted empty state ("There are N results without them" + the primary button) must render in
  this situation on desktop AND in Load more mode (TH-6 §3) — pin both.
- Server-rendered first paint: not affected (facets are client-side); confirm.
- Tests: vitest cases for both list widgets (selected-but-absent value rendered checked with 0,
  untick removes it and re-queries), a11y gate green, fixtures updated if markup changed
  (`themes/fixtures/facet-list.html`, `category-tree.html`).

## 3. Docs + verification

- `docs/guides/theming.md`: a short "Specificity and host styles" section — the root selector,
  what the theme guarantees, how to override deliberately (a host that WANTS its button style
  uses a selector at least as specific as the theme's, and the section shows the exact selector).
- `themes/MARKUP.md`: the root selector rule; facet list "selected but absent" row state.
- `docs/internal/KNOWN-LIMITATIONS.md`: remove the cosmetic-quartet reference if it lives there
  (it lives in `.paul/STATE.md` deferred issues — leave that to the lead); add anything you
  simplified.
- CHANGELOG: `**Fixed (themes, widgets):**` one entry (hardening + refinements at zero hits).
- Suites: themes `build` + `check` (with the new hostile-CSS check), widgets client `build` +
  `test`, Widgets C# suite after the client build, Core suite.
- One commit on `unit/th-7`: `fix(themes,widgets): theme wins over site styles by specificity; selected refinements stay visible at zero hits (TH-7)`.

## Report

The mechanism chosen + where (0,3,x) was needed; the hostile-CSS check's failing lines BEFORE and
the pass AFTER; per-quartet-item status; suite lines; files changed; commit hash; concerns.
