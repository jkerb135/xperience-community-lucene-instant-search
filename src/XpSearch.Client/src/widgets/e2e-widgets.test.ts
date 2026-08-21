// @vitest-environment jsdom
/**
 * The demo page's widget set against the mock server over real HTTP: type, refine, sort,
 * paginate, clear — asserting the rendered DOM each time, not the state.
 */
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { startMockServer } from '../../mock/server.ts';
import { QUERY_ROUTE } from '../contract/constants';
import { createSearch } from '../instance';
import type { SearchInstance } from '../types';
import {
  clearFilters,
  activeFilters,
  results,
  pagination,
  facetList,
  searchBox,
  sortSelect,
  resultStats,
} from './index';

let server: Awaited<ReturnType<typeof startMockServer>>;

beforeAll(async () => {
  server = await startMockServer(0);
});
afterAll(async () => {
  await server.close();
});

const settled = (search: SearchInstance, predicate: () => boolean): Promise<void> =>
  vi.waitFor(() => expect(predicate()).toBe(true), { timeout: 3000 }) as Promise<void>;

describe('the demo widget set against the mock server', () => {
  it('searches, refines, sorts, paginates and clears', async () => {
    document.body.innerHTML = `<div id="box"></div><div id="resultStats"></div><div id="sort"></div>
      <div id="facet"></div><div id="chips"></div><div id="clear"></div>
      <div id="results"></div><div id="pages"></div>`;

    const search = createSearch({
      index: 'site-content',
      endpoint: `${server.url}${QUERY_ROUTE}`,
      debounceMs: 5,
      initialState: { pageSize: 5 },
      highlight: { fields: ['title', 'content'] },
    });
    search.addWidgets([
      searchBox({ container: '#box' }),
      resultStats({ container: '#resultStats' }),
      sortSelect({
        container: '#sort',
        items: [
          { label: 'Relevance', value: 'relevance' },
          { label: 'Price, low to high', value: 'price_asc' },
        ],
      }),
      facetList({ container: '#facet', attribute: 'contentType', label: 'Content type' }),
      activeFilters({ container: '#chips' }),
      clearFilters({ container: '#clear' }),
      results({ container: '#results' }),
      pagination({ container: '#pages', padding: 2 }),
    ]);
    search.start();

    const list = () => [...document.querySelectorAll('.xps-results__item')];
    const statsText = () => document.querySelector('.xps-result-stats__text')?.textContent ?? '';

    await settled(search, () => list().length === 5);
    expect(statsText()).toContain('54 results');
    expect(document.querySelectorAll('.xps-pagination__item--page').length).toBeGreaterThan(1);

    // Type: the results and the count follow, and the server's highlight reaches the DOM.
    const input = document.querySelector('.xps-search-box__input') as HTMLInputElement;
    input.value = 'espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settled(search, () => statsText().includes('54 results') === false);
    expect(document.querySelector('.xps-result__title mark')?.className).toBe('xps-highlight');

    // Refine on a facet: a chip appears and the clear button becomes usable.
    const before = search.results?.total ?? 0;
    const checkbox = document.querySelector('.xps-facet-list__checkbox') as HTMLInputElement;
    const value = checkbox.value;
    checkbox.click();
    await settled(search, () => (search.results?.total ?? 0) < before);
    expect(document.querySelector('.xps-chip__label')?.textContent).toContain(value);
    expect((document.querySelector('.xps-clear-filters__button') as HTMLButtonElement).disabled).toBe(
      false
    );
    for (const item of list()) {
      expect(item.querySelector('.xps-result__meta-item')?.textContent).toBe(value);
    }

    // Sort.
    const select = document.querySelector('.xps-sort-select .xps-select__control') as HTMLSelectElement;
    select.value = 'price_asc';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settled(search, () => search.state.sort === 'price_asc' && search.status === 'idle');

    // Paginate: clicking page 2 refines instead of navigating, and the list changes.
    const firstTitle = () => list()[0]?.querySelector('.xps-result__link')?.textContent ?? '';
    const page1 = firstTitle();
    const links = [...document.querySelectorAll<HTMLAnchorElement>('.xps-pagination__item--page a')];
    const second = links.find((link) => link.dataset['xpsPage'] === '2');
    second?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    await settled(search, () => search.results?.page === 2);
    expect(firstTitle()).not.toBe(page1);
    expect(
      document.querySelector('.xps-pagination__item--current a')?.getAttribute('aria-current')
    ).toBe('page');

    // Clear: the chips go, and so does the facet refinement.
    (document.querySelector('.xps-clear-filters__button') as HTMLButtonElement).click();
    await settled(search, () => document.querySelectorAll('.xps-chip').length === 0);
    expect(document.querySelector('.xps-active-filters--empty')).not.toBeNull();
    expect((document.querySelector('.xps-clear-filters__button') as HTMLButtonElement).disabled).toBe(
      true
    );

    search.dispose();
    expect(document.querySelector('#results')?.innerHTML).toBe('');
  }, 20_000);
});
