import { afterEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { xpsearch } from '../instance';
import type { InstantSearch, RenderOptions } from '../types';
import { connectCurrentRefinements } from './currentRefinements';
import { connectHits } from './hits';
import { connectPagination } from './pagination';
import { connectRange } from './range';
import { connectRefinementList } from './refinementList';
import { connectSearchBox } from './searchBox';
import { connectSortBy } from './sortBy';
import { connectStats } from './stats';

const BODY: SearchResponse = {
  hits: [
    { objectID: 'doc-1', title: 'Espresso Basics', price: 12 },
    { objectID: 'doc-2', title: 'Cold Brew Guide', price: 30 },
  ],
  facets: { tags: { coffee: 12, brewing: 7, milk: 3 }, contentType: { Article: 20 } },
  page: 1,
  hitsPerPage: 2,
  nbHits: 9,
  nbPages: 5,
  processingTimeMs: 7,
  queryId: 'q-42',
};

function stubFetch(): { fetchFn: typeof fetch; requests: SearchRequest[]; urls: string[] } {
  const requests: SearchRequest[] = [];
  const urls: string[] = [];
  const fetchFn = vi.fn(async (url: string, init: RequestInit) => {
    urls.push(url);
    if (url.endsWith('/events')) {
      return new Response('', { status: 202, headers: { [API_VERSION_HEADER]: '1' } });
    }
    requests.push(JSON.parse(String(init.body)) as SearchRequest);
    return new Response(JSON.stringify(BODY), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  });
  return { fetchFn: fetchFn as unknown as typeof fetch, requests, urls };
}

const live: InstantSearch[] = [];
afterEach(() => {
  while (live.length > 0) live.pop()?.dispose();
});

/** Boots an instance with one connector widget and returns every render it received. */
async function mount<T>(
  makeWidget: (record: (options: T, isFirstRender: boolean) => void) => Parameters<InstantSearch['addWidgets']>[0][number],
  options: Partial<Parameters<typeof xpsearch>[0]> = {}
): Promise<{ renders: T[]; firstRenders: boolean[]; search: InstantSearch; requests: SearchRequest[]; urls: string[] }> {
  const { fetchFn, requests, urls } = stubFetch();
  const renders: T[] = [];
  const firstRenders: boolean[] = [];
  const search = xpsearch({ index: 'site-content', debounceMs: 0, fetchFn, ...options });
  live.push(search);
  search.addWidgets([
    makeWidget((rendered, isFirstRender) => {
      renders.push(rendered);
      firstRenders.push(isFirstRender);
    }),
  ]);
  search.start();
  await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));
  return { renders, firstRenders, search, requests, urls };
}

describe('every connector', () => {
  it('renders once on init with no results and again after the response', async () => {
    const { renders, firstRenders } = await mount<
      { nbHits: number } & RenderOptions<Record<string, never>>
    >((record) => connectStats(record)({}));
    expect(firstRenders).toEqual([true, false]);
    expect(renders[0]?.results).toBeNull();
    expect(renders[0]?.nbHits).toBe(0);
    expect(renders[1]?.nbHits).toBe(9);
  });

  it('hands the render function widgetParams, state, helper and the instance', async () => {
    const { renders, search } = await mount<RenderOptions<{ marker: string }>>((record) =>
      connectStats<{ marker: string }>(record)({ marker: 'x' })
    );
    expect(renders[0]?.widgetParams).toEqual({ marker: 'x' });
    expect(renders[0]?.helper).toBe(search.helper);
    expect(renders[0]?.instantSearchInstance).toBe(search);
    expect(renders[0]?.state.query).toBe('');
  });

  it('calls unmountFn on dispose', () => {
    const unmount = vi.fn();
    const widget = connectStats(() => {}, unmount)({});
    widget.dispose?.();
    expect(unmount).toHaveBeenCalledTimes(1);
  });
});

describe('connectSearchBox', () => {
  it('exposes query, refine, clear and isSearchStalled', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectSearchBox>[0]>[0]
    >((record) => connectSearchBox(record)({}));
    expect(renders[0]?.query).toBe('');
    expect(renders[0]?.isSearchStalled).toBe(false);

    renders[1]!.refine('espresso');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.query).toBe('espresso');

    const latest = renders[renders.length - 1]!;
    latest.clear();
    await vi.waitFor(() => expect(requests).toHaveLength(3));
    expect(requests[2]?.query).toBe('');
  });

  it('routes refinements through queryHook when one is given', async () => {
    const seen: string[] = [];
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectSearchBox>[0]>[0]
    >((record) =>
      connectSearchBox(record)({
        queryHook: (query, search) => {
          seen.push(query);
          if (query !== 'blocked') search(query);
        },
      })
    );
    renders[1]!.refine('blocked');
    await new Promise((r) => setTimeout(r, 5));
    expect(seen).toEqual(['blocked']);
    expect(requests).toHaveLength(1);
  });
});

describe('connectHits', () => {
  it('exposes typed hits, results and a sendEvent wired to the queryId', async () => {
    interface Doc extends Record<string, unknown> {
      title: string;
      price: number;
    }
    const { renders, urls } = await mount<
      Parameters<Parameters<typeof connectHits<Doc>>[0]>[0]
    >((record) => connectHits<Doc>(record)({}));

    const hits = renders[1]!.hits;
    expect(hits.map((hit) => hit.title)).toEqual(['Espresso Basics', 'Cold Brew Guide']);
    expect(hits[0]?.price).toBe(12);
    expect(renders[1]?.results?.nbHits).toBe(9);

    renders[1]!.sendEvent('click', hits[1]!);
    await vi.waitFor(() => expect(urls.some((url) => url.endsWith('/events'))).toBe(true));
  });

  it('applies transformItems', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof connectHits>[0]>[0]>((record) =>
      connectHits(record)({ transformItems: (items) => items.slice(0, 1) })
    );
    expect(renders[1]?.hits).toHaveLength(1);
  });
});

describe('connectRefinementList', () => {
  const widget = (
    record: (options: Parameters<Parameters<typeof connectRefinementList>[0]>[0], first: boolean) => void,
    params: { attribute: string; limit?: number; showMore?: boolean; showMoreLimit?: number; operator?: 'and' | 'or' }
  ) => connectRefinementList(record)(params);

  it('shapes items with label, value, count and isRefined, and asks for the facet', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectRefinementList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags' }));

    expect(requests[0]?.facets).toEqual(['tags']);
    expect(renders[1]?.items).toEqual([
      { label: 'coffee', value: 'coffee', count: 12, isRefined: false },
      { label: 'brewing', value: 'brewing', count: 7, isRefined: false },
      { label: 'milk', value: 'milk', count: 3, isRefined: false },
    ]);
    expect(renders[0]?.items).toEqual([]);
    expect(renders[0]?.canRefine).toBe(false);
    expect(renders[1]?.canRefine).toBe(true);
  });

  it('refines, marks the value refined and builds a URL for it', async () => {
    const { renders, requests, search } = await mount<
      Parameters<Parameters<typeof connectRefinementList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags' }));

    expect(renders[1]?.createURL('coffee')).toContain('tags=coffee');
    renders[1]!.refine('coffee');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.facetFilters).toEqual([['tags:coffee']]);
    expect(search.state.facetFilters).toEqual({ tags: ['coffee'] });

    const latest = renders[renders.length - 1]!;
    expect(latest.items.find((item) => item.value === 'coffee')?.isRefined).toBe(true);
  });

  it('sends the declared operator to the wire', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectRefinementList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags', operator: 'and' }));
    renders[1]!.refine('coffee');
    renders[1]!.refine('milk');
    await vi.waitFor(() => expect(requests.length).toBeGreaterThanOrEqual(2));
    expect(requests[requests.length - 1]?.facetFilters).toEqual([['tags:coffee'], ['tags:milk']]);
  });

  it('limits, and toggles show more without a new search', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectRefinementList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags', limit: 1, showMore: true, showMoreLimit: 3 }));

    expect(renders[1]?.items).toHaveLength(1);
    expect(renders[1]?.canToggleShowMore).toBe(true);
    expect(renders[1]?.isShowingMore).toBe(false);

    renders[1]!.toggleShowMore();
    const expanded = renders[renders.length - 1]!;
    expect(expanded.items).toHaveLength(3);
    expect(expanded.isShowingMore).toBe(true);
    expect(requests).toHaveLength(1);
  });
});

describe('connectPagination', () => {
  it('exposes the page window, the current page and refine', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectPagination>[0]>[0]
    >((record) => connectPagination(record)({ padding: 1 }), { initialState: { page: 1 } });

    const rendered = renders[1]!;
    expect(rendered.currentRefinement).toBe(1);
    expect(rendered.nbPages).toBe(5);
    expect(rendered.pages).toEqual([0, 1, 2]);
    expect(rendered.isFirstPage).toBe(false);
    expect(rendered.isLastPage).toBe(false);
    expect(rendered.canRefine).toBe(true);
    expect(rendered.createURL(3)).toContain('page=4');

    rendered.refine(3);
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.page).toBe(3);
  });
});

describe('connectStats', () => {
  it('reports the numbers a "46 results in 14ms" label needs', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof connectStats>[0]>[0]>(
      (record) => connectStats(record)({}),
      { initialState: { query: 'espresso' } }
    );
    expect(renders[1]).toMatchObject({
      nbHits: 9,
      processingTimeMS: 7,
      query: 'espresso',
      page: 1,
      nbPages: 5,
      hitsPerPage: 2,
      hasResults: true,
    });
    expect(renders[0]?.hasResults).toBe(false);
  });
});

describe('connectSortBy', () => {
  it('exposes the options and refines the sort key', async () => {
    const items = [
      { label: 'Relevance', value: 'relevance' },
      { label: 'Newest', value: 'date_desc' },
    ];
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectSortBy>[0]>[0]
    >((record) => connectSortBy(record)({ items }));

    expect(renders[1]?.options).toEqual(items);
    expect(renders[1]?.currentRefinement).toBe('relevance');
    expect(renders[1]?.canRefine).toBe(true);
    expect(renders[1]?.createURL('date_desc')).toContain('sort=date_desc');

    renders[1]!.refine('date_desc');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.sort).toBe('date_desc');
  });
});

describe('connectCurrentRefinements', () => {
  it('lists facet and numeric refinements, each removable on its own', async () => {
    const { renders, requests, search } = await mount<
      Parameters<Parameters<typeof connectCurrentRefinements>[0]>[0]
    >((record) => connectCurrentRefinements(record)({}), {
      initialState: {
        facetFilters: { tags: ['coffee'] },
        numericFilters: [{ attribute: 'price', operator: '<=', value: 50 }],
      },
    });

    const rendered = renders[1]!;
    expect(rendered.canRefine).toBe(true);
    expect(rendered.items.map((item) => item.label)).toEqual(['coffee', 'price <= 50']);
    expect(rendered.items[0]?.createURL()).not.toContain('tags=coffee');

    rendered.items[0]!.refine();
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(search.state.facetFilters).toEqual({});

    renders[renders.length - 1]!.clearAll();
    await vi.waitFor(() => expect(search.state.numericFilters).toEqual([]));
  });
});

describe('connectRange', () => {
  it('exposes the bounds and refines both ends at once', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof connectRange>[0]>[0]
    >((record) => connectRange(record)({ attribute: 'price', min: 0, max: 100 }));

    expect(renders[1]?.range).toEqual({ min: 0, max: 100 });
    expect(renders[1]?.start).toEqual([0, 100]);
    expect(renders[1]?.canRefine).toBe(true);

    renders[1]!.refine([10, 50]);
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.numericFilters).toEqual(['price>=10', 'price<=50']);

    const latest = renders[renders.length - 1]!;
    expect(latest.start).toEqual([10, 50]);
    latest.refine([0, 100]);
    await vi.waitFor(() => expect(requests).toHaveLength(3));
    expect(requests[2]?.numericFilters).toBeUndefined();
  });

  it('cannot refine without bounds', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof connectRange>[0]>[0]>((record) =>
      connectRange(record)({ attribute: 'price' })
    );
    expect(renders[1]?.canRefine).toBe(false);
  });
});
