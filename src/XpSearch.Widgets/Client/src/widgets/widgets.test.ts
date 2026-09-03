// @vitest-environment jsdom
/**
 * The default renderers against the markup contract (`themes/fixtures/*.html`) and against real
 * DOM events. Every assertion here is either "the contract says this class/attribute" or
 * "this interaction reaches the state".
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mountAll } from '../bootstrap';
import { API_VERSION_HEADER, EVENTS_ROUTE, SUGGEST_ROUTE } from '../contract/constants';
import type { SearchRequest, SearchResponse, Suggestion } from '../contract/generated';
import { createSearch } from '../instance';
import type { SearchInstance, Widget } from '../types';
import { html } from '../templates/html';
import { widgetId } from './dom';
import {
  DEFAULT_WIDGETS,
  categoryTree,
  clearFilters,
  activeFilters,
  loadMore,
  rangeFilter,
  results,
  pagination,
  facetList,
  filterSort,
  searchBox,
  sortSelect,
  suggestions,
  resultStats,
  toggleFilter,
} from './index';

const RESPONSE: SearchResponse = {
  results: [
    {
      id: 'doc-1',
      attributes: {
        title: 'Choosing an espresso machine',
        url: '/blog/choosing-an-espresso-machine',
        content: 'A dual-boiler espresso machine holds temperature.',
        contentType: 'Article',
        image: '/img/1.png',
      },
      highlights: {
        title: 'Choosing an <mark>espresso</mark> machine',
        content: 'A dual-boiler <mark>espresso</mark> machine holds temperature.',
      },
    },
    {
      id: 'doc-2',
      attributes: { title: 'Rocket Appartamento', url: '/products/rocket', contentType: 'Product' },
    },
    {
      id: 'doc-3',
      attributes: { title: 'Descaling <b>your</b> machine', url: '/support/descaling' },
    },
  ],
  facets: {
    contentType: [
      { value: 'Article', label: 'Article', count: 24 },
      { value: 'Product', label: 'Product', count: 11 },
      { value: 'Event', label: 'Event', count: 0 },
    ],
    // A three-level taxonomy, shaped exactly like themes/fixtures/category-tree.html.
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
  pageSize: 5,
  total: 46,
  totalPages: 9,
  tookMs: 14,
  redirect: null,
  queryId: 'q-1',
};

const calls: Array<{ url: string; body: Record<string, unknown> }> = [];
/** Every instance a test starts, so none of them leaks into the next test's `calls`. */
const started: SearchInstance[] = [];

/**
 * A fixed response, or one chosen per request — which is what a probe test needs, since a probe and
 * the search it previews hit the same endpoint and must answer differently.
 */
type Responder = SearchResponse | ((body: Record<string, unknown>) => Promise<SearchResponse>);

function start(widgets: Widget[], response: Responder = RESPONSE): SearchInstance {
  const fetchFn = (async (url: string, init: RequestInit) => {
    const body = JSON.parse(String(init.body)) as Record<string, unknown>;
    calls.push({ url: String(url), body });
    const answer = typeof response === 'function' ? await response(body) : response;
    return new Response(JSON.stringify(answer), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  }) as unknown as typeof fetch;

  const search = createSearch({
    index: 'site-content',
    fetchFn,
    debounceMs: 0,
    stalledSearchDelayMs: 5,
  });
  search.addWidgets(widgets);
  search.start();
  started.push(search);
  return search;
}

/** Waits for the response to have reached the widgets. */
const settled = (search: SearchInstance): Promise<void> =>
  vi.waitFor(() => expect(search.results).not.toBeNull()) as Promise<void>;

const container = (id: string): HTMLElement => {
  const element = document.createElement('div');
  element.id = id;
  document.body.appendChild(element);
  return element;
};

const classesOf = (element: Element | null | undefined): string[] => [...(element?.classList ?? [])];

/** The resultStats template separates the number from "ms" with a non-breaking space. */
const text = (element: Element | null | undefined): string => (element?.textContent ?? '').replace(/ /g, ' ');

let search: SearchInstance | undefined;

beforeEach(() => {
  calls.length = 0;
  document.body.innerHTML = '';
  // Recent searches (SG-1) persist across renders on purpose, so one test's submitted query would
  // otherwise show up in the next one's panel.
  localStorage.clear();
});
afterEach(() => {
  for (const instance of started.splice(0)) instance.dispose();
  search = undefined;
});

describe('searchBox', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLFormElement => {
    const host = container('search');
    search = start([searchBox({ container: host, ...params })]);
    return host.querySelector('form') as HTMLFormElement;
  };

  it('renders the contract markup', () => {
    const form = mount();
    expect(classesOf(form)).toEqual(['xps', 'xps-search-box']);
    expect(form.getAttribute('role')).toBe('search');
    expect(form.hasAttribute('novalidate')).toBe(true);

    const label = form.querySelector('label') as HTMLLabelElement;
    const input = form.querySelector('input') as HTMLInputElement;
    expect(classesOf(label)).toEqual(['xps-search-box__label', 'xps-sr-only']);
    expect(label.htmlFor).toBe(input.id);
    expect(input.id).not.toBe('');
    expect(input.type).toBe('search');
    expect(input.name).toBe('q');
    expect(input.getAttribute('autocomplete')).toBe('off');
    expect(classesOf(form.querySelector('.xps-search-box__field'))).toEqual([
      'xps-search-box__field',
    ]);
    expect(classesOf(form.querySelector('.xps-search-box__loading'))).toEqual([
      'xps-search-box__loading',
      'xps-skeleton',
    ]);

    const reset = form.querySelector('.xps-search-box__reset') as HTMLButtonElement;
    expect(reset.type).toBe('reset');
    expect(reset.getAttribute('aria-label')).toBe('Clear the search query');
    expect(reset.hidden).toBe(true);
    expect(reset.querySelector('[aria-hidden="true"]')).not.toBeNull();
    expect(form.querySelector('.xps-search-box__submit')).toBeNull();
  });

  it('renders the submit button only when asked, and shows the label when asked', () => {
    const form = mount({ showSubmit: true, showLabel: true, label: 'Find', placeholder: 'Go…' });
    const submit = form.querySelector('.xps-search-box__submit') as HTMLButtonElement;
    expect(submit.type).toBe('submit');
    expect(submit.getAttribute('aria-label')).toBe('Submit the search query');
    expect(classesOf(form.querySelector('label'))).toEqual(['xps-search-box__label']);
    expect(form.querySelector('label')?.textContent).toBe('Find');
    expect(form.querySelector('input')?.placeholder).toBe('Go…');
  });

  it('refines while typing and reveals the reset button', async () => {
    const form = mount();
    const input = form.querySelector('input') as HTMLInputElement;
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(search?.state.query).toBe('espresso');
    await settled(search!);
    expect((form.querySelector('.xps-search-box__reset') as HTMLButtonElement).hidden).toBe(false);
  });

  it('keeps focus and the caret across a re-render', async () => {
    const form = mount();
    const input = form.querySelector('input') as HTMLInputElement;
    input.focus();
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.setSelectionRange(3, 3);

    await settled(search!);
    expect(document.activeElement).toBe(input);
    expect(input.selectionStart).toBe(3);
    expect(input.value).toBe('espresso');
  });

  it('clears on reset and picks up an external query change', async () => {
    const form = mount();
    const input = form.querySelector('input') as HTMLInputElement;
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settled(search!);

    (form.querySelector('.xps-search-box__reset') as HTMLButtonElement).click();
    expect(search?.state.query).toBe('');
    expect(input.value).toBe('');

    search?.actions.setQuery('cold brew').search();
    await vi.waitFor(() => expect(input.value).toBe('cold brew'));
  });

  it('routes the query through queryHook', () => {
    const form = mount({
      queryHook: (query: string, apply: (value: string) => void) => apply(query.trim()),
    });
    const input = form.querySelector('input') as HTMLInputElement;
    input.value = '  latte  ';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(search?.state.query).toBe('latte');
  });

  it('follows a redirect rule on submit only, never on load or while typing', async () => {
    const assigned: string[] = [];
    const windowRef = {
      location: { assign: (url: string) => assigned.push(url) },
    } as unknown as Window;
    const host = container('search');
    search = start([searchBox({ container: host, windowRef })], {
      ...RESPONSE,
      redirect: { url: '/support', rule: 'Support redirect' },
    });
    const form = host.querySelector('form') as HTMLFormElement;
    const input = form.querySelector('input') as HTMLInputElement;

    await settled(search);
    expect(assigned).toEqual([]);

    input.value = 'help';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(assigned).toEqual([]);

    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(assigned).toEqual(['/support']));
  });

  it('followRedirects: false leaves the visitor on the page', async () => {
    const assigned: string[] = [];
    const windowRef = {
      location: { assign: (url: string) => assigned.push(url) },
    } as unknown as Window;
    const host = container('search');
    search = start([searchBox({ container: host, windowRef, followRedirects: false })], {
      ...RESPONSE,
      redirect: { url: '/support', rule: 'Support redirect' },
    });
    const form = host.querySelector('form') as HTMLFormElement;

    await settled(search);
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(assigned).toEqual([]);
  });

  it('marks the root stalled while a slow request is in flight', async () => {
    const host = container('search');
    const slow = (async () => {
      await new Promise((resolve) => setTimeout(resolve, 300));
      return new Response(JSON.stringify(RESPONSE), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({ index: 'i', fetchFn: slow, debounceMs: 0, stalledSearchDelayMs: 1 });
    search.addWidgets([searchBox({ container: host })]);
    search.start();
    started.push(search);
    await vi.waitFor(() =>
      expect(host.querySelector('.xps-search-box--stalled')).not.toBeNull()
    );
  });
});

/** An empty result set: what every filtered-empty-state test searches into. */
const NOTHING: SearchResponse = { ...RESPONSE, results: [], total: 0, totalPages: 0 };

describe('results', () => {
  const mount = (params: Record<string, unknown> = {}, response: Responder = RESPONSE): HTMLElement => {
    const host = container('results');
    search = start([results({ container: host, ...params })], response);
    return host.querySelector('.xps-results') as HTMLElement;
  };

  it('renders the contract markup for a result list', async () => {
    const root = mount();
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-results']);

    const status = root.querySelector('.xps-results__status') as HTMLElement;
    expect(classesOf(status)).toEqual(['xps-results__status', 'xps-sr-only']);
    expect(status.getAttribute('role')).toBe('status');
    expect(status.tagName).toBe('P');

    const list = root.querySelector('.xps-results__list') as HTMLElement;
    expect(list.tagName).toBe('OL');
    const items = root.querySelectorAll('.xps-results__item');
    expect(items.length).toBe(3);
    expect(items[0]?.firstElementChild?.tagName).toBe('ARTICLE');

    const first = items[0] as HTMLElement;
    expect(classesOf(first.querySelector('article'))).toEqual(['xps-result']);
    expect(classesOf(first.querySelector('.xps-result__image'))).toEqual(['xps-result__image']);
    expect(first.querySelector('.xps-result__image')?.getAttribute('alt')).toBe('');
    expect(first.querySelector('.xps-result__title')?.tagName).toBe('H3');
    const link = first.querySelector('.xps-result__link') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/blog/choosing-an-espresso-machine');
    expect(link.querySelector('mark')?.className).toBe('xps-highlight');
    expect(first.querySelector('.xps-result__snippet')?.tagName).toBe('P');
    expect(first.querySelector('.xps-result__meta-item')?.textContent).toBe('Article');

    // No image and no highlight: the media block and the snippet are omitted, not emptied.
    const third = items[2] as HTMLElement;
    expect(third.querySelector('.xps-result__media')).toBeNull();
    expect(third.querySelector('.xps-result__link')?.textContent).toBe('Descaling <b>your</b> machine');
  });

  it('takes over the server-rendered first paint on its first render', async () => {
    const host = container('results');
    // What the Page Builder widget renders inside its mount element (spec 5.8).
    host.innerHTML =
      '<div data-xps-server-rendered class="xps xps-results"><ol class="xps-results__list">' +
      '<li class="xps-results__item">server</li></ol></div>';
    search = start([results({ container: host })]);

    // The first render empties the container, so the server block never coexists with the client's.
    expect(host.querySelector('[data-xps-server-rendered]')).toBeNull();
    expect(host.textContent).not.toContain('server');
    expect(classesOf(host.firstElementChild)).toEqual(['xps', 'xps-results']);

    await settled(search);
    expect(host.querySelectorAll('.xps-results__item').length).toBe(3);
  });

  it('reads the default attribute names the server projects, and honours the overrides', async () => {
    const root = mount();
    await settled(search!);
    const first = root.querySelector('.xps-results__item') as HTMLElement;

    // title / url / contentType are what the server calls the base fields of every document.
    expect((first.querySelector('.xps-result__link') as HTMLAnchorElement).getAttribute('href')).toBe(
      '/blog/choosing-an-espresso-machine'
    );
    expect(first.querySelector('.xps-result__link')?.textContent).toBe('Choosing an espresso machine');
    expect(first.querySelector('.xps-result__meta-item')?.textContent).toBe('Article');
    // The snippet is highlighted, so the fallback list is searched against the highlights too.
    expect(first.querySelector('.xps-result__snippet mark')?.textContent).toBe('espresso');
  });

  it('takes the title, url and snippet attribute names from the params', async () => {
    const response: SearchResponse = {
      ...RESPONSE,
      results: [
        {
          id: 'doc-1',
          attributes: {
            ProductFieldName: 'Rocket Appartamento',
            ProductPageUrl: '/products/rocket',
            ProductFieldDescription: 'A heat exchanger machine.',
          },
        },
      ],
    };
    const root = mount(
      {
        titleAttribute: 'ProductFieldName',
        urlAttribute: 'ProductPageUrl',
        snippetAttributes: ['ProductFieldDescription'],
      },
      response
    );
    await settled(search!);
    const link = root.querySelector('.xps-result__link') as HTMLAnchorElement;

    expect(link.getAttribute('href')).toBe('/products/rocket');
    expect(link.textContent).toBe('Rocket Appartamento');
    expect(root.querySelector('.xps-result__snippet')?.textContent).toBe('A heat exchanger machine.');
  });

  it('announces the count in the live region, and only when it changes', async () => {
    const root = mount();
    search?.actions.setQuery('espresso');
    await settled(search!);
    const status = root.querySelector('.xps-results__status') as HTMLElement;
    expect(status.textContent).toBe('46 results for “espresso”');

    const before = status;
    const spy = vi.spyOn(status, 'textContent', 'set');
    search?.actions.setPage(1).search();
    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(1));
    expect(root.querySelector('.xps-results__status')).toBe(before);
    expect(spy).not.toHaveBeenCalled();
  });

  it('renders the path line and the type label, and reads pathAttribute', async () => {
    const response: SearchResponse = {
      ...RESPONSE,
      results: [
        {
          id: 'doc-1',
          attributes: {
            title: 'Choosing an espresso machine',
            url: '/blog/choosing',
            breadcrumb: 'Home / Blog / Coffee',
            contentType: 'Article',
          },
        },
      ],
    };
    const root = mount({ pathAttribute: 'breadcrumb' }, response);
    await settled(search!);

    const path = root.querySelector('.xps-result__path') as HTMLElement;
    expect(path.tagName).toBe('P');
    expect(path.textContent).toBe('Home / Blog / Coffee');
    // The path sits between the title and where a snippet would be.
    expect(path.previousElementSibling?.className).toBe('xps-result__title');
    expect(classesOf(root.querySelector('.xps-result__meta-item'))).toEqual([
      'xps-result__meta-item',
      'xps-result__type',
    ]);
  });

  it('omits the path line when the attribute is absent', async () => {
    const root = mount();
    await settled(search!);
    expect(root.querySelector('.xps-result__path')).toBeNull();
  });

  it('falls back to the file-type glyph when a result has a fileType but no image', async () => {
    const response: SearchResponse = {
      ...RESPONSE,
      results: [
        { id: 'doc-1', attributes: { title: 'Warranty', url: '/w.pdf', fileType: 'pdf' } },
        { id: 'doc-2', attributes: { title: 'No media', url: '/none' } },
      ],
    };
    const root = mount({}, response);
    await settled(search!);
    const [file, none] = [...root.querySelectorAll('.xps-results__item')];

    const icon = file?.querySelector('.xps-result__icon') as SVGElement;
    expect(icon.tagName).toBe('svg');
    expect(icon.parentElement?.className).toBe('xps-result__media');
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.getAttribute('stroke')).toBe('currentColor');
    expect(none?.querySelector('.xps-result__media')).toBeNull();
  });

  it('renders the empty state', async () => {
    const root = mount({}, { ...RESPONSE, results: [], total: 0, totalPages: 0 });
    search?.actions.setQuery('xyzzy');
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-results', 'xps-results--empty']);
    expect(root.querySelector('.xps-results__list')).toBeNull();
    expect(root.querySelector('.xps-results__empty')?.textContent).toContain('No results for');
    expect(root.querySelector('.xps-results__clear')).toBeNull();
    expect(root.querySelector('.xps-results__status')?.textContent).toBe('No results for “xyzzy”');
  });

  /**
   * SG-1: the two ways out of a dead end. Both come off the response, both render only when the
   * server offered them, and both run their query through the same delegated handler.
   */
  it('offers the verified correction and the popular searches the response carried', async () => {
    const root = mount({}, {
      ...NOTHING,
      didYouMean: 'espresso',
      popularSearches: ['espresso', 'latte art'],
    });
    search?.actions.setQuery('esspresso').search();
    await settled(search!);

    const correction = root.querySelector('.xps-results__correction') as HTMLButtonElement;
    expect(root.querySelector('.xps-results__did-you-mean')?.textContent).toBe(
      'Did you mean espresso?'
    );
    expect(correction.type).toBe('button');
    expect(correction.dataset['xpsRecover']).toBe('espresso');
    expect(root.querySelectorAll('.xps-results__popular-item')).toHaveLength(2);

    correction.click();
    await vi.waitFor(() => expect(calls[calls.length - 1]?.body['query']).toBe('espresso'));
    expect(search?.state.query).toBe('espresso');
  });

  it('runs a popular search when its chip is clicked', async () => {
    const root = mount({}, { ...NOTHING, popularSearches: ['espresso', 'latte art'] });
    search?.actions.setQuery('xyzzy').search();
    await settled(search!);

    expect(root.querySelector('.xps-results__did-you-mean')).toBeNull();
    const chips = [...root.querySelectorAll<HTMLButtonElement>('.xps-results__popular-button')];
    expect(chips.map((chip) => chip.textContent)).toEqual(['espresso', 'latte art']);
    expect(classesOf(chips[1])).toEqual(['xps-button', 'xps-chip', 'xps-results__popular-button']);

    chips[1]?.click();
    await vi.waitFor(() => expect(search?.state.query).toBe('latte art'));
  });

  it('renders no recovery at all when the response carried none', async () => {
    const root = mount({}, NOTHING);
    search?.actions.setQuery('xyzzy').search();
    await settled(search!);

    expect(root.querySelector('.xps-results__did-you-mean')).toBeNull();
    expect(root.querySelector('.xps-results__popular')).toBeNull();
  });

  it('hands the recovery members to a custom empty template', async () => {
    const seen: Array<Record<string, unknown>> = [];
    const root = mount(
      {
        templates: {
          empty: (data: Record<string, unknown>) => {
            seen.push(data);
            return html`<p class="custom">${String(data['didYouMean'])}</p>`;
          },
        },
      },
      { ...NOTHING, didYouMean: 'espresso', popularSearches: ['latte art'] }
    );
    search?.actions.setQuery('esspresso').search();
    await settled(search!);

    expect(root.querySelector('.custom')?.textContent).toBe('espresso');
    expect(seen[seen.length - 1]?.['popularSearches']).toEqual(['latte art']);
  });

  it('offers to clear the filters when the empty state is a refined one', async () => {
    const root = mount({}, { ...RESPONSE, results: [], total: 0, totalPages: 0 });
    search?.actions.setQuery('xyzzy').toggleFacet('contentType', 'Article').search();
    await settled(search!);

    expect(root.querySelector('.xps-results__empty')?.textContent).toContain('with these filters');
    const clear = root.querySelector('.xps-results__clear') as HTMLButtonElement;
    expect(clear.tagName).toBe('BUTTON');
    expect(clear.type).toBe('button');
    expect(classesOf(clear)).toEqual(['xps-button', 'xps-button--primary', 'xps-results__clear']);
    // The live region is unchanged by the variant: still one announcement of the empty result.
    expect(root.querySelector('.xps-results__status')?.textContent).toBe('No results for “xyzzy”');

    const before = calls.length;
    clear.click();
    // Clearing searches: the state drops the facet and the next request carries no filters at all.
    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(before));
    expect(search?.state.filters.facets).toEqual([]);
    expect(calls[calls.length - 1]?.body['filters']).toBeUndefined();
  });

  /**
   * ES-1: the refined empty state probes for how many results the same query returns unfiltered,
   * and says so. `unfiltered` answers a probe with `total`, and the search itself with nothing.
   */
  const emptyWithProbe = (total: number | 'fail'): Responder =>
    async (body) => {
      if (body['probe'] !== true) return NOTHING;
      if (total === 'fail') throw new Error('probe is down');
      return { ...RESPONSE, results: [], total, totalPages: 1 };
    };

  const refine = async (root: HTMLElement): Promise<void> => {
    search?.actions.setQuery('xyzzy').toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(root.querySelector('.xps-results__clear')).not.toBeNull());
  };

  it('counts the empty state from an unfiltered probe, in the copy and on the button', async () => {
    const root = mount({}, emptyWithProbe(7));
    await refine(root);

    await vi.waitFor(() =>
      expect(root.querySelector('.xps-results__clear')?.textContent).toBe(
        'Clear filters and show 7 results'
      )
    );
    expect(root.querySelector('.xps-results__empty')?.textContent).toContain(
      'There are 7 results without them.'
    );

    // The probe is one request, unfiltered, flagged, and it left the widget's own state alone.
    const probe = calls.filter((call) => call.body['probe'] === true);
    expect(probe).toHaveLength(1);
    expect(probe[0]?.body['filters']).toBeUndefined();
    expect(probe[0]?.body['query']).toBe('xyzzy');
    expect(search?.state.filters.facets.map((facet) => facet.attribute)).toEqual(['contentType']);
  });

  it('says "1 result", not "1 results"', async () => {
    const root = mount({}, emptyWithProbe(1));
    await refine(root);

    await vi.waitFor(() =>
      expect(root.querySelector('.xps-results__clear')?.textContent).toBe(
        'Clear filters and show 1 result'
      )
    );
  });

  it.each([
    ['the probe finds nothing behind the filters either', 0 as const],
    ['the probe fails', 'fail' as const],
  ])('keeps the countless copy when %s', async (_why, total) => {
    const root = mount({}, emptyWithProbe(total));
    await refine(root);

    await vi.waitFor(() => expect(calls.some((call) => call.body['probe'] === true)).toBe(true));
    // Give the answer a turn to land: it must not change the copy at all.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(root.querySelector('.xps-results__clear')?.textContent).toBe('Clear filters');
    expect(root.querySelector('.xps-results__empty')?.textContent).not.toContain('without them');
  });

  it('discards a probe answer for a query that is no longer the current one', async () => {
    let release = (): void => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const root = mount({}, async (body) => {
      if (body['probe'] !== true) return NOTHING;
      await held;
      return { ...RESPONSE, results: [], total: 7, totalPages: 1 };
    });
    await refine(root);
    await vi.waitFor(() => expect(calls.some((call) => call.body['probe'] === true)).toBe(true));

    // The visitor types on while the probe is in flight; its 7 belongs to "xyzzy", not to this.
    search?.actions.setQuery('plugh').search();
    await vi.waitFor(() => expect(search?.state.query).toBe('plugh'));
    release();
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(root.querySelector('.xps-results__empty')?.textContent).not.toContain('7 results');
  });

  it('passes the probed count to a custom templates.empty', async () => {
    const seen: Array<number | undefined> = [];
    const root = mount(
      {
        templates: {
          empty: (data: { unfilteredCount?: number }) => {
            seen.push(data.unfilteredCount);
            return html`<button type="button" class="xps-results__clear">clear</button>`;
          },
        },
      },
      emptyWithProbe(7)
    );
    await refine(root);

    await vi.waitFor(() => expect(seen).toContain(7));
    expect(seen[0]).toBeUndefined(); // countless until the probe answers
  });

  it('renders the empty-state icon in both variants', async () => {
    const root = mount({}, NOTHING);
    search?.actions.setQuery('xyzzy').search();
    await vi.waitFor(() => expect(root.querySelector('.xps-results__empty')).not.toBeNull());

    const icon = root.querySelector('.xps-results__empty-icon') as SVGElement;
    expect(icon.tagName).toBe('svg');
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.getAttribute('stroke')).toBe('currentColor');
    expect(icon.getAttribute('viewBox')).toBe('0 0 24 24');

    await refine(root);
    expect(root.querySelector('.xps-results__empty')?.firstElementChild?.tagName).toBe('svg');
  });

  it('hands templates.empty the refinement state and the clear action', async () => {
    const seen: Array<{ query: string; hasRefinements: boolean }> = [];
    let clear = (): void => {};
    const root = mount(
      {
        templates: {
          empty: (data: { query: string; hasRefinements: boolean; clearRefinements: () => void }) => {
            seen.push({ query: data.query, hasRefinements: data.hasRefinements });
            clear = data.clearRefinements;
            return html`<p id="mine">custom</p>`;
          },
        },
      },
      { ...RESPONSE, results: [], total: 0, totalPages: 0 }
    );
    search?.actions.setQuery('xyzzy').toggleFacet('contentType', 'Article').search();
    await settled(search!);

    expect(root.querySelector('#mine')?.textContent).toBe('custom');
    expect(seen[seen.length - 1]).toEqual({ query: 'xyzzy', hasRefinements: true });

    clear();
    await vi.waitFor(() => expect(search?.state.filters.facets.length).toBe(0));
  });

  it('renders skeletons once a first search outlives the stall threshold', async () => {
    const host = container('results');
    const slow = (async () => {
      await new Promise((resolve) => setTimeout(resolve, 300));
      return new Response(JSON.stringify(RESPONSE), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({ index: 'i', fetchFn: slow, debounceMs: 0, stalledSearchDelayMs: 1 });
    search.addWidgets([results({ container: host })]);
    search.start();
    started.push(search);

    const root = host.querySelector('.xps-results') as HTMLElement;
    await vi.waitFor(() => expect(classesOf(root)).toContain('xps-results--loading'));
    expect(root.getAttribute('aria-busy')).toBe('true');
    const skeletons = root.querySelectorAll('.xps-result--skeleton');
    expect(skeletons.length).toBe(3);
    expect(skeletons[0]?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('.xps-results__status')?.textContent).toBe('Searching…');
    await settled(search);
    expect(root.querySelectorAll('.xps-result--skeleton').length).toBe(0);
  });

  it('applies transformItems and custom templates', async () => {
    const root = mount({
      transformItems: (items: Array<{ attributes: { title: string } }>) => items.slice(0, 1),
      templates: {
        item: (result: { attributes: { title: string } }, tools: { html: typeof html }) =>
          tools.html`<article class="xps-result"><span>${result.attributes.title}</span></article>`,
      },
    });
    await settled(search!);
    expect(root.querySelectorAll('.xps-results__item').length).toBe(1);
    expect(root.querySelector('span')?.textContent).toBe('Choosing an espresso machine');
  });

  it('sends a click event with the id and one-based position', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelectorAll('.xps-result__link')[1] as HTMLAnchorElement).click();
    await vi.waitFor(() => expect(calls.some((call) => call.url.endsWith(EVENTS_ROUTE))).toBe(true));
    const event = calls.find((call) => call.url.endsWith(EVENTS_ROUTE))?.body;
    expect(event).toMatchObject({
      type: 'click',
      resultId: 'doc-2',
      position: 2,
      queryId: 'q-1',
    });
  });
});

describe('facetList', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLElement => {
    const host = container('facet');
    search = start([
      facetList({ container: host, attribute: 'contentType', label: 'Content type', ...params }),
    ]);
    return host.querySelector('.xps-facet-list') as HTMLElement;
  };

  it('renders the contract markup with real checkboxes', async () => {
    const root = mount();
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-facet-list']);

    const title = root.querySelector('.xps-facet-list__title') as HTMLElement;
    const list = root.querySelector('.xps-facet-list__list') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.textContent).toBe('Content type');
    expect(list.tagName).toBe('UL');
    expect(list.getAttribute('aria-labelledby')).toBe(title.id);

    const items = [...root.querySelectorAll('.xps-facet-list__item')];
    expect(items.map((item) => item.querySelector('.xps-facet-list__value')?.textContent)).toEqual(
      ['Article', 'Product', 'Event']
    );
    expect(items.map((item) => item.querySelector('.xps-facet-list__count')?.textContent)).toEqual(
      ['24', '11', '0']
    );
    const checkbox = items[0]?.querySelector('input') as HTMLInputElement;
    expect(checkbox.type).toBe('checkbox');
    expect(checkbox.name).toBe('contentType');
    expect(checkbox.value).toBe('Article');
    expect(checkbox.closest('label')?.className).toBe('xps-facet-list__label');
    // A value nothing matches any more is a disabled row (fixture: canApply=false).
    expect(classesOf(items[2])).toEqual([
      'xps-facet-list__item',
      'xps-facet-list__item--disabled',
    ]);
    expect((items[2]?.querySelector('input') as HTMLInputElement).disabled).toBe(true);
  });

  it('refines when a checkbox is clicked and marks the row selected', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelector('input') as HTMLInputElement).click();
    expect(search?.state.filters.facets[0]?.values).toEqual(['Article']);
    await vi.waitFor(() =>
      expect(classesOf(root.querySelector('.xps-facet-list__item'))).toContain(
        'xps-facet-list__item--selected'
      )
    );
    expect((root.querySelector('input') as HTMLInputElement).checked).toBe(true);
  });

  /**
   * TH-7 (owner item 128): a refinement that narrows the search to nothing takes the facet counts
   * with it. The value the visitor picked has to stay on screen, checked, so it can be un-picked —
   * otherwise the only way back is Clear all.
   */
  it('keeps a selected value the response no longer carries, checked and at zero', async () => {
    let answer: SearchResponse = RESPONSE;
    const host = container('facet');
    search = start(
      [facetList({ container: host, attribute: 'contentType', label: 'Content type' })],
      async () => answer
    );
    const root = host.querySelector('.xps-facet-list') as HTMLElement;
    await settled(search);

    answer = { ...RESPONSE, results: [], total: 0, totalPages: 0, facets: {} };
    search.actions.toggleFacet('contentType', 'Recipe').search();
    await vi.waitFor(() => expect(search?.results?.total).toBe(0));

    const items = [...root.querySelectorAll('.xps-facet-list__item')];
    expect(items).toHaveLength(1);
    expect(items[0]?.querySelector('.xps-facet-list__value')?.textContent).toBe('Recipe');
    expect(items[0]?.querySelector('.xps-facet-list__count')?.textContent).toBe('0');
    expect(classesOf(items[0])).toEqual([
      'xps-facet-list__item',
      'xps-facet-list__item--selected',
      'xps-facet-list__item--empty',
    ]);
    const checkbox = items[0]?.querySelector('input') as HTMLInputElement;
    expect(checkbox.checked).toBe(true);
    // Never disabled: this row is the way out.
    expect(checkbox.disabled).toBe(false);
    expect(root.querySelector<HTMLElement>('.xps-facet-list__no-results')?.hidden).toBe(true);

    // Unticking it drops the filter and re-queries without it.
    const before = calls.length;
    checkbox.click();
    expect(search.state.filters.facets[0]?.values ?? []).toEqual([]);
    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(before));
    expect(calls[calls.length - 1]?.body['filters']).toBeUndefined();
  });

  it('says "No matching filters" only when there is nothing selected either', async () => {
    const host = container('facet');
    search = start([facetList({ container: host, attribute: 'contentType' })], {
      ...RESPONSE,
      results: [],
      total: 0,
      totalPages: 0,
      facets: {},
    });
    const root = host.querySelector('.xps-facet-list') as HTMLElement;
    await settled(search);

    expect(root.querySelectorAll('.xps-facet-list__item')).toHaveLength(0);
    expect(root.querySelector<HTMLElement>('.xps-facet-list__no-results')?.hidden).toBe(false);
  });

  it('orders items with sortSelect', async () => {
    const root = mount({ sortBy: ['name:asc'] });
    await settled(search!);
    expect(
      [...root.querySelectorAll('.xps-facet-list__value')].map((node) => node.textContent)
    ).toEqual(['Article', 'Event', 'Product']);
  });

  it('toggles show more with aria-expanded and keeps the button in the DOM', async () => {
    const root = mount({ showMore: true, limit: 1 });
    await settled(search!);
    const button = root.querySelector('.xps-facet-list__show-more') as HTMLButtonElement;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.textContent).toBe('Show more');
    expect(root.querySelectorAll('.xps-facet-list__item').length).toBe(1);

    button.click();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.textContent).toBe('Show less');
    expect(root.querySelectorAll('.xps-facet-list__item').length).toBe(3);
    expect(root.querySelector('.xps-facet-list__show-more')).toBe(button);
  });

  it('disables show more when there is nothing more to show', async () => {
    const root = mount({ showMore: true, limit: 10 });
    await settled(search!);
    const button = root.querySelector('.xps-facet-list__show-more') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(classesOf(button)).toContain('xps-facet-list__show-more--disabled');
  });

  it('collapses the group from the title by default, and drops the button when told to', async () => {
    const root = mount();
    await settled(search!);
    const toggle = root.querySelector('.xps-facet-list__toggle') as HTMLButtonElement;
    const body = root.querySelector('.xps-facet-list__body') as HTMLElement;
    expect(toggle.closest('h3')).toBe(root.querySelector('.xps-facet-list__title'));
    expect(toggle.getAttribute('aria-controls')).toBe(body.id);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(body.hidden).toBe(false);

    toggle.click();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(body.hidden).toBe(true);
    // Collapsing is a view state: it refines nothing and survives the next render.
    expect(search?.state.filters.facets).toEqual([]);
    search?.actions.setQuery('espresso').search();
    await settled(search!);
    expect((root.querySelector('.xps-facet-list__body') as HTMLElement).hidden).toBe(true);

    toggle.click();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(body.hidden).toBe(false);

    const plain = mount({ collapsible: false });
    await settled(search!);
    expect(plain.querySelector('.xps-facet-list__toggle')).toBeNull();
    expect((plain.querySelector('.xps-facet-list__title') as HTMLElement).textContent).toBe(
      'Content type'
    );
    expect((plain.querySelector('.xps-facet-list__body') as HTMLElement).hidden).toBe(false);
  });

  it('filters client-side when searchable, and keeps focus in the search input', async () => {
    const root = mount({ searchable: true });
    await settled(search!);
    expect(classesOf(root)).toContain('xps-facet-list--searchable');
    const field = root.querySelector('.xps-facet-list__search-input') as HTMLInputElement;
    expect(field.type).toBe('search');
    expect(root.querySelector('.xps-facet-list__search label')?.className).toBe('xps-sr-only');

    field.focus();
    field.value = 'art';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    expect(document.activeElement).toBe(field);
    const values = [...root.querySelectorAll('.xps-facet-list__value')];
    expect(values.length).toBe(1);
    expect(values[0]?.innerHTML).toBe('<mark class="xps-highlight">Art</mark>icle');
    expect((root.querySelector('.xps-facet-list__no-results') as HTMLElement).hidden).toBe(true);

    field.value = 'zzz';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    expect((root.querySelector('.xps-facet-list__list') as HTMLElement).hidden).toBe(true);
    const none = root.querySelector('.xps-facet-list__no-results') as HTMLElement;
    expect(none.hidden).toBe(false);
    expect(none.getAttribute('role')).toBe('status');
  });
});

describe('categoryTree', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLElement => {
    const host = container('tree');
    search = start([
      categoryTree({ container: host, attribute: 'category', label: 'Categories', ...params }),
    ]);
    return host.querySelector('.xps-category-tree') as HTMLElement;
  };

  const values = (list: Element | null): string[] =>
    [...(list?.children ?? [])].map(
      (item) => item.querySelector('.xps-category-tree__value')?.textContent ?? ''
    );

  /** TH-7 (owner item 128): the tree used to empty itself — and hide — at zero hits. */
  it('keeps the selected category when the response carries no values at all', async () => {
    let answer: SearchResponse = RESPONSE;
    const host = container('tree');
    search = start(
      [categoryTree({ container: host, attribute: 'category', label: 'Categories' })],
      async () => answer
    );
    const root = host.querySelector('.xps-category-tree') as HTMLElement;
    await settled(search);

    answer = { ...RESPONSE, results: [], total: 0, totalPages: 0, facets: {} };
    search.actions.toggleFacet('category', 'espresso').search();
    await vi.waitFor(() => expect(search?.results?.total).toBe(0));

    expect(root.hidden).toBe(false);
    const items = [...root.querySelectorAll('.xps-category-tree__item')];
    expect(items).toHaveLength(1);
    expect(items[0]?.querySelector('.xps-category-tree__value')?.textContent).toBe('espresso');
    expect(items[0]?.querySelector('.xps-category-tree__count')?.textContent).toBe('0');
    expect(classesOf(items[0])).toEqual([
      'xps-category-tree__item',
      'xps-category-tree__item--selected',
    ]);
    // A real link, not the disabled span a count of zero usually gets: clicking it clears.
    const link = items[0]?.querySelector('.xps-category-tree__link') as HTMLAnchorElement;
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('aria-current')).toBe('true');

    link.click();
    await vi.waitFor(() => expect(search?.state.filters.facets).toEqual([]));
  });

  it('renders the nested lists of the markup contract', async () => {
    const root = mount();
    await settled(search!);
    expect(root.tagName).toBe('NAV');
    expect(classesOf(root)).toEqual(['xps', 'xps-category-tree']);
    expect(root.getAttribute('aria-label')).toBe('Categories');

    const title = root.querySelector('.xps-category-tree__title') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.textContent).toBe('Categories');

    const level0 = root.querySelector('.xps-category-tree__list--lvl0') as HTMLElement;
    expect(level0.tagName).toBe('UL');
    expect(values(level0)).toEqual(['Coffee', 'Tea', 'Accessories']);

    const coffee = level0.children[0] as HTMLElement;
    expect(classesOf(coffee)).toEqual([
      'xps-category-tree__item',
      'xps-category-tree__item--parent',
    ]);
    const level1 = coffee.querySelector('.xps-category-tree__list--lvl1') as HTMLElement;
    expect(values(level1)).toEqual(['Grinders', 'Machines']);
    const level2 = level1.querySelector('.xps-category-tree__list--lvl2') as HTMLElement;
    expect(values(level2)).toEqual(['Espresso', 'Filter']);

    // Counts and crawlable links on every enabled node.
    expect(coffee.querySelector('.xps-category-tree__count')?.textContent).toBe('42');
    const link = coffee.querySelector('a.xps-category-tree__link') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toContain('category=coffee');
  });

  it('renders a count of zero as a disabled span, not a link', async () => {
    const root = mount();
    await settled(search!);
    const accessories = root.querySelector('.xps-category-tree__list--lvl0')!.children[2] as HTMLElement;

    expect(classesOf(accessories)).toEqual([
      'xps-category-tree__item',
      'xps-category-tree__item--disabled',
    ]);
    const control = accessories.querySelector('.xps-category-tree__link') as HTMLElement;
    expect(control.tagName).toBe('SPAN');
    expect(control.getAttribute('aria-disabled')).toBe('true');
  });

  it('marks the whole open path selected with aria-current', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelector('a[data-xps-value="espresso"]') as HTMLAnchorElement).click();
    expect(search?.state.filters.facets[0]?.values).toEqual(['espresso']);

    await vi.waitFor(() =>
      expect(root.querySelectorAll('[aria-current="true"]').length).toBe(3)
    );
    expect(
      [...root.querySelectorAll('.xps-category-tree__item--selected')].map(
        (item) => item.querySelector('.xps-category-tree__value')?.textContent
      )
    ).toEqual(['Coffee', 'Machines', 'Espresso']);
  });

  it('replaces the selection instead of adding to it, and clears on a second click', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelector('a[data-xps-value="machines"]') as HTMLAnchorElement).click();
    expect(search?.state.filters.facets[0]?.values).toEqual(['machines']);

    (root.querySelector('a[data-xps-value="tea"]') as HTMLAnchorElement).click();
    expect(search?.state.filters.facets[0]?.values).toEqual(['tea']);

    // The second click on the open node only closes it once the response marked it selected.
    await vi.waitFor(() =>
      expect(root.querySelector('a[data-xps-value="tea"]')?.getAttribute('aria-current')).toBe('true')
    );
    (root.querySelector('a[data-xps-value="tea"]') as HTMLAnchorElement).click();
    expect(search?.state.filters.facets).toEqual([]);
  });

  it('caps each level at limit and hides itself when there is nothing to navigate', async () => {
    const root = mount({ limit: 1 });
    await settled(search!);
    expect(values(root.querySelector('.xps-category-tree__list--lvl0'))).toEqual(['Coffee']);
    expect(values(root.querySelector('.xps-category-tree__list--lvl1'))).toEqual(['Grinders']);

    const empty = container('tree-empty');
    const other = start([categoryTree({ container: empty, attribute: 'nothing' })]);
    await settled(other);
    expect((empty.querySelector('.xps-category-tree') as HTMLElement).hidden).toBe(true);
  });

  it('collapses from the title by default and keeps the state across a re-render', async () => {
    const root = mount();
    await settled(search!);
    const toggle = root.querySelector('.xps-category-tree__toggle') as HTMLButtonElement;
    const body = () => root.querySelector('.xps-category-tree__body') as HTMLElement;
    expect(toggle.getAttribute('aria-controls')).toBe(body().id);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(body().hidden).toBe(false);

    toggle.click();
    expect(
      (root.querySelector('.xps-category-tree__toggle') as HTMLElement).getAttribute('aria-expanded')
    ).toBe('false');
    expect(body().hidden).toBe(true);
    expect(search?.state.filters.facets).toEqual([]);

    // The tree is rebuilt on every response; the fold survives it.
    search?.actions.setQuery('espresso').search();
    await settled(search!);
    expect(body().hidden).toBe(true);
    expect(
      (root.querySelector('.xps-category-tree__toggle') as HTMLElement).getAttribute('aria-expanded')
    ).toBe('false');

    const plain = mount({ collapsible: false });
    await settled(search!);
    expect(plain.querySelector('.xps-category-tree__toggle')).toBeNull();
    expect(plain.querySelector('.xps-category-tree__title')?.textContent).toBe('Categories');
  });
});

describe('pagination', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLElement => {
    const host = container('pages');
    search = start([pagination({ container: host, padding: 2, ...params })]);
    return host.querySelector('.xps-pagination') as HTMLElement;
  };

  it('renders the contract markup with disabled ends as spans', async () => {
    const root = mount();
    await settled(search!);
    expect(root.tagName).toBe('NAV');
    expect(classesOf(root)).toEqual(['xps', 'xps-pagination']);
    expect(root.getAttribute('aria-label')).toBe('Search results pages');
    expect(root.querySelector('.xps-pagination__list')?.tagName).toBe('UL');

    const first = root.querySelector('.xps-pagination__item--first') as HTMLElement;
    expect(classesOf(first)).toContain('xps-pagination__item--disabled');
    const firstLink = first.querySelector('.xps-pagination__link') as HTMLElement;
    expect(firstLink.tagName).toBe('SPAN');
    expect(firstLink.getAttribute('aria-disabled')).toBe('true');
    expect(firstLink.querySelector('.xps-sr-only')?.textContent).toBe('First page');
    expect(firstLink.querySelector('[aria-hidden="true"]')).not.toBeNull();

    const current = root.querySelector('.xps-pagination__item--current') as HTMLElement;
    expect(classesOf(current)).toEqual([
      'xps-pagination__item',
      'xps-pagination__item--page',
      'xps-pagination__item--current',
    ]);
    expect(current.querySelector('a')?.getAttribute('aria-current')).toBe('page');
    expect(current.textContent).toBe('Page 1');

    // padding 2 on page 0 shows pages 1..5, an ellipsis and the last page.
    expect(root.querySelectorAll('.xps-pagination__item--page').length).toBe(6);
    expect(root.querySelectorAll('.xps-pagination__item--ellipsis').length).toBe(1);
    expect(root.querySelector('.xps-pagination__ellipsis')?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('.xps-pagination__item--last a .xps-sr-only')?.textContent).toBe(
      'Last page'
    );
  });

  it('gives every link the href urlFor produces', async () => {
    const host = container('pages');
    search = createSearch({
      index: 'i',
      routing: true,
      fetchFn: (async () =>
        new Response(JSON.stringify(RESPONSE), {
          status: 200,
          headers: { [API_VERSION_HEADER]: '1' },
        })) as unknown as typeof fetch,
      debounceMs: 0,
    });
    const widget = pagination({ container: host, padding: 2 });
    search.addWidgets([widget]);
    search.start();
    started.push(search);
    await settled(search);

    const root = host.querySelector('.xps-pagination') as HTMLElement;
    const links = [...root.querySelectorAll<HTMLAnchorElement>('.xps-pagination__item--page a')];
    for (const link of links) {
      const page = Number(link.dataset['xpsPage']);
      expect(link.getAttribute('href')).toBe(
        search.urlFor({ ...search.state, page })
      );
    }
    expect(links[1]?.getAttribute('href')).toContain('page=2');
  });

  it('refines on click instead of navigating', async () => {
    const root = mount();
    await settled(search!);
    const link = root.querySelectorAll('.xps-pagination__item--page a')[2] as HTMLAnchorElement;
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    link.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(search?.state.page).toBe(3);
  });

  it('hides itself when there is only one page', async () => {
    const root = mount();
    await settled(search!);
    expect(root.hidden).toBe(false);

    const host = container('pages-2');
    search = start([pagination({ container: host })], { ...RESPONSE, totalPages: 1, total: 3 });
    await settled(search);
    expect((host.querySelector('.xps-pagination') as HTMLElement).hidden).toBe(true);
  });
});

describe('resultStats', () => {
  it('renders the default text and the time element', async () => {
    const host = container('resultStats');
    search = start([resultStats({ container: host })]);
    const root = host.querySelector('.xps-result-stats') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-result-stats', 'xps-result-stats--empty']);
    expect(root.textContent).toBe('Type to search.');

    await settled(search);
    expect(classesOf(root)).toEqual(['xps', 'xps-result-stats']);
    const line = root.querySelector('.xps-result-stats__text') as HTMLElement;
    expect(line.tagName).toBe('SPAN');
    expect(text(line)).toBe('46 results (14 ms)');
    expect(text(root.querySelector('.xps-result-stats__total'))).toBe('46');
    expect(text(root.querySelector('.xps-result-stats__time'))).toBe('14 ms');

    // With a query the line names it, as the design does.
    search.actions.setQuery('espresso');
    await settled(search);
    expect(text(root.querySelector('.xps-result-stats__text'))).toBe(
      '46 results for “espresso” (14 ms)'
    );
    // The count lives in a live region on `results` only (spec 5.6).
    expect(root.getAttribute('role')).toBeNull();
    expect(root.getAttribute('aria-live')).toBeNull();
  });

  it('accepts a string textTemplate with placeholders, escaping the template and the values', async () => {
    const host = container('resultStats');
    search = start([
      resultStats({
        container: host,
        textTemplate: '<b>{total}</b> hits for "{query}" in {tookMs} ms - page {page} of {totalPages}',
      }),
    ]);
    search.actions.setQuery('espresso & <cream>');
    await settled(search);

    const line = host.querySelector('.xps-result-stats__text') as HTMLElement;
    expect(text(line)).toBe(
      '<b>46</b> hits for "espresso & <cream>" in 14 ms - page 1 of 9'
    );
    // Escaped, so neither the editor's template nor the visitor's query can inject markup — the
    // only element the template produces is the <strong> around {total}.
    expect(line.innerHTML).toContain(
      '&lt;b&gt;<strong class="xps-result-stats__total">46</strong>&lt;/b&gt;'
    );
    expect(line.querySelector('b')).toBeNull();
    expect(line.querySelector('.xps-result-stats__total')?.textContent).toBe('46');
  });

  it('leaves a template without {total} as plain text', async () => {
    const host = container('resultStats');
    search = start([resultStats({ container: host, textTemplate: 'Found something in {tookMs} ms' })]);
    await settled(search);

    const line = host.querySelector('.xps-result-stats__text') as HTMLElement;
    expect(line.innerHTML).toBe('Found something in 14 ms');
  });

  it('lets templates.text win over textTemplate', async () => {
    const host = container('resultStats');
    search = start([
      resultStats({
        container: host,
        textTemplate: '{total} from the string template',
        templates: { text: (data, tools) => tools.html`<b>${tools.formatNumber(data.total)}</b>` },
      }),
    ]);
    await settled(search);
    expect(host.querySelector('.xps-result-stats__text')?.innerHTML).toBe('<b>46</b>');
  });

  it('accepts templates.text', async () => {
    const host = container('resultStats');
    search = start([
      resultStats({
        container: host,
        templates: {
          text: (data, tools) => tools.html`<b>${tools.formatNumber(data.total)}</b>`,
        },
      }),
    ]);
    await settled(search);
    expect(host.querySelector('.xps-result-stats__text')?.innerHTML).toBe('<b>46</b>');
  });
});

describe('sortSelect', () => {
  const items = [
    { label: 'Relevance', value: 'relevance' },
    { label: 'Newest first', value: 'date_desc' },
  ];

  it('renders a labelled native select and refines on change', async () => {
    const host = container('sort');
    search = start([sortSelect({ container: host, items })]);
    const root = host.querySelector('.xps-sort-select') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-sort-select', 'xps-select']);
    const label = root.querySelector('label') as HTMLLabelElement;
    const select = root.querySelector('select') as HTMLSelectElement;
    expect(classesOf(label)).toEqual(['xps-select__label']);
    expect(classesOf(select)).toEqual(['xps-select__control']);
    // A native <select> in the wrapper that carries the design's own chevron.
    expect(classesOf(select.parentElement)).toEqual(['xps-select__field']);
    const chevron = root.querySelector('.xps-select__chevron') as SVGElement;
    expect(chevron.getAttribute('aria-hidden')).toBe('true');
    expect(chevron.getAttribute('stroke')).toBe('currentColor');
    expect(label.htmlFor).toBe(select.id);
    expect(select.name).toBe('sort');
    expect([...select.options].map((option) => option.value)).toEqual(['relevance', 'date_desc']);
    expect(select.value).toBe('relevance');

    await settled(search);
    select.value = 'date_desc';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    expect(search.state.sort).toBe('date_desc');
    await vi.waitFor(() => expect(select.value).toBe('date_desc'));
    expect(root.querySelector('select')).toBe(select);
  });
});

describe('clearFilters', () => {
  it('is disabled with nothing to clear and clears every refinement', async () => {
    const host = container('clear');
    search = start([clearFilters({ container: host })]);
    const root = host.querySelector('.xps-clear-filters') as HTMLElement;
    const button = root.querySelector('button') as HTMLButtonElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-clear-filters', 'xps-clear-filters--disabled']);
    expect(classesOf(button)).toEqual([
      'xps-button',
      'xps-button--link',
      'xps-clear-filters__button',
    ]);
    expect(button.textContent).toBe('Clear all');
    expect(button.type).toBe('button');
    expect(button.disabled).toBe(true);

    search.actions.toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(button.disabled).toBe(false));
    expect(classesOf(root)).toEqual(['xps', 'xps-clear-filters']);

    button.click();
    expect(search.state.filters.facets).toEqual([]);
    await vi.waitFor(() => expect(button.disabled).toBe(true));
    expect(root.querySelector('button')).toBe(button);
  });
});

describe('activeFilters', () => {
  /**
   * TH-7: the chips, the Clear all button and the range's own from/to all read the STATE, not the
   * response, so a refinement that returns nothing keeps every way back on screen.
   */
  it('keeps the chips, Clear all and the range values when the search returns nothing', async () => {
    const chips = container('chips');
    const clear = container('clear');
    const range = container('range');
    let answer: SearchResponse = RESPONSE;
    search = start(
      [
        activeFilters({ container: chips, attributeLabels: { contentType: 'Content type' } }),
        clearFilters({ container: clear }),
        rangeFilter({ container: range, attribute: 'price', label: 'Price', min: 0, max: 500 }),
      ],
      async () => answer
    );
    await settled(search);

    answer = { ...RESPONSE, results: [], total: 0, totalPages: 0, facets: {} };
    search.actions
      .toggleFacet('contentType', 'Recipe')
      .setNumericFilter('price', 'gte', 10)
      .setNumericFilter('price', 'lte', 20)
      .search();
    await vi.waitFor(() => expect(search?.results?.total).toBe(0));

    // One chip for the facet value, one per numeric bound.
    const labels = [...chips.querySelectorAll('.xps-chip__label')].map((chip) => text(chip));
    expect(labels).toHaveLength(3);
    expect(labels[0]).toContain('Recipe');
    expect(labels.join(' ')).toContain('10');
    expect(
      (clear.querySelector('.xps-clear-filters__button') as HTMLButtonElement).disabled
    ).toBe(false);
    const inputs = [...range.querySelectorAll<HTMLInputElement>('.xps-range-filter__input')];
    expect(inputs.map((input) => input.value)).toEqual(['10', '20']);
  });

  it('renders a removable chip per refinement', async () => {
    const host = container('chips');
    search = start([
      activeFilters({ container: host, attributeLabels: { contentType: 'Content type' } }),
    ]);
    const root = host.querySelector('.xps-active-filters') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-active-filters', 'xps-active-filters--empty']);
    const list = root.querySelector('.xps-active-filters__list') as HTMLElement;
    const title = root.querySelector('.xps-active-filters__title') as HTMLElement;
    expect(list.tagName).toBe('UL');
    expect(list.children.length).toBe(0);
    expect(list.getAttribute('aria-labelledby')).toBe(title.id);
    expect(classesOf(title)).toEqual(['xps-active-filters__title', 'xps-sr-only']);

    search.actions.toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(root.querySelectorAll('.xps-chip').length).toBe(1));
    expect(classesOf(root)).toEqual(['xps', 'xps-active-filters']);
    const chip = root.querySelector('.xps-chip') as HTMLElement;
    expect(chip.querySelector('.xps-chip__attribute')?.textContent).toBe('Content type');
    expect(chip.querySelector('.xps-chip__label')?.textContent).toBe('Content type Article');
    const remove = chip.querySelector('.xps-chip__remove') as HTMLButtonElement;
    expect(remove.getAttribute('aria-label')).toBe('Remove filter Content type: Article');

    remove.click();
    expect(search.state.filters.facets).toEqual([]);
    await vi.waitFor(() => expect(root.querySelectorAll('.xps-chip').length).toBe(0));
  });

  it('keeps the chips on one scrolling row with scroll: true', async () => {
    const host = container('chips-scroll');
    search = start([activeFilters({ container: host, scroll: true })]);
    const root = host.querySelector('.xps-active-filters') as HTMLElement;
    expect(classesOf(root)).toContain('xps-active-filters--scroll');

    search.actions.toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(root.querySelectorAll('.xps-chip').length).toBe(1));
    // The modifier is on the root and survives a re-render of the list.
    expect(classesOf(root)).toEqual([
      'xps',
      'xps-active-filters',
      'xps-active-filters--scroll',
    ]);
  });

  it('labels a numeric refinement with its operator', async () => {
    const host = container('chips');
    search = start([activeFilters({ container: host })]);
    search.actions.setNumericFilter('price', 'lte', 50).search();
    await vi.waitFor(() => expect(host.querySelectorAll('.xps-chip').length).toBe(1));
    expect(host.querySelector('.xps-chip__label')?.textContent).toBe('price lte 50');
  });
});

describe('toggleFilter', () => {
  it('renders one real checkbox and refines on change', async () => {
    const host = container('toggle');
    search = start([
      toggleFilter({ container: host, attribute: 'contentType', value: 'Article', label: 'Articles only' }),
    ]);
    const root = host.querySelector('.xps-toggle-filter') as HTMLElement;
    await settled(search);
    const checkbox = root.querySelector('input') as HTMLInputElement;
    expect(checkbox.type).toBe('checkbox');
    expect(checkbox.closest('label')?.className).toBe('xps-toggle-filter__label');
    expect(root.querySelector('.xps-toggle-filter__value')?.textContent).toBe('Articles only');
    expect(root.querySelector('.xps-toggle-filter__count')?.textContent).toBe('24');
    expect(checkbox.disabled).toBe(false);

    checkbox.click();
    expect(search.state.filters.facets[0]?.values).toEqual(['Article']);
    await vi.waitFor(() => expect(checkbox.checked).toBe(true));
    expect(root.querySelector('input')).toBe(checkbox);
  });

  it('disables itself when no document carries the value', async () => {
    const host = container('toggle');
    search = start([toggleFilter({ container: host, attribute: 'contentType', value: 'Event' })]);
    await settled(search);
    const root = host.querySelector('.xps-toggle-filter') as HTMLElement;
    expect(classesOf(root)).toContain('xps-toggle-filter--disabled');
    expect((root.querySelector('input') as HTMLInputElement).disabled).toBe(true);
  });
});

describe('the .xps-mount bootstrap', () => {
  it('mounts every first-party widget from data-xps-widget', () => {
    const config = JSON.stringify({ index: 'site-content', searchOnInitialLoad: false });
    const mounts: Array<[string, Record<string, unknown>]> = [
      ['searchBox', {}],
      ['results', {}],
      ['facetList', { attribute: 'contentType' }],
      ['pagination', {}],
      ['resultStats', {}],
      ['sortSelect', { items: [{ label: 'Relevance', value: 'relevance' }] }],
      ['clearFilters', {}],
      ['activeFilters', {}],
      ['toggleFilter', { attribute: 'contentType', value: 'Article' }],
    ];
    document.body.innerHTML = mounts
      .map(
        ([type, widgetConfig]) =>
          `<div class="xps-mount" data-xps-widget="${type}" data-xps-instance="mounted" data-xps-instance-config='${config}' data-xps-config='${JSON.stringify(widgetConfig)}'></div>`
      )
      .join('');

    const instances = mountAll(document, { widgets: DEFAULT_WIDGETS });
    expect(instances.length).toBe(1);
    const roots = [...document.querySelectorAll('.xps-mount > .xps')];
    expect(roots.length).toBe(9);
    expect(roots.map((root) => [...root.classList][1])).toEqual([
      'xps-search-box',
      'xps-results',
      'xps-facet-list',
      'xps-pagination',
      'xps-result-stats',
      'xps-sort-select',
      'xps-clear-filters',
      'xps-active-filters',
      'xps-toggle-filter',
    ]);
    instances[0]?.dispose();
  });
});

describe('two instances on one page', () => {
  it('do not interfere (spec 12)', async () => {
    const one = container('one');
    const oneStats = container('one-resultStats');
    const two = container('two');
    const first = start([searchBox({ container: one }), resultStats({ container: oneStats })]);
    const second = start([searchBox({ container: two })], { ...RESPONSE, total: 7 });
    await settled(first);
    await settled(second);

    const input = two.querySelector('input') as HTMLInputElement;
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(second.state.query).toBe('espresso');
    expect(first.state.query).toBe('');
    expect((one.querySelector('input') as HTMLInputElement).value).toBe('');
    expect(text(oneStats.querySelector('.xps-result-stats__text'))).toBe('46 results (14 ms)');
    expect(second.results?.total).toBe(7);
  });
});

describe('request parameters', () => {
  it('asks the server for the facet a facetList needs', async () => {
    const host = container('facet');
    search = start([facetList({ container: host, attribute: 'tags' })]);
    await settled(search);
    expect((calls[0]?.body as unknown as SearchRequest).facets).toEqual(['tags']);
  });
});

describe('widgetId (MARKUP.md rule 4)', () => {
  const el = (attrs: Record<string, string> = {}): HTMLElement => {
    const node = document.createElement('div');
    for (const [name, value] of Object.entries(attrs)) node.setAttribute(name, value);
    return node;
  };

  it('prefers data-xps-instance over the container id', () => {
    const node = el({ 'data-xps-instance': 'search-1', id: 'ignored' });
    expect(widgetId(node, 'wid-a', 'select')).toBe('xps-search-1-wid-a-select');
  });

  it('falls back to the container id, then to "default"', () => {
    expect(widgetId(el({ id: 'facet-brand' }), 'wid-b', 'label')).toBe(
      'xps-facet-brand-wid-b-label'
    );
    expect(widgetId(el(), 'wid-c', 'input')).toBe('xps-default-wid-c-input');
  });

  it('gives every part of one widget the same prefix, render after render', () => {
    const node = el({ 'data-xps-instance': 'search-2' });
    expect(widgetId(node, 'wid-d', 'label')).toBe('xps-search-2-wid-d-label');
    expect(widgetId(node, 'wid-d', 'select')).toBe('xps-search-2-wid-d-select');
    expect(widgetId(node, 'wid-d', 'label')).toBe('xps-search-2-wid-d-label');
  });

  it('suffixes the widget segment when the same widget is mounted twice in one instance', () => {
    const first = el({ 'data-xps-instance': 'search-3' });
    const second = el({ 'data-xps-instance': 'search-3' });
    const third = el({ 'data-xps-instance': 'search-3' });
    expect(widgetId(first, 'wid-e', 'select')).toBe('xps-search-3-wid-e-select');
    expect(widgetId(second, 'wid-e', 'select')).toBe('xps-search-3-wid-e-2-select');
    expect(widgetId(third, 'wid-e', 'select')).toBe('xps-search-3-wid-e-3-select');
  });
});

describe('rangeFilter', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLElement => {
    const host = container('range');
    search = start([
      rangeFilter({ container: host, attribute: 'price', label: 'Price', ...params }),
    ]);
    return host;
  };

  it('renders two sliders, two number inputs and the values line', async () => {
    const host = mount({ min: 0, max: 500, step: 5 });
    await settled(search!);
    const root = host.firstElementChild!;
    expect(classesOf(root)).toEqual(['xps', 'xps-range-filter']);
    const track = root.querySelector('.xps-range-filter__track')!;
    expect(track.getAttribute('role')).toBe('group');
    expect(track.getAttribute('aria-labelledby')).toBe(
      root.querySelector('.xps-range-filter__title')?.id
    );
    const inputs = [
      ...root.querySelectorAll<HTMLInputElement>(
        '.xps-range-filter__range, .xps-range-filter__input'
      ),
    ];
    expect(inputs.map((input) => input.type)).toEqual(['range', 'range', 'number', 'number']);
    expect(inputs.map((input) => input.value)).toEqual(['0', '500', '0', '500']);
    expect(inputs.every((input) => input.min === '0' && input.max === '500' && input.step === '5')).toBe(
      true
    );
    expect(inputs[0]?.getAttribute('aria-describedby')).toBe(
      root.querySelector('.xps-range-filter__values')?.id
    );
    expect(inputs[0]?.id).toMatch(/^xps-range-price(-\d+)?-range-min$/);
    expect(text(root.querySelector('.xps-range-filter__values'))).toBe('0 to 500');
    expect(root.querySelector('.xps-range-filter__unit')).toBeNull();
    // Every control has a real, associated label (spec 5.6).
    for (const input of inputs) {
      expect(root.querySelector('label[for="' + input.id + '"]')).not.toBeNull();
    }
  });

  it('mirrors the two halves of the control and filters on change', async () => {
    const host = mount({ min: 0, max: 500, step: 5 });
    await settled(search!);
    const root = host.firstElementChild!;
    const numberMax = root.querySelectorAll<HTMLInputElement>('.xps-range-filter__input')[1]!;
    numberMax.value = '300';
    numberMax.dispatchEvent(new Event('input', { bubbles: true }));
    expect(root.querySelector<HTMLInputElement>('.xps-range-filter__range--max')?.value).toBe('300');
    // The values line states the bounds of the control, not the pending selection.
    expect(text(root.querySelector('.xps-range-filter__values'))).toBe('0 to 500');
    expect(calls.length).toBe(1); // dragging does not search

    numberMax.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(calls.length).toBe(2));
    expect((calls[1]?.body as unknown as SearchRequest).filters?.numeric).toEqual([
      { attribute: 'price', operator: 'lte', value: 300 },
    ]);
  });

  it('puts the unit at the end of the input row', async () => {
    const host = mount({ min: 0, max: 500, unit: 'USD' });
    await settled(search!);
    const row = host.querySelector('.xps-range-filter__inputs') as HTMLElement;
    expect(row.lastElementChild?.className).toBe('xps-range-filter__unit');
    expect(row.lastElementChild?.textContent).toBe('USD');
  });

  it('keeps neither end past the other, and clamps to the bounds', async () => {
    const host = mount({ min: 0, max: 500 });
    await settled(search!);
    const root = host.firstElementChild!;
    const min = root.querySelector<HTMLInputElement>('.xps-range-filter__range--min')!;
    min.value = '900';
    min.dispatchEvent(new Event('input', { bubbles: true }));
    expect(min.value).toBe('500');
    const max = root.querySelector<HTMLInputElement>('.xps-range-filter__range--max')!;
    max.value = '-10';
    max.dispatchEvent(new Event('input', { bubbles: true }));
    expect(max.value).toBe('500');
  });

  it('publishes the selection as the two rail percentages, clamped to the bounds', async () => {
    const host = mount({ min: 0, max: 500, step: 5 });
    await settled(search!);
    const root = host.firstElementChild!;
    const track = root.querySelector<HTMLElement>('.xps-range-filter__track')!;
    const vars = (): [string, string] => [
      track.style.getPropertyValue('--xps-range-from'),
      track.style.getPropertyValue('--xps-range-to'),
    ];
    expect(vars()).toEqual(['0%', '100%']);

    const max = root.querySelector<HTMLInputElement>('.xps-range-filter__range--max')!;
    max.value = '300';
    max.dispatchEvent(new Event('input', { bubbles: true }));
    expect(vars()).toEqual(['0%', '60%']);

    // Out of bounds on either end stays inside 0%-100%.
    max.value = '900';
    max.dispatchEvent(new Event('input', { bubbles: true }));
    expect(vars()).toEqual(['0%', '100%']);
    const min = root.querySelector<HTMLInputElement>('.xps-range-filter__range--min')!;
    min.value = '-40';
    min.dispatchEvent(new Event('input', { bubbles: true }));
    expect(vars()).toEqual(['0%', '100%']);
  });

  it('keeps both overlaid sliders operable from the keyboard', async () => {
    const host = mount({ min: 0, max: 500, step: 5 });
    await settled(search!);
    const root = host.firstElementChild!;
    const track = root.querySelector<HTMLElement>('.xps-range-filter__track')!;
    const max = root.querySelector<HTMLInputElement>('.xps-range-filter__range--max')!;
    max.focus();
    expect(document.activeElement).toBe(max);
    // jsdom has no native range key handling, so step the value the way ArrowLeft does and fire
    // the events the UA fires: the widget must mirror it and commit on `change`.
    max.value = String(Number(max.value) - Number(max.step));
    max.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    max.dispatchEvent(new Event('input', { bubbles: true }));
    max.dispatchEvent(new Event('change', { bubbles: true }));
    expect(track.style.getPropertyValue('--xps-range-to')).toBe('99%');
    expect(root.querySelectorAll<HTMLInputElement>('.xps-range-filter__input')[1]?.value).toBe('495');
    await vi.waitFor(() => expect(calls.length).toBe(2));

    // Only the thumbs take a pointer, so the input on top cannot swallow the other one's drag.
    const shell = readFileSync(join(import.meta.dirname, '../../../../../themes/src/shell.css'), 'utf8');
    const rule = (selector: string): string =>
      shell.slice(shell.indexOf(selector), shell.indexOf('}', shell.indexOf(selector)));
    expect(rule('.xps-range-filter__range {')).toContain('pointer-events: none');
    for (const thumb of ['::-webkit-slider-thumb', '::-moz-range-thumb']) {
      expect(rule(`.xps-range-filter__range${thumb}`)).toContain('pointer-events: auto');
    }
  });

  it('renders disabled when it has no bounds to offer', async () => {
    const host = mount();
    await settled(search!);
    const root = host.firstElementChild!;
    expect(classesOf(root)).toContain('xps-range-filter--disabled');
    expect([...root.querySelectorAll<HTMLInputElement>('input')].every((i) => i.disabled)).toBe(
      true
    );
    expect(text(root.querySelector('.xps-range-filter__values'))).toBe(
      'No price range in these results.'
    );
    const track = root.querySelector<HTMLElement>('.xps-range-filter__track')!;
    expect(track.style.getPropertyValue('--xps-range-from')).toBe('0%');
    expect(track.style.getPropertyValue('--xps-range-to')).toBe('0%');
  });
});

describe('loadMore', () => {
  const CORPUS = ['a', 'b', 'c', 'd', 'e'];
  /** One page of a five-document corpus, so a second load is a different set of ids. */
  const paged = (request: SearchRequest): SearchResponse => {
    if (request.query === 'xyzzy') {
      return {
        ...RESPONSE,
        results: [],
        page: 1,
        pageSize: 2,
        total: 0,
        totalPages: 0,
        didYouMean: 'espresso',
      };
    }
    const at = request.page ?? 1;
    const ids = CORPUS.slice((at - 1) * 2, at * 2);
    return {
      ...RESPONSE,
      results: ids.map((id) => ({ id, attributes: { title: id.toUpperCase(), url: '/' + id } })),
      page: at,
      pageSize: 2,
      total: CORPUS.length,
      totalPages: 3,
    };
  };

  const mount = (): HTMLElement => {
    const host = container('more');
    const fetchFn = (async (url: string, init: RequestInit) => {
      const body = JSON.parse(String(init.body)) as SearchRequest;
      calls.push({ url: String(url), body: body as unknown as Record<string, unknown> });
      return new Response(JSON.stringify(paged(body)), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({
      index: 'site-content',
      fetchFn,
      debounceMs: 0,
      initialState: { pageSize: 2 },
    });
    started.push(search);
    search.addWidgets([loadMore({ container: host })]);
    search.start();
    return host;
  };

  const items = (host: HTMLElement): string[] =>
    [...host.querySelectorAll('.xps-load-more__item .xps-result__link')].map(
      (link) => link.textContent ?? ''
    );

  it('renders the results item template, a live region, a sentinel and a button', async () => {
    const host = mount();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    const root = host.firstElementChild!;
    expect(classesOf(root)).toEqual(['xps', 'xps-load-more']);
    expect(root.querySelector('.xps-load-more__list')?.tagName).toBe('OL');
    expect(root.querySelector('.xps-load-more__item article')?.className).toBe('xps-result');
    const status = root.querySelector('.xps-load-more__status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(classesOf(status)).toContain('xps-sr-only');
    expect(text(status)).toBe('Showing 2 of 5 results');
    expect(root.querySelector('.xps-load-more__sentinel')?.getAttribute('aria-hidden')).toBe('true');
    const button = root.querySelector<HTMLButtonElement>('.xps-load-more__load-more')!;
    expect(button.type).toBe('button');
    expect(button.disabled).toBe(false);
    expect(button.textContent).toBe('Load more results');
  });

  it('appends the next page to the same <ol> instead of rebuilding it', async () => {
    const host = mount();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    const list = host.querySelector('.xps-load-more__list')!;
    const first = list.firstElementChild;

    host.querySelector<HTMLButtonElement>('.xps-load-more__load-more')!.click();
    await vi.waitFor(() => expect(items(host).length).toBe(4));
    expect(items(host)).toEqual(['A', 'B', 'C', 'D']);
    // The same element, still first: appending is what keeps scroll position and focus.
    expect(host.querySelector('.xps-load-more__list')).toBe(list);
    expect(list.firstElementChild).toBe(first);
    expect(text(host.querySelector('.xps-load-more__status'))).toBe('Showing 4 of 5 results');
  });

  it('disables the button and says so when everything is loaded', async () => {
    const host = mount();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    const button = host.querySelector<HTMLButtonElement>('.xps-load-more__load-more')!;
    button.click();
    await vi.waitFor(() => expect(items(host).length).toBe(4));
    button.click();
    await vi.waitFor(() => expect(items(host).length).toBe(5));
    expect(classesOf(host.firstElementChild)).toContain('xps-load-more--exhausted');
    expect(button.disabled).toBe(true);
    expect(button.textContent).toBe('No more results');
    expect(text(host.querySelector('.xps-load-more__status'))).toBe('Showing all 5 results');
  });

  it('rebuilds the list when the search changes', async () => {
    const host = mount();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    host.querySelector<HTMLButtonElement>('.xps-load-more__load-more')!.click();
    await vi.waitFor(() => expect(items(host).length).toBe(4));

    search!.actions.setQuery('espresso').setPage(1).search();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    expect(items(host)).toEqual(['A', 'B']);
  });

  /**
   * TH-6: under 1024px MB-1's mount-time swap puts `loadMore` where `results` would be, so a
   * fruitless query has to show the same empty state here — the control alone said nothing.
   */
  it('renders the shared empty state and hides the control when nothing was found', async () => {
    const host = mount();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    const button = host.querySelector<HTMLButtonElement>('.xps-load-more__load-more')!;

    search!.actions.setQuery('xyzzy').setPage(1).search();
    await vi.waitFor(() => expect(host.querySelector('.xps-results__empty')).not.toBeNull());

    const empty = host.querySelector('.xps-results__empty')!;
    expect(text(empty.querySelector('.xps-results__empty-title'))).toBe('No results for “xyzzy”');
    expect(empty.querySelector('.xps-results__empty-icon')).not.toBeNull();
    expect(items(host)).toEqual([]);
    expect(button.hidden).toBe(true);

    // The recovery offers are live here too, through the widget's own delegated handler.
    host.querySelector<HTMLButtonElement>('.xps-results__correction')!.click();
    await vi.waitFor(() => expect(items(host).length).toBe(2));
    expect(search?.state.query).toBe('espresso');
    expect(host.querySelector('.xps-results__empty')).toBeNull();
    expect(button.hidden).toBe(false);
  });

  /**
   * TH-7: the same counted empty state the desktop `results` widget renders — MB-1 swaps this
   * widget in under 1024px, and a filtered dead end has to offer the same way out there.
   */
  it('counts the empty state from an unfiltered probe in Load more mode too', async () => {
    const host = container('more');
    const fetchFn = (async (url: string, init: RequestInit) => {
      const body = JSON.parse(String(init.body)) as Record<string, unknown>;
      calls.push({ url: String(url), body });
      const answer =
        body['probe'] === true
          ? { ...RESPONSE, results: [], total: 7, totalPages: 1 }
          : { ...RESPONSE, results: [], total: 0, totalPages: 0 };
      return new Response(JSON.stringify(answer), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({ index: 'site-content', fetchFn, debounceMs: 0 });
    started.push(search);
    search.addWidgets([loadMore({ container: host })]);
    search.start();

    search.actions.setQuery('xyzzy').toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(host.querySelector('.xps-results__clear')).not.toBeNull());
    await vi.waitFor(() =>
      expect(host.querySelector('.xps-results__clear')?.textContent).toBe(
        'Clear filters and show 7 results'
      )
    );
    expect(text(host.querySelector('.xps-results__empty'))).toContain(
      'There are 7 results without them.'
    );
    // The probe is one unfiltered, flagged request; it left the widget's own state alone.
    const probes = calls.filter((call) => call.body['probe'] === true);
    expect(probes).toHaveLength(1);
    expect(probes[0]?.body['filters']).toBeUndefined();
    expect(search.state.filters.facets.map((facet) => facet.attribute)).toEqual(['contentType']);

    // And the button clears them, through the same delegated handler.
    host.querySelector<HTMLButtonElement>('.xps-results__clear')!.click();
    await vi.waitFor(() => expect(search?.state.filters.facets).toEqual([]));
  });
});

describe('suggestions', () => {
  const SUGGESTIONS: Suggestion[] = [
    { text: 'espresso machine' },
    { text: 'espresso grinder' },
    {
      text: 'Choosing an espresso machine',
      url: '/blog/choosing',
      result: { id: 'doc-1', attributes: { contentType: 'Article' } },
    },
  ];

  const mount = (
    params: Record<string, unknown> = {},
    answers: Suggestion[] = SUGGESTIONS
  ): HTMLElement => {
    const host = container('suggest');
    const fetchFn = (async (url: string, init: RequestInit) => {
      calls.push({
        url: String(url),
        body: JSON.parse(String(init.body)) as Record<string, unknown>,
      });
      const body = String(url).endsWith(SUGGEST_ROUTE) ? { suggestions: answers } : RESPONSE;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({ index: 'site-content', fetchFn, debounceMs: 0 });
    started.push(search);
    search.addWidgets([suggestions({ container: host, debounceMs: 0, ...params })]);
    search.start();
    return host;
  };

  const type = async (host: HTMLElement, value: string): Promise<HTMLInputElement> => {
    const input = host.querySelector<HTMLInputElement>('.xps-suggestions__input')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.waitFor(() => expect(host.querySelector('.xps-suggestions--open')).not.toBeNull());
    return input;
  };

  const key = (input: HTMLInputElement, name: string): void => {
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })
    );
  };

  it('renders a closed combobox before anything is typed', async () => {
    const host = mount();
    await settled(search!);
    const root = host.firstElementChild!;
    expect(classesOf(root)).toEqual(['xps', 'xps-suggestions']);
    const input = root.querySelector<HTMLInputElement>('.xps-suggestions__input')!;
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    const prefix = input.id.replace(/-input$/, '');
    expect(prefix).toMatch(/^xps-suggest-suggestions(-\d+)?$/);
    expect(input.getAttribute('aria-controls')).toBe(prefix + '-listbox');
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);
    // The listbox exists even when closed, so aria-controls never dangles.
    expect(root.querySelector('.xps-suggestions__list')?.getAttribute('role')).toBe('listbox');
    expect(root.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    expect(root.querySelector<HTMLElement>('.xps-suggestions__reset')?.hidden).toBe(true);
    expect(root.querySelector('label[for="' + input.id + '"]')).not.toBeNull();
  });

  it('groups query suggestions and documents, numbering the option ids in visual order', async () => {
    const host = mount();
    await settled(search!);
    const input = await type(host, 'esp');
    expect(input.getAttribute('aria-expanded')).toBe('true');

    const groups = [...host.querySelectorAll('.xps-suggestions__group')];
    expect(groups.map((group) => group.getAttribute('role'))).toEqual(['group', 'group']);
    expect(
      groups.map((group) => text(group.querySelector('.xps-suggestions__group-title')))
    ).toEqual(['Suggestions', 'Pages']);
    const prefix = input.id.replace(/-input$/, '');
    const options = [...host.querySelectorAll('[role="option"]')];
    expect(options.map((option) => option.id)).toEqual([
      prefix + '-option-0',
      prefix + '-option-1',
      prefix + '-option-2',
    ]);
    expect(options.every((option) => option.getAttribute('aria-selected') === 'false')).toBe(true);
    expect(options[0]?.querySelector('mark')?.className).toBe('xps-highlight');
    expect(text(options[2]?.querySelector('.xps-suggestions__option-meta'))).toBe('Article');
  });

  it('moves aria-activedescendant with the arrow keys and never moves DOM focus', async () => {
    const host = mount();
    await settled(search!);
    const input = await type(host, 'esp');
    input.focus();

    const option = (at: number): string => input.id.replace(/-input$/, '') + '-option-' + at;
    key(input, 'ArrowDown');
    expect(input.getAttribute('aria-activedescendant')).toBe(option(0));
    expect(document.activeElement).toBe(input);
    expect(
      host.querySelector('.xps-suggestions__option--active')?.getAttribute('aria-selected')
    ).toBe('true');

    key(input, 'End');
    expect(input.getAttribute('aria-activedescendant')).toBe(option(2));
    key(input, 'Home');
    expect(input.getAttribute('aria-activedescendant')).toBe(option(0));
    key(input, 'ArrowUp');
    expect(input.getAttribute('aria-activedescendant')).toBe(option(2));

    key(input, 'Escape');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
  });

  it('searches for a picked query suggestion', async () => {
    const host = mount();
    await settled(search!);
    const input = await type(host, 'esp');
    key(input, 'ArrowDown');
    key(input, 'Enter');
    await vi.waitFor(() => expect(search!.state.query).toBe('espresso machine'));
    expect(input.value).toBe('espresso machine');
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
  });

  it('navigates to a picked document suggestion', async () => {
    const assign = vi.fn();
    const host = mount({ windowRef: { location: { assign } } as unknown as Window });
    await settled(search!);
    await type(host, 'esp');
    host.querySelectorAll<HTMLElement>('[role="option"]')[2]?.click();
    expect(assign).toHaveBeenCalledWith('/blog/choosing');
  });

  it('offers "see all" and submits to the results page when one is configured', async () => {
    const assign = vi.fn();
    const host = mount({
      resultsUrl: '/search',
      windowRef: { location: { assign } } as unknown as Window,
    });
    await settled(search!);
    const input = await type(host, 'esp');
    const seeAll = host.querySelector<HTMLAnchorElement>('.xps-suggestions__see-all')!;
    expect(seeAll.getAttribute('href')).toBe(location.origin + '/search?q=esp');
    expect(host.querySelector('form')?.getAttribute('action')).toBe('/search');

    key(input, 'Enter');
    expect(assign).toHaveBeenCalledWith(location.origin + '/search?q=esp');
  });

  it('prefixes the footer with decorative keyboard hints', async () => {
    const host = mount({ resultsUrl: '/search' });
    await settled(search!);
    await type(host, 'esp');

    const footer = host.querySelector('.xps-suggestions__footer')!;
    const hints = footer.firstElementChild!;
    expect(classesOf(hints)).toEqual(['xps-suggestions__hints']);
    // Decoration only: the combobox roles already convey the keyboard model.
    expect(hints.getAttribute('aria-hidden')).toBe('true');
    expect([...hints.querySelectorAll('kbd')].map((kbd) => kbd.className)).toEqual(
      Array(4).fill('xps-suggestions__key')
    );
    expect(footer.lastElementChild?.className).toBe('xps-suggestions__see-all');
  });

  it('says so when there is nothing to suggest, and the reset button clears', async () => {
    const host = mount({}, []);
    await settled(search!);
    const input = host.querySelector<HTMLInputElement>('.xps-suggestions__input')!;
    input.value = 'xyzzy';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.waitFor(() => expect(host.querySelector('.xps-suggestions__empty')).not.toBeNull());
    const empty = host.querySelector('.xps-suggestions__empty')!;
    expect(empty.getAttribute('role')).toBe('status');
    expect(text(empty)).toBe('No suggestions for “xyzzy”.');
    expect(host.querySelector<HTMLElement>('.xps-suggestions__reset')?.hidden).toBe(false);

    host
      .querySelector('form')
      ?.dispatchEvent(new Event('reset', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(input.value).toBe(''));
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
  });

  /**
   * SG-1: recents are composed into the panel at the widget layer — the behaviour's transport and
   * state machine are untouched — so every one of these is a panel-level assertion.
   */
  describe('recent searches', () => {
    const seed = (...queries: string[]): void =>
      localStorage.setItem('xps-recent:site-content', JSON.stringify(queries));

    const focus = async (host: HTMLElement): Promise<HTMLInputElement> => {
      const input = host.querySelector<HTMLInputElement>('.xps-suggestions__input')!;
      input.dispatchEvent(new FocusEvent('focus', { bubbles: false }));
      return input;
    };

    it('opens on the recents alone when an empty field is focused', async () => {
      seed('espresso machine', 'grinder');
      const host = mount();
      await settled(search!);
      await focus(host);

      const groups = [...host.querySelectorAll('.xps-suggestions__group')];
      expect(groups).toHaveLength(1);
      // The recents' title row is a sibling of the listbox (it carries the Clear button), and the
      // group inside points at it.
      const title = host.querySelector('.xps-suggestions__group-header .xps-suggestions__group-title')!;
      expect(text(title)).toBe('Recent searches');
      expect(groups[0]?.getAttribute('aria-labelledby')).toBe(title.id);
      expect(title.closest('.xps-suggestions__list')).toBeNull();
      expect([...host.querySelectorAll('[role="option"]')].map((o) => text(o))).toEqual([
        'espresso machine',
        'grinder',
      ]);
      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(false);
      // Nothing was asked of the server: the list came out of the browser.
      expect(calls.filter((call) => call.url.endsWith(SUGGEST_ROUTE))).toHaveLength(0);
    });

    it('stays closed on focus with the feature off', async () => {
      seed('espresso machine');
      const host = mount({ recentSearches: false });
      await settled(search!);
      await focus(host);

      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    });

    it('stays closed on focus when nothing was ever searched', async () => {
      const host = mount();
      await settled(search!);
      await focus(host);

      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    });

    it(`is the first group, prefix-filtered, above the server own`, async () => {
      seed('espresso machine', 'grinder');
      const host = mount();
      await settled(search!);
      await focus(host);
      const input = await type(host, 'esp');
      await vi.waitFor(() =>
        expect(host.querySelectorAll('.xps-suggestions__group')).toHaveLength(3)
      );

      const groups = [...host.querySelectorAll('.xps-suggestions__group')];
      expect(
        [...host.querySelectorAll('.xps-suggestions__group-title')].map((title) => text(title))
      ).toEqual(['Recent searches', 'Suggestions', 'Pages']);
      // "grinder" does not start with "esp", so it is not offered.
      expect(text(groups[0])).toContain('espresso machine');
      expect(text(groups[0])).not.toContain('grinder');
      // Ids stay in visual order, recents included.
      const prefix = input.id.replace(/-input$/, '');
      expect([...host.querySelectorAll('[role="option"]')].map((option) => option.id)).toEqual([
        prefix + '-option-0',
        prefix + '-option-1',
        prefix + '-option-2',
        prefix + '-option-3',
      ]);
    });

    it('walks the whole merged list with the arrow keys', async () => {
      seed('espresso beans');
      const host = mount();
      await settled(search!);
      await focus(host);
      const input = await type(host, 'esp');
      await vi.waitFor(() => expect(host.querySelectorAll('[role="option"]')).toHaveLength(4));

      // Down into the recents group first, then on into the server's own suggestions.
      key(input, 'ArrowDown');
      const active = (): Element | null => host.querySelector('.xps-suggestions__option--active');
      expect(text(active())).toBe('espresso beans');
      expect(active()?.closest('.xps-suggestions__group')?.getAttribute('aria-labelledby')).toContain(
        'group-recent'
      );
      key(input, 'ArrowDown');
      expect(text(active())).toBe('espresso machine');
      key(input, 'ArrowUp');
      expect(text(active())).toBe('espresso beans');
      expect(input.getAttribute('aria-activedescendant')).toBe(active()?.id);
    });

    it('records a submitted query and a picked query suggestion, but not a picked page', async () => {
      const host = mount();
      await settled(search!);
      const input = await type(host, 'esp');

      // The document suggestion navigates to a page; nobody searched for its title.
      const options = [...host.querySelectorAll<HTMLElement>('[role="option"]')];
      options[2]?.click();
      expect(localStorage.getItem('xps-recent:site-content')).toBeNull();

      await type(host, 'esp');
      [...host.querySelectorAll<HTMLElement>('[role="option"]')][1]?.click();
      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual([
        'espresso grinder',
      ]);

      input.value = 'latte';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      host.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual([
        'latte',
        'espresso grinder',
      ]);

      // The keyboard path: the combobox preventDefaults Enter, so the form's submit event never
      // fires for a real keystroke — recording must happen on the combobox's own submit route.
      await type(host, 'mocha');
      key(input, 'Enter');
      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual([
        'mocha',
        'latte',
        'espresso grinder',
      ]);
      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    });

    it('searches for a recent when it is picked, and remembers it again', async () => {
      seed('grinder');
      const host = mount();
      await settled(search!);
      await focus(host);

      const before = calls.length;
      host.querySelector<HTMLElement>('[role="option"]')?.click();
      await vi.waitFor(() => expect(calls.length).toBeGreaterThan(before));
      expect(search?.state.query).toBe('grinder');
      expect(host.querySelector<HTMLInputElement>('.xps-suggestions__input')?.value).toBe('grinder');
      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual(['grinder']);
    });

    it(`empties the store from the group Clear control`, async () => {
      seed('espresso machine', 'grinder');
      const host = mount();
      await settled(search!);
      await focus(host);

      const clear = host.querySelector<HTMLButtonElement>('.xps-suggestions__group-clear')!;
      expect(clear.type).toBe('button');
      expect(clear.getAttribute('aria-label')).toBe('Clear recent searches');
      // Outside the element the group is named by, so the name stays "Recent searches".
      expect(clear.closest('.xps-suggestions__group-title')).toBeNull();

      clear.click();
      expect(localStorage.getItem('xps-recent:site-content')).toBeNull();
      expect(host.querySelector('.xps-suggestions__group')).toBeNull();
      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    });

    /** TH-6: the row's own X, and the keyboard half of it. */
    it('drops one entry from its row control, without closing the panel', async () => {
      seed('espresso machine', 'grinder');
      const host = mount();
      await settled(search!);
      await focus(host);

      const remove = host.querySelector<HTMLElement>('.xps-suggestions__option-remove')!;
      expect(remove.dataset['xpsRecentRemove']).toBe('espresso machine');
      expect(remove.title).toBe('Remove from recent searches');
      // Not a focusable control, and not inside the option: a listbox owns options and groups only,
      // and a focusable descendant of an option is swallowed by its accessible name (axe).
      expect(remove.tagName).toBe('SPAN');
      expect(remove.getAttribute('aria-hidden')).toBe('true');
      expect(remove.closest('[role="option"]')).toBeNull();

      const before = calls.length;
      remove.click();
      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual(['grinder']);
      expect([...host.querySelectorAll('[role="option"]')].map((option) => text(option))).toEqual([
        'grinder',
      ]);
      expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(false);
      // Removing an entry is not picking it: nothing was searched for.
      expect(calls.length).toBe(before);
      expect(search?.state.query).toBe('');
    });

    it('removes the active recent row with the Delete key', async () => {
      seed('espresso machine', 'grinder');
      const host = mount();
      await settled(search!);
      const input = await focus(host);

      key(input, 'ArrowDown');
      expect(text(host.querySelector('.xps-suggestions__option--active'))).toBe('espresso machine');
      key(input, 'Delete');

      expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual(['grinder']);
      expect([...host.querySelectorAll('[role="option"]')].map((option) => text(option))).toEqual([
        'grinder',
      ]);
      expect(host.querySelector('.xps-suggestions__option--active')).toBeNull();
    });
  });
});

describe('searchBox with integrated suggestions', () => {
  const SUGGESTIONS: Suggestion[] = [
    { text: 'espresso machine' },
    {
      text: 'Choosing an espresso machine',
      url: '/blog/choosing',
      result: { id: 'doc-1', attributes: { contentType: 'Article' } },
    },
  ];

  /** One instance carrying whichever widgets the test wants, answering `/suggest` for real. */
  const startSuggesting = (build: (host: HTMLElement) => Widget, hostId: string): HTMLElement => {
    const host = container(hostId);
    const fetchFn = (async (url: string, init: RequestInit) => {
      calls.push({
        url: String(url),
        body: JSON.parse(String(init.body)) as Record<string, unknown>,
      });
      const body = String(url).endsWith(SUGGEST_ROUTE) ? { suggestions: SUGGESTIONS } : RESPONSE;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }) as unknown as typeof fetch;
    search = createSearch({ index: 'site-content', fetchFn, debounceMs: 0 });
    started.push(search);
    search.addWidgets([build(host)]);
    search.start();
    return host;
  };

  const mount = (params: Record<string, unknown> = {}): HTMLElement =>
    startSuggesting(
      (host) => searchBox({ container: host, suggestions: { debounceMs: 0 }, ...params }),
      'search'
    );

  const type = async (host: HTMLElement, value: string): Promise<HTMLInputElement> => {
    const input = host.querySelector<HTMLInputElement>('input')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.waitFor(() => expect(host.querySelector('[role="option"]')).not.toBeNull());
    return input;
  };

  it('renders the decorative magnifier icon, with or without suggestions', () => {
    const plain = container('plain');
    search = start([searchBox({ container: plain })]);
    for (const host of [plain, mount()]) {
      const icon = host.querySelector('.xps-search-box__icon') as SVGElement;
      expect(icon.tagName.toLowerCase()).toBe('svg');
      expect(icon.getAttribute('aria-hidden')).toBe('true');
      expect(icon.getAttribute('focusable')).toBe('false');
      expect(icon.getAttribute('stroke')).toBe('currentColor');
      // Inside the field, before the input.
      expect(icon.parentElement?.className).toBe('xps-search-box__field');
      expect(icon.nextElementSibling?.className).toBe('xps-search-box__input');
    }
  });

  it('turns its own input into a combobox, and leaves it alone when the option is off', () => {
    const off = container('off');
    search = start([searchBox({ container: off })]);
    const plain = off.querySelector('input')!;
    expect(plain.hasAttribute('role')).toBe(false);
    expect(plain.hasAttribute('aria-expanded')).toBe(false);
    expect(plain.hasAttribute('aria-controls')).toBe(false);
    expect(off.querySelector('.xps-suggestions__panel')).toBeNull();

    const host = mount();
    const input = host.querySelector('input')!;
    const prefix = input.id.replace(/-input$/, '');
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.getAttribute('aria-controls')).toBe(prefix + '-listbox');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);
    // The listbox exists even when closed, so aria-controls never dangles.
    expect(host.querySelector('.xps-suggestions__list')?.id).toBe(prefix + '-listbox');
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
    // The panel hangs under the field, inside the search box's own form.
    expect(host.querySelector('.xps-suggestions__panel')?.parentElement?.className).toBe(
      'xps xps-search-box'
    );
  });

  it('moves aria-activedescendant with the arrows and searches for the picked suggestion', async () => {
    const host = mount();
    await settled(search!);
    const input = await type(host, 'esp');
    input.focus();
    expect(input.getAttribute('aria-expanded')).toBe('true');

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(input.getAttribute('aria-activedescendant')).toBe(
      input.id.replace(/-input$/, '') + '-option-0'
    );
    expect(document.activeElement).toBe(input);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await vi.waitFor(() => expect(search!.state.query).toBe('espresso machine'));
    expect(input.value).toBe('espresso machine');
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
  });

  /** SG-1: the same shared module drives both consumers, so the box gets the same third group. */
  it('shows and records recent searches, and drops them with the opt-out', async () => {
    localStorage.setItem('xps-recent:site-content', JSON.stringify(['grinder']));
    const host = mount();
    await settled(search!);
    const input = host.querySelector<HTMLInputElement>('input')!;
    input.dispatchEvent(new FocusEvent('focus'));

    expect(text(host.querySelector('.xps-suggestions__group-title'))).toBe('Recent searches');
    expect(host.querySelector('.xps-suggestions__group-clear')).not.toBeNull();
    expect(host.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(false);

    // A submitted query is remembered, most recent first.
    input.value = 'latte';
    host.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(search?.state.query).toBe('latte'));
    expect(JSON.parse(localStorage.getItem('xps-recent:site-content')!)).toEqual([
      'latte',
      'grinder',
    ]);

    const off = startSuggesting(
      (element) =>
        searchBox({
          container: element,
          suggestions: { debounceMs: 0, recentSearches: false },
        }),
      'search-no-recents'
    );
    off.querySelector<HTMLInputElement>('input')?.dispatchEvent(new FocusEvent('focus'));
    expect(off.querySelector<HTMLElement>('.xps-suggestions__panel')?.hidden).toBe(true);
  });

  it('still follows a redirect rule when Enter submits with no active option', async () => {
    const assigned: string[] = [];
    const host = container('redirect');
    search = start(
      [
        searchBox({
          container: host,
          suggestions: { debounceMs: 0 },
          windowRef: {
            location: { assign: (url: string) => assigned.push(url) },
          } as unknown as Window,
        }),
      ],
      { ...RESPONSE, redirect: { url: '/support', rule: 'Support redirect' } }
    );
    const input = host.querySelector('input')!;
    await settled(search);

    input.value = 'help';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await vi.waitFor(() => expect(assigned).toEqual(['/support']));
  });

  it('renders the same panel as the standalone widget, "see all" aside', async () => {
    const box = mount();
    const standalone = startSuggesting(
      (host) => suggestions({ container: host, debounceMs: 0, resultsUrl: '/search' }),
      'standalone'
    );
    await type(box, 'esp');
    await type(standalone, 'esp');

    /**
     * The two differ only in the id prefix MARKUP.md rule 4 gives them and in the "see all" link,
     * which an in-place search has nowhere to point at. The keyboard hints are in both.
     */
    const panel = (host: HTMLElement): string =>
      host
        .querySelector('.xps-suggestions__panel')!
        .innerHTML.split(host.querySelector('input')!.id.replace(/-input$/, ''))
        .join('xps-id')
        .replace(/<a class="xps-suggestions__see-all"[\s\S]*?<\/a>/, '');

    expect(panel(box)).toBe(panel(standalone));
    expect(panel(box)).toContain('xps-suggestions__option-meta');
    expect(panel(box)).toContain('xps-suggestions__hints');
  });

  it('keeps the keyboard hints, without a "see all" link that leads nowhere', async () => {
    const box = mount();
    const inPlace = startSuggesting(
      (host) => suggestions({ container: host, debounceMs: 0 }),
      'standalone'
    );
    await type(box, 'esp');
    await type(inPlace, 'esp');

    const footer = box.querySelector('.xps-suggestions__footer')!;
    expect(footer.children.length).toBe(1);
    expect(classesOf(footer.firstElementChild)).toEqual(['xps-suggestions__hints']);
    expect(footer.firstElementChild?.getAttribute('aria-hidden')).toBe('true');
    expect([...footer.querySelectorAll('kbd')].map((kbd) => kbd.className)).toEqual(
      Array(4).fill('xps-suggestions__key')
    );
    expect(box.querySelector('.xps-suggestions__see-all')).toBeNull();

    // The standalone widget without a results page keeps its footerless panel, as before.
    expect(inPlace.querySelector('.xps-suggestions__footer')).toBeNull();
    expect(inPlace.querySelector('.xps-suggestions__hints')).toBeNull();
  });
});

describe('the samples in docs/guides/widget-reference.md', () => {
  it('mount and render exactly as written', async () => {
    const price = container('filter-price');
    const list = container('search-results');
    const suggest = container('search-suggest');
    const tree = container('facet-category');
    const box = container('search-box');
    search = start([
      searchBox({
        container: '#search-box',
        placeholder: 'Search…',
        label: 'Search this site',
        showLabel: false,
        showReset: true,
        showSubmit: false,
        autofocus: false,
        followRedirects: true,
        queryHook: (query: string, apply: (value: string) => void) => apply(query.trim()),
        suggestions: { debounceMs: 150, minQueryLength: 1, limit: 5 },
      }),
      rangeFilter({ container: '#filter-price', attribute: 'price', label: 'Price', min: 0, max: 500, step: 5 }),
      categoryTree({ container: '#facet-category', attribute: 'category', label: 'Categories', limit: 10 }),
      loadMore({ container: '#search-results', autoLoad: true }),
      suggestions({
        container: '#search-suggest',
        resultsUrl: '/search',
        debounceMs: 150,
        minQueryLength: 1,
        limit: 5,
      }),
    ]);
    await settled(search);
    expect(price.querySelector('.xps-range-filter')).not.toBeNull();
    expect(list.querySelectorAll('.xps-load-more__item').length).toBe(3);
    expect(suggest.querySelector('.xps-suggestions__input')).not.toBeNull();
    expect(tree.querySelector('.xps-category-tree__list--lvl2')).not.toBeNull();
    expect(box.querySelector('input')?.getAttribute('role')).toBe('combobox');
  });

  it('clear the filters from a custom empty template that reuses the shipped button class', async () => {
    const host = container('search-results');
    search = start(
      [
        results({
          container: '#search-results',
          templates: {
            empty: ({ query, hasRefinements, unfilteredCount }, { html: markup }) =>
              hasRefinements
                ? markup`<p>Nothing matched “${query}” with these filters.</p>
               <button type="button" class="xps-button xps-button--primary xps-results__clear">${
                 unfilteredCount === undefined ? 'Start over' : `Start over (${unfilteredCount})`
               }</button>`
                : markup`<p>Nothing matched “${query}”.</p>`,
          },
        }),
      ],
      async (body) =>
        body['probe'] === true ? { ...RESPONSE, total: 7 } : { ...RESPONSE, results: [], total: 0, totalPages: 0 }
    );
    search.actions.setQuery('xyzzy').toggleFacet('contentType', 'Article').search();
    await settled(search);

    await vi.waitFor(() =>
      expect(host.querySelector('.xps-results__clear')?.textContent).toBe('Start over (7)')
    );
    host.querySelector<HTMLButtonElement>('.xps-results__clear')!.click();
    await vi.waitFor(() => expect(search?.state.filters.facets).toEqual([]));
  });
});

describe('filterSort', () => {
  const SORT = [
    { label: 'Most relevant', value: 'relevance' },
    { label: 'Newest first', value: 'date_desc' },
  ];

  const mount = async (
    params: Record<string, unknown> = {},
    response: Responder = RESPONSE
  ): Promise<HTMLElement> => {
    const host = container('filter-sort');
    search = start(
      [
        filterSort({
          container: host,
          facets: [{ attribute: 'contentType', label: 'Content type' }],
          sortOptions: SORT,
          ...params,
        }),
      ],
      response
    );
    await settled(search);
    return host;
  };

  const apply = (): HTMLButtonElement =>
    document.querySelector<HTMLButtonElement>('.xps-sheet__apply')!;

  const trigger = (host: HTMLElement): HTMLButtonElement =>
    host.querySelector<HTMLButtonElement>('.xps-filter-sort__trigger')!;
  const sheet = (): HTMLElement | null => document.querySelector('.xps-sheet');
  const box = (value: string): HTMLInputElement =>
    document.querySelector<HTMLInputElement>(`.xps-sheet__checkbox[value="${value}"]`)!;
  const check = (value: string): void => {
    const input = box(value);
    input.checked = !input.checked;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const press = (key: string): void => {
    sheet()?.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  };

  it('renders the trigger contract markup and hides the badge at zero', async () => {
    const host = await mount();
    const root = host.firstElementChild!;
    expect(classesOf(root)).toEqual(['xps', 'xps-filter-sort']);

    const button = trigger(host);
    expect(classesOf(button)).toEqual(['xps-button', 'xps-filter-sort__trigger']);
    expect(button.type).toBe('button');
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.querySelector('.xps-filter-sort__icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(button.querySelector('.xps-filter-sort__label')?.textContent).toBe('Filter & Sort');
    expect(button.querySelector<HTMLElement>('.xps-filter-sort__badge')?.hidden).toBe(true);
    expect(sheet()).toBeNull();
  });

  it('counts the active refinements of its attributes and a non-default sort in the badge', async () => {
    const host = await mount();
    search!.actions.toggleFacet('contentType', 'Article').toggleFacet('language', 'en').search();
    await vi.waitFor(() =>
      expect(host.querySelector<HTMLElement>('.xps-filter-sort__badge')?.hidden).toBe(false)
    );
    // `language` is not one of its groups, so it does not count.
    expect(host.querySelector('.xps-filter-sort__badge')?.textContent).toBe('1');

    search!.actions.setSort('date_desc').search();
    await vi.waitFor(() =>
      expect(host.querySelector('.xps-filter-sort__badge')?.textContent).toBe('2')
    );
  });

  it('opens a labelled modal sheet, moves focus into it and locks the page scroll', async () => {
    const host = await mount();
    trigger(host).click();

    const panel = document.querySelector('.xps-sheet__panel')!;
    expect(panel.getAttribute('role')).toBe('dialog');
    expect(panel.getAttribute('aria-modal')).toBe('true');
    expect(panel.getAttribute('aria-labelledby')).toBe(
      document.querySelector('.xps-sheet__title')!.id
    );
    expect(trigger(host).getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(document.querySelector('.xps-sheet__close'));
    expect(document.body.style.overflow).toBe('hidden');

    // The sort section plus one section per configured facet group, with the facet counts.
    expect([...document.querySelectorAll('.xps-sheet__section-title')].map((h) => h.textContent))
      .toEqual(['Sort by', 'Content type']);
    expect([...document.querySelectorAll('.xps-sheet__pill')].map((p) => p.getAttribute('aria-pressed')))
      .toEqual(['true', 'false']);
    expect([...document.querySelectorAll('.xps-sheet__value-count')].map((c) => c.textContent))
      .toEqual(['24', '11', '0']);
    expect(document.querySelector('.xps-sheet__apply')?.textContent).toBe('Show results');
  });

  it('keeps every selection pending until Apply, then replays them in one search', async () => {
    const host = await mount();
    trigger(host).click();
    const before = calls.length;

    check('Article');
    check('Product');
    check('Product'); // toggled back: it cancels out
    document.querySelector<HTMLButtonElement>('[data-xps-sort="date_desc"]')!.click();

    // Nothing refined and nothing searched while the sheet is open.
    expect(calls.length).toBe(before);
    expect(search!.state.filters.facets).toEqual([]);
    expect(search!.state.sort).toBe('relevance');
    expect(document.querySelector('[data-xps-sort="date_desc"]')?.getAttribute('aria-pressed')).toBe(
      'true'
    );

    document.querySelector<HTMLButtonElement>('.xps-sheet__apply')!.click();
    expect(sheet()).toBeNull();
    expect(document.activeElement).toBe(trigger(host));
    expect(document.body.style.overflow).toBe('');
    expect(search!.state.sort).toBe('date_desc');
    expect(search!.state.filters.facets.map((facet) => [facet.attribute, [...facet.values]])).toEqual([
      ['contentType', ['Article']],
    ]);
    await vi.waitFor(() => expect(calls.length).toBe(before + 1));
  });

  it('pends "Clear all" and applies it as a removal of every configured attribute', async () => {
    const host = await mount();
    search!.actions.toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(search!.state.filters.facets.length).toBe(1));

    trigger(host).click();
    expect(box('Article').checked).toBe(true);

    document.querySelector<HTMLButtonElement>('.xps-sheet__clear')!.click();
    expect(box('Article').checked).toBe(false);
    expect(search!.state.filters.facets.length).toBe(1); // still pending

    document.querySelector<HTMLButtonElement>('.xps-sheet__apply')!.click();
    expect(search!.state.filters.facets).toEqual([]);
  });

  it('discards the pending selection on Escape and on the backdrop', async () => {
    const host = await mount();
    for (const dismiss of [
      () => press('Escape'),
      () => document.querySelector<HTMLElement>('.xps-sheet__backdrop')!.click(),
    ]) {
      trigger(host).click();
      check('Article');
      dismiss();
      expect(sheet()).toBeNull();
      expect(search!.state.filters.facets).toEqual([]);
      expect(document.activeElement).toBe(trigger(host));
    }
    // Re-opening starts from the committed state, not from what was discarded.
    trigger(host).click();
    expect(box('Article').checked).toBe(false);
  });

  it('traps Tab inside the sheet', async () => {
    const host = await mount();
    trigger(host).click();
    const panel = document.querySelector('.xps-sheet__panel')!;
    const stops = [...panel.querySelectorAll<HTMLElement>('a[href], button, input')];
    const first = stops[0]!;
    const last = stops[stops.length - 1]!;

    last.focus();
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(first);

    first.focus();
    panel.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })
    );
    expect(document.activeElement).toBe(last);
  });

  // --- ES-1: the apply button's live count -------------------------------

  /** Answers a probe with `total` and every real search with the ordinary fixture. */
  const previewing = (total: number | 'fail'): Responder =>
    async (body) => {
      if (body['probe'] !== true) return RESPONSE;
      if (total === 'fail') throw new Error('probe is down');
      return { ...RESPONSE, total };
    };

  it('previews the pending count on the apply button, from one flagged probe', async () => {
    const host = await mount({}, previewing(12));
    trigger(host).click();
    expect(apply().textContent).toBe('Show results');

    check('Article');
    await vi.waitFor(() => expect(apply().textContent).toBe('Show 12 results'));

    const probes = calls.filter((call) => call.body['probe'] === true);
    expect(probes).toHaveLength(1);
    // Committed filters with the pending tick applied — what Apply would commit.
    expect(probes[0]?.body['filters']).toEqual({
      facets: [{ attribute: 'contentType', values: ['Article'] }],
    });
  });

  it('debounces the preview: three ticks in a row cost one probe', async () => {
    const host = await mount({}, previewing(12));
    trigger(host).click();

    check('Article');
    check('Product');
    check('Product');
    await vi.waitFor(() => expect(apply().textContent).toBe('Show 12 results'));
    expect(calls.filter((call) => call.body['probe'] === true)).toHaveLength(1);
  });

  it('keeps the countless label when the probe fails', async () => {
    const host = await mount({}, previewing('fail'));
    trigger(host).click();

    check('Article');
    await vi.waitFor(() => expect(calls.some((call) => call.body['probe'] === true)).toBe(true));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(apply().textContent).toBe('Show results');
  });

  it('discards a probe that lands after Apply, and starts the next sheet countless', async () => {
    let release = (): void => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const host = await mount({}, async (body) => {
      if (body['probe'] !== true) return RESPONSE;
      await held;
      return { ...RESPONSE, total: 12 };
    });

    trigger(host).click();
    check('Article');
    await vi.waitFor(() => expect(calls.some((call) => call.body['probe'] === true)).toBe(true));

    apply().click();
    release();
    await new Promise((resolve) => setTimeout(resolve, 20));

    trigger(host).click();
    expect(apply().textContent).toBe('Show results');
  });

  it('substitutes {count} into a custom applyLabel and drops it while the count is unknown', async () => {
    const host = await mount({ applyLabel: 'Apply ({count})' }, previewing(12));
    trigger(host).click();
    expect(apply().textContent).toBe('Apply ()');

    check('Article');
    await vi.waitFor(() => expect(apply().textContent).toBe('Apply (12)'));
  });

  it('asks the server to count every configured attribute and cleans up on dispose', async () => {
    const host = await mount();
    expect(calls[calls.length - 1]?.body['facets']).toEqual(['contentType']);

    trigger(host).click();
    expect(sheet()).not.toBeNull();
    search!.dispose();
    expect(sheet()).toBeNull();
    expect(document.body.style.overflow).toBe('');
    expect(host.innerHTML).toBe('');
  });
});
