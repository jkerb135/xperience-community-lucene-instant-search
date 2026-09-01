// ESM + UMD bundles (spec 5.9). esbuild strips the types and minifies; rollup does the module
// graph and the UMD wrapper. No babel, no plugins beyond these ~40 lines.
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { transformSync } from 'esbuild';
import { WIDGET_ENTRIES } from './scripts/widget-entries.mjs';

const TARGET = 'es2020';

/** `./widgets/<kebab>` subpaths (PK-1), sharing chunks with the main entries. */
const widgetInputs = Object.fromEntries(
  Object.entries(WIDGET_ENTRIES).map(([name, module]) => [
    `widgets/${name}`,
    `src/widgets/${module}.ts`,
  ])
);

/** Resolves the extensionless relative imports the source uses, then strips types. */
const typescript = () => ({
  name: 'esbuild-typescript',
  resolveId(source, importer) {
    if (!importer || !source.startsWith('.')) return null;
    const base = resolve(dirname(importer), source);
    for (const candidate of [base, `${base}.ts`, `${base}/index.ts`]) {
      try {
        readFileSync(candidate);
        return candidate;
      } catch {
        /* try the next shape */
      }
    }
    return null;
  },
  transform(code, id) {
    if (!id.endsWith('.ts')) return null;
    const { code: js, map } = transformSync(code, {
      loader: 'ts',
      target: TARGET,
      sourcefile: id,
      sourcemap: true,
    });
    return { code: js, map };
  },
  renderChunk(code) {
    const { code: min, map } = transformSync(code, {
      loader: 'js',
      target: TARGET,
      minify: true,
      sourcemap: true,
    });
    return { code: min, map };
  },
});

export default [
  {
    input: {
      xpsearch: 'src/index.ts',
      behaviors: 'src/behaviors.ts',
      widgets: 'src/widgets/index.ts',
      ...widgetInputs,
    },
    output: {
      dir: 'dist',
      format: 'es',
      entryFileNames: '[name].mjs',
      chunkFileNames: 'shared-[hash].mjs',
      sourcemap: true,
    },
    plugins: [typescript()],
  },
  {
    input: 'src/umd.ts',
    output: {
      file: 'dist/xpsearch.umd.js',
      format: 'umd',
      name: 'xpsearch',
      exports: 'default',
      sourcemap: true,
    },
    plugins: [typescript()],
  },
];
