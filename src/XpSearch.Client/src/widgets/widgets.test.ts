// @vitest-environment jsdom
/**
 * The default renderers against the markup contract (`themes/fixtures/*.html`) and against real
 * DOM events. Every assertion here is either "the contract says this class/attribute" or
 * "this interaction reaches the state".
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mountAll } from '../bootstrap';
import { API_VERSION_HEADER, EVENTS_ROUTE } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { xpsearch } from '../instance';
import type { InstantSearch, Widget } from '../types';
import { html } from '../templates/html';
import {
  clearRefinements,
  currentRefinements,
  hits,
  pagination,
  refinementList,
  searchBox,
  sortBy,
  stats,
  toggleRefinement,
} from './index';

const RESPONSE: SearchResponse = {
  hits: [
    {
      objectID: 'doc-1',
      title: 'Choosing an espresso machine',
      url: '/blog/choosing-an-espresso-machine',
      content: 'A dual-boiler espresso machine holds temperature.',
      contentType: 'Article',
      image: '/img/1.png',
      _highlights: {
        title: 'Choosing an <mark>espresso</mark> machine',
        content: 'A dual-boiler <mark>espresso</mark> machine holds temperature.',
      },
    },
    { objectID: 'doc-2', title: 'Rocket Appartamento', url: '/products/rocket', contentType: 'Product' },
    { objectID: 'doc-3', title: 'Descaling <b>your</b> machine', url: '/support/descaling' },
  ],
  facets: { contentType: { Article: 24, Product: 11, Event: 0 } },
  page: 0,
  hitsPerPage: 5,
  nbHits: 46,
  nbPages: 9,
  processingTimeMs: 14,
  queryId: 'q-1',
};

const calls: Array<{ url: string; body: Record<string, unknown> }> = [];
/** Every instance a test starts, so none of them leaks into the next test's `calls`. */
const started: InstantSearch[] = [];

function start(widgets: Widget[], response: SearchResponse = RESPONSE): InstantSearch {
  const fetchFn = (async (url: string, init: RequestInit) => {
    calls.push({ url: String(url), body: JSON.parse(String(init.body)) as Record<string, unknown> });
    return new Response(JSON.stringify(response), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    });
  }) as unknown as typeof fetch;

  const search = xpsearch({
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
const settled = (search: InstantSearch): Promise<void> =>
  vi.waitFor(() => expect(search.results).not.toBeNull()) as Promise<void>;

const container = (id: string): HTMLElement => {
  const element = document.createElement('div');
  element.id = id;
  document.body.appendChild(element);
  return element;
};

const classesOf = (element: Element | null | undefined): string[] => [...(element?.classList ?? [])];

/** The stats template separates the number from "ms" with a non-breaking space. */
const text = (element: Element | null | undefined): string => (element?.textContent ?? '').replace(/ /g, ' ');

let search: InstantSearch | undefined;

beforeEach(() => {
  calls.length = 0;
  document.body.innerHTML = '';
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

    search?.helper.setQuery('cold brew').search();
    await vi.waitFor(() => expect(input.value).toBe('cold brew'));
  });

  it('routes the query through queryHook', () => {
    const form = mount({
      queryHook: (query: string, refine: (value: string) => void) => refine(query.trim()),
    });
    const input = form.querySelector('input') as HTMLInputElement;
    input.value = '  latte  ';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(search?.state.query).toBe('latte');
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
    search = xpsearch({ index: 'i', fetchFn: slow, debounceMs: 0, stalledSearchDelayMs: 1 });
    search.addWidgets([searchBox({ container: host })]);
    search.start();
    started.push(search);
    await vi.waitFor(() =>
      expect(host.querySelector('.xps-search-box--stalled')).not.toBeNull()
    );
  });
});

describe('hits', () => {
  const mount = (params: Record<string, unknown> = {}, response = RESPONSE): HTMLElement => {
    const host = container('results');
    search = start([hits({ container: host, ...params })], response);
    return host.querySelector('.xps-hits') as HTMLElement;
  };

  it('renders the contract markup for a result list', async () => {
    const root = mount();
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-hits']);

    const status = root.querySelector('.xps-hits__status') as HTMLElement;
    expect(classesOf(status)).toEqual(['xps-hits__status', 'xps-sr-only']);
    expect(status.getAttribute('role')).toBe('status');
    expect(status.tagName).toBe('P');

    const list = root.querySelector('.xps-hits__list') as HTMLElement;
    expect(list.tagName).toBe('OL');
    const items = root.querySelectorAll('.xps-hits__item');
    expect(items.length).toBe(3);
    expect(items[0]?.firstElementChild?.tagName).toBe('ARTICLE');

    const first = items[0] as HTMLElement;
    expect(classesOf(first.querySelector('article'))).toEqual(['xps-hit']);
    expect(classesOf(first.querySelector('.xps-hit__image'))).toEqual(['xps-hit__image']);
    expect(first.querySelector('.xps-hit__image')?.getAttribute('alt')).toBe('');
    expect(first.querySelector('.xps-hit__title')?.tagName).toBe('H3');
    const link = first.querySelector('.xps-hit__link') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/blog/choosing-an-espresso-machine');
    expect(link.querySelector('mark')?.className).toBe('xps-highlight');
    expect(first.querySelector('.xps-hit__snippet')?.tagName).toBe('P');
    expect(first.querySelector('.xps-hit__meta-item')?.textContent).toBe('Article');

    // No image and no highlight: the media block and the snippet are omitted, not emptied.
    const third = items[2] as HTMLElement;
    expect(third.querySelector('.xps-hit__media')).toBeNull();
    expect(third.querySelector('.xps-hit__link')?.textContent).toBe('Descaling <b>your</b> machine');
  });

  it('announces the count in the live region, and only when it changes', async () => {
    const root = mount();
    search?.helper.setQuery('espresso');
    await settled(search!);
    const status = root.querySelector('.xps-hits__status') as HTMLElement;
    expect(status.textContent).toBe('46 results for “espresso”');

    const before = status;
    const spy = vi.spyOn(status, 'textContent', 'set');
    search?.helper.setPage(0).search();
    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(1));
    expect(root.querySelector('.xps-hits__status')).toBe(before);
    expect(spy).not.toHaveBeenCalled();
  });

  it('renders the empty state', async () => {
    const root = mount({}, { ...RESPONSE, hits: [], nbHits: 0, nbPages: 0 });
    search?.helper.setQuery('xyzzy');
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-hits', 'xps-hits--empty']);
    expect(root.querySelector('.xps-hits__list')).toBeNull();
    expect(root.querySelector('.xps-hits__empty')?.textContent).toContain('No results for');
    expect(root.querySelector('.xps-hits__status')?.textContent).toBe('No results for “xyzzy”');
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
    search = xpsearch({ index: 'i', fetchFn: slow, debounceMs: 0, stalledSearchDelayMs: 1 });
    search.addWidgets([hits({ container: host })]);
    search.start();
    started.push(search);

    const root = host.querySelector('.xps-hits') as HTMLElement;
    await vi.waitFor(() => expect(classesOf(root)).toContain('xps-hits--loading'));
    expect(root.getAttribute('aria-busy')).toBe('true');
    const skeletons = root.querySelectorAll('.xps-hit--skeleton');
    expect(skeletons.length).toBe(3);
    expect(skeletons[0]?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('.xps-hits__status')?.textContent).toBe('Searching…');
    await settled(search);
    expect(root.querySelectorAll('.xps-hit--skeleton').length).toBe(0);
  });

  it('applies transformItems and custom templates', async () => {
    const root = mount({
      transformItems: (items: Array<{ title: string }>) => items.slice(0, 1),
      templates: {
        item: (hit: { title: string }, tools: { html: typeof html }) =>
          tools.html`<article class="xps-hit"><span>${hit.title}</span></article>`,
      },
    });
    await settled(search!);
    expect(root.querySelectorAll('.xps-hits__item').length).toBe(1);
    expect(root.querySelector('span')?.textContent).toBe('Choosing an espresso machine');
  });

  it('sends a click event with the objectID and one-based position', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelectorAll('.xps-hit__link')[1] as HTMLAnchorElement).click();
    await vi.waitFor(() => expect(calls.some((call) => call.url.endsWith(EVENTS_ROUTE))).toBe(true));
    const event = calls.find((call) => call.url.endsWith(EVENTS_ROUTE))?.body;
    expect(event).toMatchObject({
      eventType: 'click',
      objectID: 'doc-2',
      position: 2,
      queryId: 'q-1',
    });
  });
});

describe('refinementList', () => {
  const mount = (params: Record<string, unknown> = {}): HTMLElement => {
    const host = container('facet');
    search = start([
      refinementList({ container: host, attribute: 'contentType', label: 'Content type', ...params }),
    ]);
    return host.querySelector('.xps-refinement-list') as HTMLElement;
  };

  it('renders the contract markup with real checkboxes', async () => {
    const root = mount();
    await settled(search!);
    expect(classesOf(root)).toEqual(['xps', 'xps-refinement-list']);

    const title = root.querySelector('.xps-refinement-list__title') as HTMLElement;
    const list = root.querySelector('.xps-refinement-list__list') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.textContent).toBe('Content type');
    expect(list.tagName).toBe('UL');
    expect(list.getAttribute('aria-labelledby')).toBe(title.id);

    const items = [...root.querySelectorAll('.xps-refinement-list__item')];
    expect(items.map((item) => item.querySelector('.xps-refinement-list__value')?.textContent)).toEqual(
      ['Article', 'Product', 'Event']
    );
    expect(items.map((item) => item.querySelector('.xps-refinement-list__count')?.textContent)).toEqual(
      ['24', '11', '0']
    );
    const checkbox = items[0]?.querySelector('input') as HTMLInputElement;
    expect(checkbox.type).toBe('checkbox');
    expect(checkbox.name).toBe('contentType');
    expect(checkbox.value).toBe('Article');
    expect(checkbox.closest('label')?.className).toBe('xps-refinement-list__label');
    // A value nothing matches any more is a disabled row (fixture: canRefine=false).
    expect(classesOf(items[2])).toEqual([
      'xps-refinement-list__item',
      'xps-refinement-list__item--disabled',
    ]);
    expect((items[2]?.querySelector('input') as HTMLInputElement).disabled).toBe(true);
  });

  it('refines when a checkbox is clicked and marks the row selected', async () => {
    const root = mount();
    await settled(search!);
    (root.querySelector('input') as HTMLInputElement).click();
    expect(search?.state.facetFilters['contentType']).toEqual(['Article']);
    await vi.waitFor(() =>
      expect(classesOf(root.querySelector('.xps-refinement-list__item'))).toContain(
        'xps-refinement-list__item--selected'
      )
    );
    expect((root.querySelector('input') as HTMLInputElement).checked).toBe(true);
  });

  it('orders items with sortBy', async () => {
    const root = mount({ sortBy: ['name:asc'] });
    await settled(search!);
    expect(
      [...root.querySelectorAll('.xps-refinement-list__value')].map((node) => node.textContent)
    ).toEqual(['Article', 'Event', 'Product']);
  });

  it('toggles show more with aria-expanded and keeps the button in the DOM', async () => {
    const root = mount({ showMore: true, limit: 1 });
    await settled(search!);
    const button = root.querySelector('.xps-refinement-list__show-more') as HTMLButtonElement;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.textContent).toBe('Show more');
    expect(root.querySelectorAll('.xps-refinement-list__item').length).toBe(1);

    button.click();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.textContent).toBe('Show less');
    expect(root.querySelectorAll('.xps-refinement-list__item').length).toBe(3);
    expect(root.querySelector('.xps-refinement-list__show-more')).toBe(button);
  });

  it('disables show more when there is nothing more to show', async () => {
    const root = mount({ showMore: true, limit: 10 });
    await settled(search!);
    const button = root.querySelector('.xps-refinement-list__show-more') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(classesOf(button)).toContain('xps-refinement-list__show-more--disabled');
  });

  it('filters client-side when searchable, and keeps focus in the search input', async () => {
    const root = mount({ searchable: true });
    await settled(search!);
    expect(classesOf(root)).toContain('xps-refinement-list--searchable');
    const field = root.querySelector('.xps-refinement-list__search-input') as HTMLInputElement;
    expect(field.type).toBe('search');
    expect(root.querySelector('.xps-refinement-list__search label')?.className).toBe('xps-sr-only');

    field.focus();
    field.value = 'art';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    expect(document.activeElement).toBe(field);
    const values = [...root.querySelectorAll('.xps-refinement-list__value')];
    expect(values.length).toBe(1);
    expect(values[0]?.innerHTML).toBe('<mark class="xps-highlight">Art</mark>icle');
    expect((root.querySelector('.xps-refinement-list__no-results') as HTMLElement).hidden).toBe(true);

    field.value = 'zzz';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    expect((root.querySelector('.xps-refinement-list__list') as HTMLElement).hidden).toBe(true);
    const none = root.querySelector('.xps-refinement-list__no-results') as HTMLElement;
    expect(none.hidden).toBe(false);
    expect(none.getAttribute('role')).toBe('status');
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

  it('gives every link the href createURL produces', async () => {
    const host = container('pages');
    search = xpsearch({
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
        search.createURL({ ...search.state, page })
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
    expect(search?.state.page).toBe(2);
  });

  it('hides itself when there is only one page', async () => {
    const root = mount();
    await settled(search!);
    expect(root.hidden).toBe(false);

    const host = container('pages-2');
    search = start([pagination({ container: host })], { ...RESPONSE, nbPages: 1, nbHits: 3 });
    await settled(search);
    expect((host.querySelector('.xps-pagination') as HTMLElement).hidden).toBe(true);
  });
});

describe('stats', () => {
  it('renders the default text and the time element', async () => {
    const host = container('stats');
    search = start([stats({ container: host })]);
    const root = host.querySelector('.xps-stats') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-stats', 'xps-stats--empty']);
    expect(root.textContent).toBe('Type to search.');

    await settled(search);
    expect(classesOf(root)).toEqual(['xps', 'xps-stats']);
    const line = root.querySelector('.xps-stats__text') as HTMLElement;
    expect(line.tagName).toBe('SPAN');
    expect(text(line)).toBe('46 results in 14 ms');
    expect(text(root.querySelector('.xps-stats__time'))).toBe('14 ms');
    // The count lives in a live region on `hits` only (spec 5.6).
    expect(root.getAttribute('role')).toBeNull();
    expect(root.getAttribute('aria-live')).toBeNull();
  });

  it('accepts templates.text', async () => {
    const host = container('stats');
    search = start([
      stats({
        container: host,
        templates: {
          text: (data, tools) => tools.html`<b>${tools.formatNumber(data.nbHits)}</b>`,
        },
      }),
    ]);
    await settled(search);
    expect(host.querySelector('.xps-stats__text')?.innerHTML).toBe('<b>46</b>');
  });
});

describe('sortBy', () => {
  const items = [
    { label: 'Relevance', value: 'relevance' },
    { label: 'Newest first', value: 'date_desc' },
  ];

  it('renders a labelled native select and refines on change', async () => {
    const host = container('sort');
    search = start([sortBy({ container: host, items })]);
    const root = host.querySelector('.xps-sort-by') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-sort-by']);
    const label = root.querySelector('label') as HTMLLabelElement;
    const select = root.querySelector('select') as HTMLSelectElement;
    expect(classesOf(label)).toEqual(['xps-sort-by__label']);
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

describe('clearRefinements', () => {
  it('is disabled with nothing to clear and clears every refinement', async () => {
    const host = container('clear');
    search = start([clearRefinements({ container: host })]);
    const root = host.querySelector('.xps-clear-refinements') as HTMLElement;
    const button = root.querySelector('button') as HTMLButtonElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-clear-refinements', 'xps-clear-refinements--disabled']);
    expect(classesOf(button)).toEqual(['xps-button', 'xps-clear-refinements__button']);
    expect(button.type).toBe('button');
    expect(button.disabled).toBe(true);

    search.helper.toggleFacetRefinement('contentType', 'Article').search();
    await vi.waitFor(() => expect(button.disabled).toBe(false));
    expect(classesOf(root)).toEqual(['xps', 'xps-clear-refinements']);

    button.click();
    expect(search.state.facetFilters).toEqual({});
    await vi.waitFor(() => expect(button.disabled).toBe(true));
    expect(root.querySelector('button')).toBe(button);
  });
});

describe('currentRefinements', () => {
  it('renders a removable chip per refinement', async () => {
    const host = container('chips');
    search = start([
      currentRefinements({ container: host, attributeLabels: { contentType: 'Content type' } }),
    ]);
    const root = host.querySelector('.xps-current-refinements') as HTMLElement;
    expect(classesOf(root)).toEqual(['xps', 'xps-current-refinements', 'xps-current-refinements--empty']);
    const list = root.querySelector('.xps-current-refinements__list') as HTMLElement;
    const title = root.querySelector('.xps-current-refinements__title') as HTMLElement;
    expect(list.tagName).toBe('UL');
    expect(list.children.length).toBe(0);
    expect(list.getAttribute('aria-labelledby')).toBe(title.id);
    expect(classesOf(title)).toEqual(['xps-current-refinements__title', 'xps-sr-only']);

    search.helper.toggleFacetRefinement('contentType', 'Article').search();
    await vi.waitFor(() => expect(root.querySelectorAll('.xps-chip').length).toBe(1));
    expect(classesOf(root)).toEqual(['xps', 'xps-current-refinements']);
    const chip = root.querySelector('.xps-chip') as HTMLElement;
    expect(chip.querySelector('.xps-chip__attribute')?.textContent).toBe('Content type');
    expect(chip.querySelector('.xps-chip__label')?.textContent).toBe('Content type Article');
    const remove = chip.querySelector('.xps-chip__remove') as HTMLButtonElement;
    expect(remove.getAttribute('aria-label')).toBe('Remove filter Content type: Article');

    remove.click();
    expect(search.state.facetFilters['contentType']).toBeUndefined();
    await vi.waitFor(() => expect(root.querySelectorAll('.xps-chip').length).toBe(0));
  });

  it('labels a numeric refinement with its operator', async () => {
    const host = container('chips');
    search = start([currentRefinements({ container: host })]);
    search.helper.setNumericRefinement('price', '<=', 50).search();
    await vi.waitFor(() => expect(host.querySelectorAll('.xps-chip').length).toBe(1));
    expect(host.querySelector('.xps-chip__label')?.textContent).toBe('price <= 50');
  });
});

describe('toggleRefinement', () => {
  it('renders one real checkbox and refines on change', async () => {
    const host = container('toggle');
    search = start([
      toggleRefinement({ container: host, attribute: 'contentType', value: 'Article', label: 'Articles only' }),
    ]);
    const root = host.querySelector('.xps-toggle-refinement') as HTMLElement;
    await settled(search);
    const checkbox = root.querySelector('input') as HTMLInputElement;
    expect(checkbox.type).toBe('checkbox');
    expect(checkbox.closest('label')?.className).toBe('xps-toggle-refinement__label');
    expect(root.querySelector('.xps-toggle-refinement__value')?.textContent).toBe('Articles only');
    expect(root.querySelector('.xps-toggle-refinement__count')?.textContent).toBe('24');
    expect(checkbox.disabled).toBe(false);

    checkbox.click();
    expect(search.state.facetFilters['contentType']).toEqual(['Article']);
    await vi.waitFor(() => expect(checkbox.checked).toBe(true));
    expect(root.querySelector('input')).toBe(checkbox);
  });

  it('disables itself when no document carries the value', async () => {
    const host = container('toggle');
    search = start([toggleRefinement({ container: host, attribute: 'contentType', value: 'Event' })]);
    await settled(search);
    const root = host.querySelector('.xps-toggle-refinement') as HTMLElement;
    expect(classesOf(root)).toContain('xps-toggle-refinement--disabled');
    expect((root.querySelector('input') as HTMLInputElement).disabled).toBe(true);
  });
});

describe('the .xps-mount bootstrap', () => {
  it('mounts every first-party widget from data-xps-widget', () => {
    const config = JSON.stringify({ index: 'site-content', searchOnInitialLoad: false });
    const mounts: Array<[string, Record<string, unknown>]> = [
      ['searchBox', {}],
      ['hits', {}],
      ['refinementList', { attribute: 'contentType' }],
      ['pagination', {}],
      ['stats', {}],
      ['sortBy', { items: [{ label: 'Relevance', value: 'relevance' }] }],
      ['clearRefinements', {}],
      ['currentRefinements', {}],
      ['toggleRefinement', { attribute: 'contentType', value: 'Article' }],
    ];
    document.body.innerHTML = mounts
      .map(
        ([type, widgetConfig]) =>
          `<div class="xps-mount" data-xps-widget="${type}" data-xps-instance="mounted" data-xps-instance-config='${config}' data-xps-config='${JSON.stringify(widgetConfig)}'></div>`
      )
      .join('');

    const instances = mountAll(document);
    expect(instances.length).toBe(1);
    const roots = [...document.querySelectorAll('.xps-mount > .xps')];
    expect(roots.length).toBe(9);
    expect(roots.map((root) => [...root.classList][1])).toEqual([
      'xps-search-box',
      'xps-hits',
      'xps-refinement-list',
      'xps-pagination',
      'xps-stats',
      'xps-sort-by',
      'xps-clear-refinements',
      'xps-current-refinements',
      'xps-toggle-refinement',
    ]);
    instances[0]?.dispose();
  });
});

describe('two instances on one page', () => {
  it('do not interfere (spec 12)', async () => {
    const one = container('one');
    const oneStats = container('one-stats');
    const two = container('two');
    const first = start([searchBox({ container: one }), stats({ container: oneStats })]);
    const second = start([searchBox({ container: two })], { ...RESPONSE, nbHits: 7 });
    await settled(first);
    await settled(second);

    const input = two.querySelector('input') as HTMLInputElement;
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(second.state.query).toBe('espresso');
    expect(first.state.query).toBe('');
    expect((one.querySelector('input') as HTMLInputElement).value).toBe('');
    expect(text(oneStats.querySelector('.xps-stats__text'))).toBe('46 results in 14 ms');
    expect(second.results?.nbHits).toBe(7);
  });
});

describe('request parameters', () => {
  it('asks the server for the facet a refinementList needs', async () => {
    const host = container('facet');
    search = start([refinementList({ container: host, attribute: 'tags' })]);
    await settled(search);
    expect((calls[0]?.body as unknown as SearchRequest).facets).toEqual(['tags']);
  });
});
