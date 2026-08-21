// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';
import { xpsearch } from './instance';
import { defaultRouteToState, defaultStateToRoute } from './routing';
import {
  addNumericRefinement,
  createState,
  setPage,
  setSort,
  toggleFacetRefinement,
} from './state';
import type { InstantSearch } from './types';

const BODY: SearchResponse = {
  hits: [],
  page: 0,
  hitsPerPage: 20,
  nbHits: 0,
  nbPages: 1,
  processingTimeMs: 1,
};

function stubFetch(): { fetchFn: typeof fetch; requests: SearchRequest[] } {
  const requests: SearchRequest[] = [];
  const fetchFn = vi.fn(async (_url: string, init: RequestInit) => {
    requests.push(JSON.parse(String(init.body)) as SearchRequest);
    return new Response(JSON.stringify(BODY), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  });
  return { fetchFn: fetchFn as unknown as typeof fetch, requests };
}

let instance: InstantSearch | undefined;

beforeEach(() => {
  window.history.replaceState(null, '', '/search');
});

afterEach(() => {
  instance?.dispose();
  instance = undefined;
});

describe('default route mapping (spec 5.5)', () => {
  it('maps query, one-based page, sort, facets and numeric filters', () => {
    let state = createState({ query: 'espresso' });
    state = toggleFacetRefinement(state, 'contentType', 'Article');
    state = toggleFacetRefinement(state, 'contentType', 'Product');
    state = addNumericRefinement(state, 'price', '<=', 50);
    state = setSort(state, 'price_asc');
    state = setPage(state, 2);

    expect(defaultStateToRoute(state)).toEqual({
      q: 'espresso',
      page: '3',
      sort: 'price_asc',
      contentType: 'Article,Product',
      price_lte: '50',
    });
  });

  it('omits every default so a pristine URL stays clean', () => {
    expect(defaultStateToRoute(createState())).toEqual({});
  });

  it('round-trips, including values that contain a comma', () => {
    const state = toggleFacetRefinement(createState({ query: 'a b' }), 'tags', 'coffee, milk');
    const route = defaultStateToRoute(state);
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(route)) {
      for (const one of Array.isArray(value) ? value : [value]) params.append(key, one);
    }
    const read: Record<string, string[]> = {};
    for (const key of params.keys()) read[key] = params.getAll(key);
    expect(createState(defaultRouteToState(read))).toEqual(state);
  });

  it('leaves absent params out, so initialState still applies', () => {
    expect(defaultRouteToState({})).toEqual({});
  });
});

describe('routing: true', () => {
  it('reads the initial state out of the URL, overriding initialState', async () => {
    window.history.replaceState(null, '', '/search?q=espresso&page=2&tags=coffee&price_lte=50');
    const { fetchFn, requests } = stubFetch();
    instance = xpsearch({
      index: 'site-content',
      routing: true,
      debounceMs: 0,
      fetchFn,
      initialState: { query: 'ignored' },
    });
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      query: 'espresso',
      page: 1,
      facetFilters: [['tags:coffee']],
      numericFilters: ['price<=50'],
    });
  });

  it('replaces on a query change and pushes on a refinement', async () => {
    const { fetchFn } = stubFetch();
    const push = vi.spyOn(window.history, 'pushState');
    const replace = vi.spyOn(window.history, 'replaceState');
    instance = xpsearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();

    instance.helper.setQuery('espresso');
    expect(replace).toHaveBeenCalledTimes(1);
    expect(window.location.search).toBe('?q=espresso');
    expect(push).not.toHaveBeenCalled();

    instance.helper.toggleFacetRefinement('tags', 'coffee');
    expect(push).toHaveBeenCalledTimes(1);
    expect(window.location.search).toBe('?q=espresso&tags=coffee');
  });

  it('preserves query params it does not own', () => {
    window.history.replaceState(null, '', '/search?utm_source=newsletter');
    const { fetchFn } = stubFetch();
    instance = xpsearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();
    instance.helper.setQuery('espresso');
    expect(window.location.search).toBe('?q=espresso&utm_source=newsletter');
  });

  it('restores state and re-searches on popstate', async () => {
    const { fetchFn, requests } = stubFetch();
    instance = xpsearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));

    window.history.replaceState(null, '', '/search?q=beans&tags=coffee');
    window.dispatchEvent(new PopStateEvent('popstate'));

    expect(instance.state.query).toBe('beans');
    expect(instance.state.facetFilters).toEqual({ tags: ['coffee'] });
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toMatchObject({ query: 'beans', facetFilters: [['tags:coffee']] });
  });

  it('createURL uses the same mapping, so connector links are crawlable', () => {
    const { fetchFn } = stubFetch();
    instance = xpsearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();
    const url = instance.createURL(toggleFacetRefinement(instance.state, 'tags', 'coffee'));
    expect(new URL(url).search).toBe('?tags=coffee');
    expect(new URL(url).pathname).toBe('/search');
  });

  it('leaves the URL alone when routing is off, but still builds URLs', () => {
    const { fetchFn } = stubFetch();
    instance = xpsearch({ index: 'site-content', debounceMs: 0, fetchFn });
    instance.start();
    instance.helper.setQuery('espresso');
    expect(window.location.search).toBe('');
    expect(instance.createURL()).toContain('q=espresso');
  });
});

describe('routing: { stateToRoute, routeToState }', () => {
  it('uses the supplied mapping in both directions', async () => {
    window.history.replaceState(null, '', '/search?search=beans');
    const { fetchFn, requests } = stubFetch();
    instance = xpsearch({
      index: 'site-content',
      debounceMs: 0,
      fetchFn,
      routing: {
        stateToRoute: (state): Record<string, string> =>
          state.query === '' ? {} : { search: state.query },
        routeToState: (route) => ({ query: route['search']?.[0] ?? '' }),
      },
    });
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]?.query).toBe('beans');

    instance.helper.setQuery('milk');
    expect(window.location.search).toBe('?search=milk');
  });
});
