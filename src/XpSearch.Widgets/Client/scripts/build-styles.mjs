// The stylesheet half of the npm tarball (PK-1):
//   scss/                     the sass sources, copied verbatim from themes/src/scss
//   themes/*.css              the full stylesheets: shell, default and the two shipped palettes
//   styles/base.css           reset + tokens + shared primitives, both layers
//   styles/widgets/<name>.css one widget each, for a pipeline without sass
// All of it is a gitignored build product, like dist/.
//
// themes/*.css is compiled here AND compared with the committed themes/src/*.css that the RCL
// serves: the npm package and the tag helper must never ship different rules.
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compileString } from 'sass';
import { WIDGET_ENTRIES } from './widget-entries.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const themesSrc = resolve(clientDir, '../../../themes/src');
const scssOut = join(clientDir, 'scss');

/** Rules only: comments and formatting dropped, so two compilers can be compared honestly. */
const rules = (css) =>
  css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .replace(/\s*([{};:,])\s*/g, '$1')
    .split('}')
    .map((rule) => rule.trim())
    .filter(Boolean);

const build = (source) =>
  compileString(source, { loadPaths: [scssOut], style: 'expanded', charset: false }).css + '\n';

rmSync(scssOut, { recursive: true, force: true });
cpSync(join(themesSrc, 'scss'), scssOut, { recursive: true });

// 1. themes/*.css — the same files the .NET package serves as static web assets.
const themesOut = join(clientDir, 'themes');
mkdirSync(themesOut, { recursive: true });
for (const name of ['shell', 'default', 'kentico-violet', 'kentico-orange']) {
  const css = build(`@use '${name}';`);
  const shipped = readFileSync(join(themesSrc, `${name}.css`), 'utf8').replace(/\r\n/g, '\n');
  const [ours, theirs] = [rules(css), rules(shipped)];
  const drift = ours.find((rule, at) => rule !== theirs[at]) ?? (ours.length === theirs.length ? null : '(rule count)');
  if (drift) {
    console.error(
      `themes/${name}.css compiled from scss/${name}.scss does not match the committed themes/src/${name}.css.\n` +
        `  first difference: ${drift}\n  run \`npm run build\` in themes/ if the sources moved on.`
    );
    process.exit(1);
  }
  writeFileSync(join(themesOut, `${name}.css`), css);
}

// 2. styles/ — the a la carte CSS for consumers whose pipeline cannot compile sass.
const widgetsOut = join(clientDir, 'styles/widgets');
mkdirSync(widgetsOut, { recursive: true });
writeFileSync(join(clientDir, 'styles/base.css'), build(`@use 'base';`));
for (const name of Object.keys(WIDGET_ENTRIES)) {
  writeFileSync(join(widgetsOut, `${name}.css`), build(`@use 'widgets/${name}';`));
}

console.log(
  `packaged scss/, themes/{shell,default,kentico-violet,kentico-orange}.css (rule-identical to themes/src/), ` +
    `styles/base.css and ${Object.keys(WIDGET_ENTRIES).length} styles/widgets/*.css`
);
