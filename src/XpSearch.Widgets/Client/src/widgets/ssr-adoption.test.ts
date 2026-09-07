// @vitest-environment jsdom
/**
 * SK-1: hydration is a handover, not a repaint. A mount holding what the server rendered
 * (`data-xps-server-rendered`) keeps it on screen until that widget has a response — the client
 * never paints its own skeleton over server content, and an empty mount still skeletons as before.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchResponse } from '../contract/generated';
import { createSearch } from '../instance';
import type { SearchInstance, Widget } from '../types';
import { facetList, pagination, results, searchBox } from './index';

const RESPONSE: SearchResponse = {
  results: [
    { id: 'doc-1', attributes: { title: 'Client card', url: '/client' } },
    { id: 'doc-2', attributes: { title: 'Second client card', url: '/client-2' } },
  ],
  facets: {
    contentType: [
      { value: 'Article', label: 'Article', count: 24 },
      { value: 'Product', label: 'Product', count: 11 },
    ],
  },
  page: 1,
  pageSize: 5,
  total: 46,
  totalPages: 9,
  tookMs: 14,
  redirect: null,
  queryId: 'q-1',
};

const started: SearchInstance[] = [];
/** Held until a test releases it, so it can assert what is on screen while the query is in flight. */
let pending = Promise.resolve();
let answer: () => void = () => {};

function start(widgets: Widget[]): SearchInstance {
  const fetchFn = (async () => {
    await pending;
    return new Response(JSON.stringify(RESPONSE), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  }) as unknown as typeof fetch;

  const search = createSearch({ index: 'site-content', fetchFn, debounceMs: 0 });
  search.addWidgets(widgets);
  search.start();
  started.push(search);
  return search;
}

const settled = (search: SearchInstance): Promise<void> =>
  vi.waitFor(() => expect(search.results).not.toBeNull()) as Promise<void>;

/** A mount holding what the server rendered inside it. */
function served(inner: string): HTMLElement {
  const host = document.createElement('div');
  host.className = 'xps-mount';
  host.innerHTML = inner;
  document.body.appendChild(host);
  return host;
}

const SERVER_RESULTS =
  '<div data-xps-server-rendered class="xps xps-results"><ol class="xps-results__list">' +
  '<li class="xps-results__item"><article class="xps-result">Server card</article></li>' +
  '</ol></div>';

/** The `xps-facet-list` block of `themes/fixtures/skeleton.html`, trimmed to two rows. */
const SERVER_FACETS =
  '<div data-xps-server-rendered aria-hidden="true" class="xps xps-facet-list xps-facet-list--skeleton">' +
  '<h3 class="xps-facet-list__title"><span class="xps-skeleton xps-skeleton--heading"></span></h3>' +
  '<ul class="xps-facet-list__list">' +
  '<li class="xps-facet-list__item"><span class="xps-skeleton xps-skeleton--box"></span></li>' +
  '<li class="xps-facet-list__item"><span class="xps-skeleton xps-skeleton--box"></span></li>' +
  '</ul></div>';

beforeEach(() => {
  document.body.innerHTML = '';
  pending = new Promise<void>((resolve) => {
    answer = resolve;
  });
});
afterEach(() => {
  for (const instance of started.splice(0)) instance.dispose();
});

describe('server-rendered first paint', () => {
  it('keeps the server result cards until the response, then replaces them', async () => {
    const host = served(SERVER_RESULTS);
    const search = start([results({ container: host })]);
    await vi.waitFor(() => expect(search.status).toBe('loading'));

    expect(host.textContent).toContain('Server card');
    expect(host.querySelector('.xps-result--skeleton')).toBeNull();
    expect(host.querySelector('[data-xps-server-rendered]')).not.toBeNull();
    // The live region is wired on the first render even though nothing is painted yet.
    expect(host.querySelector('.xps-results__status')).not.toBeNull();

    answer();
    await settled(search);
    expect(host.textContent).not.toContain('Server card');
    expect(host.querySelectorAll('.xps-results__item').length).toBe(2);
    expect(host.querySelector('[data-xps-server-rendered]')).toBeNull();
    expect(host.querySelector('.xps-results__status')).not.toBeNull();
  });

  it('keeps a server skeleton block until the widget has facets to draw', async () => {
    const host = served(SERVER_FACETS);
    const search = start([facetList({ container: host, attribute: 'contentType' })]);
    await vi.waitFor(() => expect(search.status).toBe('loading'));

    expect(host.querySelectorAll('.xps-skeleton--box').length).toBe(2);
    expect(host.querySelector('.xps-facet-list__checkbox')).toBeNull();

    answer();
    await settled(search);
    expect(host.querySelector('.xps-skeleton')).toBeNull();
    expect(host.querySelector('[data-xps-server-rendered]')).toBeNull();
    expect(host.querySelectorAll('.xps-facet-list__item').length).toBe(2);
  });

  it('still paints its own skeleton into an empty mount', async () => {
    const host = served('');
    start([results({ container: host })]);
    await vi.waitFor(() =>
      expect(host.querySelector('.xps-results')?.classList.contains('xps-results--loading')).toBe(
        true
      )
    );

    expect(host.querySelectorAll('.xps-result--skeleton').length).toBe(3);
    expect(host.querySelector('.xps-result--skeleton')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('carries a value typed into the server form over to the client input, with the focus', () => {
    const host = served(
      '<form data-xps-server-rendered class="xps xps-search-box" role="search" method="get">' +
        '<input class="xps-search-box__input" type="search" name="q" value="coff">' +
        '</form>'
    );
    (host.querySelector('input') as HTMLInputElement).focus();
    start([searchBox({ container: host })]);

    const input = host.querySelector('.xps-search-box__input') as HTMLInputElement;
    expect(input.value).toBe('coff');
    expect(document.activeElement).toBe(input);
    expect(host.querySelector('[data-xps-server-rendered]')).toBeNull();
  });

  it('marks the previous and next page links with rel', async () => {
    const host = served('');
    const search = start([pagination({ container: host })]);
    answer();
    await settled(search);

    const link = (kind: string): Element | null =>
      host.querySelector(`.xps-pagination__item--${kind} .xps-pagination__link`);
    expect(link('next')?.getAttribute('rel')).toBe('next');
    // Page 1: previous is a disabled span, so the rel belongs to the enabled next link only.
    expect(link('previous')?.tagName).toBe('SPAN');
    expect(host.querySelector('.xps-pagination__item--last a')?.hasAttribute('rel')).toBe(false);
  });
});
