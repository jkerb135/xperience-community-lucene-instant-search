import { afterEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';
import { xpsearch } from './instance';
import type { InstantSearch, SearchState, Widget } from './types';

const BODY: SearchResponse = {
  hits: [{ objectID: 'doc-1', title: 'Espresso Basics' }],
  facets: { tags: { coffee: 3, milk: 1 } },
  page: 0,
  hitsPerPage: 20,
  nbHits: 1,
  nbPages: 1,
  processingTimeMs: 4,
  queryId: 'q-1',
};

/** Records every request body and answers with `BODY`. */
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

const instances: InstantSearch[] = [];
const create = (options: Parameters<typeof xpsearch>[0]): InstantSearch => {
  const instance = xpsearch({ debounceMs: 0, ...options });
  instances.push(instance);
  return instance;
};

afterEach(() => {
  while (instances.length > 0) instances.pop()?.dispose();
  vi.restoreAllMocks();
});

describe('widget lifecycle', () => {
  it('runs getSearchParameters, init and render in widget-add order, init exactly once', async () => {
    const { fetchFn, requests } = stubFetch();
    const order: string[] = [];
    const widget = (name: string): Widget => ({
      $$type: name,
      getSearchParameters: (state) => {
        order.push(`params:${name}`);
        return state;
      },
      init: () => order.push(`init:${name}`),
      render: () => order.push(`render:${name}`),
      dispose: () => order.push(`dispose:${name}`),
    });

    const search = create({ index: 'site-content', fetchFn });
    search.addWidgets([widget('a'), widget('b')]);
    search.start();
    await vi.waitFor(() => expect(order).toContain('render:b'));

    expect(order).toEqual([
      'init:a',
      'init:b',
      'params:a',
      'params:b',
      'render:a',
      'render:b',
    ]);
    expect(requests).toHaveLength(1);

    search.dispose();
    expect(order.slice(-2)).toEqual(['dispose:a', 'dispose:b']);
  });

  it('merges getSearchParameters and getRequestParameters into the outgoing request', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, initialState: { query: 'espresso' } });
    search.addWidgets([
      { getSearchParameters: (state) => ({ ...state, hitsPerPage: 5 }) },
      { getRequestParameters: (request) => ({ ...request, facets: ['tags'] }) },
    ]);
    search.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      index: 'site-content',
      query: 'espresso',
      hitsPerPage: 5,
      facets: ['tags'],
    });
  });

  it('does not search on initial load when asked not to, but still renders', async () => {
    const { fetchFn, requests } = stubFetch();
    const rendered: Array<unknown> = [];
    const search = create({
      index: 'site-content',
      fetchFn,
      searchOnInitialLoad: false,
    });
    search.addWidgets([{ render: ({ results }) => rendered.push(results) }]);
    search.start();
    await new Promise((r) => setTimeout(r, 10));
    expect(requests).toHaveLength(0);
    expect(rendered).toEqual([null]);
  });

  it('renders on state change with the last results, before the response arrives', async () => {
    const { fetchFn } = stubFetch();
    const seen: Array<{ query: string; hits: number }> = [];
    const search = create({ index: 'site-content', fetchFn });
    search.addWidgets([
      {
        render: ({ results, state }) =>
          seen.push({ query: state.query, hits: results?.hits.length ?? -1 }),
      },
    ]);
    search.start();
    await vi.waitFor(() => expect(seen).toHaveLength(1));
    search.helper.setQuery('espresso');
    await vi.waitFor(() => expect(seen).toHaveLength(2));
    expect(seen[1]).toEqual({ query: 'espresso', hits: 1 });
  });
});

describe('SearchHelper', () => {
  it('is chainable and searches only when told to', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, searchOnInitialLoad: false });
    search.start();

    const returned = search.helper
      .setQuery('espresso')
      .toggleFacetRefinement('tags', 'coffee')
      .addNumericRefinement('price', '<=', 50)
      .setSort('price_asc');
    expect(returned).toBe(search.helper);
    await new Promise((r) => setTimeout(r, 5));
    expect(requests).toHaveLength(0);

    search.helper.search();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      query: 'espresso',
      facetFilters: [['tags:coffee']],
      numericFilters: ['price<=50'],
      sort: 'price_asc',
    });
  });

  it('honours a declared facet operator', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, searchOnInitialLoad: false });
    search.start();
    search.helper
      .setFacetOperator('tags', 'and')
      .toggleFacetRefinement('tags', 'coffee')
      .toggleFacetRefinement('tags', 'milk')
      .search();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]?.facetFilters).toEqual([['tags:coffee'], ['tags:milk']]);
  });

  it('hands widgets a frozen state they cannot write to', async () => {
    const { fetchFn } = stubFetch();
    let captured: SearchState | undefined;
    const search = create({ index: 'site-content', fetchFn });
    search.addWidgets([{ render: ({ state }) => (captured = state) }]);
    search.start();
    await vi.waitFor(() => expect(captured).toBeDefined());
    expect(() => {
      (captured as { query: string }).query = 'hacked';
    }).toThrow();
    expect(search.state.query).toBe('');
  });
});

describe('error isolation (spec 5.7)', () => {
  it('keeps the other widgets rendering when one throws, and reports it', async () => {
    const { fetchFn } = stubFetch();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    const errors: Array<{ widget?: string; message: string }> = [];
    const renders: string[] = [];

    const search = create({ index: 'site-content', fetchFn });
    search.on('error', ({ error, widget }) => errors.push({ widget, message: error.message }));
    search.addWidgets([
      { $$type: 'good.before', render: () => renders.push('before') },
      {
        $$type: 'bad.widget',
        render: () => {
          throw new Error('boom');
        },
      },
      { $$type: 'good.after', render: () => renders.push('after') },
    ]);
    search.start();

    await vi.waitFor(() => expect(renders).toEqual(['before', 'after']));
    expect(errors).toEqual([{ widget: 'bad.widget', message: 'boom' }]);
    expect(consoleError).toHaveBeenCalled();
    expect(search.results?.nbHits).toBe(1);
  });

  it('emits an error and keeps working when a search fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const fetchFn = vi.fn().mockResolvedValue(new Response('nope', { status: 400 }));
    const errors: string[] = [];
    const search = create({
      index: 'site-content',
      fetchFn: fetchFn as unknown as typeof fetch,
      retries: 0,
    });
    search.on('error', ({ phase }) => errors.push(phase));
    search.start();
    await vi.waitFor(() => expect(errors).toContain('search'));
    expect(search.status).toBe('error');
  });
});

describe('event bus', () => {
  it('emits stateChange and render, and stops after off()', async () => {
    const { fetchFn } = stubFetch();
    const events: string[] = [];
    const onRender = (): void => void events.push('render');
    const search = create({ index: 'site-content', fetchFn });
    search.on('stateChange', () => events.push('stateChange')).on('render', onRender);
    search.start();
    await vi.waitFor(() => expect(events).toContain('render'));
    search.helper.setQuery('a');
    await vi.waitFor(() => expect(events.filter((e) => e === 'stateChange')).toHaveLength(1));

    search.off('render', onRender);
    const before = events.filter((e) => e === 'render').length;
    search.helper.setQuery('b');
    await new Promise((r) => setTimeout(r, 10));
    expect(events.filter((e) => e === 'render')).toHaveLength(before);
  });
});

describe('multi-instance (spec 12)', () => {
  it('refining one instance leaves the other untouched', async () => {
    const first = stubFetch();
    const second = stubFetch();
    const a = create({ index: 'index-a', fetchFn: first.fetchFn });
    const b = create({ index: 'index-b', fetchFn: second.fetchFn });
    a.start();
    b.start();
    await vi.waitFor(() => expect(second.requests).toHaveLength(1));

    a.helper.setQuery('espresso').toggleFacetRefinement('tags', 'coffee').search();
    await vi.waitFor(() => expect(first.requests).toHaveLength(2));

    expect(b.state.query).toBe('');
    expect(b.state.facetFilters).toEqual({});
    expect(second.requests).toHaveLength(1);
    expect(first.requests[1]).toMatchObject({ index: 'index-a', query: 'espresso' });
  });
});

describe('analytics', () => {
  it('correlates a click event with the last queryId', async () => {
    const posts: Array<{ url: string; body: string }> = [];
    const fetchFn = vi.fn(async (url: string, init: RequestInit) => {
      posts.push({ url, body: String(init.body) });
      return url.endsWith('/events')
        ? new Response('', { status: 202, headers: { [API_VERSION_HEADER]: '1' } })
        : new Response(JSON.stringify(BODY), { status: 200, headers: { [API_VERSION_HEADER]: '1' } });
    });
    const search = create({ index: 'site-content', fetchFn: fetchFn as unknown as typeof fetch });
    search.start();
    await vi.waitFor(() => expect(search.results).not.toBeNull());

    search.sendEvent('click', 'doc-1', 1);
    await vi.waitFor(() => expect(posts).toHaveLength(2));
    expect(posts[1]?.url).toBe('/api/xpsearch/events');
    expect(JSON.parse(posts[1]!.body)).toEqual({
      eventType: 'click',
      objectID: 'doc-1',
      queryId: 'q-1',
      index: 'site-content',
      position: 1,
    });
  });
});
