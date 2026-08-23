// @vitest-environment jsdom
/**
 * `withCategoryTree` (ADR-0018): the tree comes from `FacetValue.path`, selection is
 * single-value, and re-selecting the open node clears the attribute.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { withCategoryTree, type CategoryTreeRenderState } from './categoryTree';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { createSearch } from '../instance';
import type { SearchInstance } from '../types';

const BODY: SearchResponse = {
  results: [],
  facets: {
    category: [
      { value: 'coffee', label: 'Coffee', count: 42 },
      { value: 'machines', label: 'Machines', count: 18, path: ['coffee'] },
      { value: 'espresso', label: 'Espresso', count: 11, path: ['coffee', 'machines'] },
      { value: 'filter', label: 'Filter', count: 7, path: ['coffee', 'machines'] },
      { value: 'grinders', label: 'Grinders', count: 24, path: ['coffee'] },
      { value: 'tea', label: 'Tea', count: 9 },
      { value: 'accessories', label: 'Accessories', count: 0 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 42,
  totalPages: 3,
  tookMs: 4,
  redirect: null,
};

let search: SearchInstance | undefined;
afterEach(() => {
  search?.dispose();
  search = undefined;
});

interface Harness {
  requests: SearchRequest[];
  /** The render state of the most recent render. */
  last(): CategoryTreeRenderState;
}

async function mount(limit?: number): Promise<Harness> {
  const requests: SearchRequest[] = [];
  const renders: CategoryTreeRenderState[] = [];

  const fetchFn = vi.fn(async (_url: string, init: RequestInit) => {
    requests.push(JSON.parse(String(init.body)) as SearchRequest);
    return new Response(JSON.stringify(BODY), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  });

  const probe = withCategoryTree<Record<string, unknown>>((options) => {
    renders.push(options);
  });

  search = createSearch({ index: 'site-content', debounceMs: 0, fetchFn: fetchFn as unknown as typeof fetch });
  search.addWidgets([probe({ attribute: 'category', ...(limit === undefined ? {} : { limit }) })]);
  search.start();
  await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));

  return { requests, last: () => renders[renders.length - 1]! };
}

describe('withCategoryTree', () => {
  it('asks the server for its attribute', async () => {
    const { requests } = await mount();
    expect(requests[0]?.facets).toEqual(['category']);
  });

  it('builds a tree out of the flat values and their paths', async () => {
    const { last } = await mount();
    const roots = last().items;

    expect(roots.map((item) => item.value)).toEqual(['coffee', 'tea', 'accessories']);
    const coffee = roots[0]!;
    // Each level is ordered by count, most documents first.
    expect(coffee.children.map((item) => item.value)).toEqual(['grinders', 'machines']);
    expect(coffee.children[1]!.children.map((item) => item.value)).toEqual(['espresso', 'filter']);
    expect(coffee.children[1]!.children[0]!.path).toEqual(['coffee', 'machines']);
    expect(coffee.count).toBe(42);
    expect(last().canApply).toBe(true);
    expect(last().selected).toBeUndefined();
  });

  it('caps every level at limit', async () => {
    const { last } = await mount(1);
    const roots = last().items;

    expect(roots.map((item) => item.value)).toEqual(['coffee']);
    expect(roots[0]!.children.map((item) => item.value)).toEqual(['grinders']);
  });

  it('marks the whole open path active, not just the selected node', async () => {
    const { last } = await mount();
    last().apply('espresso');
    await vi.waitFor(() => expect(last().selected).toBe('espresso'));

    const state = last();
    expect(state.isActive('espresso')).toBe(true);
    expect(state.isActive('machines')).toBe(true);
    expect(state.isActive('coffee')).toBe(true);
    expect(state.isActive('tea')).toBe(false);
    expect(state.items[0]!.isActive).toBe(true);
  });

  it('replaces the attribute filter rather than adding to it', async () => {
    const { last, requests } = await mount();
    last().apply('machines');
    await vi.waitFor(() => expect(last().selected).toBe('machines'));
    last().apply('tea');
    await vi.waitFor(() => expect(last().selected).toBe('tea'));

    expect(requests[requests.length - 1]?.filters?.facets).toEqual([
      { attribute: 'category', values: ['tea'] },
    ]);
  });

  it('clears the attribute when the open node is selected again', async () => {
    const { last, requests } = await mount();
    last().apply('tea');
    await vi.waitFor(() => expect(last().selected).toBe('tea'));
    last().apply('tea');
    await vi.waitFor(() => expect(last().selected).toBeUndefined());

    expect(requests[requests.length - 1]?.filters).toBeUndefined();
  });

  it('gives every node a real URL for the state it would apply', async () => {
    const { last } = await mount();
    const url = new URL(last().urlFor('machines'), 'https://example.test');

    expect(url.searchParams.get('category')).toBe('machines');
  });
});
