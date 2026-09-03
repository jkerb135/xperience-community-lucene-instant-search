// Self-check for the theme layer. Zero dependencies, regex tokenizer — see
// docs/internal/KNOWN-LIMITATIONS.md for what that cannot see.
// Run: npm run check (from themes/)
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { compileString } from 'sass';

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

// The shipped palettes (TH-8). default.css is the violet build under its old name — build-css.mjs
// asserts they are byte-identical — so everything below that reads `theme` speaks for it too.
const PALETTES = ['kentico-violet', 'kentico-orange'];
const palettes = new Map(PALETTES.map((name) => [name, read(`src/${name}.css`)]));

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

// (iii) every built theme puts its colours behind a variable: a colour literal may only appear in a
// custom-property declaration on an .xps selector (the token block and the dark-mode override).
// That rule is what lets one design source carry two palettes.
for (const [file, css] of [['src/default.css', theme], ...[...palettes].map(([n, c]) => [`src/${n}.css`, c])]) {
  for (const { selector, body } of blocks(css)) {
    for (const [prop, value] of decls(body)) {
      if (prop.startsWith('--') && selector.startsWith('.xps')) continue;
      if (LITERAL.test(value)) {
        fail(file, `"${selector}" hard-codes a colour in "${prop}: ${value}" — use var(--xps-…)`);
      }
    }
  }
}

// (ii) leak guard + (v) focus guard, both files.
const cssClasses = new Set();
for (const [name, css] of [['src/shell.css', shell], ['src/default.css', theme]]) {
  for (const { selector, body } of blocks(css)) {
    if (isKeyframeStep(selector)) continue;
    // Split on the commas BETWEEN selectors, not on the ones inside `:is(a, button)`.
    for (const one of selector.replace(/\([^()]*\)/g, '()').split(',')) {
      if (!/(^|[^\w-])\.?xps[\w-]*/.test(one)) fail(name, `selector "${one.trim()}" is not scoped to xps-`);
    }
    for (const m of selector.matchAll(/\.(xps[\w-]*)/g)) cssClasses.add(m[1]);
    const killsOutline = decls(body).some(([p, v]) => p === 'outline' && /^(none|0)$/.test(v));
    const replaces = decls(body).some(([p]) => p === 'box-shadow' || p === 'outline-offset' || p === 'border');
    if (killsOutline && !replaces) fail(name, `"${selector}" removes the outline without a replacement`);
  }
}

// (vi) the shipped palette is actually readable, and re-skinnable through the one token.
// The theme's RULES are stated inside `.xps.xps.xps` (src/scss/default/_root.scss), but the tokens
// stay on the plain `.xps`: they are the documented override surface.
const ROOT = '.xps';
const RATIOS = [
  ['light', ROOT],
  ['dark', `${ROOT}[data-xps-theme=auto]`],
];
const channel = (c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
const luminance = (hex) => {
  let h = hex.replace('#', '');
  if (h.length === 3) h = [...h].map((c) => c + c).join('');
  const [r, g, b] = [0, 2, 4].map((i) => channel(parseInt(h.slice(i, i + 2), 16) / 255));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const contrast = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const tokensOf = (css, selector) => {
  const block = blocks(css).find((b) => b.selector === selector);
  return Object.fromEntries(decls(block?.body ?? '').filter(([p]) => p.startsWith('--')));
};

// A token may be declared as an indirection (`--xps-color-accent-ink: var(--xps-color-accent)`),
// which is what keeps the one-token re-skin working. Follow the chain, falling back to the light
// block for anything the dark block does not restate.
const resolve = (t, light, name) => {
  let value = t[name] ?? light[name];
  for (let hop = 0; hop < 4; hop += 1) {
    const reference = /^var\(\s*(--[\w-]+)/.exec(value ?? '');
    if (!reference) return value;
    value = t[reference[1]] ?? light[reference[1]];
  }
  return value;
};

// The accent has three roles and three thresholds. `accent-ink` is the accent used AS TEXT on the
// surface, so it owes AA's 4.5:1 like any body text. `accent` is the fill and the decoration, so
// it owes WCAG 1.4.11's 3:1 for non-text UI. `on-accent` is the label ON that fill: the owner
// accepted 3:1 there for the brand button (TH-8, docs/internal/KNOWN-LIMITATIONS.md), so the check
// asserts 3 and prints the number rather than letting it pass unseen.
const PAIRS = [
  ['--xps-color-accent-ink', '--xps-color-surface', 4.5, 'AA text'],
  ['--xps-color-text', '--xps-color-surface', 4.5, 'AA text'],
  ['--xps-color-muted', '--xps-color-surface', 4.5, 'AA text'],
  ['--xps-color-accent', '--xps-color-surface', 3, 'WCAG 1.4.11 non-text'],
  ['--xps-color-on-accent', '--xps-color-accent', 3, 'owner-accepted (brand button)'],
];

// Both shipped palettes owe those ratios in both modes — the design source is shared, the colours
// are not.
for (const [name, css] of palettes) {
  const light = tokensOf(css, ROOT);
  for (const [mode, selector] of RATIOS) {
    const t = tokensOf(css, selector);
    for (const [token, against, floor, why] of PAIRS) {
      const [fg, bg] = [resolve(t, light, token), resolve(t, light, against)];
      const ratio = contrast(fg, bg);
      console.log(`        ${name} ${mode} ${token} ${fg} on ${bg}: ${ratio.toFixed(2)}:1 (needs ${floor}, ${why})`);
      if (ratio < floor) fail(`src/${name}.css`, `${mode} ${token} is ${ratio.toFixed(2)}:1 on ${bg} — ${why} needs ${floor}:1`);
    }
  }
}

// One token re-skins everything, whichever palette you started from: compiling an entry with a
// different $color-accent must leave none of that palette's accent behind anywhere in the file.
for (const name of PALETTES) {
  const shipped = tokensOf(palettes.get(name), ROOT)['--xps-color-accent'];
  const shippedDark = tokensOf(palettes.get(name), `${ROOT}[data-xps-theme=auto]`)['--xps-color-accent'];
  const reskinned = compileString(`@use '${name}' with ($color-accent: #b8005c);`, {
    loadPaths: [join(themes, 'src/scss')],
    charset: false,
  }).css.toLowerCase();
  if (reskinned.includes(shipped)) fail('src/scss', `${shipped} survives a $color-accent override of ${name} — derive it with color-mix`);
  if (!reskinned.includes('#b8005c')) fail('src/scss', `the overridden $color-accent never reaches --xps-color-accent in ${name}`);
  // The dark accent is its own knob, so it survives — but only as its one variable declaration.
  const darkLeft = reskinned.split(shippedDark).length - 1;
  if (darkLeft !== 1) fail('src/scss', `${shippedDark} appears ${darkLeft} times after a re-skin of ${name}; only the dark --xps-color-accent may hold it`);
}

// What the documented one-token re-skin actually yields. The default palette leaves `accent-ink`
// following `accent`, so a re-skin to a light brand colour drags the link text down with it. That
// is a real ceiling of the one-token story, not a build failure: report it, and let the guide tell
// a host to set `--xps-color-on-accent` and `--xps-color-accent-ink` when its accent is light.
{
  const reskinned = compileString(`@use 'default' with ($color-accent: #f05a22);`, {
    loadPaths: [join(themes, 'src/scss')],
    charset: false,
  }).css;
  const t = tokensOf(reskinned, ROOT);
  const ink = resolve(t, t, '--xps-color-accent-ink');
  const on = resolve(t, t, '--xps-color-on-accent');
  const [inkRatio, onRatio] = [contrast(ink, t['--xps-color-surface']), contrast(on, t['--xps-color-accent'])];
  console.log(
    `        one-token re-skin to #f05a22: accent-ink ${ink} on ${t['--xps-color-surface']} ` +
      `${inkRatio.toFixed(2)}:1 (${inkRatio < 4.5 ? 'BELOW AA — set --xps-color-accent-ink too' : 'AA'}), ` +
      `on-accent ${on} on ${t['--xps-color-accent']} ${onRatio.toFixed(2)}:1`
  );
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
console.log(`        default.css + ${PALETTES.join('.css + ')}.css: colour literals only in --xps-* declarations on an .xps selector`);
console.log('        both: every selector scoped to xps-, no outline removed without a replacement');

if (errors.length) {
  console.error(`\n${errors.length} problem(s):`);
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}
console.log('\nOK');
