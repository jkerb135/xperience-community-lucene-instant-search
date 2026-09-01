// @vitest-environment jsdom
/**
 * The recent-searches store (SG-1). The widget-level behaviour — the third panel group, the Clear
 * control, the opt-out — is covered in `widgets.test.ts`; this file pins the store itself, whose
 * whole job is to never throw at a visitor whose browser refuses to hold it.
 */
import { beforeEach, describe, expect, it } from 'vitest';
import { clearRecents, groupOf, readRecents, recentsStorage, recordRecent } from './recentSearches';

const INDEX = 'site-content';

describe('the recent searches store', () => {
  beforeEach(() => localStorage.clear());

  it('keeps the most recent first, under its own per-index key', () => {
    recordRecent(localStorage, INDEX, 'espresso');
    recordRecent(localStorage, INDEX, 'grinder');
    recordRecent(localStorage, 'other-index', 'unrelated');

    expect(readRecents(localStorage, INDEX)).toEqual(['grinder', 'espresso']);
    expect(localStorage.getItem('xps-recent:site-content')).toBe('["grinder","espresso"]');
    expect(readRecents(localStorage, 'other-index')).toEqual(['unrelated']);
  });

  it('dedupes case-insensitively, keeping the newest spelling', () => {
    recordRecent(localStorage, INDEX, 'espresso');
    recordRecent(localStorage, INDEX, 'grinder');
    recordRecent(localStorage, INDEX, 'ESPRESSO');

    expect(readRecents(localStorage, INDEX)).toEqual(['ESPRESSO', 'grinder']);
  });

  it('caps the list at five and never records blank input', () => {
    for (const query of ['one', 'two', 'three', 'four', 'five', 'six']) {
      recordRecent(localStorage, INDEX, query);
    }
    recordRecent(localStorage, INDEX, '   ');
    recordRecent(localStorage, INDEX, '');

    expect(readRecents(localStorage, INDEX)).toEqual(['six', 'five', 'four', 'three', 'two']);
  });

  it('trims what it records', () => {
    recordRecent(localStorage, INDEX, '  espresso  ');

    expect(readRecents(localStorage, INDEX)).toEqual(['espresso']);
  });

  it('clears the list', () => {
    recordRecent(localStorage, INDEX, 'espresso');
    clearRecents(localStorage, INDEX);

    expect(readRecents(localStorage, INDEX)).toEqual([]);
  });

  it('reads nothing out of a key someone else wrote', () => {
    localStorage.setItem('xps-recent:site-content', '{"not":"an array"}');
    expect(readRecents(localStorage, INDEX)).toEqual([]);

    localStorage.setItem('xps-recent:site-content', 'not json at all');
    expect(readRecents(localStorage, INDEX)).toEqual([]);

    localStorage.setItem('xps-recent:site-content', '["espresso", 7, "", null]');
    expect(readRecents(localStorage, INDEX)).toEqual(['espresso']);
  });

  /** A private window throws on every access; the feature has to disappear, not break the page. */
  it('survives a storage that throws on every call', () => {
    const hostile = {
      getItem: () => {
        throw new Error('denied');
      },
      setItem: () => {
        throw new Error('denied');
      },
      removeItem: () => {
        throw new Error('denied');
      },
    } as unknown as Storage;

    expect(readRecents(hostile, INDEX)).toEqual([]);
    expect(() => recordRecent(hostile, INDEX, 'espresso')).not.toThrow();
    expect(() => clearRecents(hostile, INDEX)).not.toThrow();
  });

  it('survives a window that throws on the localStorage property itself', () => {
    const win = {
      get localStorage(): Storage {
        throw new Error('blocked by the browser');
      },
    } as unknown as Window;

    expect(recentsStorage(win)).toBeUndefined();
    // No storage at all is the same as an empty one, in every direction.
    expect(readRecents(undefined, INDEX)).toEqual([]);
    expect(() => recordRecent(undefined, INDEX, 'espresso')).not.toThrow();
    expect(() => clearRecents(undefined, INDEX)).not.toThrow();
  });
});

describe('the suggestion group', () => {
  it('prefers the server label and falls back to the pre-SG-1 inference', () => {
    expect(groupOf({ text: 'a', group: 'query' })).toBe('query');
    expect(groupOf({ text: 'a', group: 'document' })).toBe('document');
    expect(groupOf({ text: 'a', group: 'recent' })).toBe('recent');
    // An older server sends neither: a suggestion with a document behind it is one.
    expect(groupOf({ text: 'a' })).toBe('query');
    expect(groupOf({ text: 'a', result: { id: 'doc-1', attributes: {} } })).toBe('document');
  });
});
