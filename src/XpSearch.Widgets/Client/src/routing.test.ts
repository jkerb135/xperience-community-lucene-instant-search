// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';
import { withFacetList, withRange } from './behaviors';
import { createSearch } from './instance';
import { defaultRouteToState, defaultStateToRoute } from './routing';
import {
  createState,
  setNumericFilter,
  setPage,
  setSort,
  toggleFacet,
} from './state';
import type { SearchInstance, Widget } from './types';

const BODY: SearchResponse = {
  results: [],
  page: 1,
  pageSize: 20,
  total: 0,
  totalPages: 1,
  tookMs: 1,
  redirect: null,
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

/** The widgets that make an attribute routable, with rendering stubbed out. */
const facetWidget = (attribute: string): Widget => withFacetList(() => {})({ attribute });
const rangeWidget = (attribute: string): Widget => withRange(() => {})({ attribute });

let instance: SearchInstance | undefined;

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
    state = toggleFacet(state, 'contentType', 'Article');
    state = toggleFacet(state, 'contentType', 'Product');
    state = setNumericFilter(state, 'price', 'lte', 50);
    state = setSort(state, 'price_asc');
    state = setPage(state, 3);

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
    const state = toggleFacet(createState({ query: 'a b' }), 'tags', 'coffee, milk');
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

  it('adopts only routable attributes when the page declares them', () => {
    const routable = { facets: new Set(['tags']), numeric: new Set(['price']) };
    const route = {
      q: ['beans'],
      uh: ['abc123'],
      tags: ['coffee'],
      tags_op: ['and'],
      brand: ['acme'],
      brand_op: ['and'],
      price_lte: ['50'],
      weight_gte: ['5'],
    };
    expect(defaultRouteToState(route, routable)).toEqual({
      query: 'beans',
      filters: {
        facets: [{ attribute: 'tags', values: ['coffee'], operator: 'and' }],
        numeric: [{ attribute: 'price', operator: 'lte', value: 50 }],
      },
    });
  });
});

describe('routing: true', () => {
  it('reads the initial state out of the URL, overriding initialState', async () => {
    window.history.replaceState(null, '', '/search?q=espresso&page=2&tags=coffee&price_lte=50');
    const { fetchFn, requests } = stubFetch();
    instance = createSearch({
      index: 'site-content',
      routing: true,
      debounceMs: 0,
      fetchFn,
      initialState: { query: 'ignored' },
    });
    instance.addWidgets([facetWidget('tags'), rangeWidget('price')]);
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      query: 'espresso',
      page: 2,
      filters: {
        facets: [{ attribute: 'tags', values: ['coffee'] }],
        numeric: [{ attribute: 'price', operator: 'lte', value: 50 }],
      },
    });
  });

  it('replaces on a query change and pushes on a refinement', async () => {
    const { fetchFn } = stubFetch();
    const push = vi.spyOn(window.history, 'pushState');
    const replace = vi.spyOn(window.history, 'replaceState');
    instance = createSearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();

    instance.actions.setQuery('espresso');
    expect(replace).toHaveBeenCalledTimes(1);
    expect(window.location.search).toBe('?q=espresso');
    expect(push).not.toHaveBeenCalled();

    instance.actions.toggleFacet('tags', 'coffee');
    expect(push).toHaveBeenCalledTimes(1);
    expect(window.location.search).toBe('?q=espresso&tags=coffee');
  });

  it('preserves query params it does not own', () => {
    window.history.replaceState(null, '', '/search?utm_source=newsletter');
    const { fetchFn } = stubFetch();
    instance = createSearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();
    instance.actions.setQuery('espresso');
    expect(window.location.search).toBe('?q=espresso&utm_source=newsletter');
  });

  it('ignores a foreign param and keeps it in the URL (Kentico `uh`)', async () => {
    window.history.replaceState(null, '', '/search?q=beans&uh=abc123&price_lte=50');
    const { fetchFn, requests } = stubFetch();
    instance = createSearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.addWidgets([facetWidget('tags')]);
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    // `uh` is not a facet and `price` has no range widget: neither may reach the API.
    expect(requests[0]?.filters).toBeUndefined();

    instance.actions.setQuery('espresso');
    expect(window.location.search).toBe('?price_lte=50&q=espresso&uh=abc123');
  });

  it('restores state and re-searches on popstate', async () => {
    const { fetchFn, requests } = stubFetch();
    instance = createSearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.addWidgets([facetWidget('tags')]);
    instance.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));

    window.history.replaceState(null, '', '/search?q=beans&tags=coffee');
    window.dispatchEvent(new PopStateEvent('popstate'));

    expect(instance.state.query).toBe('beans');
    expect(instance.state.filters.facets).toEqual([{ attribute: 'tags', values: ['coffee'] }]);
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toMatchObject({
      query: 'beans',
      filters: { facets: [{ attribute: 'tags', values: ['coffee'] }] },
    });
  });

  it('urlFor uses the same mapping, so behaviour links are crawlable', () => {
    const { fetchFn } = stubFetch();
    instance = createSearch({ index: 'site-content', routing: true, debounceMs: 0, fetchFn });
    instance.start();
    const url = instance.urlFor(toggleFacet(instance.state, 'tags', 'coffee'));
    expect(new URL(url).search).toBe('?tags=coffee');
    expect(new URL(url).pathname).toBe('/search');
  });

  it('leaves the URL alone when routing is off, but still builds URLs', () => {
    const { fetchFn } = stubFetch();
    instance = createSearch({ index: 'site-content', debounceMs: 0, fetchFn });
    instance.start();
    instance.actions.setQuery('espresso');
    expect(window.location.search).toBe('');
    expect(instance.urlFor()).toContain('q=espresso');
  });
});

describe('routing: { stateToRoute, routeToState }', () => {
  // No widget declares `search`, yet it is adopted: a custom mapping bypasses the routable
  // registry entirely and stays the escape hatch for adopt-everything routing.
  it('uses the supplied mapping in both directions', async () => {
    window.history.replaceState(null, '', '/search?search=beans');
    const { fetchFn, requests } = stubFetch();
    instance = createSearch({
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

    instance.actions.setQuery('milk');
    expect(window.location.search).toBe('?search=milk');
  });
});
