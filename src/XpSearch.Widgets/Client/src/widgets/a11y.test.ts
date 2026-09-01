// @vitest-environment jsdom
/**
 * Automated accessibility gate (spec 5.6, spec 12): every default widget, rendered with results
 * from the mock corpus, must produce zero axe violations.
 *
 * jsdom has no layout engine, so two axe rule sets cannot be evaluated here and are disabled:
 * - `color-contrast` (https://dequeuniversity.com/rules/axe/4.13/color-contrast) needs computed
 *   colours and box geometry; it is a manual/browser check, and the widgets ship no colours
 *   anyway — the theme does (spec 6).
 * - the `cat.keyboard`/best-practice "region", "landmark-*" and "page-has-heading-one" rules are
 *   *page* rules, not widget rules; the run is restricted to the WCAG A/AA tags, which excludes
 *   them, because a widget fragment is not a page.
 */
import { describe, expect, it, vi } from 'vitest';
import axe from 'axe-core';
import { API_VERSION_HEADER, SUGGEST_ROUTE } from '../contract/constants';
import { query, suggest } from '../../mock/server.ts';
import { createSearch } from '../instance';
import type { SearchInstance } from '../types';
import {
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

const AXE_OPTIONS: axe.RunOptions = {
  runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
  rules: { 'color-contrast': { enabled: false } },
};

/** Answers from the mock corpus, so the DOM under test is the one a real response produces. */
const fetchFn = (async (url: string, init: RequestInit) => {
  const body = JSON.parse(String(init.body));
  const answer = String(url).endsWith(SUGGEST_ROUTE) ? suggest(body) : query(body);
  return new Response(JSON.stringify(answer), {
    status: 200,
    headers: { [API_VERSION_HEADER]: '1' },
  });
}) as unknown as typeof fetch;

function page(): SearchInstance {
  document.body.innerHTML = `<main>
    <h1>Search</h1>
    <div id="box"></div><div id="sort"></div><div id="resultStats"></div>
    <div id="facet"></div><div id="tree"></div><div id="toggle"></div><div id="chips"></div><div id="clear"></div>
    <div id="range"></div><div id="suggest"></div><div id="sheet"></div>
    <div id="results"></div><div id="pages"></div><div id="more"></div>
  </main>`;

  const search = createSearch({
    index: 'site-content',
    fetchFn,
    debounceMs: 0,
    initialState: { pageSize: 5 },
    highlight: { fields: ['title', 'content'] },
  });
  search.addWidgets([
    searchBox({ container: '#box', showSubmit: true }),
    sortSelect({
      container: '#sort',
      items: [
        { label: 'Relevance', value: 'relevance' },
        { label: 'Newest first', value: 'date_desc' },
      ],
    }),
    resultStats({ container: '#resultStats' }),
    facetList({
      container: '#facet',
      attribute: 'contentType',
      label: 'Content type',
      searchable: true,
      showMore: true,
      limit: 2,
    }),
    categoryTree({ container: '#tree', attribute: 'tags', label: 'Categories' }),
    toggleFilter({ container: '#toggle', attribute: 'language', value: 'en', label: 'English' }),
    activeFilters({
      container: '#chips',
      attributeLabels: { contentType: 'Content type' },
    }),
    clearFilters({ container: '#clear' }),
    rangeFilter({ container: '#range', attribute: 'price', label: 'Price', min: 5, max: 60, step: 5 }),
    suggestions({ container: '#suggest', debounceMs: 0 }),
    filterSort({
      container: '#sheet',
      facets: [{ attribute: 'contentType', label: 'Content type' }],
      sortOptions: [
        { label: 'Relevance', value: 'relevance' },
        { label: 'Newest first', value: 'date_desc' },
      ],
    }),
    results({ container: '#results' }),
    pagination({ container: '#pages', padding: 2 }),
    loadMore({ container: '#more' }),
  ]);
  search.start();
  return search;
}

const violations = async (): Promise<string[]> => {
  const result = await axe.run(document.body, AXE_OPTIONS);
  return result.violations.map(
    (violation) => `${violation.id}: ${violation.nodes.map((node) => node.html).join(' | ')}`
  );
};

describe('accessibility (axe-core)', () => {
  it('reports no violations with the filter & sort sheet open', async () => {
    const search = page();
    await vi.waitFor(() => expect(search.results).not.toBeNull(), { timeout: 3000 });

    document.querySelector<HTMLButtonElement>('.xps-filter-sort__trigger')!.click();
    expect(document.querySelector('.xps-sheet__panel')).not.toBeNull();
    expect(await violations()).toEqual([]);
    search.dispose();
  }, 20_000);

  it('reports no violations for the fourteen widgets with results', async () => {
    const search = page();
    search.actions.setQuery('espresso').search();
    await vi.waitFor(() => expect(search.results?.total).toBeGreaterThan(0), { timeout: 3000 });

    expect(await violations()).toEqual([]);
    search.dispose();
  }, 20_000);

  it('reports no violations with the suggestions popup open', async () => {
    const search = page();
    await vi.waitFor(() => expect(search.results).not.toBeNull(), { timeout: 3000 });
    // Closed first: the listbox is in the DOM either way, so aria-controls cannot dangle.
    expect(await violations()).toEqual([]);

    const input = document.querySelector('.xps-suggestions__input') as HTMLInputElement;
    input.value = 'Espresso';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.waitFor(() => expect(document.querySelectorAll('[role="option"]').length).toBeGreaterThan(0), {
      timeout: 3000,
    });
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(input.getAttribute('aria-activedescendant')).not.toBeNull();
    expect(await violations()).toEqual([]);
    search.dispose();
  }, 20_000);

  it('reports no violations with refinements applied and with no results', async () => {
    const search = page();
    search.actions
      .setQuery('espresso')
      .toggleFacet('contentType', 'Article')
      .toggleFacet('language', 'en')
      .search();
    await vi.waitFor(() => expect(search.results).not.toBeNull(), { timeout: 3000 });
    expect(document.querySelectorAll('.xps-chip').length).toBeGreaterThan(0);
    expect(await violations()).toEqual([]);

    search.actions.setQuery('zzzz-no-such-term').search();
    await vi.waitFor(() => expect(search.results?.total).toBe(0), { timeout: 3000 });
    expect(document.querySelector('.xps-results--empty')).not.toBeNull();
    expect(await violations()).toEqual([]);
    search.dispose();
  }, 20_000);
});
