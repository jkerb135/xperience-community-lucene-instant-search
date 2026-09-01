// The runnable mock server the tarball has to carry besides `dist/` (spec 5.9;
// docs/guides/js-client.md promises it to a JS-only consumer). The stylesheets are built by
// scripts/build-styles.mjs. Both outputs are gitignored build products, like dist/.
import { buildSync } from 'esbuild';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// mock/server.mjs — bundled, because mock/server.ts imports the contract constants from src/.
buildSync({
  entryPoints: [join(clientDir, 'mock/server.ts')],
  outfile: join(clientDir, 'mock/server.mjs'),
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node20',
  banner: { js: '#!/usr/bin/env node' },
});

console.log('packaged mock/server.mjs');
