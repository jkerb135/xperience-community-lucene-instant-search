// Compiles the four entry points in src/scss/ to the committed src/*.css. Those files are what the
// RCL and the npm package ship, so they stay in source control.
// Run: npm run build  —  npm run check passes --check, which fails if they have drifted.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { compile } from 'sass';

const themes = join(dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');

// shell = structure; default = the visual layer in its default palette; the two palette entries are
// the same design with the palette selected by name. TH-8 kept `default` so no host breaks, which
// only holds if it stays the violet build byte for byte — asserted below, not assumed.
export const ENTRIES = ['shell', 'default', 'kentico-violet', 'kentico-orange'];

const built = {};

for (const name of ENTRIES) {
  const { css } = compile(join(themes, 'src', 'scss', `${name}.scss`), { style: 'expanded', charset: false });
  const doc = `${css}\n`;
  const path = join(themes, 'src', `${name}.css`);
  built[name] = doc;

  if (checkOnly) {
    // compare line-ending-insensitively: git may have checked the file out with CRLF
    const current = (() => { try { return readFileSync(path, 'utf8').replace(/\r\n/g, '\n'); } catch { return null; } })();
    if (current !== doc) {
      console.error(`src/${name}.css is out of date with src/scss/${name}.scss — run: npm run build`);
      process.exit(1);
    }
  } else {
    writeFileSync(path, doc);
    console.log(`wrote src/${name}.css (${doc.length} B)`);
  }
}

if (built['default'] !== built['kentico-violet']) {
  console.error('src/default.css and src/kentico-violet.css differ — `default` must stay the violet build');
  process.exit(1);
}

if (checkOnly) console.log(`        src/*.css match src/scss/*.scss (${ENTRIES.join(', ')})`);
console.log('        default.css is byte-identical to kentico-violet.css');
