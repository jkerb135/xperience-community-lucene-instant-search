// The two things the tarball has to carry besides `dist/`: the stylesheets and a runnable mock
// server (spec 5.9; docs/guides/theming.md and js-client.md both promise them to a JS-only
// consumer). Both outputs are gitignored build products, like dist/.
import { buildSync } from 'esbuild';
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const themesSrc = resolve(clientDir, '../../../themes/src');

// 1. themes/*.css — the same two files the .NET package serves as static web assets.
const themesOut = join(clientDir, 'themes');
mkdirSync(themesOut, { recursive: true });
for (const name of ['shell.css', 'default.css']) {
  copyFileSync(join(themesSrc, name), join(themesOut, name));
}

// 2. mock/server.mjs — bundled, because mock/server.ts imports the contract constants from src/.
buildSync({
  entryPoints: [join(clientDir, 'mock/server.ts')],
  outfile: join(clientDir, 'mock/server.mjs'),
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node20',
  banner: { js: '#!/usr/bin/env node' },
});

console.log('packaged themes/shell.css, themes/default.css, mock/server.mjs');
