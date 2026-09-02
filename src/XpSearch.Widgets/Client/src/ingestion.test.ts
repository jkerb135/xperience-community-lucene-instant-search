/**
 * The Node ingestion client (CL-1): the same matrix as `XpSearch.Client.Tests` — batching at both
 * caps, aggregation, the backoff schedule, `Retry-After`, no retry on a 400, partial failure and the
 * bearer header — plus one end-to-end happy path against the mock server.
 */
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { MOCK_API_KEY, PUSHED, startMockServer } from '../mock/server.ts';
import { createIngestionClient, XpSearchIngestionError, type IngestionClientOptions } from './ingestion';
import type { PushDocument } from './contract/ingestion-generated';

interface Call {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: unknown;
}

const ok = (body: unknown, status = 200, headers: Record<string, string> = {}): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', ...headers } });

const UPSERT_OK = { indexed: 1, failed: 0, errors: [], taskId: 't', tookMs: 5 };

/** A scripted fetch: each step answers or throws, and every call is recorded. */
function stub(steps: Array<Response | Error | (() => Response | Error)>) {
  const calls: Call[] = [];
  let at = 0;
  const fetchFn = (async (url: string | URL | Request, init?: RequestInit) => {
    calls.push({
      url: String(url),
      method: init?.method ?? 'GET',
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: init?.body === undefined ? undefined : JSON.parse(init.body as string),
    });
    const step = steps[Math.min(at++, steps.length - 1)]!;
    const answer = typeof step === 'function' ? step() : step;
    if (answer instanceof Error) throw answer;
    return answer.clone();
  }) as unknown as typeof fetch;
  return { calls, fetchFn };
}

const delays: number[] = [];

function client(steps: Array<Response | Error>, options: Partial<IngestionClientOptions> = {}) {
  const { calls, fetchFn } = stub(steps);
  delays.length = 0;
  return {
    calls,
    client: createIngestionClient({
      endpoint: 'https://example.com',
      apiKey: 'xps_secret',
      fetchFn,
      // The schedule is asserted, so the jitter is pinned and nothing sleeps.
      randomFn: () => 1,
      sleepFn: (ms) => {
        delays.push(ms);
        return Promise.resolve();
      },
      ...options,
    }),
  };
}

/** Awaits a rejection and returns it typed; fails loudly if the call unexpectedly succeeds. */
async function rejection(promise: Promise<unknown>): Promise<XpSearchIngestionError> {
  try {
    await promise;
  } catch (error) {
    return error as XpSearchIngestionError;
  }
  throw new Error('expected the call to reject');
}

const documents = (count: number): PushDocument[] =>
  Array.from({ length: count }, (_, i) => ({ id: `id-${i + 1}`, _source: 'pim', title: `Doc ${i + 1}` }));

describe('createIngestionClient', () => {
  it('sends the bearer key to the documents route', async () => {
    const scripted = client([ok(UPSERT_OK)]);

    await scripted.client.index('products').upsert(documents(1));

    expect(scripted.calls[0]).toMatchObject({
      url: 'https://example.com/api/xpsearch/admin/indexes/products/documents',
      method: 'POST',
      headers: { authorization: 'Bearer xps_secret', 'content-type': 'application/json' },
      body: { documents: [{ id: 'id-1', _source: 'pim', title: 'Doc 1' }] },
    });
  });

  it('splits on the document count cap', async () => {
    const scripted = client([ok(UPSERT_OK)], { maxDocumentsPerRequest: 2 });

    const result = await scripted.client.index('products').upsert(documents(5));

    expect(scripted.calls.map((call) => (call.body as { documents: unknown[] }).documents.length)).toEqual([2, 2, 1]);
    expect(result.batches).toBe(3);
    expect(result.indexed).toBe(3);
    expect(result.taskIds).toEqual(['t', 't', 't']);
  });

  it('splits on the body size cap', async () => {
    const scripted = client([ok(UPSERT_OK)], { maxRequestBytes: 150 });

    const result = await scripted.client.index('products').upsert(documents(5));

    expect(result.batches).toBeGreaterThan(1);
    expect(scripted.calls.map((call) => (call.body as { documents: unknown[] }).documents.length).reduce((a, b) => a + b)).toBe(5);
    for (const call of scripted.calls) {
      expect(JSON.stringify(call.body).length).toBeLessThanOrEqual(150);
    }
  });

  it('aggregates totals and every per-document error', async () => {
    const scripted = client(
      [
        ok({ indexed: 1, failed: 1, errors: [{ id: 'id-2', field: 'price', message: 'not a number' }], taskId: 'a', tookMs: 5 }),
        ok({ indexed: 2, failed: 0, errors: [], tookMs: 5 }),
      ],
      { maxDocumentsPerRequest: 2 }
    );

    const result = await scripted.client.index('products').upsert(documents(4));

    expect(result).toMatchObject({ indexed: 3, failed: 1, batches: 2, taskIds: ['a'] });
    expect(result.errors).toEqual([{ id: 'id-2', field: 'price', message: 'not a number' }]);
  });

  it('reports what was already written when a later batch fails', async () => {
    const scripted = client(
      [ok({ indexed: 2, failed: 0, errors: [], taskId: 'a', tookMs: 5 }), new TypeError('fetch failed')],
      { maxDocumentsPerRequest: 2, maxAttempts: 1 }
    );

    const error = await rejection(scripted.client.index('products').upsert(documents(4)));

    expect(error).toBeInstanceOf(XpSearchIngestionError);
    expect(error.partialUpsert).toMatchObject({ indexed: 2, batches: 1 });
    expect(error.cause).toBeInstanceOf(TypeError);
  });

  it('backs off exponentially on retryable statuses and transport failures', async () => {
    const scripted = client([ok({}, 408), new TypeError('fetch failed'), ok({}, 500), ok(UPSERT_OK)]);

    await scripted.client.index('products').upsert(documents(1));

    expect(scripted.calls).toHaveLength(4);
    expect(delays).toEqual([500, 1000, 2000]);
  });

  it('honours Retry-After instead of the backoff, capped at maxRetryMs', async () => {
    const scripted = client([ok({}, 429, { 'retry-after': '7' }), ok({}, 503, { 'retry-after': '600' }), ok(UPSERT_OK)]);

    await scripted.client.index('products').upsert(documents(1));

    expect(delays).toEqual([7000, 30_000]);
  });

  it('never retries a validation failure and surfaces the problem details', async () => {
    const scripted = client([
      ok({ title: 'The request is not valid.', status: 400, errors: { documents: ['At least one document is required.'] } }, 400),
    ]);

    const error = await rejection(scripted.client.index('products').upsert(documents(1)));

    expect(scripted.calls).toHaveLength(1);
    expect(delays).toEqual([]);
    expect(error.status).toBe(400);
    expect(error.problem?.errors?.['documents']).toEqual(['At least one document is required.']);
  });

  it('maps every verb onto its frozen route', async () => {
    const scripted = client([ok({ indexed: 0, failed: 0, errors: [], deleted: 1, tookMs: 1, indexes: [] })]);
    const index = scripted.client.index('products');

    await scripted.client.listIndexes();
    await index.status();
    await index.patch('a b', { price: 9.5 }, { waitForIndex: true });
    await index.delete('a b');
    await index.deleteMany(['x', 'y', 'z'], { waitForIndex: true });
    await index.clear('pim');
    await index.clear();
    await index.rebuild();

    expect(scripted.calls.map((call) => `${call.method} ${call.url.replace('https://example.com/api/xpsearch/admin/', '')}`)).toEqual([
      'GET indexes',
      'GET indexes/products/status',
      'PATCH indexes/products/documents/a%20b?waitForIndex=true',
      'DELETE indexes/products/documents/a%20b',
      'POST indexes/products/documents/delete?waitForIndex=true',
      'POST indexes/products/clear?source=pim',
      'POST indexes/products/clear',
      'POST indexes/products/rebuild',
    ]);
    // The patch body IS the attribute bag.
    expect(scripted.calls[2]?.body).toEqual({ price: 9.5 });
  });

  it('splits deleteMany on the count cap and sums the deletes', async () => {
    const scripted = client([ok({ deleted: 1, taskId: 't', tookMs: 1 })], { maxDocumentsPerRequest: 2 });

    const result = await scripted.client.index('products').deleteMany(['a', 'b', 'c']);

    expect(scripted.calls.map((call) => call.body)).toEqual([{ ids: ['a', 'b'] }, { ids: ['c'] }]);
    expect(result).toMatchObject({ deleted: 2, batches: 2 });
  });

  it('refuses a missing endpoint or key', () => {
    expect(() => createIngestionClient({ endpoint: '', apiKey: 'k' })).toThrow(TypeError);
    expect(() => createIngestionClient({ endpoint: 'https://example.com', apiKey: '' })).toThrow(TypeError);
  });
});

describe('against the mock server', () => {
  let server: Awaited<ReturnType<typeof startMockServer>>;

  beforeAll(async () => {
    server = await startMockServer(0);
  });
  afterAll(async () => {
    await server.close();
  });

  it('pushes documents over real HTTP and reports the rejected one', async () => {
    const index = createIngestionClient({ endpoint: server.url, apiKey: MOCK_API_KEY }).index('site-content');

    const result = await index.upsert([
      { id: 'pim-1', _source: 'pim', title: 'Yirgacheffe', price: 18.5 },
      { id: 'pim-2', _source: 'pim', title: 'Broken', price: 'not a number' },
    ]);

    expect(result).toMatchObject({ indexed: 1, failed: 1, batches: 1 });
    expect(result.errors[0]).toMatchObject({ id: 'pim-2', field: 'price' });
    expect(result.taskIds).toHaveLength(1);
    expect(PUSHED.get('site-content')?.get('pim-1')).toMatchObject({ title: 'Yirgacheffe' });
  });

  it('surfaces a rejected API key as a typed error', async () => {
    const index = createIngestionClient({ endpoint: server.url, apiKey: 'xps_wrong' }).index('site-content');

    const error = await rejection(index.upsert([{ id: 'pim-3' }]));

    expect(error).toBeInstanceOf(XpSearchIngestionError);
    expect(error.status).toBe(401);
    expect(error.problem?.title).toBe('The API key is not valid.');
  });
});
