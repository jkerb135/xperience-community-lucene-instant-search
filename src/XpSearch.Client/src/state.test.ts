import { describe, expect, it } from 'vitest';
import {
  addNumericRefinement,
  clearRefinements,
  createState,
  createStore,
  parseNumericFilter,
  setPage,
  setQuery,
  stateFromWireFragment,
  stateToWireFragment,
  toggleFacetRefinement,
} from './state';

describe('SearchState', () => {
  it('is frozen, so a widget cannot write to it', () => {
    const state = createState({ facetFilters: { tags: ['coffee'] } });
    expect(Object.isFrozen(state)).toBe(true);
    expect(Object.isFrozen(state.facetFilters)).toBe(true);
    expect(Object.isFrozen(state.facetFilters['tags'])).toBe(true);
    expect(() => {
      (state as { query: string }).query = 'nope';
    }).toThrow();
    expect(state.query).toBe('');
  });

  it('returns a new state instead of mutating the old one', () => {
    const first = createState();
    const second = setQuery(first, 'espresso');
    expect(first.query).toBe('');
    expect(second.query).toBe('espresso');
  });

  it('resets to the first page on every refinement but setPage', () => {
    const paged = setPage(createState(), 4);
    expect(setQuery(paged, 'x').page).toBe(0);
    expect(toggleFacetRefinement(paged, 'tags', 'coffee').page).toBe(0);
    expect(setPage(paged, 2).page).toBe(2);
  });

  it('toggles a facet value off and drops the empty attribute', () => {
    const on = toggleFacetRefinement(createState(), 'tags', 'coffee');
    expect(on.facetFilters).toEqual({ tags: ['coffee'] });
    expect(toggleFacetRefinement(on, 'tags', 'coffee').facetFilters).toEqual({});
  });

  it('clears one attribute, facet and numeric alike', () => {
    let state = toggleFacetRefinement(createState(), 'tags', 'coffee');
    state = toggleFacetRefinement(state, 'contentType', 'Article');
    state = addNumericRefinement(state, 'price', '<=', 50);
    const cleared = clearRefinements(state, 'tags');
    expect(cleared.facetFilters).toEqual({ contentType: ['Article'] });
    expect(clearRefinements(state, 'price').numericFilters).toEqual([]);
    expect(clearRefinements(state).facetFilters).toEqual({});
  });
});

describe('wire serialization (spec 4.2)', () => {
  it('puts an "or" attribute in one inner array and an "and" attribute in one array per value', () => {
    let state = toggleFacetRefinement(createState(), 'contentType', 'Article');
    state = toggleFacetRefinement(state, 'contentType', 'Product');
    state = toggleFacetRefinement(state, 'tags', 'coffee');
    state = toggleFacetRefinement(state, 'tags', 'brewing');

    const wire = stateToWireFragment(state, { contentType: 'or', tags: 'and' });
    expect(wire.facetFilters).toEqual([
      ['contentType:Article', 'contentType:Product'],
      ['tags:coffee'],
      ['tags:brewing'],
    ]);
  });

  it('defaults an attribute with no declared operator to OR', () => {
    let state = toggleFacetRefinement(createState(), 'tags', 'coffee');
    state = toggleFacetRefinement(state, 'tags', 'milk');
    expect(stateToWireFragment(state).facetFilters).toEqual([['tags:coffee', 'tags:milk']]);
  });

  it('writes numeric filters in the schema grammar', () => {
    let state = addNumericRefinement(createState(), 'price', '<=', 50);
    state = addNumericRefinement(state, 'publishedAt', '>=', 1700000000);
    expect(stateToWireFragment(state).numericFilters).toEqual([
      'price<=50',
      'publishedAt>=1700000000',
    ]);
    for (const filter of stateToWireFragment(state).numericFilters ?? []) {
      expect(filter).toMatch(/^[A-Za-z_][\w.]*\s*(<=|>=|<|>|=|!=)\s*-?\d+(\.\d+)?$/);
    }
  });

  it('omits empty collections and always carries query, page and sort', () => {
    expect(stateToWireFragment(createState())).toEqual({ query: '', page: 0, sort: 'relevance' });
  });

  it('round-trips through the wire fragment', () => {
    let state = toggleFacetRefinement(createState({ query: 'espresso', sort: 'price_asc' }), 'tags', 'coffee');
    state = addNumericRefinement(state, 'price', '>', 10);
    state = setPage(state, 2);
    expect(stateFromWireFragment(stateToWireFragment(state))).toEqual(state);
  });

  it('parses the numeric grammar and rejects anything off it', () => {
    expect(parseNumericFilter('price<=50')).toEqual({ attribute: 'price', operator: '<=', value: 50 });
    expect(parseNumericFilter('price <= -1.5')).toEqual({ attribute: 'price', operator: '<=', value: -1.5 });
    expect(parseNumericFilter('price<=abc')).toBeNull();
    expect(parseNumericFilter('1price<=5')).toBeNull();
  });
});

describe('store', () => {
  it('notifies subscribers and stops after unsubscribe', () => {
    const store = createStore(createState());
    const seen: string[] = [];
    const off = store.subscribe((state) => seen.push(state.query));
    store.set(setQuery(store.get(), 'a'));
    off();
    store.set(setQuery(store.get(), 'b'));
    expect(seen).toEqual(['a']);
    expect(store.get().query).toBe('b');
  });
});
