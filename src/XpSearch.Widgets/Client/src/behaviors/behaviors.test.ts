import { afterEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { createSearch } from '../instance';
import type { RenderOptions, SearchInstance } from '../types';
import { withActiveFilters } from './activeFilters';
import { withFacetList } from './facetList';
import { withPagination } from './pagination';
import { withRange } from './range';
import { withResults } from './results';
import { withResultStats } from './resultStats';
import { withSearchBox, type SearchBoxBehaviorParams } from './searchBox';
import { withSortSelect } from './sortSelect';

const BODY: SearchResponse = {
  results: [
    { id: 'doc-1', attributes: { title: 'Espresso Basics', price: 12 } },
    { id: 'doc-2', attributes: { title: 'Cold Brew Guide', price: 30 } },
  ],
  facets: {
    tags: [
      { value: 'coffee', label: 'Coffee', count: 12 },
      { value: 'brewing', label: 'Brewing', count: 7 },
      { value: 'milk', label: 'Milk drinks', count: 3 },
    ],
    contentType: [{ value: 'Article', label: 'Article', count: 20 }],
  },
  page: 2,
  pageSize: 2,
  total: 9,
  totalPages: 5,
  tookMs: 7,
  redirect: null,
  ruleData: { banner: 'espresso-week', layout: 'grid' },
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

const live: SearchInstance[] = [];
afterEach(() => {
  while (live.length > 0) live.pop()?.dispose();
});

/** Boots an instance with one behaviour-based widget and returns every render it received. */
async function mount<T>(
  makeWidget: (record: (options: T, isFirstRender: boolean) => void) => Parameters<SearchInstance['addWidgets']>[0][number],
  options: Partial<Parameters<typeof createSearch>[0]> = {}
): Promise<{ renders: T[]; firstRenders: boolean[]; search: SearchInstance; requests: SearchRequest[]; urls: string[] }> {
  const { fetchFn, requests, urls } = stubFetch();
  const renders: T[] = [];
  const firstRenders: boolean[] = [];
  const search = createSearch({ index: 'site-content', debounceMs: 0, fetchFn, ...options });
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

describe('every behaviour', () => {
  it('renders once on init with no results and again after the response', async () => {
    const { renders, firstRenders } = await mount<
      { total: number } & RenderOptions<Record<string, never>>
    >((record) => withResultStats(record)({}));
    expect(firstRenders).toEqual([true, false]);
    expect(renders[0]?.results).toBeNull();
    expect(renders[0]?.total).toBe(0);
    expect(renders[1]?.total).toBe(9);
  });

  it('hands the render function params, state, actions and the instance', async () => {
    const { renders, search } = await mount<RenderOptions<{ marker: string }>>((record) =>
      withResultStats<{ marker: string }>(record)({ marker: 'x' })
    );
    expect(renders[0]?.params).toEqual({ marker: 'x' });
    expect(renders[0]?.actions).toBe(search.actions);
    expect(renders[0]?.search).toBe(search);
    expect(renders[0]?.state.query).toBe('');
  });

  it('hands the data a matching rule attached to the response', async () => {
    const { renders, search } = await mount<RenderOptions<Record<string, never>>>((record) =>
      withResultStats(record)({})
    );
    // `ruleData` is optional: absent on the render before the first response, and passed through
    // untouched afterwards, so a behaviour can drive a banner from an editor-authored rule.
    expect(renders[0]?.results).toBeNull();
    expect(renders[1]?.results?.ruleData).toEqual({ banner: 'espresso-week', layout: 'grid' });
    expect(search.results?.ruleData).toEqual({ banner: 'espresso-week', layout: 'grid' });
  });

  it('calls unmountFn on dispose', () => {
    const unmount = vi.fn();
    const widget = withResultStats(() => {}, unmount)({});
    widget.dispose?.();
    expect(unmount).toHaveBeenCalledTimes(1);
  });
});

describe('withSearchBox', () => {
  it('exposes query, apply, clear and isStalled', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withSearchBox>[0]>[0]
    >((record) => withSearchBox(record)({}));
    expect(renders[0]?.query).toBe('');
    expect(renders[0]?.isStalled).toBe(false);

    renders[1]!.apply('espresso');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.query).toBe('espresso');

    const latest = renders[renders.length - 1]!;
    latest.clear();
    await vi.waitFor(() => expect(requests).toHaveLength(3));
    expect(requests[2]?.query).toBe('');
  });

  it('routes queries through queryHook when one is given', async () => {
    const seen: string[] = [];
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withSearchBox>[0]>[0]
    >((record) =>
      withSearchBox(record)({
        queryHook: (query, search) => {
          seen.push(query);
          if (query !== 'blocked') search(query);
        },
      })
    );
    renders[1]!.apply('blocked');
    await new Promise((r) => setTimeout(r, 5));
    expect(seen).toEqual(['blocked']);
    expect(requests).toHaveLength(1);
  });
});

/**
 * Redirect rules (contract `SearchResponse.redirect`). The rule is on the response of every
 * search for "espresso"; only a submitted query may act on it, so a visitor can type past it.
 */
describe('withSearchBox and a redirect rule', () => {
  const REDIRECT: SearchResponse = {
    ...BODY,
    redirect: { url: '/promotions/espresso', rule: 'Espresso landing page' },
  };

  type BoxRender = Parameters<Parameters<typeof withSearchBox>[0]>[0];

  function mountBox(
    params: SearchBoxBehaviorParams = {},
    options: Partial<Parameters<typeof createSearch>[0]> = {}
  ): { renders: BoxRender[]; assigned: string[] } {
    const assigned: string[] = [];
    const windowRef = {
      location: { assign: (url: string) => assigned.push(url) },
    } as unknown as Window;
    const renders: BoxRender[] = [];
    const fetchFn = vi.fn(async (_url: string, init: RequestInit) => {
      const request = JSON.parse(String(init.body)) as SearchRequest;
      return new Response(JSON.stringify(request.query === 'espresso' ? REDIRECT : BODY), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    });
    const search = createSearch({
      index: 'site-content',
      debounceMs: 0,
      fetchFn: fetchFn as unknown as typeof fetch,
      ...options,
    });
    live.push(search);
    search.addWidgets([
      withSearchBox((rendered) => renders.push(rendered))({ windowRef, ...params }),
    ]);
    search.start();
    return { renders, assigned };
  }

  const lastRender = (renders: BoxRender[]): BoxRender => renders[renders.length - 1]!;

  it('navigates when the visitor submits the query', async () => {
    const { renders, assigned } = mountBox();
    await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));

    lastRender(renders).submit('espresso');
    await vi.waitFor(() => expect(assigned).toEqual(['/promotions/espresso']));
  });

  it('never navigates as the visitor types, however often the rule matches', async () => {
    const { renders, assigned } = mountBox();
    await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));

    lastRender(renders).apply('espresso');
    await vi.waitFor(() => expect(lastRender(renders).results?.redirect).not.toBeNull());
    lastRender(renders).apply('espresso');
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(assigned).toEqual([]);
  });

  it('does not navigate for the search a restored URL state runs on page load', async () => {
    const { renders, assigned } = mountBox({}, { initialState: { query: 'espresso' } });
    await vi.waitFor(() => expect(lastRender(renders).results?.redirect).not.toBeNull());
    expect(assigned).toEqual([]);
  });

  it('navigates once per response, and lets the visitor type past the rule afterwards', async () => {
    const { renders, assigned } = mountBox();
    await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));

    lastRender(renders).submit('espresso');
    await vi.waitFor(() => expect(assigned).toHaveLength(1));
    lastRender(renders).apply('espresso beans');
    lastRender(renders).apply('espresso');
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(assigned).toHaveLength(1);
  });

  it('followRedirects: false reports the redirect and stays put', async () => {
    const { renders, assigned } = mountBox({ followRedirects: false });
    await vi.waitFor(() => expect(renders.length).toBeGreaterThanOrEqual(2));

    lastRender(renders).submit('espresso');
    await vi.waitFor(() =>
      expect(lastRender(renders).results?.redirect).toEqual({
        url: '/promotions/espresso',
        rule: 'Espresso landing page',
      })
    );
    expect(assigned).toEqual([]);
  });
});

describe('withResults', () => {
  it('exposes typed results and a sendEvent wired to the queryId', async () => {
    interface Doc extends Record<string, unknown> {
      title: string;
      price: number;
    }
    const { renders, urls } = await mount<
      Parameters<Parameters<typeof withResults<Doc>>[0]>[0]
    >((record) => withResults<Doc>(record)({}));

    const items = renders[1]!.items;
    expect(items.map((result) => result.attributes.title)).toEqual([
      'Espresso Basics',
      'Cold Brew Guide',
    ]);
    expect(items[0]?.attributes.price).toBe(12);
    expect(renders[1]?.results?.total).toBe(9);

    renders[1]!.sendEvent('click', items[1]!);
    await vi.waitFor(() => expect(urls.some((url) => url.endsWith('/events'))).toBe(true));
  });

  it('applies transformItems', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof withResults>[0]>[0]>((record) =>
      withResults(record)({ transformItems: (items) => items.slice(0, 1) })
    );
    expect(renders[1]?.items).toHaveLength(1);
  });
});

describe('withFacetList', () => {
  const widget = (
    record: (options: Parameters<Parameters<typeof withFacetList>[0]>[0], first: boolean) => void,
    params: { attribute: string; limit?: number; showMore?: boolean; showMoreLimit?: number; operator?: 'and' | 'or' }
  ) => withFacetList(record)(params);

  it('shapes items with the server label, value, count and isActive, and asks for the facet', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withFacetList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags' }));

    expect(requests[0]?.facets).toEqual(['tags']);
    expect(renders[1]?.items).toEqual([
      { label: 'Coffee', value: 'coffee', count: 12, isActive: false },
      { label: 'Brewing', value: 'brewing', count: 7, isActive: false },
      { label: 'Milk drinks', value: 'milk', count: 3, isActive: false },
    ]);
    expect(renders[0]?.items).toEqual([]);
    expect(renders[0]?.canApply).toBe(false);
    expect(renders[1]?.canApply).toBe(true);
  });

  it('applies a value, marks it active and builds a URL for it', async () => {
    const { renders, requests, search } = await mount<
      Parameters<Parameters<typeof withFacetList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags' }));

    expect(renders[1]?.urlFor('coffee')).toContain('tags=coffee');
    renders[1]!.apply('coffee');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    // `or` is the default, so it is not written out.
    expect(requests[1]?.filters?.facets).toEqual([{ attribute: 'tags', values: ['coffee'] }]);
    expect(search.state.filters.facets).toEqual([{ attribute: 'tags', values: ['coffee'] }]);

    const latest = renders[renders.length - 1]!;
    expect(latest.items.find((item) => item.value === 'coffee')?.isActive).toBe(true);
  });

  it('sends the declared operator to the wire', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withFacetList>[0]>[0]
    >((record) => widget(record, { attribute: 'tags', operator: 'and' }));
    renders[1]!.apply('coffee');
    renders[1]!.apply('milk');
    await vi.waitFor(() => expect(requests.length).toBeGreaterThanOrEqual(2));
    expect(requests[requests.length - 1]?.filters?.facets).toEqual([
      { attribute: 'tags', values: ['coffee', 'milk'], operator: 'and' },
    ]);
  });

  it('limits, and toggles show more without a new search', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withFacetList>[0]>[0]
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

describe('withPagination', () => {
  it('exposes the one-based page window, the current page and apply', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withPagination>[0]>[0]
    >((record) => withPagination(record)({ padding: 1 }), { initialState: { page: 2 } });

    const rendered = renders[1]!;
    expect(rendered.current).toBe(2);
    expect(rendered.totalPages).toBe(5);
    expect(rendered.pages).toEqual([1, 2, 3]);
    expect(rendered.isFirstPage).toBe(false);
    expect(rendered.isLastPage).toBe(false);
    expect(rendered.canApply).toBe(true);
    expect(rendered.urlFor(4)).toContain('page=4');

    rendered.apply(4);
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.page).toBe(4);
  });
});

describe('withResultStats', () => {
  it('reports the numbers a "46 results in 14ms" label needs', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof withResultStats>[0]>[0]>(
      (record) => withResultStats(record)({}),
      { initialState: { query: 'espresso' } }
    );
    expect(renders[1]).toMatchObject({
      total: 9,
      tookMs: 7,
      query: 'espresso',
      page: 2,
      totalPages: 5,
      pageSize: 2,
      hasResults: true,
    });
    expect(renders[0]?.hasResults).toBe(false);
  });
});

describe('withSortSelect', () => {
  it('exposes the options and applies the sort key', async () => {
    const items = [
      { label: 'Relevance', value: 'relevance' },
      { label: 'Newest', value: 'newest' },
    ];
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withSortSelect>[0]>[0]
    >((record) => withSortSelect(record)({ items }));

    expect(renders[1]?.options).toEqual(items);
    expect(renders[1]?.current).toBe('relevance');
    expect(renders[1]?.canApply).toBe(true);
    expect(renders[1]?.urlFor('newest')).toContain('sort=newest');

    renders[1]!.apply('newest');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.sort).toBe('newest');
  });
});

describe('withActiveFilters', () => {
  it('lists facet and numeric filters, each removable on its own', async () => {
    const { renders, requests, search } = await mount<
      Parameters<Parameters<typeof withActiveFilters>[0]>[0]
    >((record) => withActiveFilters(record)({}), {
      initialState: {
        filters: {
          facets: [{ attribute: 'tags', values: ['coffee'] }],
          numeric: [{ attribute: 'price', operator: 'lte', value: 50 }],
        },
      },
    });

    const rendered = renders[1]!;
    expect(rendered.canApply).toBe(true);
    // TH-12: the item's label is the VALUE as a visitor reads it — the response's label for the
    // facet value, a sentence for the numeric bound. No code, no operator.
    expect(rendered.items.map((item) => item.label)).toEqual(['Coffee', 'up to 50']);
    expect(rendered.items[0]?.urlFor()).not.toContain('tags=coffee');

    rendered.items[0]!.apply();
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(search.state.filters.facets).toEqual([]);

    renders[renders.length - 1]!.clearAll();
    await vi.waitFor(() => expect(search.state.filters.numeric).toEqual([]));
  });
});

describe('withRange', () => {
  it('exposes the bounds and applies both ends at once', async () => {
    const { renders, requests } = await mount<
      Parameters<Parameters<typeof withRange>[0]>[0]
    >((record) => withRange(record)({ attribute: 'price', min: 0, max: 100 }));

    expect(renders[1]?.range).toEqual({ min: 0, max: 100 });
    expect(renders[1]?.start).toEqual([0, 100]);
    expect(renders[1]?.canApply).toBe(true);

    renders[1]!.apply([10, 50]);
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.filters?.numeric).toEqual([
      { attribute: 'price', operator: 'gte', value: 10 },
      { attribute: 'price', operator: 'lte', value: 50 },
    ]);

    const latest = renders[renders.length - 1]!;
    expect(latest.start).toEqual([10, 50]);
    latest.apply([0, 100]);
    await vi.waitFor(() => expect(requests).toHaveLength(3));
    expect(requests[2]?.filters).toBeUndefined();
  });

  it('cannot apply without bounds', async () => {
    const { renders } = await mount<Parameters<Parameters<typeof withRange>[0]>[0]>((record) =>
      withRange(record)({ attribute: 'price' })
    );
    expect(renders[1]?.canApply).toBe(false);
  });
});
