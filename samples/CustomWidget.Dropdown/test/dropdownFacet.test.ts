import { describe, expect, it, vi } from 'vitest';

import createSearch, {
  API_VERSION,
  API_VERSION_HEADER,
  getWidgetType,
  mountAll,
} from '@xperience-community/xperience-search';
import type { SearchResponse } from '@xperience-community/xperience-search';

import { dropdownFacet, registerDropdownFacet, WIDGET_TYPE } from '../src/dropdownFacet';

/** The documented response shape (docs/guides/search-api.md, "POST /api/xpsearch/query"). */
const response: SearchResponse = {
  results: [{ id: 'web-page-42-en', score: 8.42, attributes: { title: 'Espresso Basics' } }],
  facets: {
    contentType: [
      { value: 'Article', label: 'Article', count: 34 },
      { value: 'Product', label: 'Product', count: 12 },
      { value: 'faq"s', label: 'FAQ "s" & <b>bold</b>', count: 1 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 47,
  totalPages: 3,
  tookMs: 14,
  queryId: 'generated-guid',
  redirect: null,
};

const fetchFn = vi.fn<typeof fetch>(
  async () =>
    new Response(JSON.stringify(response), {
      status: 200,
      headers: { 'content-type': 'application/json', [API_VERSION_HEADER]: API_VERSION },
    }),
);

function mount(label = 'Content type') {
  const container = document.createElement('div');
  container.setAttribute('data-xps-instance', 'search-1');
  document.body.append(container);

  const search = createSearch({
    index: 'site-content',
    fetchFn,
    debounceMs: 0,
    routing: false,
  });
  search.addWidgets([
    dropdownFacet({ container, attribute: 'contentType', label, allLabel: 'Any type' }),
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
  it('renders an "All" option plus one option per facet value, on the documented classes', async () => {
    const { container } = mount();

    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(4));

    const select = selectOf(container);
    expect([...select.options].map((o) => o.text.trim())).toEqual([
      'Any type',
      'Article (34)',
      'Product (12)',
      'FAQ "s" & <b>bold</b> (1)',
    ]);
    expect([...select.options].map((o) => o.value)).toEqual(['', 'Article', 'Product', 'faq"s']);
    expect(select.value).toBe('');

    // Shell contract: xps on the root, the shared select block, the id pattern of MARKUP.md rule 4.
    const root = container.querySelector('div');
    expect([...(root?.classList ?? [])]).toEqual(['xps', 'xps-stack', 'xps-select']);
    expect(select.className).toBe('xps-select__control');
    expect(select.id).toBe('xps-search-1-dropdown-facet-control');
    expect(container.querySelector('label')?.getAttribute('for')).toBe(select.id);
  });

  it('escapes editor and taxonomy text instead of interpolating it into markup', async () => {
    const { container } = mount('Type <img src=x onerror=alert(1)>');
    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(4));

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('label')?.textContent).toBe(
      'Type <img src=x onerror=alert(1)>',
    );
    // A quote in a taxonomy code name stays inside the attribute.
    expect([...selectOf(container).options][3]?.value).toBe('faq"s');
  });

  it('applies the chosen value and clears the previous one (single select)', async () => {
    const { search, container } = mount();
    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(4));

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

  it('stays single-select when two changes happen without a render in between', async () => {
    const { search, container } = mount();
    await vi.waitFor(() => expect(selectOf(container).options.length).toBe(4));

    const select = selectOf(container);
    change(select, 'Article');
    change(select, 'Product'); // no await: the re-render is still queued

    expect(search.actions.getState().filters.facets).toEqual([
      { attribute: 'contentType', values: ['Product'] },
    ]);
  });
});

describe('Page Builder mount', () => {
  it('resolves data-xps-widget="myCompany.dropdownFacet" after registration', () => {
    registerDropdownFacet();
    expect(getWidgetType(WIDGET_TYPE)).toBeTypeOf('function');

    document.body.innerHTML = `<div class="xps-mount"
      data-xps-widget="${WIDGET_TYPE}"
      data-xps-instance="search-2"
      data-xps-instance-config='{"index":"site-content","searchOnInitialLoad":false}'
      data-xps-config='{"attribute":"brand","label":"Brand","allLabel":"Any brand"}'></div>`;

    const instances = mountAll(document);

    expect(instances).toHaveLength(1);
    expect(document.querySelector('.xps-select__control')).not.toBeNull();
    expect(document.querySelector('.xps-select__label')?.textContent).toBe('Brand');
  });

  it('skips the widget with one console.error when the editor left "attribute" empty', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    registerDropdownFacet();

    document.body.innerHTML = `<div class="xps-mount"
      data-xps-widget="${WIDGET_TYPE}"
      data-xps-instance="search-3"
      data-xps-instance-config='{"index":"site-content","searchOnInitialLoad":false}'
      data-xps-config='{"label":"Brand"}'></div>`;

    mountAll(document);

    expect(consoleError.mock.calls.flat().join(' ')).toContain('failed to build');
    consoleError.mockRestore();
  });
});
