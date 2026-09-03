// Playwright for the two browser checks (`check-isolation.mjs`, `check-sheet.mjs`).
//
// It is not a dependency of this package: it is the one the docs screenshot tooling installs, and
// both checks borrow it from there. Run `npm install` in tools/screenshots once.
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

export const themes = join(dirname(fileURLToPath(import.meta.url)), '..');

export function chromium() {
  try {
    return createRequire(join(themes, '..', 'tools', 'screenshots', 'package.json'))('playwright')
      .chromium;
  } catch {
    console.error(
      'playwright is missing — it belongs to the screenshot tooling, which shares it with this check.\n' +
        '  Run: cd tools/screenshots && npm install'
    );
    process.exit(1);
  }
}

/** `file://` URL of a path inside themes/, for a `<link>` in a generated document. */
export const href = (file) => pathToFileURL(join(themes, file)).href;
