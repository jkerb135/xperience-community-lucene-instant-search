// Compiles src/scss/{shell,default}.scss to the committed src/{shell,default}.css.
// Those two CSS files are what the RCL and the npm package ship, so they stay in source control.
// Run: npm run build  —  npm run check passes --check, which fails if they have drifted.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { compile } from 'sass';

const themes = join(dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');

for (const name of ['shell', 'default']) {
  const { css } = compile(join(themes, 'src', 'scss', `${name}.scss`), { style: 'expanded', charset: false });
  const doc = `${css}\n`;
  const path = join(themes, 'src', `${name}.css`);

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

if (checkOnly) console.log('        src/shell.css + src/default.css match src/scss/*.scss');
