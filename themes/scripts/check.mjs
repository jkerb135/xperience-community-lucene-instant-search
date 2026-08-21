// Self-check for the theme layer. Zero dependencies, regex tokenizer — see
// docs/internal/KNOWN-LIMITATIONS.md for what that cannot see.
// Run: npm run check (from themes/)
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const themes = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => readFileSync(join(themes, p), 'utf8');
const strip = (css) => css.replace(/\/\*[\s\S]*?\*\//g, '');

const NAMED = 'aqua|black|blue|brown|fuchsia|gray|grey|green|lime|magenta|maroon|navy|olive|orange|pink|purple|red|silver|teal|violet|white|yellow';
const LITERAL = new RegExp(`#[0-9a-f]{3,8}\\b|\\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch)\\(|(?<![-\\w])(?:${NAMED})(?![-\\w])`, 'i');
const BANNED_PROP = /^(color|background|background-color|background-image|border|border-color|border-(top|right|bottom|left|inline|block)(-(start|end))?-color|font|font-family|font-size|font-weight)$/;
const STRUCTURAL_VALUE = /^(currentcolor|transparent|inherit|none|unset|initial|revert|0)$/i;
const CLASS_SHAPE = /^xps(?:-[a-z0-9]+)*(?:__[a-z0-9]+(?:-[a-z0-9]+)*)?(?:--[a-z0-9]+(?:-[a-z0-9]+)*)?$/;

const errors = [];
const fail = (file, msg) => errors.push(`${file}: ${msg}`);

/** Flat list of { selector, body } — nested at-rules are skipped, their inner blocks are not. */
function blocks(css) {
  return [...strip(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({
    selector: m[1].trim(),
    body: m[2],
  }));
}

const decls = (body) =>
  body
    .split(';')
    .map((d) => d.trim())
    .filter(Boolean)
    .map((d) => [d.slice(0, d.indexOf(':')).trim().toLowerCase(), d.slice(d.indexOf(':') + 1).trim()]);

const isKeyframeStep = (sel) => sel.split(',').every((s) => /^(from|to|[\d.]+%)$/.test(s.trim()));

const shell = read('src/shell.css');
const theme = read('src/default.css');

// (i) shell.css carries no colour, no font, no design.
for (const { selector, body } of blocks(shell)) {
  for (const [prop, value] of decls(body)) {
    if (prop.startsWith('--')) continue;
    if (BANNED_PROP.test(prop) && !STRUCTURAL_VALUE.test(value)) {
      fail('src/shell.css', `"${selector}" sets ${prop}: ${value} — shell is structure only`);
    }
    if (LITERAL.test(value)) {
      fail('src/shell.css', `"${selector}" uses the colour literal in "${prop}: ${value}"`);
    }
  }
}

// (iii) default.css puts every colour behind a variable: a colour literal may only appear in a
// custom-property declaration on an .xps selector (the spec §6 block and the dark-mode override).
for (const { selector, body } of blocks(theme)) {
  for (const [prop, value] of decls(body)) {
    if (prop.startsWith('--') && selector.startsWith('.xps')) continue;
    if (LITERAL.test(value)) {
      fail('src/default.css', `"${selector}" hard-codes a colour in "${prop}: ${value}" — use var(--xps-…)`);
    }
  }
}

// (ii) leak guard + (v) focus guard, both files.
const cssClasses = new Set();
for (const [name, css] of [['src/shell.css', shell], ['src/default.css', theme]]) {
  for (const { selector, body } of blocks(css)) {
    if (isKeyframeStep(selector)) continue;
    for (const one of selector.split(',')) {
      if (!/(^|[^\w-])\.?xps[\w-]*/.test(one)) fail(name, `selector "${one.trim()}" is not scoped to xps-`);
    }
    for (const m of selector.matchAll(/\.(xps[\w-]*)/g)) cssClasses.add(m[1]);
    const killsOutline = decls(body).some(([p, v]) => p === 'outline' && /^(none|0)$/.test(v));
    const replaces = decls(body).some(([p]) => p === 'box-shadow' || p === 'outline-offset' || p === 'border');
    if (killsOutline && !replaces) fail(name, `"${selector}" removes the outline without a replacement`);
  }
}

// (iv) three-way agreement between fixtures, CSS and MARKUP.md.
const fixtureClasses = new Map(); // class -> fixture that uses it
for (const file of readdirSync(join(themes, 'fixtures')).filter((f) => f.endsWith('.html'))) {
  for (const m of read(`fixtures/${file}`).matchAll(/class="([^"]+)"/g)) {
    for (const c of m[1].split(/\s+/)) if (c && !fixtureClasses.has(c)) fixtureClasses.set(c, file);
  }
}

const markup = read('MARKUP.md')
  .replace(/id="[^"]*"/g, '')
  .replace(/--xps-[a-z-]+/g, '')
  .replace(/data-xps-[a-z-]+/g, '');
const markupClasses = new Set(
  [...markup.matchAll(/(?<![-\w])xps[a-z0-9_-]*/g)]
    .map((m) => m[0].replace(/-+$/, ''))
    .filter((c) => CLASS_SHAPE.test(c)),
);

for (const [c, file] of fixtureClasses) {
  if (!cssClasses.has(c) && !markupClasses.has(c)) {
    fail(`fixtures/${file}`, `class "${c}" is neither styled nor documented in MARKUP.md`);
  }
}
for (const c of cssClasses) {
  if (!fixtureClasses.has(c)) fail('src/*.css', `".${c}" is styled but appears in no fixture`);
}
for (const c of markupClasses) {
  if (!fixtureClasses.has(c)) fail('MARKUP.md', `"${c}" is documented but appears in no fixture`);
}

console.log(`checked ${fixtureClasses.size} fixture classes across ${readdirSync(join(themes, 'fixtures')).length} fixtures`);
console.log(`        ${cssClasses.size} classes styled in shell.css + default.css, ${markupClasses.size} documented in MARKUP.md`);
console.log('        shell.css: no colour/font/border declarations, no colour literals');
console.log('        default.css: colour literals only in --xps-* declarations on an .xps selector');
console.log('        both: every selector scoped to xps-, no outline removed without a replacement');

if (errors.length) {
  console.error(`\n${errors.length} problem(s):`);
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}
console.log('\nOK');
