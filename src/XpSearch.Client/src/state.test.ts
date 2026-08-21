import { describe, expect, it } from 'vitest';
import {
  clearFilters,
  createState,
  createStore,
  facetOperator,
  facetValues,
  setFacetOperator,
  setNumericFilter,
  setPage,
  setQuery,
  stateFromWireFragment,
  stateToWireFragment,
  toggleFacet,
} from './state';

describe('SearchState', () => {
  it('is frozen, so a widget cannot write to it', () => {
    const state = createState({ filters: { facets: [{ attribute: 'tags', values: ['coffee'] }], numeric: [] } });
    expect(Object.isFrozen(state)).toBe(true);
    expect(Object.isFrozen(state.filters)).toBe(true);
    expect(Object.isFrozen(state.filters.facets)).toBe(true);
    expect(Object.isFrozen(state.filters.facets[0]!.values)).toBe(true);
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

  it('counts pages from one', () => {
    expect(createState().page).toBe(1);
    expect(setPage(createState(), 0).page).toBe(1);
  });

  it('resets to the first page on every filter change but setPage', () => {
    const paged = setPage(createState(), 4);
    expect(setQuery(paged, 'x').page).toBe(1);
    expect(toggleFacet(paged, 'tags', 'coffee').page).toBe(1);
    expect(setPage(paged, 2).page).toBe(2);
  });

  it('toggles a facet value off and drops the empty attribute', () => {
    const on = toggleFacet(createState(), 'tags', 'coffee');
    expect(on.filters.facets).toEqual([{ attribute: 'tags', values: ['coffee'] }]);
    expect(toggleFacet(on, 'tags', 'coffee').filters.facets).toEqual([]);
  });

  it('keeps the declared operator on the attribute it applies to', () => {
    let state = setFacetOperator(createState(), 'tags', 'and');
    state = toggleFacet(state, 'tags', 'coffee');
    expect(facetOperator(state, 'tags')).toBe('and');
    expect(facetOperator(state, 'contentType')).toBe('or');
    expect(state.filters.facets[0]).toEqual({ attribute: 'tags', values: ['coffee'], operator: 'and' });
  });

  it('clears one attribute, facet and numeric alike', () => {
    let state = toggleFacet(createState(), 'tags', 'coffee');
    state = toggleFacet(state, 'contentType', 'Article');
    state = setNumericFilter(state, 'price', 'lte', 50);
    const cleared = clearFilters(state, 'tags');
    expect(cleared.filters.facets).toEqual([{ attribute: 'contentType', values: ['Article'] }]);
    expect(clearFilters(state, 'price').filters.numeric).toEqual([]);
    expect(clearFilters(state).filters).toEqual({ facets: [], numeric: [] });
  });

  it('replaces a bound on the same attribute and operator instead of stacking it', () => {
    let state = setNumericFilter(createState(), 'price', 'lte', 50);
    state = setNumericFilter(state, 'price', 'lte', 20);
    state = setNumericFilter(state, 'price', 'gte', 5);
    expect(state.filters.numeric).toEqual([
      { attribute: 'price', operator: 'lte', value: 20 },
      { attribute: 'price', operator: 'gte', value: 5 },
    ]);
  });

  it('reads the selected values of one attribute', () => {
    const state = toggleFacet(createState(), 'tags', 'coffee');
    expect(facetValues(state, 'tags')).toEqual(['coffee']);
    expect(facetValues(state, 'contentType')).toEqual([]);
  });
});

describe('wire serialization (contract 4.2)', () => {
  it('sends the filters exactly as the state holds them', () => {
    let state = toggleFacet(createState(), 'contentType', 'Article');
    state = toggleFacet(state, 'contentType', 'Product');
    state = setFacetOperator(state, 'tags', 'and');
    state = toggleFacet(state, 'tags', 'coffee');
    state = setNumericFilter(state, 'price', 'lte', 50);

    expect(stateToWireFragment(state).filters).toEqual({
      facets: [
        { attribute: 'contentType', values: ['Article', 'Product'] },
        { attribute: 'tags', values: ['coffee'], operator: 'and' },
      ],
      numeric: [{ attribute: 'price', operator: 'lte', value: 50 }],
    });
  });

  it('omits empty collections and always carries query, page and sort', () => {
    expect(stateToWireFragment(createState())).toEqual({ query: '', page: 1, sort: 'relevance' });
  });

  it('round-trips through the wire fragment', () => {
    let state = toggleFacet(createState({ query: 'espresso', sort: 'price_asc' }), 'tags', 'coffee');
    state = setNumericFilter(state, 'price', 'gt', 10);
    state = setPage(state, 2);
    expect(stateFromWireFragment(stateToWireFragment(state))).toEqual(state);
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
