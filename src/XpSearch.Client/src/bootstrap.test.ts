// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mountAll, registerWidgetType } from './bootstrap';
import { withFacetList } from './behaviors/facetList';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';
import type { SearchInstance } from './types';

const BODY: SearchResponse = {
  results: [{ id: 'doc-1', attributes: { title: 'Espresso Basics' } }],
  facets: {
    tags: [
      { value: 'coffee', label: 'Coffee', count: 12 },
      { value: 'milk', label: 'Milk drinks', count: 3 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
  tookMs: 2,
};

const requests: SearchRequest[] = [];

beforeEach(() => {
  requests.length = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, init: RequestInit) => {
      requests.push(JSON.parse(String(init.body)) as SearchRequest);
      return new Response(JSON.stringify(BODY), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    })
  );
  document.body.innerHTML = '';
});

const started: SearchInstance[] = [];
afterEach(() => {
  while (started.length > 0) started.pop()?.dispose();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/** A minimal renderer, standing in for what js-widgets will register. */
const facetWidget = withFacetList<{ container: HTMLElement }>((options) => {
  options.params.container.innerHTML = options.items
    .map((item) => `<button data-value="${item.value}" aria-pressed="${item.isActive}">${item.label} (${item.count})</button>`)
    .join('');
});

describe('registerWidgetType (spec 5.7 guardrails)', () => {
  it('rejects a custom identifier without a dot', () => {
    expect(() => registerWidgetType('ratingFilter', () => ({}))).toThrow(/must contain a dot/);
  });

  it('accepts a namespaced identifier and the reserved first-party names', () => {
    expect(() => registerWidgetType('myCompany.ratingFilter', () => ({}))).not.toThrow();
    expect(() => registerWidgetType('facetList', (config) => facetWidget(config as { container: HTMLElement; attribute: string }))).not.toThrow();
  });

  it('rejects an empty id or a non-function factory', () => {
    expect(() => registerWidgetType('', () => ({}))).toThrow(/non-empty string/);
    expect(() =>
      registerWidgetType('myCompany.broken', undefined as unknown as () => never)
    ).toThrow(/must be a function/);
  });
});

describe('mountAll (spec 7.1)', () => {
  const mount = (attributes: Record<string, string>): HTMLElement => {
    const element = document.createElement('div');
    element.className = 'xps-mount';
    for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, value);
    document.body.append(element);
    return element;
  };

  it('groups mounts by data-xps-instance and starts one instance per group', async () => {
    registerWidgetType('facetList', (config) =>
      facetWidget(config as { container: HTMLElement; attribute: string })
    );
    const a = mount({
      'data-xps-widget': 'facetList',
      'data-xps-instance': 'search-1',
      'data-xps-instance-config': '{"index":"index-a"}',
      'data-xps-config': '{"attribute":"tags"}',
    });
    const b = mount({
      'data-xps-widget': 'facetList',
      'data-xps-instance': 'search-2',
      'data-xps-instance-config': '{"index":"index-b"}',
      'data-xps-config': '{"attribute":"tags"}',
    });

    started.push(...mountAll());
    expect(started).toHaveLength(2);
    await vi.waitFor(() => expect(a.innerHTML).toContain('Coffee (12)'));
    expect(b.innerHTML).toContain('Coffee (12)');
    expect(requests.map((request) => request.index).sort()).toEqual(['index-a', 'index-b']);

    // Multi-instance (spec 12): refining one leaves the other's state and DOM alone.
    const before = b.innerHTML;
    started[0]!.actions.toggleFacet('tags', 'coffee').search();
    await vi.waitFor(() => expect(a.innerHTML).toContain('aria-pressed="true"'));
    expect(b.innerHTML).toBe(before);
    expect(started[1]!.state.filters.facets).toEqual([]);
  });

  it('defaults the group to "default" and takes instance options from any mount in it', async () => {
    registerWidgetType('facetList', (config) =>
      facetWidget(config as { container: HTMLElement; attribute: string })
    );
    mount({ 'data-xps-widget': 'facetList', 'data-xps-config': '{"attribute":"tags"}' });
    mount({
      'data-xps-widget': 'facetList',
      'data-xps-instance-config': '{"index":"site-content"}',
      'data-xps-config': '{"attribute":"contentType"}',
    });

    started.push(...mountAll());
    expect(started).toHaveLength(1);
    await vi.waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]?.facets?.sort()).toEqual(['contentType', 'tags']);
  });

  it('skips an unknown widget type with a console error instead of throwing', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    registerWidgetType('facetList', (config) =>
      facetWidget(config as { container: HTMLElement; attribute: string })
    );
    const unknown = mount({
      'data-xps-widget': 'nobody.knowsMe',
      'data-xps-instance-config': '{"index":"site-content"}',
    });
    const known = mount({
      'data-xps-widget': 'facetList',
      'data-xps-config': '{"attribute":"tags"}',
    });

    expect(() => started.push(...mountAll())).not.toThrow();
    await vi.waitFor(() => expect(known.innerHTML).toContain('Coffee (12)'));
    expect(unknown.innerHTML).toBe('');
    expect(consoleError.mock.calls[0]?.[0]).toMatch(/unknown widget type "nobody.knowsMe"/);
  });

  it('skips a group with no index and does not throw', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    mount({ 'data-xps-widget': 'facetList', 'data-xps-config': '{"attribute":"tags"}' });
    expect(mountAll()).toEqual([]);
    expect(consoleError.mock.calls[0]?.[0]).toMatch(/no usable data-xps-instance-config/);
  });

  it('skips a mount whose config is not valid JSON, and mounts the rest', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    registerWidgetType('facetList', (config) =>
      facetWidget(config as { container: HTMLElement; attribute: string })
    );
    mount({
      'data-xps-widget': 'facetList',
      'data-xps-instance-config': '{"index":"site-content"}',
      'data-xps-config': '{not json}',
    });
    const good = mount({
      'data-xps-widget': 'facetList',
      'data-xps-config': '{"attribute":"tags"}',
    });
    started.push(...mountAll());
    await vi.waitFor(() => expect(good.innerHTML).toContain('Coffee (12)'));
    expect(consoleError.mock.calls[0]?.[0]).toMatch(/data-xps-config is not valid JSON/);
  });

  it('does not mount the same element twice', () => {
    registerWidgetType('facetList', (config) =>
      facetWidget(config as { container: HTMLElement; attribute: string })
    );
    mount({
      'data-xps-widget': 'facetList',
      'data-xps-instance-config': '{"index":"site-content"}',
      'data-xps-config': '{"attribute":"tags"}',
    });
    started.push(...mountAll());
    expect(mountAll()).toEqual([]);
  });
});
