import assert from 'node:assert/strict';
import { test } from 'node:test';

import { composeExpression, parseExpression } from './expression.ts';

/*
 * The one runnable check of the filter-expression grammar on this side of the wire. Run it with
 * `npm test` (node's own runner, which strips the types); it is excluded from tsconfig.json because
 * the type-checked build has no node typings and does not ship it. Its C# twin is
 * RuleFilterExpressionTests / TuningTests.FilterExpression_ComposesBackWhatItParsed - the cases here
 * are deliberately the same ones.
 */

test('parse drops malformed pairs, exactly as the server does', () => {
  assert.deepEqual(parseExpression('Category:coffee, Tags:brewing, nonsense, :empty, trailing:'), [
    { attribute: 'Category', value: 'coffee' },
    { attribute: 'Tags', value: 'brewing' },
  ]);
});

test('compose(parse(x)) is the canonical form the storage keeps', () => {
  assert.equal(composeExpression(parseExpression('  Category :  coffee ,nonsense,Tags:brewing')), 'Category:coffee, Tags:brewing');
  assert.equal(composeExpression(parseExpression('Category:coffee, Tags:brewing')), 'Category:coffee, Tags:brewing');
});

test('a half-filled row is left out rather than written as rubbish', () => {
  assert.equal(
    composeExpression([
      { attribute: 'Category', value: 'coffee' },
      { attribute: ' ', value: 'x' },
      { attribute: 'Tags', value: '' },
    ]),
    'Category:coffee',
  );
  assert.equal(composeExpression([]), '');
});

test('a value may contain a colon, which is what a URL value needs', () => {
  assert.deepEqual(parseExpression('url:https://example.com'), [{ attribute: 'url', value: 'https://example.com' }]);
});
