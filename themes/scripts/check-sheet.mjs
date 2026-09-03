// The mobile filter sheet has to fit the viewport and scroll inside itself (owner item 130).
//
// A bottom sheet is a column flex box capped at 92vh: header, scrolling body, sticky footer. That
// only works while the body is allowed to shrink below its content — a flex item's `min-height`
// defaults to `auto`, and without `min-height: 0` the body refuses to, the panel grows past the
// cap, and the whole thing runs off the bottom of the screen with nothing scrollable.
//
// This check renders the real fixture at a phone viewport with enough facet content to overflow,
// then measures. Run: npm run check (or node scripts/check-sheet.mjs).
import { readFileSync } from 'node:fs';
import { writeFileSync, mkdtempSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { chromium, href, themes } from './browser.mjs';

const VIEWPORT = { width: 390, height: 600 };
/** How many times the last facet section is repeated, so the sheet is longer than the screen. */
const SECTIONS = 8;

const html = readFileSync(join(themes, 'fixtures', 'filter-sort.html'), 'utf8');
const document_ = `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="${href('src/shell.css')}">
<link rel="stylesheet" href="${href('src/default.css')}">
<style>body { margin: 0 }</style>
</head><body>${html}</body></html>`;

const file = join(mkdtempSync(join(tmpdir(), 'xps-sheet-')), 'sheet.html');
writeFileSync(file, document_);

const browser = await chromium().launch();
// The sheet slides up over 180ms; measuring through the transform would measure the animation.
// `reduce` is also the guard the shell already honours, so this exercises it.
const page = await browser.newPage({ viewport: VIEWPORT, reducedMotion: 'reduce' });
await page.goto(pathToFileURL(file).href);

const measured = await page.evaluate((sections) => {
  // A real facet sheet is longer than a phone: repeat the last section until it is.
  const body = document.querySelector('.xps-sheet__body');
  const last = body.lastElementChild;
  for (let at = 0; at < sections; at += 1) body.appendChild(last.cloneNode(true));

  const box = (selector) => {
    const { top, bottom, height } = document.querySelector(selector).getBoundingClientRect();
    return { top: Math.round(top), bottom: Math.round(bottom), height: Math.round(height) };
  };
  const panel = box('.xps-sheet__panel');
  const header = box('.xps-sheet__header');
  const footer = box('.xps-sheet__footer');

  body.scrollTop = 10_000;
  const scrolled = body.scrollTop;
  body.scrollTop = 0;

  return {
    viewport: window.innerHeight,
    panel,
    header,
    footer,
    body: { scrollHeight: body.scrollHeight, clientHeight: body.clientHeight, scrolled },
  };
}, SECTIONS);

await browser.close();

/**
 * The other half of the fix cannot be measured in a headless browser: `dvh` and `vh` are the same
 * there, because there is no browser chrome to retract. On a phone they are not — `100vh` is the
 * height with the URL bar hidden, so a sheet capped in `vh` alone puts its footer under the bar
 * with the page locked behind it. So this one is a text assertion on the shipped CSS: both the
 * fallback and the dynamic unit have to survive.
 */
const shell = readFileSync(join(themes, 'src', 'shell.css'), 'utf8');
const units = ['height: 100vh', 'height: 100dvh', 'max-height: 92vh', 'max-height: 92dvh'].filter(
  (declaration) => !shell.includes(declaration)
);

const { viewport, panel, header, footer, body } = measured;
const fits = (part) => part.top >= -1 && part.bottom <= viewport + 1;
const problems = [
  panel.bottom > viewport + 1 &&
    `the panel runs ${panel.bottom - viewport}px past the bottom of the ${viewport}px viewport`,
  panel.height > viewport * 0.92 + 2 &&
    `the panel is ${panel.height}px tall, past its ${Math.round(viewport * 0.92)}px cap (92vh)`,
  body.scrollHeight <= body.clientHeight &&
    `the body does not scroll: scrollHeight ${body.scrollHeight} <= clientHeight ${body.clientHeight}`,
  body.scrolled === 0 && 'the body did not move when scrolled to the end',
  !fits(header) && `the header is outside the viewport (${header.top}–${header.bottom})`,
  !fits(footer) &&
    `the footer with Clear all / Show results is outside the viewport (${footer.top}–${footer.bottom})`,
  units.length > 0 &&
    `src/shell.css lost ${units.join(' / ')} — the sheet must be sized in dvh with a vh fallback`,
].filter(Boolean);

console.log(
  `        sheet: ${VIEWPORT.width}×${VIEWPORT.height}, ${SECTIONS + 1} sections — ` +
    `panel ${panel.top}–${panel.bottom} (${panel.height}px of ${viewport}), ` +
    `body ${body.clientHeight}px showing ${body.scrollHeight}px, ` +
    `header ${header.top}–${header.bottom}, footer ${footer.top}–${footer.bottom}`
);

if (problems.length) {
  console.error(`\n${problems.length} problem(s) with the open filter sheet:`);
  for (const problem of problems) console.error(`  - ${problem}`);
  process.exit(1);
}
console.log(
  '        sheet: fits the viewport, body scrolls, header and footer both reachable, sized in dvh'
);
