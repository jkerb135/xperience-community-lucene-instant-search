// @vitest-environment jsdom
/**
 * The demo page's widget set against the mock server over real HTTP: type, refine, sort,
 * paginate, clear — asserting the rendered DOM each time, not the state.
 */
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { startMockServer } from '../../mock/server.ts';
import { QUERY_ROUTE, SUGGEST_ROUTE } from '../contract/constants';
import { createSearch } from '../instance';
import type { SearchInstance } from '../types';
import {
  categoryTree,
  clearFilters,
  activeFilters,
  loadMore,
  results,
  pagination,
  facetList,
  searchBox,
  sortSelect,
  suggestions,
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
      <div id="facet"></div><div id="tree"></div><div id="chips"></div><div id="clear"></div>
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
      categoryTree({ container: '#tree', attribute: 'tags', label: 'Categories' }),
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

    // Drill into the taxonomy: the mock's `tags` are hierarchical, so the tree nests and a
    // parent's count is the roll-up of everything below it.
    const tree = document.querySelector('.xps-category-tree') as HTMLElement;
    const nested = tree.querySelector('.xps-category-tree__list--lvl1') as HTMLElement;
    expect(nested).not.toBeNull();
    const child = nested.querySelector('a[data-xps-value]') as HTMLAnchorElement;
    const parentValue = child.closest('.xps-category-tree__list')!
      .closest('.xps-category-tree__item')!
      .querySelector('a[data-xps-value]')!
      .getAttribute('data-xps-value');
    child.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    await settled(
      search,
      () => tree.querySelectorAll('[aria-current="true"]').length === 2
    );
    // The child and its parent are both on the open path.
    expect(
      [...tree.querySelectorAll('a[aria-current="true"]')].map((link) =>
        link.getAttribute('data-xps-value')
      )
    ).toEqual([parentValue, child.getAttribute('data-xps-value')]);

    // Selecting the open node again clears the attribute.
    const open = [...tree.querySelectorAll<HTMLAnchorElement>('a[aria-current="true"]')].pop()!;
    open.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    await settled(search, () => tree.querySelectorAll('[aria-current="true"]').length === 0);

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

  it('accumulates pages and autocompletes over the same transport', async () => {
    document.body.innerHTML = '<div id="suggest"></div><div id="more"></div>';
    const assign = vi.fn();

    const search = createSearch({
      index: 'site-content',
      endpoint: `${server.url}${QUERY_ROUTE}`,
      suggestEndpoint: `${server.url}${SUGGEST_ROUTE}`,
      debounceMs: 5,
      initialState: { pageSize: 5 },
    });
    search.addWidgets([
      suggestions({
        container: '#suggest',
        debounceMs: 0,
        windowRef: { location: { assign } } as unknown as Window,
      }),
      loadMore({ container: '#more' }),
    ]);
    search.start();

    const items = (): number => document.querySelectorAll('.xps-load-more__item').length;
    const status = (): string =>
      document.querySelector('.xps-load-more__status')?.textContent ?? '';

    await settled(search, () => items() === 5);
    expect(status()).toBe('Showing 5 of 54 results');

    // The button appends the next page; the <ol> is never rebuilt.
    const list = document.querySelector('.xps-load-more__list');
    (document.querySelector('.xps-load-more__load-more') as HTMLButtonElement).click();
    await settled(search, () => items() === 10);
    expect(document.querySelector('.xps-load-more__list')).toBe(list);
    expect(status()).toBe('Showing 10 of 54 results');

    // Autocomplete over the real /suggest route.
    const input = document.querySelector('.xps-suggestions__input') as HTMLInputElement;
    input.value = 'Espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settled(search, () => document.querySelectorAll('[role="option"]').length > 0);
    expect(document.querySelector('.xps-suggestions--open')).not.toBeNull();

    // Typing searched in place, so the accumulated list was rebuilt for the new query.
    await settled(search, () => items() > 0 && items() <= 5);

    (document.querySelector('[role="option"]') as HTMLElement).click();
    expect(assign).toHaveBeenCalledWith(expect.stringContaining('/docs/espresso-basics'));

    search.dispose();
  }, 20_000);
});
