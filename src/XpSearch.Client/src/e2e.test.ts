/**
 * End to end against the mock server: three behaviour-based widgets, a real HTTP round trip,
 * a filter, and the response flowing back into every widget.
 */
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { startMockServer } from '../mock/server.ts';
import { QUERY_ROUTE } from './contract/constants';
import { withFacetList } from './behaviors/facetList';
import { withResults } from './behaviors/results';
import { withResultStats } from './behaviors/resultStats';
import { createSearch } from './instance';

interface Doc extends Record<string, unknown> {
  title: string;
  content: string;
  contentType: string;
  tags: string[];
  url: string;
  price: number;
}

let server: Awaited<ReturnType<typeof startMockServer>>;

beforeAll(async () => {
  server = await startMockServer(0);
});
afterAll(async () => {
  await server.close();
});

describe('against the mock server', () => {
  it('searches, refines and delivers the response to every widget', async () => {
    const rendered: Array<Array<{ title: string }>> = [];
    const facets: Array<Array<{ value: string; label: string; count: number; isActive: boolean }>> = [];
    const stats: Array<{ total: number }> = [];

    const search = createSearch({
      index: 'site-content',
      endpoint: `${server.url}${QUERY_ROUTE}`,
      eventsEndpoint: `${server.url}/api/xpsearch/events`,
      debounceMs: 10,
      highlight: { fields: ['title', 'content'] },
      initialState: { pageSize: 5 },
    });

    search.addWidgets([
      withResults<Doc>((options) =>
        rendered.push(options.items.map((result) => ({ title: result.attributes.title })))
      )({}),
      withFacetList((options) =>
        facets.push(
          options.items.map(({ value, label, count, isActive }) => ({ value, label, count, isActive }))
        )
      )({ attribute: 'contentType' }),
      withResultStats((options) => stats.push({ total: options.total }))({}),
    ]);
    search.start();

    await vi.waitFor(() => expect(search.results).not.toBeNull(), { timeout: 2000 });
    expect(search.results?.total).toBe(54);
    expect(rendered[rendered.length - 1]).toHaveLength(5);
    expect(facets[facets.length - 1]).toEqual(
      expect.arrayContaining([{ value: 'Article', label: 'Article', count: 22, isActive: false }])
    );
    expect(stats[stats.length - 1]?.total).toBe(54);

    // Filter through the behaviour's own action, exactly as a renderer would.
    search.actions.setQuery('espresso').toggleFacet('contentType', 'Article').search();
    await vi.waitFor(() => expect(search.results?.total).toBeLessThan(54), { timeout: 2000 });

    const response = search.results!;
    expect(response.results.length).toBeGreaterThan(0);
    for (const result of response.results) {
      const { title, content, contentType, tags } = result.attributes as Doc;
      expect(contentType).toBe('Article');
      // The term can match the title, the body or a tag — all three are searchable.
      expect(`${title} ${content} ${tags}`.toLowerCase()).toContain('espresso');
      expect(`${result.highlights?.['title']}${result.highlights?.['content']}`).toContain('<mark>');
    }
    expect(facets[facets.length - 1]?.find((item) => item.value === 'Article')?.isActive).toBe(true);
    // Disjunctive counts: the other content types stay visible under an `or` filter.
    expect(facets[facets.length - 1]!.length).toBeGreaterThan(1);

    search.sendEvent('click', response.results[0]!.id, 1);
    search.dispose();
  });

  it('answers suggest and accepts events', async () => {
    const suggest = await fetch(`${server.url}/api/xpsearch/suggest`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ index: 'site-content', query: 'espresso', limit: 3 }),
    });
    const body = (await suggest.json()) as { suggestions: Array<{ text: string }> };
    expect(body.suggestions.length).toBeGreaterThan(0);
    expect(body.suggestions[0]?.text.toLowerCase()).toContain('espresso');

    const event = await fetch(`${server.url}/api/xpsearch/events`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ type: 'click', resultId: 'doc-1', queryId: 'q', position: 1 }),
    });
    expect(event.status).toBe(202);
  });
});
