// @vitest-environment jsdom
/**
 * Spec 12, "Extensibility" and "Connector coverage": the documented dropdown-facet example
 * (docs/guides/custom-widgets.md) built from the public connector API only — no `any`, no
 * imports from internal paths — and driven through a real instance.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { connectRefinementList } from '../connectors';
import { API_VERSION_HEADER } from '../contract/constants';
import type { SearchRequest, SearchResponse } from '../contract/generated';
import { xpsearch } from '../instance';
import type { InstantSearch } from '../types';

const BODY: SearchResponse = {
  hits: [],
  facets: { brand: { Rancilio: 8, Gaggia: 5 } },
  page: 0,
  hitsPerPage: 20,
  nbHits: 13,
  nbPages: 1,
  processingTimeMs: 3,
};

const dropdownFacet = connectRefinementList<{
  container: HTMLElement;
  label?: string;
  allLabel?: string;
}>((renderOptions, isFirstRender) => {
  const { items, refine, widgetParams } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = widgetParams;

  if (isFirstRender) {
    container.innerHTML = `
      <label class="xps-dropdown__label" for="${container.id}-select">${label}</label>
      <select class="xps-dropdown__select" id="${container.id}-select"></select>`;

    container.querySelector('select')!.addEventListener('change', (event) => {
      const current = renderOptions.items.find((item) => item.isRefined);
      if (current) refine(current.value);
      const picked = (event.target as HTMLSelectElement).value;
      if (picked) refine(picked);
    });
  }

  const select = container.querySelector('select')!;
  select.innerHTML =
    `<option value="">${allLabel}</option>` +
    items
      .map(
        (item) =>
          `<option value="${item.value}" ${item.isRefined ? 'selected' : ''}>${item.label} (${item.count})</option>`
      )
      .join('');
  select.value = items.find((item) => item.isRefined)?.value ?? '';
});

let search: InstantSearch | undefined;
afterEach(() => {
  search?.dispose();
  search = undefined;
  document.body.innerHTML = '';
});

describe('the documented dropdown facet', () => {
  it('renders the facet values and refines through the select', async () => {
    const requests: SearchRequest[] = [];
    const fetchFn = vi.fn(async (_url: string, init: RequestInit) => {
      requests.push(JSON.parse(String(init.body)) as SearchRequest);
      return new Response(JSON.stringify(BODY), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    });

    const container = document.createElement('div');
    container.id = 'facet-brand';
    document.body.append(container);

    search = xpsearch({
      index: 'site-content',
      debounceMs: 0,
      fetchFn: fetchFn as unknown as typeof fetch,
    });
    search.addWidgets([
      dropdownFacet({ container, attribute: 'brand', label: 'Brand', limit: 50, sortBy: ['name:asc'] }),
    ]);
    search.start();

    await vi.waitFor(() => expect(container.textContent).toContain('Rancilio (8)'));
    expect(requests[0]?.facets).toEqual(['brand']);
    expect(container.querySelector('label')?.textContent).toBe('Brand');

    const select = container.querySelector('select')!;
    select.value = 'Gaggia';
    select.dispatchEvent(new Event('change'));

    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]?.facetFilters).toEqual([['brand:Gaggia']]);
    await vi.waitFor(() => expect(select.value).toBe('Gaggia'));
  });
});
