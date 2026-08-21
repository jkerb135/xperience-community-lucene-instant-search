import { describe, expect, it, vi } from 'vitest';

import createSearch, {
  API_VERSION,
  API_VERSION_HEADER,
  getWidgetType,
  mountAll,
} from '@yourco/xperience-search';
import type { SearchResponse } from '@yourco/xperience-search';

import { dropdownFacet, registerDropdownFacet, WIDGET_TYPE } from '../src/dropdownFacet';

/** The documented response shape (docs/search-api.md, "POST /api/xpsearch/query"). */
const response: SearchResponse = {
  results: [{ id: 'web-page-42-en', score: 8.42, attributes: { title: 'Espresso Basics' } }],
  facets: {
    contentType: [
      { value: 'Article', label: 'Article', count: 34 },
      { value: 'Product', label: 'Product', count: 12 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 46,
  totalPages: 3,
  tookMs: 14,
  queryId: 'generated-guid',
};

const fetchFn = vi.fn<typeof fetch>(
  async () =>
    new Response(JSON.stringify(response), {
      status: 200,
      headers: { 'content-type': 'application/json', [API_VERSION_HEADER]: API_VERSION },
    }),
);

function mount() {
  const container = document.createElement('div');
  container.id = 'facet-content-type';
  document.body.append(container);

  const search = createSearch({
    index: 'site-content',
    fetchFn,
    debounceMs: 0,
    routing: false,
  });
  search.addWidgets([
    dropdownFacet({
      container,
      attribute: 'contentType',
      label: 'Content type',
      allLabel: 'Any type',
    }),
  ]);
  search.start();
  return { search, container };
}

const selectOf = (container: HTMLElement): HTMLSelectElement => {
  const select = container.querySelector('select');
  if (select === null) {
    throw new Error('the widget rendered no <select>');
  }
  return select;
};

const change = (select: HTMLSelectElement, value: string): void => {
  select.value = value;
  select.dispatchEvent(new Event('change'));
};

describe('dropdownFacet', () => {
  it('renders an "All" option plus one option per facet value', async () => {
    const { container } = mount();

    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(3));

    const select = selectOf(container);
    expect([...select.options].map((o) => o.text.trim())).toEqual([
      'Any type',
      'Article (34)',
      'Product (12)',
    ]);
    expect([...select.options].map((o) => o.value)).toEqual(['', 'Article', 'Product']);
    expect(select.value).toBe('');
    expect(container.querySelector('label')?.getAttribute('for')).toBe(select.id);
  });

  it('applies the chosen value and clears the previous one (single select)', async () => {
    const { search, container } = mount();
    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(3));

    change(selectOf(container), 'Article');
    expect(search.actions.getState().filters.facets).toEqual([
      { attribute: 'contentType', values: ['Article'] },
    ]);

    await vi.waitFor(() => expect(selectOf(container).value).toBe('Article'));

    change(selectOf(container), 'Product');
    expect(search.actions.getState().filters.facets).toEqual([
      { attribute: 'contentType', values: ['Product'] },
    ]);

    change(selectOf(container), '');
    expect(search.actions.getState().filters.facets.flatMap((f) => f.values)).toEqual([]);
  });
});

describe('Page Builder mount', () => {
  it('resolves data-xps-widget="myCompany.dropdownFacet" after registration', () => {
    registerDropdownFacet();
    expect(getWidgetType(WIDGET_TYPE)).toBeTypeOf('function');

    document.body.innerHTML = `<div class="xps-mount"
      data-xps-widget="${WIDGET_TYPE}"
      data-xps-instance="search-1"
      data-xps-instance-config='{"index":"site-content","searchOnInitialLoad":false}'
      data-xps-config='{"attribute":"brand","label":"Brand","allLabel":"Any brand"}'></div>`;

    const instances = mountAll(document);

    expect(instances).toHaveLength(1);
    const select = document.querySelector('.xps-dropdown-facet__select');
    expect(select).not.toBeNull();
    expect(document.querySelector('.xps-dropdown-facet__label')?.textContent).toBe('Brand');
  });
});
