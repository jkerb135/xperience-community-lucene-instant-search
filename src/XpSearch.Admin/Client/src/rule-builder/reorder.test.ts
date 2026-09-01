import assert from 'node:assert/strict';
import { test } from 'node:test';

import { move } from './model.ts';
import type { Action } from './model.ts';
import { announce, gripLabel, landing, lift, step } from './reorder.ts';

/*
 * The runnable check of the action reorder: the grab state machine, its bounds, and where a drop
 * lands. Run it with `npm test` (node's own runner, which strips the types).
 *
 * What is NOT covered, and cannot be here: the HTML5 drag events themselves (dragstart / dragover /
 * drop) and the focus that follows a moved row need a browser, so they are host-pass items. What
 * these tests do cover is every piece of arithmetic those handlers hand off to.
 */

/** A list of distinguishable actions; only `type` and the pinned id matter to `move`. */
const actions = (...ids: string[]): Action[] => ids.map((id) => ({ type: 'pin', itemId: id }) as unknown as Action);

const ids = (list: Action[]): string[] => list.map((action) => (action as unknown as { itemId: string }).itemId);

test('a lift starts where the row already is', () => {
  assert.deepEqual(lift(2), { from: 2, at: 2 });
});

test('the arrow keys walk a lifted row and stop at both ends', () => {
  const grabbed = lift(1);

  assert.deepEqual(step(grabbed, 1, 3), { from: 1, at: 2 });
  assert.deepEqual(step(grabbed, -1, 3), { from: 1, at: 0 });

  // Off either end is refused, not clamped into a fresh object: the caller reads "nothing moved".
  assert.deepEqual(step({ from: 1, at: 0 }, -1, 3), { from: 1, at: 0 });
  assert.deepEqual(step({ from: 1, at: 2 }, 1, 3), { from: 1, at: 2 });
  assert.deepEqual(step(lift(0), 1, 1), { from: 0, at: 0 });
});

test('a lift remembers where it came from however far it walks, which is what escape needs', () => {
  const walked = step(step(lift(0), 1, 4), 1, 4);

  assert.deepEqual(walked, { from: 0, at: 2 });
  assert.deepEqual(ids(move(actions('a', 'b', 'c', 'd'), walked.at, walked.from)), ['c', 'a', 'b', 'd']);
});

test('escape undoes exactly what the arrows did', () => {
  const before = actions('a', 'b', 'c', 'd');
  let grabbed = lift(3);
  let list = before;

  for (const by of [-1, -1, -1] as const) {
    const next = step(grabbed, by, list.length);

    list = move(list, grabbed.at, next.at);
    grabbed = next;
  }

  assert.deepEqual(ids(list), ['d', 'a', 'b', 'c']);
  assert.deepEqual(ids(move(list, grabbed.at, grabbed.from)), ids(before));
});

test('a drop lands in the gap it was aimed at, above and below the row it came from', () => {
  // Taking the row out first shifts every gap below it up one; above it, nothing shifts.
  assert.equal(landing(3, 0), 0);
  assert.equal(landing(3, 3), 3);
  assert.equal(landing(3, 4), 3); // the gap just under itself is where it already is
  assert.equal(landing(0, 4), 3); // the gap past the last row of four
  assert.equal(landing(1, 3), 2);
});

test('move takes a row out and puts it back, so a drag past several leaves them in their own order', () => {
  assert.deepEqual(ids(move(actions('a', 'b', 'c', 'd'), 0, 3)), ['b', 'c', 'd', 'a']);
  assert.deepEqual(ids(move(actions('a', 'b', 'c', 'd'), 3, 0)), ['d', 'a', 'b', 'c']);
  // Adjacent is the old swap, which is what the arrow keys ask for.
  assert.deepEqual(ids(move(actions('a', 'b', 'c'), 1, 2)), ['a', 'c', 'b']);
});

test('move refuses an index that is not there, and a move to where the row already is', () => {
  const list = actions('a', 'b', 'c');

  assert.equal(move(list, 1, 1), list);
  assert.equal(move(list, 0, 3), list);
  assert.equal(move(list, -1, 0), list);
  assert.equal(move(list, 3, 0), list);
});

test('what a screen reader is told names the action and counts from one', () => {
  assert.equal(gripLabel('Pin an item', 0, 3), 'Reorder Pin an item, 1 of 3');
  assert.match(announce.grabbed('Pin an item', 0, 3), /^Pin an item grabbed, position 1 of 3\./);
  assert.equal(announce.moved('Pin an item', 2, 3), 'Pin an item moved to position 3 of 3.');
  assert.equal(announce.dropped('Pin an item', 1, 3), 'Pin an item dropped at position 2 of 3.');
  assert.equal(announce.cancelled('Pin an item', 0, 3), 'Reorder cancelled. Pin an item is back at position 1 of 3.');
});
