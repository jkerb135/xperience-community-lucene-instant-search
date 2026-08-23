// @vitest-environment jsdom
/**
 * What `withFacetList`'s `apply(value)` actually does, and when a state change reaches `render`.
 * Both are load-bearing for the single-select idiom documented in
 * `docs/guides/custom-widgets.md`, and both were wrong or unstated in the docs before DX-1.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { withFacetList } from '../behaviors';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { createSearch } from '../instance';
import type { SearchInstance } from '../types';

const BODY: SearchResponse = {
  results: [],
  facets: {
    brand: [
      { value: 'Rancilio', label: 'Rancilio', count: 8 },
      { value: 'Gaggia', label: 'Gaggia', count: 5 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 13,
  totalPages: 1,
  tookMs: 3,
  redirect: null,
};

let search: SearchInstance | undefined;
afterEach(() => {
  search?.dispose();
  search = undefined;
});

interface Harness {
  requests: SearchRequest[];
  renders: number;
  /** The `apply` of the most recent render. */
  apply: (value: string) => void;
}

function harness(): Harness {
  const requests: SearchRequest[] = [];
  const state: Harness = { requests, renders: 0, apply: () => {} };

  const fetchFn = vi.fn(async (_url: string, init: RequestInit) => {
    requests.push(JSON.parse(String(init.body)) as SearchRequest);
    return new Response(JSON.stringify(BODY), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  });

  const probe = withFacetList<Record<string, unknown>>((options) => {
    state.renders++;
    state.apply = options.apply;
  });

  search = createSearch({
    index: 'site-content',
    debounceMs: 0,
    routing: false,
    fetchFn: fetchFn as unknown as typeof fetch,
  });
  search.addWidgets([probe({ attribute: 'brand' })]);
  search.start();
  return state;
}

describe('withFacetList apply()', () => {
  it('toggles the value and runs a search', async () => {
    const h = harness();
    await vi.waitFor(() => expect(h.requests).toHaveLength(1));

    h.apply('Gaggia');

    // The toggle is synchronous: the state is already correct when `apply` returns.
    expect(search?.state.filters.facets).toEqual([{ attribute: 'brand', values: ['Gaggia'] }]);
    // The search is not: it is dispatched through the debounced transport.
    await vi.waitFor(() => expect(h.requests).toHaveLength(2));
    expect(h.requests[1]?.filters?.facets).toEqual([{ attribute: 'brand', values: ['Gaggia'] }]);
  });

  it('coalesces two applies in one handler into a single request', async () => {
    const h = harness();
    await vi.waitFor(() => expect(h.requests).toHaveLength(1));

    // The single-select idiom: clear the old value, then apply the new one.
    h.apply('Gaggia');
    h.apply('Gaggia');
    h.apply('Rancilio');

    await vi.waitFor(() => expect(h.requests).toHaveLength(2));
    expect(h.requests[1]?.filters?.facets).toEqual([{ attribute: 'brand', values: ['Rancilio'] }]);

    // Nothing more arrives: the earlier two were superseded before the debounce elapsed.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(h.requests).toHaveLength(2);
  });

  it('re-renders on a state change on a microtask, not synchronously', async () => {
    const h = harness();
    await vi.waitFor(() => expect(h.requests).toHaveLength(1));
    const before = h.renders;

    h.apply('Gaggia');
    expect(h.renders).toBe(before); // no synchronous re-render

    await Promise.resolve();
    expect(h.renders).toBe(before + 1); // one microtask later, exactly once
  });

  it('coalesces the renders of several state changes into one', async () => {
    const h = harness();
    await vi.waitFor(() => expect(h.requests).toHaveLength(1));
    const before = h.renders;

    h.apply('Gaggia');
    h.apply('Gaggia');
    h.apply('Rancilio');
    await Promise.resolve();

    expect(h.renders).toBe(before + 1);
  });
});
