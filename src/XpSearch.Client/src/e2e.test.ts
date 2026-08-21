/**
 * End to end against the mock server: two connector-based widgets, a real HTTP round trip,
 * a refinement, and the response flowing back into both widgets.
 */
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { startMockServer } from '../mock/server.ts';
import { QUERY_ROUTE } from './contract/constants';
import { connectHits } from './connectors/hits';
import { connectRefinementList } from './connectors/refinementList';
import { connectStats } from './connectors/stats';
import { xpsearch } from './instance';

interface Doc extends Record<string, unknown> {
  title: string;
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
    const hits: Array<Array<{ title: string }>> = [];
    const facets: Array<Array<{ value: string; count: number; isRefined: boolean }>> = [];
    const stats: Array<{ nbHits: number }> = [];

    const search = xpsearch({
      index: 'site-content',
      endpoint: `${server.url}${QUERY_ROUTE}`,
      eventsEndpoint: `${server.url}/api/xpsearch/events`,
      debounceMs: 10,
      highlight: { fields: ['title', 'content'] },
      initialState: { hitsPerPage: 5 },
    });

    search.addWidgets([
      connectHits<Doc>((options) => hits.push(options.hits.map((hit) => ({ title: hit.title }))))({}),
      connectRefinementList((options) =>
        facets.push(options.items.map(({ value, count, isRefined }) => ({ value, count, isRefined })))
      )({ attribute: 'contentType' }),
      connectStats((options) => stats.push({ nbHits: options.nbHits }))({}),
    ]);
    search.start();

    await vi.waitFor(() => expect(search.results).not.toBeNull(), { timeout: 2000 });
    expect(search.results?.nbHits).toBe(54);
    expect(hits[hits.length - 1]).toHaveLength(5);
    expect(facets[facets.length - 1]).toEqual(
      expect.arrayContaining([{ value: 'Article', count: 22, isRefined: false }])
    );
    expect(stats[stats.length - 1]?.nbHits).toBe(54);

    // Refine through the connector's own action, exactly as a renderer would.
    search.helper.setQuery('espresso').toggleFacetRefinement('contentType', 'Article').search();
    await vi.waitFor(() => expect(search.results?.nbHits).toBeLessThan(54), { timeout: 2000 });

    const results = search.results!;
    expect(results.hits.length).toBeGreaterThan(0);
    for (const hit of results.hits) {
      expect(String(hit['contentType'])).toBe('Article');
      // The term can match the title, the body or a tag — all three are searchable.
      expect(`${hit['title']} ${hit['content']} ${hit['tags']}`.toLowerCase()).toContain('espresso');
      expect(`${hit._highlights?.['title']}${hit._highlights?.['content']}`).toContain('<mark>');
    }
    expect(facets[facets.length - 1]?.find((item) => item.value === 'Article')?.isRefined).toBe(true);
    // Disjunctive counts: the other content types stay visible under an `or` refinement.
    expect(facets[facets.length - 1]!.length).toBeGreaterThan(1);

    search.sendEvent('click', results.hits[0]!.objectID, 1);
    search.dispose();
  });

  it('answers suggest and accepts events', async () => {
    const suggest = await fetch(`${server.url}/api/xpsearch/suggest`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ index: 'site-content', query: 'espresso', maxItems: 3 }),
    });
    const body = (await suggest.json()) as { suggestions: Array<{ text: string }> };
    expect(body.suggestions.length).toBeGreaterThan(0);
    expect(body.suggestions[0]?.text.toLowerCase()).toContain('espresso');

    const event = await fetch(`${server.url}/api/xpsearch/events`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ eventType: 'click', objectID: 'doc-1', queryId: 'q', position: 1 }),
    });
    expect(event.status).toBe(202);
  });
});
