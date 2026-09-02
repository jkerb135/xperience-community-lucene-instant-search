// Two guards on the built package (PK-1). Run by `npm run build`, or on its own: npm run package:check
//
//   1. exports walk  — every target in the `exports` map exists in dist/themes/scss/styles, and
//                      every subpath is covered by `files`. Keeps the map and the tarball together.
//   2. tree-shake    — a real bundler pass over a fixture that imports only createSearch and
//                      searchBox must not carry a single byte of results.ts. That is the whole
//                      point of keeping bootstrap.ts free of widget imports.
import { buildSync } from 'esbuild';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { WIDGET_ENTRIES } from './widget-entries.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(readFileSync(join(clientDir, 'package.json'), 'utf8'));
const errors = [];

// --- 1. exports map ---------------------------------------------------------
// A `*` subpath is a pattern, so it is checked against a representative file instead.
const SAMPLES = { './scss/*': 'shell.scss', './styles/*': 'widgets/results.css' };
let targets = 0;

for (const [subpath, target] of Object.entries(pkg.exports)) {
  if (subpath === './package.json') continue; // npm always packs it, `files` never lists it
  const paths = typeof target === 'string' ? [target] : Object.values(target);
  for (const path of new Set(paths)) {
    const file = path.includes('*') ? path.replace('*', SAMPLES[subpath]) : path;
    targets += 1;
    if (!existsSync(join(clientDir, file))) {
      errors.push(`exports["${subpath}"] points at ${file}, which the build did not produce`);
    }
    const top = file.split('/')[1];
    if (!pkg.files.some((entry) => entry === top || entry.startsWith(`${top}/`) || entry === file.slice(2))) {
      errors.push(`exports["${subpath}"] -> ${file} is not covered by "files"`);
    }
  }
}

for (const name of Object.keys(WIDGET_ENTRIES)) {
  if (!pkg.exports[`./widgets/${name}`]) errors.push(`package.json has no "./widgets/${name}" export`);
  const css = `styles/widgets/${name}.css`;
  if (!existsSync(join(clientDir, css))) errors.push(`${css} was not built`);
}

console.log(`exports: ${targets} target(s) across ${Object.keys(pkg.exports).length} subpaths exist and are packed`);

// --- 2. tree-shaking --------------------------------------------------------
const RESULTS_MARKER = 'xps-result__snippet'; // only results.ts renders it
const SEARCH_BOX_MARKER = 'xps-search-box__field';
const INGESTION_MARKER = '/api/xpsearch/admin'; // only ingestion.ts knows the admin routes

const bundle = (source) =>
  buildSync({
    stdin: { contents: source, resolveDir: clientDir, sourcefile: 'treeshake-fixture.js' },
    bundle: true,
    minify: true,
    format: 'esm',
    target: 'es2020',
    write: false,
    logLevel: 'silent',
  }).outputFiles[0].text;

const fixtures = {
  'the package entry': `
    import { createSearch, searchBox } from './dist/xpsearch.mjs';
    globalThis.demo = [createSearch, searchBox];`,
  'the ./widgets/search-box subpath': `
    import { createSearch } from './dist/xpsearch.mjs';
    import { searchBox } from './dist/widgets/search-box.mjs';
    globalThis.demo = [createSearch, searchBox];`,
  'the ./widgets barrel': `
    import { createSearch } from './dist/xpsearch.mjs';
    import { searchBox } from './dist/widgets.mjs';
    globalThis.demo = [createSearch, searchBox];`,
};

for (const [what, source] of Object.entries(fixtures)) {
  const out = bundle(source);
  const before = errors.length;
  if (!out.includes(SEARCH_BOX_MARKER)) {
    errors.push(`tree-shake fixture for ${what} lost searchBox itself — the check proves nothing`);
  }
  if (out.includes(RESULTS_MARKER)) {
    errors.push(
      `importing createSearch + searchBox through ${what} still bundles results.ts ` +
        `("${RESULTS_MARKER}" is in the output). Something reachable from the entry references the widget map again.`
    );
  }
  const verdict = errors.length === before ? 'no results.ts' : 'FAILED';
  console.log(`tree-shake: ${what} -> ${(out.length / 1024).toFixed(1)} KB, ${verdict}`);
}

// --- 3. the ingestion subpath is Node-only and isolated both ways (CL-1) ----
// Its API key is a server-side secret, so no browser bundle may reach it: the root entry must not
// pull it in, and it must not drag a widget along either.
{
  const entry = bundle(`
    import { createSearch, searchBox } from './dist/xpsearch.mjs';
    globalThis.demo = [createSearch, searchBox];`);
  if (entry.includes(INGESTION_MARKER)) {
    errors.push(`the package entry bundles the ingestion client ("${INGESTION_MARKER}" is in the output); it must stay a subpath`);
  }
  const ingestion = bundle(`
    import { createIngestionClient } from './dist/ingestion.mjs';
    globalThis.demo = [createIngestionClient];`);
  if (!ingestion.includes(INGESTION_MARKER)) {
    errors.push('tree-shake fixture for ./ingestion lost the client itself — the check proves nothing');
  }
  for (const [marker, what] of [[SEARCH_BOX_MARKER, 'searchBox'], [RESULTS_MARKER, 'results.ts']]) {
    if (ingestion.includes(marker)) errors.push(`the ./ingestion subpath bundles ${what}; it must import contract types only`);
  }
  console.log(`ingestion: ./ingestion -> ${(ingestion.length / 1024).toFixed(1)} KB, no widget code; entry has no ingestion client`);
}

if (errors.length) {
  console.error(`\n${errors.length} problem(s):`);
  for (const error of errors) console.error(`  - ${error}`);
  process.exit(1);
}
console.log('\nOK');
