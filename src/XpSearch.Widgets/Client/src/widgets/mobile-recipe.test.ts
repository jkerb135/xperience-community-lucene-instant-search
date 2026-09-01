// @vitest-environment jsdom
/**
 * The mobile swap recipe of `docs/guides/widget-reference.md` ("The other half of the swap"),
 * run against the mock server with `matchMedia` forced both ways: below 1024px the Page Builder
 * `results` mount renders a load-more list and the `pagination` mount stays empty; at desktop
 * width nothing changes. Also pins the two handoffs the recipe claims survive the substitution:
 * the server-rendered first paint is replaced, and the instance's `initialQueryId` is spent on
 * the first query.
 */
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { startMockServer } from '../../mock/server.ts';
import { mountAll, registerWidgetType } from '../bootstrap';
import { QUERY_ROUTE } from '../contract/constants';
import { loadMore } from './loadMore';
import { pagination } from './pagination';
import { results } from './results';

let server: Awaited<ReturnType<typeof startMockServer>>;

beforeAll(async () => {
  server = await startMockServer(0);
});
afterAll(async () => {
  await server.close();
});
afterEach(() => {
  vi.unstubAllGlobals();
});

const SERVER_QUERY_ID = '11111111-1111-1111-1111-111111111111';

/** The `/search` markup a Page Builder page emits: a results mount holding the first paint. */
function page(): void {
  const instanceConfig = JSON.stringify({
    index: 'site-content',
    endpoint: `${server.url}${QUERY_ROUTE}`,
    initialState: { pageSize: 5 },
    initialQueryId: SERVER_QUERY_ID,
  });
  document.body.innerHTML =
    `<div class="xps-mount" data-xps-widget="results" data-xps-instance-config='${instanceConfig}' data-xps-config="{}">` +
    '<div data-xps-server-rendered class="xps xps-results"><ol class="xps-results__list">' +
    '<li class="xps-results__item">server paint</li></ol></div></div>' +
    `<div class="xps-mount" data-xps-widget="pagination" data-xps-instance-config='${instanceConfig}' data-xps-config="{}"></div>`;
}

/** The recipe itself, verbatim from the guide. */
function mountRecipe(narrowViewport: boolean): void {
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: narrowViewport, media: query }));
  const narrow = window.matchMedia('(max-width: 1023.98px)').matches;
  if (narrow) {
    registerWidgetType('results', (config) => loadMore(config as Parameters<typeof loadMore>[0]));
    registerWidgetType('pagination', () => ({ $$type: 'pagination' }));
  } else {
    registerWidgetType('results', (config) => results(config as Parameters<typeof results>[0]));
    registerWidgetType('pagination', (config) =>
      pagination(config as Parameters<typeof pagination>[0])
    );
  }
  mountAll(document, { widgets: { results, pagination } });
}

/** Records the request bodies the client sends, delegating to the real fetch. */
function recordRequests(): Array<Record<string, unknown>> {
  const sent: Array<Record<string, unknown>> = [];
  const real = globalThis.fetch;
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
    if (typeof init?.body === 'string') sent.push(JSON.parse(init.body) as Record<string, unknown>);
    return real(input, init);
  });
  return sent;
}

describe('the mobile swap recipe', () => {
  it('renders a load-more list and no pagination below 1024px', async () => {
    page();
    const sent = recordRequests();
    mountRecipe(true);

    await vi.waitFor(() =>
      expect(document.querySelectorAll('.xps-load-more__item').length).toBe(5)
    );
    expect(document.querySelector('.xps-load-more__load-more')?.textContent).toBe(
      'Load more results'
    );
    expect(document.querySelector('.xps-pagination')).toBeNull();
    expect(document.querySelectorAll('.xps-results__list').length).toBe(0);
    // The mount the Pagination widget emitted is still there, and stays empty.
    expect(document.querySelector('[data-xps-widget="pagination"]')?.innerHTML).toBe('');
    // The server-rendered first paint was handed over, exactly as `results` hands it over.
    expect(document.querySelector('[data-xps-server-rendered]')).toBeNull();
    expect(sent[0]?.['queryId']).toBe(SERVER_QUERY_ID);

    (document.querySelector('.xps-load-more__load-more') as HTMLButtonElement).click();
    await vi.waitFor(() =>
      expect(document.querySelectorAll('.xps-load-more__item').length).toBe(10)
    );
  });

  it('keeps results plus numbered pagination at 1024px and above', async () => {
    page();
    const sent = recordRequests();
    mountRecipe(false);

    await vi.waitFor(() => expect(document.querySelectorAll('.xps-results__item').length).toBe(5));
    expect(document.querySelectorAll('.xps-pagination__item--page').length).toBeGreaterThan(1);
    expect(document.querySelector('.xps-load-more__load-more')).toBeNull();
    expect(document.querySelector('[data-xps-server-rendered]')).toBeNull();
    expect(sent[0]?.['queryId']).toBe(SERVER_QUERY_ID);
  });
});
