// Layout parity (TH-11): the theme never changes layout.
//
// `shell.css` is structure, `default.css` is design. That split is only real if adding the theme
// moves nothing. Every fixture is rendered twice — with the shell alone, and with shell + palette —
// and every element under every widget root is compared on the box it occupies.
//
// It exists because the theme's element reset zeroes `margin`/`padding` on `p`, `ul`, `li`, the
// headings and the controls at (0,3,0) — it has to, a host's `ul li { padding-left: 40px }` must
// lose — which silently wipes structural spacing the shell declared on such an element at (0,1,0).
// That is how the autocomplete rows lost their padding: `suggestionsPanel.ts` renders an option as
// an `<li>` when the panel shows a single source. The isolation check cannot see this; both of its
// renders carry the reset. The answer is never to weaken the reset — it is to restate the box in
// `_boxes.scss`, which both stylesheets emit from one source.
//
// WHAT IS COMPARED: the layout properties `shell.css` actually declares for that element, read off
// its own CSSOM. Comparing every layout property of both renders would mostly compare the theme
// against the *user agent* — the UA gives controls a 2px border and `p`/`ul` their margins, and the
// shell deliberately leaves what is not structural alone — which says nothing about this split. A
// property the shell states is a promise about the box; a property it does not state is not.
//
// Playwright comes from tools/screenshots, like the other browser checks.
import { readFileSync, readdirSync, writeFileSync, mkdtempSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { chromium, themes } from './browser.mjs';

/** The box an element occupies, and how it places its children. Longhands: the CSSOM expands. */
const PROPERTIES = [
  'display', 'flex-direction', 'flex-wrap', 'flex-grow', 'flex-shrink', 'flex-basis',
  'align-items', 'align-self', 'justify-content', 'row-gap', 'column-gap',
  'grid-template-columns', 'grid-template-rows', 'grid-auto-flow', 'grid-column-start', 'grid-row-start',
  'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
  'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
  'width', 'height', 'min-width', 'min-height', 'max-width', 'max-height',
  'position', 'top', 'right', 'bottom', 'left',
  'overflow-x', 'overflow-y', 'box-sizing',
];

// Both renders are levelled before they are read. Without this the comparison would mostly report
// the theme's TYPE SCALE and its borders: the shell states neither (`check.mjs` forbids `font-size`
// and colour/border declarations in it), so under the shell alone every `em` length resolves
// against the user agent's 16px and every control keeps the UA's 2px border — and `padding: 0.5em`,
// `min-width: 1.5em`, `top: 100%` and every used width then differ for a reason that is design, not
// layout. Pinning the type scale and the borders in BOTH renders leaves exactly the differences the
// split is about. `!important` belongs in a harness; it never ships.
const NEUTRAL = `.xps, .xps * { font-size: 16px !important; line-height: normal !important;
  border-width: 0 !important; }`;

/**
 * Elements whose shell-declared box the theme is allowed to change, with the reason. Empty, and it
 * should stay that way: a theme that has to move something is a shell rule in the wrong file.
 */
const EXCEPTIONS = [];

const WRAPPER_OPEN = '<div class="container">';
const WRAPPER_CLOSE = '</div>';
const PALETTES = ['kentico-violet', 'kentico-orange'];

// The stylesheets are INLINED, not linked: a `file://` document may not read the `cssRules` of a
// sheet loaded from another `file://` URL, and reading the shell's own declarations is the point.
const sheet = (name) => readFileSync(join(themes, 'src', name), 'utf8');
const document_ = (fixture, palette) => `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<style>${sheet('shell.css')}</style>
${palette ? `<style>${sheet(`${palette}.css`)}</style>` : ''}
<style>${NEUTRAL}</style>
</head><body>${WRAPPER_OPEN}${fixture}${WRAPPER_CLOSE}</body></html>`;

/**
 * Runs in the page. `declaring` is the index of the stylesheet whose declarations decide what is
 * compared (the shell); pass null to compare everything already listed by the first run.
 */
const collect = ({ properties, declaring }) => {
  const roots = [...document.querySelectorAll('[class~="xps"]')].filter(
    (element) => element.parentElement?.closest('[class~="xps"]') == null
  );

  /** Flatten the sheet to style rules that are actually in force (media queries evaluated). */
  const styleRules = [];
  if (declaring !== null) {
    const walk = (rules) => {
      for (const rule of rules) {
        if (rule.selectorText !== undefined && rule.style !== undefined) styleRules.push(rule);
        else if (rule.cssRules !== undefined && (rule.conditionText === undefined || matchMedia(rule.conditionText).matches)) {
          walk(rule.cssRules);
        }
      }
    };
    walk(document.styleSheets[declaring].cssRules);
  }

  // A shorthand that contains `var()` stays a shorthand in the CSSOM — `padding: calc(var(--xps-space)
  // / 2) var(--xps-space)` does NOT answer to `getPropertyValue('padding-top')` — and those are
  // exactly the shell's structural declarations. So the declared names are expanded by hand. The
  // logical properties map to their LTR sides; the fixtures are LTR.
  const EXPAND = {
    margin: ['margin-top', 'margin-right', 'margin-bottom', 'margin-left'],
    'margin-inline': ['margin-left', 'margin-right'],
    'margin-block': ['margin-top', 'margin-bottom'],
    'margin-inline-start': ['margin-left'],
    'margin-inline-end': ['margin-right'],
    padding: ['padding-top', 'padding-right', 'padding-bottom', 'padding-left'],
    'padding-inline': ['padding-left', 'padding-right'],
    'padding-block': ['padding-top', 'padding-bottom'],
    'padding-inline-start': ['padding-left'],
    'padding-inline-end': ['padding-right'],
    gap: ['row-gap', 'column-gap'],
    inset: ['top', 'right', 'bottom', 'left'],
    'inset-inline': ['left', 'right'],
    'inset-block': ['top', 'bottom'],
    overflow: ['overflow-x', 'overflow-y'],
    flex: ['flex-grow', 'flex-shrink', 'flex-basis'],
    'inline-size': ['width'],
    'block-size': ['height'],
    'min-inline-size': ['min-width'],
    'max-inline-size': ['max-width'],
  };

  const declared = (element) => {
    const names = new Set();
    for (const rule of styleRules) {
      let hit = false;
      try {
        hit = element.matches(rule.selectorText);
      } catch {
        hit = false; // vendor pseudo-elements etc. — nothing an element can match anyway
      }
      if (!hit) continue;
      for (let at = 0; at < rule.style.length; at += 1) {
        const authored = rule.style.item(at);
        // `auto` is not a box, it is a negotiation: `margin-inline-start: auto` on a facet count
        // resolves to whatever the row's text left over, and the theme changes that text
        // (`font-variant-numeric: tabular-nums` alone moves it). Nothing to compare.
        if (rule.style.getPropertyValue(authored).trim() === 'auto') continue;
        for (const property of EXPAND[authored] ?? [authored]) {
          if (properties.includes(property)) names.add(property);
        }
      }
    }
    return [...names];
  };

  return roots.flatMap((root) => {
    const rootName = (root.className || '').trim().split(/\s+/).join('.');
    return [root, ...root.querySelectorAll('*')].map((element, index) => {
      const style = getComputedStyle(element);
      return {
        where: `${rootName} :: [${index}] ${element.tagName.toLowerCase()}${
          typeof element.className === 'string' && element.className !== ''
            ? `.${element.className.trim().split(/\s+/).join('.')}`
            : ''
        }`,
        declared: declaring === null ? [] : declared(element),
        style: Object.fromEntries(properties.map((property) => [property, style.getPropertyValue(property)])),
      };
    });
  });
};

const temp = mkdtempSync(join(tmpdir(), 'xps-layout-'));
const write = (name, html) => {
  const file = join(temp, name);
  writeFileSync(file, html);
  return pathToFileURL(file).href;
};

const fixtures = readdirSync(join(themes, 'fixtures')).filter((f) => f.endsWith('.html'));
const browser = await chromium().launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

const problems = [];
let compared = 0;

for (const fixture of fixtures) {
  const html = readFileSync(join(themes, 'fixtures', fixture), 'utf8');
  await page.goto(write(`shell-${fixture}`, document_(html, null)));
  const shell = await page.evaluate(collect, { properties: PROPERTIES, declaring: 0 });

  for (const palette of PALETTES) {
    await page.goto(write(`${palette}-${fixture}`, document_(html, palette)));
    const themed = await page.evaluate(collect, { properties: PROPERTIES, declaring: null });
    if (shell.length !== themed.length) {
      problems.push(`${palette} ${fixture}: the theme changed the element count (${shell.length} → ${themed.length})`);
      continue;
    }
    for (const [index, element] of shell.entries()) {
      for (const property of element.declared) {
        compared += 1;
        if (element.style[property] === themed[index].style[property]) continue;
        if (EXCEPTIONS.some((e) => element.where.endsWith(e.where) && e.property === property)) continue;
        problems.push(
          `${palette} ${fixture} ${element.where}\n      ${property}: shell "${element.style[property]}" → themed "${themed[index].style[property]}"`
        );
      }
    }
  }
}

await browser.close();

console.log(
  `        layout: ${compared} shell-declared values across ${fixtures.length} fixtures × ${PALETTES.length} palettes, ` +
    'shell alone vs shell+palette'
);

if (problems.length) {
  console.error(`\n${problems.length} layout difference(s) — the theme may only add visuals:`);
  for (const problem of problems.slice(0, 60)) console.error(`  - ${problem}`);
  if (problems.length > 60) console.error(`  … and ${problems.length - 60} more`);
  process.exit(1);
}
console.log('        layout: the theme leaves every box the shell declares exactly where it was');
