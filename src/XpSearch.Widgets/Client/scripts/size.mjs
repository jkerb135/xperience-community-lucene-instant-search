// Gzip size gate for the bundles (spec 5.9). Limits live in size-limit.json; no extra package.
import { gzipSync } from 'node:zlib';
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const distDir = join(clientDir, 'dist');
const limits = JSON.parse(readFileSync(join(clientDir, 'size-limit.json'), 'utf8'));

const files = (() => {
  try {
    return readdirSync(distDir);
  } catch {
    console.error('dist/ is missing — run `npm run build` first.');
    process.exit(1);
  }
})();

const gzipOf = (names) =>
  names.reduce((total, name) => total + gzipSync(readFileSync(join(distDir, name))).length, 0);

let failed = false;
for (const [name, budget] of Object.entries(limits.budgets)) {
  const matched = files.filter((file) => new RegExp(budget.pattern).test(file));
  if (matched.length === 0) {
    console.error(`${name}: no dist file matches /${budget.pattern}/`);
    failed = true;
    continue;
  }
  const bytes = gzipOf(matched);
  const over = bytes > budget.limitBytes;
  failed ||= over;
  console.log(
    `${over ? 'FAIL' : 'ok  '} ${name.padEnd(6)} ${String(bytes).padStart(6)} B gzip / ${budget.limitBytes} B limit  (${matched.join(', ')})`
  );
}

if (failed) {
  console.error('\nBundle size budget exceeded (spec 5.9: <20 KB gzip for core + the six widgets).');
  process.exit(1);
}
