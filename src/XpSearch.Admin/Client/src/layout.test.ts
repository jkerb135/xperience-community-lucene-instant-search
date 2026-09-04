import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

/*
 * The layout guidelines of docs/guides/admin-client-development.md, checked statically: every
 * custom page's stylesheet stays on the 8px grid and names colours through the components
 * package's own tokens, with no hex and no `var(--x, #fallback)` (a fallback hides a wrong token
 * name). Spacing is what the guidelines govern, so line-height / font-size and the widths of
 * individual form controls are out of scope.
 */

const here = fileURLToPath(new URL('.', import.meta.url));

const stylesheets = (): string[] =>
  readdirSync(here, { recursive: true, encoding: 'utf8' })
    .filter((name) => name.endsWith('.css') || name.endsWith('.scss'))
    .map((name) => join(here, name));

/** Declarations whose value is a length the guidelines govern: gaps, padding, margin, offsets. */
const spacingLengths = (css: string): string[] =>
  [...css.matchAll(/(^|[\s;{])(gap|row-gap|column-gap|padding|margin|top|bottom|left|right)(-[a-z]+)?\s*:\s*([^;}]+)/g)]
    .flatMap((match) => match[4].match(/-?[\d.]+px/g) ?? []);

for (const path of stylesheets()) {
  const css = readFileSync(path, 'utf8');
  const name = path.slice(here.length);

  test(`${name} names colours with tokens only`, () => {
    assert.deepEqual(css.match(/#[0-9a-fA-F]{3,8}\b/g), null, 'hex literal');
    assert.deepEqual(css.match(/var\(--[\w-]+\s*,/g), null, 'var() fallback');
  });

  test(`${name} keeps spacing on the 8px grid`, () => {
    const offGrid = spacingLengths(css).filter((length) => {
      const value = Math.abs(Number.parseFloat(length));

      return value % 8 !== 0 && value !== 4 && value !== 12 && value !== 1 && value !== 2 && value !== 3;
    });

    assert.deepEqual(offGrid, [], `off-grid spacing in ${name}`);
  });
}

test('no page-level padding of its own: the admin shell pads the page', () => {
  for (const path of stylesheets()) {
    assert.doesNotMatch(readFileSync(path, 'utf8'), /\.page\s*{[^}]*padding/, path);
  }
});
