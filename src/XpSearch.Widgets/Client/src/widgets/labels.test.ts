// @vitest-environment jsdom
/**
 * TH-12: no widget ever prints a stored code at a visitor. The fixtures here are the live
 * Dancing Goat schema (`ProductFieldTags`, `CoffeeTastes`, `ProductFieldPrice`) and the URL the
 * unit was written against:
 * `/search?q=coffee&ProductFieldTags=HotTips&CoffeeTastes=Sweet%2FAcidy&ProductFieldPrice_lte=200`.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchResponse } from '../contract/generated';
import { mountAll } from '../bootstrap';
import { createSearch } from '../instance';
import type { SearchInstance, SearchState, Widget } from '../types';
import type { ActiveFiltersWidgetParams } from './activeFilters';
import {
  activeFilters,
  categoryTree,
  clearFilters,
  facetList,
  filterSort,
  loadMore,
  pagination,
  rangeFilter,
  results,
  resultStats,
  sortSelect,
  toggleFilter,
} from './index';

const RESPONSE: SearchResponse = {
  results: [{ id: 'doc-1', attributes: { title: 'Chemex', url: '/p/chemex' } }],
  facets: {
    ProductFieldTags: [
      { value: 'HotTips', label: 'Hot tips', count: 3 },
      { value: 'ColdBrew', label: 'Cold brew', count: 2 },
    ],
    // A nested taxonomy: the leaf's stored value is a path, its ancestry is on the wire.
    CoffeeTastes: [
      { value: 'Sweet', label: 'Sweet', count: 5 },
      { value: 'Sweet/Acidy', label: 'Acidy', count: 2, path: ['Sweet'] },
    ],
  },
  page: 1,
  pageSize: 10,
  total: 1,
  totalPages: 1,
  tookMs: 7,
  redirect: null,
  queryId: 'q-1',
};

/**
 * What the same search answers once the refinements leave nothing. FC-1: a facet always carries the
 * values the request refines it by, so a zero-hit response still names them at count 0 - the
 * counted values are simply gone.
 */
const EMPTY: SearchResponse = {
  ...RESPONSE,
  results: [],
  total: 0,
  totalPages: 0,
  facets: {
    ProductFieldTags: [{ value: 'HotTips', label: 'Hot tips', count: 0 }],
    CoffeeTastes: [
      { value: 'Sweet', label: 'Sweet', count: 0 },
      { value: 'Sweet/Acidy', label: 'Acidy', count: 0, path: ['Sweet'] },
    ],
  },
};

/** A response that carries no facets at all: the page asked for none, so nothing is ever named. */
const NAMELESS: SearchResponse = { ...EMPTY, facets: {} };

/** The state the spec's URL hydrates to. */
const FILTERED: Partial<SearchState> = {
  query: 'coffee',
  filters: {
    facets: [
      { attribute: 'ProductFieldTags', values: ['HotTips'] },
      { attribute: 'CoffeeTastes', values: ['Sweet/Acidy'] },
    ],
    numeric: [{ attribute: 'ProductFieldPrice', operator: 'lte', value: 200 }],
  },
};

const started: SearchInstance[] = [];
let answer: SearchResponse = RESPONSE;

function start(widgets: Widget[], initialState: Partial<SearchState> = {}): SearchInstance {
  const fetchFn = (async () =>
    new Response(JSON.stringify(answer), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    })) as unknown as typeof fetch;
  const search = createSearch({ index: 'site', fetchFn, debounceMs: 0, initialState });
  search.addWidgets(widgets);
  search.start();
  started.push(search);
  return search;
}

const settled = (search: SearchInstance): Promise<void> =>
  vi.waitFor(() => expect(search.results).not.toBeNull()) as Promise<void>;

const container = (id: string): HTMLElement => {
  const element = document.createElement('div');
  element.id = id;
  document.body.appendChild(element);
  return element;
};

const chipTexts = (host: HTMLElement): string[] =>
  [...host.querySelectorAll('.xps-chip__label')].map((chip) => chip.textContent ?? '');

beforeEach(() => {
  answer = RESPONSE;
  document.body.innerHTML = '';
});
afterEach(() => {
  for (const instance of started.splice(0)) instance.dispose();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('the label memory', () => {
  it('names the chips of the spec URL, and keeps naming them at zero hits', async () => {
    const chips = container('chips');
    const search = start(
      [
        facetList({ container: container('tags'), attribute: 'ProductFieldTags', label: 'Products' }),
        categoryTree({ container: container('tastes'), attribute: 'CoffeeTastes', label: 'Taste' }),
        rangeFilter({
          container: container('price'),
          attribute: 'ProductFieldPrice',
          label: 'Price',
          min: 0,
          max: 500,
          unit: 'USD',
        }),
        activeFilters({ container: chips }),
      ],
      FILTERED
    );
    await settled(search);

    // The facet widgets re-declare their operator on init, which is what orders the entries.
    expect(chipTexts(chips)).toEqual([
      'Taste: Sweet › Acidy',
      'Products: Hot tips',
      'Price: up to 200 USD',
    ]);

    // Zero hits: no counted value survives, and the selected ones come back at 0 (FC-1) - the
    // memory would have named them anyway, which is what makes the two paths agree.
    answer = EMPTY;
    search.actions.setQuery('nothing at all').search();
    await vi.waitFor(() => expect(search.results?.total).toBe(0));
    expect(chipTexts(chips)).toEqual([
      'Taste: Sweet › Acidy',
      'Products: Hot tips',
      'Price: up to 200 USD',
    ]);

    // …and so can the refinement rows the visitor has to untick (TH-7 + TH-12).
    expect(
      document.querySelector('#tags .xps-facet-list__value')?.textContent
    ).toBe('Hot tips');
    // The selected leaf comes back with its ancestor (FC-1 keeps the contract's path promise), so
    // the tree draws the open path down to the value the visitor has to untick.
    expect(
      [...document.querySelectorAll('#tastes .xps-category-tree__value')].map((node) => node.textContent)
    ).toEqual(['Sweet', 'Acidy']);
  });

  it('falls back to the stored value when no response ever named it', async () => {
    const chips = container('chips');
    answer = NAMELESS;
    const search = start([activeFilters({ container: chips, attributeLabels: { ProductFieldTags: 'Products' } })], {
      filters: { facets: [{ attribute: 'ProductFieldTags', values: ['HotTips'] }], numeric: [] },
    });
    await vi.waitFor(() => expect(search.results?.total).toBe(0));
    expect(chipTexts(chips)).toEqual(['Products: HotTips']);
  });

  it('reads the three numeric shapes as sentences, with and without a unit', async () => {
    const chips = container('chips');
    const search = start(
      [
        rangeFilter({
          container: container('price'),
          attribute: 'ProductFieldPrice',
          label: 'Price',
          min: 0,
          max: 500,
          unit: 'USD',
        }),
        activeFilters({ container: chips, attributeLabels: { weight: 'Weight' } }),
      ],
      { filters: { facets: [], numeric: [{ attribute: 'weight', operator: 'gte', value: 50 }] } }
    );
    await settled(search);
    expect(chipTexts(chips)).toEqual(['Weight: from 50']);

    search.actions.setNumericFilter('ProductFieldPrice', 'lte', 200).search();
    await vi.waitFor(() => expect(chipTexts(chips)).toHaveLength(2));
    expect(chipTexts(chips)[1]).toBe('Price: up to 200 USD');

    search.actions.setNumericFilter('ProductFieldPrice', 'gte', 50).search();
    await vi.waitFor(() => expect(chipTexts(chips)[1]).toBe('Price: 50 – 200 USD'));

    // One chip, both bounds: removing it removes the whole range in one search.
    const remove = chips.querySelectorAll<HTMLButtonElement>('.xps-chip__remove')[1] as HTMLButtonElement;
    remove.click();
    expect(search.state.filters.numeric).toEqual([
      { attribute: 'weight', operator: 'gte', value: 50 },
    ]);
  });

  it('leaves the name off, and warns the developer, when no widget declares one', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const chips = container('chips');
    const search = start(
      [
        facetList({ container: container('tags'), attribute: 'ProductFieldTags' }),
        activeFilters({ container: chips }),
      ],
      { filters: { facets: [{ attribute: 'ProductFieldTags', values: ['HotTips'] }], numeric: [] } }
    );
    await settled(search);

    // The value alone — a field code is never shown, and never humanised into a fake name.
    expect(chipTexts(chips)).toEqual(['Hot tips']);
    expect(document.querySelector('#tags .xps-facet-list__title')?.textContent).toBe('Filters');
    expect(warn.mock.calls.map((call) => String(call[0])).join('\n')).toContain(
      'no "label" for attribute "ProductFieldTags"'
    );
    // Once per attribute, however often the widgets re-render.
    search.actions.setQuery('again').search();
    await vi.waitFor(() => expect(warn.mock.calls.length).toBeGreaterThan(0));
    expect(warn.mock.calls.filter((call) => String(call[0]).includes('ProductFieldTags'))).toHaveLength(1);
  });

  it('is seeded from data-xps-labels on the first paint, before any response (FC-1)', () => {
    // The response never arrives: this is the frame between hydration and the first search.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
    document.body.innerHTML = `<div class="xps-mount" data-xps-widget="activeFilters"
      data-xps-instance-config='{"index":"site","initialState":{"filters":{"facets":[{"attribute":"ProductFieldTags","values":["HotTips"]}],"numeric":[]}}}'
      data-xps-labels='{"ProductFieldTags":{"HotTips":"Hot tips"}}'
      data-xps-config='{"attributeLabels":{"ProductFieldTags":"Products"}}'></div>`;

    started.push(
      ...mountAll(document, {
        widgets: {
          activeFilters: (config) => activeFilters(config as unknown as ActiveFiltersWidgetParams),
        },
      })
    );

    const mount = document.querySelector<HTMLElement>('.xps-mount')!;
    expect(started[0]?.results).toBeNull();
    expect(chipTexts(mount)).toEqual(['Products: Hot tips']);
  });
});

describe('the rendered DOM of a fully filtered page', () => {
  /** Attribute codes, stored codes and operators, wherever a visitor could read them. */
  const CODES = /\b(lte|gte|lt|gt)\b|ProductField|_asc|_desc/;

  it('never shows a filter code, an operator or a sort key', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    const search = start(
      [
        facetList({ container: container('tags'), attribute: 'ProductFieldTags', label: 'Products' }),
        // No label on purpose: the un-named widget must not leak its attribute either.
        facetList({ container: container('bare'), attribute: 'ProductFieldBrand' }),
        categoryTree({ container: container('tastes'), attribute: 'CoffeeTastes', label: 'Taste' }),
        rangeFilter({
          container: container('price'),
          attribute: 'ProductFieldPrice',
          label: 'Price',
          min: 0,
          max: 500,
          unit: 'USD',
        }),
        toggleFilter({ container: container('toggle'), attribute: 'ProductFieldTags', value: 'HotTips' }),
        activeFilters({ container: container('chips') }),
        clearFilters({ container: container('clear') }),
        sortSelect({
          container: container('sort'),
          items: [
            { label: 'Relevance', value: 'relevance' },
            { label: 'Price, low to high', value: 'price_asc' },
          ],
        }),
        resultStats({ container: container('stats') }),
        results({ container: container('results') }),
        pagination({ container: container('pages') }),
        loadMore({ container: container('more') }),
        filterSort({
          container: container('sheet'),
          facets: [{ attribute: 'ProductFieldTags', label: 'Products' }, { attribute: 'ProductFieldBrand' }],
          sortOptions: [
            { label: 'Relevance', value: 'relevance' },
            { label: 'Price, low to high', value: 'price_asc' },
          ],
        }),
      ],
      { ...FILTERED, sort: 'price_asc' }
    );
    await settled(search);
    // The sheet only exists once it is opened.
    (document.querySelector('.xps-filter-sort__trigger') as HTMLButtonElement).click();

    const offenders: string[] = [];
    for (const element of document.querySelectorAll<HTMLElement>('.xps *')) {
      // Own text only: a parent would otherwise report its children's text again.
      const own = [...element.childNodes]
        .filter((node) => node.nodeType === node.TEXT_NODE)
        .map((node) => node.textContent ?? '')
        .join('');
      const readable = [
        own,
        element.getAttribute('aria-label') ?? '',
        element.getAttribute('title') ?? '',
        element.getAttribute('placeholder') ?? '',
      ].join(' ');
      if (CODES.test(readable)) offenders.push(`${element.className}: ${readable.trim()}`);
    }
    expect(offenders).toEqual([]);
    // The check would be worthless if nothing had rendered.
    expect(document.querySelectorAll('.xps-chip').length).toBeGreaterThan(0);
  });
});
