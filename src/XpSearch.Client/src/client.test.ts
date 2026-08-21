import { describe, expect, it, vi } from 'vitest';
import { SearchClient, SearchError } from './client';
import { API_VERSION_HEADER } from './contract/constants';
import type { SearchRequest, SearchResponse } from './contract/generated';

const REQUEST: SearchRequest = { index: 'site-content', query: 'espresso' };

function response(body: Partial<SearchResponse>, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { [API_VERSION_HEADER]: '1' },
    ...init,
  });
}

const ok = (queryId: string): Response =>
  response({ hits: [], page: 0, hitsPerPage: 20, nbHits: 0, nbPages: 0, processingTimeMs: 1, queryId });

describe('SearchClient', () => {
  it('debounces: three keystrokes make one request and only the last answer is delivered', async () => {
    const fetchFn = vi.fn(async (_url: string, _init: RequestInit) => ok('third'));
    const client = new SearchClient({ debounceMs: 20, fetchFn: fetchFn as unknown as typeof fetch });

    const first = client.search({ ...REQUEST, query: 'e' });
    const second = client.search({ ...REQUEST, query: 'es' });
    const third = client.search({ ...REQUEST, query: 'esp' });

    expect(await first).toBeNull();
    expect(await second).toBeNull();
    expect((await third)?.queryId).toBe('third');
    expect(fetchFn).toHaveBeenCalledTimes(1);
    expect(JSON.parse(String(fetchFn.mock.calls[0]![1].body)).query).toBe('esp');
  });

  it('aborts the in-flight request and never delivers its stale answer', async () => {
    const aborted: boolean[] = [];
    const fetchFn = vi.fn((_url: string, init: RequestInit) => {
      const slow = init.body?.toString().includes('"slow"');
      return new Promise<Response>((resolve, reject) => {
        const timer = setTimeout(() => resolve(ok(slow ? 'stale' : 'fresh')), slow ? 50 : 0);
        init.signal?.addEventListener('abort', () => {
          clearTimeout(timer);
          aborted.push(true);
          reject(new DOMException('Aborted', 'AbortError'));
        });
      });
    });
    const client = new SearchClient({ debounceMs: 0, fetchFn: fetchFn as unknown as typeof fetch });

    const stale = client.search({ ...REQUEST, query: 'slow' });
    await new Promise((r) => setTimeout(r, 10));
    const fresh = client.search({ ...REQUEST, query: 'fast' });

    expect(await stale).toBeNull();
    expect((await fresh)?.queryId).toBe('fresh');
    expect(aborted).toEqual([true]);
  });

  it('retries a 500 twice and then succeeds', async () => {
    const fetchFn = vi
      .fn()
      .mockResolvedValueOnce(response({}, { status: 500 }))
      .mockResolvedValueOnce(response({}, { status: 503 }))
      .mockResolvedValueOnce(ok('recovered'));
    const client = new SearchClient({
      debounceMs: 0,
      retryDelayMs: 1,
      fetchFn: fetchFn as unknown as typeof fetch,
    });
    expect((await client.search(REQUEST))?.queryId).toBe('recovered');
    expect(fetchFn).toHaveBeenCalledTimes(3);
  });

  it('retries a network error but never a 4xx', async () => {
    const network = vi.fn().mockRejectedValueOnce(new TypeError('fetch failed')).mockResolvedValue(ok('after-network-error'));
    const client = new SearchClient({ debounceMs: 0, retryDelayMs: 1, fetchFn: network as unknown as typeof fetch });
    expect((await client.search(REQUEST))?.queryId).toBe('after-network-error');
    expect(network).toHaveBeenCalledTimes(2);

    const bad = vi.fn().mockResolvedValue(response({}, { status: 400 }));
    const strict = new SearchClient({ debounceMs: 0, retryDelayMs: 1, fetchFn: bad as unknown as typeof fetch });
    await expect(strict.search(REQUEST)).rejects.toBeInstanceOf(SearchError);
    expect(bad).toHaveBeenCalledTimes(1);
  });

  it('gives up after the configured number of retries', async () => {
    const fetchFn = vi.fn().mockResolvedValue(response({}, { status: 500 }));
    const client = new SearchClient({ debounceMs: 0, retryDelayMs: 1, retries: 2, fetchFn: fetchFn as unknown as typeof fetch });
    await expect(client.search(REQUEST)).rejects.toThrow(/answered 500/);
    expect(fetchFn).toHaveBeenCalledTimes(3);
  });

  it('surfaces a contract-version mismatch without throwing, once per version', async () => {
    const errors: Error[] = [];
    const fetchFn = vi.fn(async () =>
      response({ hits: [], page: 0, hitsPerPage: 20, nbHits: 0, nbPages: 0, processingTimeMs: 1 }, {
        headers: { [API_VERSION_HEADER]: '2' },
      })
    );
    const client = new SearchClient({
      debounceMs: 0,
      fetchFn: fetchFn as unknown as typeof fetch,
      onError: (error) => errors.push(error),
    });
    expect(await client.search(REQUEST)).not.toBeNull();
    expect(await client.search(REQUEST)).not.toBeNull();
    expect(errors).toHaveLength(1);
    expect(errors[0]?.message).toMatch(/Contract version mismatch.*answered 2/);
  });

  it('swallows event failures — click tracking never breaks search', async () => {
    const errors: Error[] = [];
    const fetchFn = vi.fn().mockRejectedValue(new TypeError('offline'));
    const client = new SearchClient({
      retries: 0,
      fetchFn: fetchFn as unknown as typeof fetch,
      onError: (error) => errors.push(error),
    });
    expect(() =>
      client.sendEvent({ eventType: 'click', objectID: 'doc-1', queryId: 'q1', position: 1 })
    ).not.toThrow();
    await vi.waitFor(() => expect(errors).toHaveLength(1));
  });

  it('posts suggest requests to the suggest route', async () => {
    const fetchFn = vi.fn(async (_url: string, _init: RequestInit) =>
      response({ suggestions: [{ text: 'Espresso Basics' }] } as never)
    );
    const client = new SearchClient({ fetchFn: fetchFn as unknown as typeof fetch });
    const suggestions = await client.suggest({ index: 'site-content', query: 'esp' });
    expect(suggestions.suggestions[0]?.text).toBe('Espresso Basics');
    expect(fetchFn.mock.calls[0]![0]).toBe('/api/xpsearch/suggest');
  });
});
