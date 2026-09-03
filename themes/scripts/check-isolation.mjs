// Isolation check (TH-7): with shell.css + default.css included, a widget is a closed styling
// boundary — no host style may change any element inside it.
//
// Every fixture is rendered twice in a real browser, once with the two stylesheets and once with
// test/site-hostile.css (Dancing Goat's own rules, re-pointed at our markup, up to (0,2,1) and
// without !important) loaded after them, and every element under every widget root is compared
// property by property. Anything that differs is a leak.
//
// Playwright is not a dependency of this package: it is the one the docs screenshot tooling
// already installs. Run `npm install` in tools/screenshots once, then `npm run check` here.
import { readFileSync, readdirSync, writeFileSync, mkdtempSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';

const themes = join(dirname(fileURLToPath(import.meta.url)), '..');
const screenshots = join(themes, '..', 'tools', 'screenshots');

let chromium;
try {
  ({ chromium } = createRequire(join(screenshots, 'package.json'))('playwright'));
} catch {
  console.error(
    'playwright is missing — it belongs to the screenshot tooling, which shares it with this check.\n' +
      '  Run: cd tools/screenshots && npm install'
  );
  process.exit(1);
}

/** Everything a host stylesheet could plausibly reach. Compared verbatim, string for string. */
const PROPERTIES = [
  'color',
  'background-color',
  'background-image',
  'border-top-width', 'border-right-width', 'border-bottom-width', 'border-left-width',
  'border-top-style', 'border-right-style', 'border-bottom-style', 'border-left-style',
  'border-top-color', 'border-right-color', 'border-bottom-color', 'border-left-color',
  'border-top-left-radius', 'border-top-right-radius',
  'border-bottom-right-radius', 'border-bottom-left-radius',
  'font-family', 'font-size', 'font-style', 'font-weight',
  'line-height', 'letter-spacing', 'text-transform', 'text-decoration-line', 'text-align',
  'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
  'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
  'box-shadow', 'appearance', 'box-sizing', 'list-style-type', 'outline-style', 'cursor',
];

/** The host chrome Dancing Goat wraps its content in, so the hostile sheet's (0,2,1) rules bite. */
const WRAPPER_OPEN =
  '<div class="container"><div class="row"><section class="section landing-page"><div class="product-filter">';
const WRAPPER_CLOSE = '</div></section></div></div>';

const href = (file) => pathToFileURL(join(themes, file)).href;

/** Both shipped palettes are checked: they share a design source, not a build (TH-8). */
const PALETTES = ['kentico-violet', 'kentico-orange'];

const document_ = (fixture, palette, hostile) => `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<link rel="stylesheet" href="${href('src/shell.css')}">
<link rel="stylesheet" href="${href(`src/${palette}.css`)}">
${hostile ? `<link rel="stylesheet" href="${href('test/site-hostile.css')}">` : ''}
</head><body>${WRAPPER_OPEN}${fixture}${WRAPPER_CLOSE}</body></html>`;

/** Runs in the page: every element under every widget root, in document order, with its styles. */
const collect = (properties) => {
  // A widget root is an element carrying the `xps` class — that is what both stylesheets key on
  // and what the mount emits (themes/MARKUP.md). Page-level utilities the host puts on its OWN
  // elements (`xps-toolbar`, `xps-mount`, `xps-stack`) are outside the boundary by definition:
  // they are the host's boxes, holding the host's text, and the theme never paints them.
  const roots = [...document.querySelectorAll('[class~="xps"]')].filter(
    (element) => element.parentElement?.closest('[class~="xps"]') == null
  );
  const path = (element, root) => {
    const parts = [];
    for (let node = element; node && node !== root; node = node.parentElement) {
      const index = [...(node.parentElement?.children ?? [])].indexOf(node);
      parts.unshift(`${node.tagName.toLowerCase()}[${index}]`);
    }
    return parts.join(' > ');
  };
  return roots.flatMap((root) => {
    const rootName = (root.className || '').trim().split(/\s+/).join('.');
    return [root, ...root.querySelectorAll('*')].map((element) => {
      const style = getComputedStyle(element);
      return {
        where: `${rootName}${path(element, root) === '' ? '' : ` :: ${path(element, root)}`}`,
        what: `${element.tagName.toLowerCase()}${
          element.className ? `.${String(element.className).trim().split(/\s+/).join('.')}` : ''
        }`,
        style: Object.fromEntries(properties.map((property) => [property, style.getPropertyValue(property)])),
      };
    });
  });
};

const temp = mkdtempSync(join(tmpdir(), 'xps-isolation-'));
const write = (name, html) => {
  const file = join(temp, name);
  writeFileSync(file, html);
  return pathToFileURL(file).href;
};

const fixtures = readdirSync(join(themes, 'fixtures')).filter((f) => f.endsWith('.html'));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

const problems = [];
let compared = 0;

for (const palette of PALETTES) {
  for (const fixture of fixtures) {
    const html = readFileSync(join(themes, 'fixtures', fixture), 'utf8');
    const runs = [];
    for (const hostile of [false, true]) {
      const name = `${palette}-${fixture}-${hostile ? 'hostile' : 'clean'}.html`;
      await page.goto(write(name, document_(html, palette, hostile)));
      runs.push(await page.evaluate(collect, PROPERTIES));
    }
    const [clean, hostileRun] = runs;
    if (clean.length !== hostileRun.length) {
      problems.push(`${palette} ${fixture}: the hostile sheet changed the element count (${clean.length} → ${hostileRun.length})`);
      continue;
    }
    for (const [index, element] of clean.entries()) {
      const other = hostileRun[index];
      for (const property of PROPERTIES) {
        compared += 1;
        if (element.style[property] !== other.style[property]) {
          problems.push(
            `${palette} ${fixture} ${element.where}\n    ${element.what}\n      ${property}: "${element.style[property]}" → "${other.style[property]}"`
          );
        }
      }
    }
  }
}

await browser.close();

console.log(
  `        isolation: ${compared} computed values across ${fixtures.length} fixtures × ${PALETTES.length} palettes, ` +
    'shell+palette vs shell+palette+test/site-hostile.css'
);

if (problems.length) {
  console.error(`\n${problems.length} host style(s) bled into the widgets:`);
  for (const problem of problems.slice(0, 60)) console.error(`  - ${problem}`);
  if (problems.length > 60) console.error(`  … and ${problems.length - 60} more`);
  process.exit(1);
}
console.log('        isolation: no host style reaches inside a widget root');
