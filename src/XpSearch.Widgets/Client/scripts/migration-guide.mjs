// Renders docs/guides/migrating-from-algolia.md from contract/algolia-map.json and the hand-written
// preamble in contract/migrating-from-algolia.template.md.
//   node scripts/migration-guide.mjs            regenerate in place  (npm run docs:migration)
//   node scripts/migration-guide.mjs --check    fail on drift        (npm run docs:check)
//
// The template is a sequence of `<!-- section: name -->` blocks; each one's prose is copied through and
// the rows of the matching `kind` from the map are appended as a table. Prose and data therefore live in
// exactly one place each, and a contract change that adds a row cannot leave this page behind.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const mapPath = resolve(clientDir, '../../../contract/algolia-map.json');
const templatePath = resolve(clientDir, '../../../contract/migrating-from-algolia.template.md');
const outPath = resolve(clientDir, '../../../docs/guides/migrating-from-algolia.md');

/** Which `kind` values each generated section renders, in order. */
const SECTION_KINDS = {
  'concept-map': ['field', 'option', 'event'],
  'widget-map': ['widget'],
  'behavior-map': ['connector'],
};

const HEADERS = { ours: 'Xperience Search', algolia: 'Algolia / InstantSearch', note: 'Notes' };

/** Escapes the pipes and newlines a markdown table cell cannot contain. */
const cell = (value) => String(value).replace(/\|/g, '\\|').replace(/\s*\n\s*/g, ' ');

const code = (value) =>
  // "no equivalent" is prose, not an identifier; anything with a space and no bracket usually is too.
  /^(no equivalent|the )/i.test(value) || /\s/.test(value.replace(/\(.*\)/, '')) ? cell(value) : `\`${cell(value)}\``;

function table(rows) {
  const lines = [
    `| ${HEADERS.ours} | ${HEADERS.algolia} | ${HEADERS.note} |`,
    '|---|---|---|',
    ...rows.map((row) => `| ${code(row.ours)} | ${code(row.algolia)} | ${cell(row.note)} |`),
  ];
  return lines.join('\n');
}

function render() {
  const map = JSON.parse(readFileSync(mapPath, 'utf8'));
  const kinds = new Set(['field', 'widget', 'connector', 'option', 'event']);
  for (const [at, row] of map.entries()) {
    for (const key of ['ours', 'algolia', 'note', 'kind']) {
      if (typeof row[key] !== 'string' || row[key] === '') {
        throw new Error(`contract/algolia-map.json[${at}] has no "${key}".`);
      }
    }
    if (!kinds.has(row.kind)) {
      throw new Error(`contract/algolia-map.json[${at}] has kind "${row.kind}"; expected one of ${[...kinds].join(', ')}.`);
    }
  }

  const template = readFileSync(templatePath, 'utf8').replace(/\r\n/g, '\n');
  const sections = template.split(/^<!-- section: ([a-z-]+) -->$/m);
  if (sections.length < 3) {
    throw new Error('the template has no <!-- section: … --> markers.');
  }

  const out = [
    [
      '<!-- Generated from contract/algolia-map.json and contract/migrating-from-algolia.template.md',
      '     by src/XpSearch.Widgets/Client/scripts/migration-guide.mjs. DO NOT EDIT.',
      '     Regenerate with: npm run docs:migration   CI guard: npm run docs:check -->',
    ].join('\n'),
  ];

  // sections[0] is the editing note before the first marker; drop it.
  for (let i = 1; i < sections.length; i += 2) {
    const name = sections[i];
    const prose = sections[i + 1].trim();
    out.push(prose);
    const wanted = SECTION_KINDS[name];
    if (!wanted) continue;
    const rows = map.filter((row) => wanted.includes(row.kind));
    if (rows.length === 0) throw new Error(`section "${name}" matched no rows in the map.`);
    out.push('', table(rows));
  }

  return `${out.join('\n\n').replace(/\n{3,}/g, '\n\n')}\n`;
}

const text = render();

if (process.argv.includes('--check')) {
  const current = readFileSync(outPath, 'utf8').replace(/\r\n/g, '\n');
  if (current === text) {
    console.log(`up to date: ${outPath}`);
  } else {
    const currentLines = current.split('\n');
    const newLines = text.split('\n');
    const at = newLines.findIndex((line, n) => line !== currentLines[n]);
    console.error(
      `DRIFT in ${outPath}\n  first difference at line ${at + 1}:\n  checked in: ${currentLines[at]}\n  generated : ${newLines[at]}\n\n` +
        'Run `npm run docs:migration` and commit the result.'
    );
    process.exit(1);
  }
} else {
  writeFileSync(outPath, text);
  console.log(`wrote: ${outPath}`);
}
