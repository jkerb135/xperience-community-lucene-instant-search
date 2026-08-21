import { afterEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';
import { createSearch } from './instance';
import type { SearchInstance, SearchState, Widget } from './types';

const BODY: SearchResponse = {
  results: [{ id: 'doc-1', attributes: { title: 'Espresso Basics' } }],
  facets: {
    tags: [
      { value: 'coffee', label: 'Coffee', count: 3 },
      { value: 'milk', label: 'Milk drinks', count: 1 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
  tookMs: 4,
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

const instances: SearchInstance[] = [];
const create = (options: Parameters<typeof createSearch>[0]): SearchInstance => {
  const instance = createSearch({ debounceMs: 0, ...options });
  instances.push(instance);
  return instance;
};

afterEach(() => {
  while (instances.length > 0) instances.pop()?.dispose();
  vi.restoreAllMocks();
});

describe('widget lifecycle', () => {
  it('runs prepareState, init and render in widget-add order, init exactly once', async () => {
    const { fetchFn, requests } = stubFetch();
    const order: string[] = [];
    const widget = (name: string): Widget => ({
      $$type: name,
      prepareState: (state) => {
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

  it('merges prepareState and prepareRequest into the outgoing request', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, initialState: { query: 'espresso' } });
    search.addWidgets([
      { prepareState: (state) => ({ ...state, pageSize: 5 }) },
      { prepareRequest: (request) => ({ ...request, facets: ['tags'] }) },
    ]);
    search.start();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      index: 'site-content',
      query: 'espresso',
      pageSize: 5,
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
    const seen: Array<{ query: string; results: number }> = [];
    const search = create({ index: 'site-content', fetchFn });
    search.addWidgets([
      {
        render: ({ results, state }) =>
          seen.push({ query: state.query, results: results?.results.length ?? -1 }),
      },
    ]);
    search.start();
    await vi.waitFor(() => expect(seen).toHaveLength(1));
    search.actions.setQuery('espresso');
    await vi.waitFor(() => expect(seen).toHaveLength(2));
    expect(seen[1]).toEqual({ query: 'espresso', results: 1 });
  });
});

describe('SearchActions', () => {
  it('is chainable and searches only when told to', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, searchOnInitialLoad: false });
    search.start();

    const returned = search.actions
      .setQuery('espresso')
      .toggleFacet('tags', 'coffee')
      .setNumericFilter('price', 'lte', 50)
      .setSort('price_asc');
    expect(returned).toBe(search.actions);
    await new Promise((r) => setTimeout(r, 5));
    expect(requests).toHaveLength(0);

    search.actions.search();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      query: 'espresso',
      filters: {
        facets: [{ attribute: 'tags', values: ['coffee'] }],
        numeric: [{ attribute: 'price', operator: 'lte', value: 50 }],
      },
      sort: 'price_asc',
    });
  });

  it('honours a declared facet operator', async () => {
    const { fetchFn, requests } = stubFetch();
    const search = create({ index: 'site-content', fetchFn, searchOnInitialLoad: false });
    search.start();
    search.actions
      .setFacetOperator('tags', 'and')
      .toggleFacet('tags', 'coffee')
      .toggleFacet('tags', 'milk')
      .search();
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]?.filters?.facets).toEqual([
      { attribute: 'tags', values: ['coffee', 'milk'], operator: 'and' },
    ]);
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
    expect(search.results?.total).toBe(1);
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
    search.actions.setQuery('a');
    await vi.waitFor(() => expect(events.filter((e) => e === 'stateChange')).toHaveLength(1));

    search.off('render', onRender);
    const before = events.filter((e) => e === 'render').length;
    search.actions.setQuery('b');
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

    a.actions.setQuery('espresso').toggleFacet('tags', 'coffee').search();
    await vi.waitFor(() => expect(first.requests).toHaveLength(2));

    expect(b.state.query).toBe('');
    expect(b.state.filters.facets).toEqual([]);
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
      type: 'click',
      resultId: 'doc-1',
      queryId: 'q-1',
      position: 1,
    });
  });
});
