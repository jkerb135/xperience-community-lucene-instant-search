/**
 * Typed client for the Xperience Search ingestion API (spec §10.5), published as the
 * `@xperience-community/xperience-search/ingestion` subpath.
 *
 * **Server-side only.** The API key it takes is a secret that can write to and clear your index;
 * putting it in a browser bundle hands that power to every visitor. This module is deliberately not
 * exported from the package root, so a widget bundle cannot pull it in by accident. Use it from a
 * build pipeline, a sync job, a serverless function or a Node service. Code running inside the
 * Xperience application should use `IXpSearchIndexer` (C#, in-process) and skip HTTP entirely.
 *
 * Runs on Node 18+'s global `fetch`; `fetchFn` is the seam for tests and exotic runtimes.
 */
import type {
  BatchDeleteRequest,
  DeleteResponse,
  IndexListResponse,
  IndexStatus,
  IngestionError,
  PushDocument,
  UpsertRequest,
  UpsertResponse,
} from './contract/ingestion-generated';

/** Common prefix of every ingestion route (`IngestionContractConstants.RoutePrefix`). */
export const INGESTION_ROUTE_PREFIX = '/api/xpsearch/admin';

/**
 * The defaults, in one place. The two caps are the ingestion API's own server-side limits
 * (`XpSearchIngestionOptions.MaxDocumentsPerRequest` / `MaxRequestBytes`); the retry numbers match
 * the C# client in `XpSearch.Client`.
 */
export const INGESTION_DEFAULTS = {
  maxDocumentsPerRequest: 1000,
  maxRequestBytes: 10 * 1024 * 1024,
  /** Sends before giving up, first try included. */
  maxAttempts: 4,
  /** Backoff before the first retry; doubles per attempt. */
  retryBaseMs: 500,
  /** Ceiling on one backoff, `Retry-After` included. */
  maxRetryMs: 30_000,
} as const;

/** An RFC 9457 Problem Details body, as every failed ingestion request answers with. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

/** The aggregate of every request one `upsert` call made. */
export interface UpsertResult {
  /** Documents the server accepted, across every batch. */
  indexed: number;
  /** Documents schema validation rejected, across every batch. */
  failed: number;
  /** How many HTTP requests the call was split into. */
  batches: number;
  /** Every per-document error, in batch order. */
  errors: IngestionError[];
  /** The task id of each batch that was queued rather than awaited, in batch order. */
  taskIds: string[];
}

/** The aggregate of every request one `deleteMany` call made. */
export interface DeleteResult {
  deleted: number;
  batches: number;
  taskIds: string[];
}

/** A failed ingestion request: a refused response, or a transport failure that outlived the retries. */
export class XpSearchIngestionError extends Error {
  /** HTTP status, absent when the server never answered. */
  readonly status?: number;
  /** The parsed Problem Details body, when the answer carried one. */
  readonly problem?: ProblemDetails;
  /** The raw response body, for a failure that was not Problem Details. */
  readonly body?: string;
  /**
   * What a multi-batch `upsert` had already written when the failing batch was reached, so a caller
   * knows where to resume. Set only by `upsert`; absent for every other verb.
   */
  partialUpsert?: UpsertResult;
  /** The transport failure underneath, when there was one. */
  readonly cause?: unknown;

  constructor(message: string, init: { status?: number; problem?: ProblemDetails; body?: string; cause?: unknown } = {}) {
    super(message);
    this.name = 'XpSearchIngestionError';
    if (init.cause !== undefined) this.cause = init.cause;
    if (init.status !== undefined) this.status = init.status;
    if (init.problem !== undefined) this.problem = init.problem;
    if (init.body !== undefined) this.body = init.body;
  }
}

export interface IngestionClientOptions {
  /** Base URL of the Xperience application, for example `https://example.com`. */
  endpoint: string;
  /** The ingestion API key. A server-side secret — never ship it to a browser. */
  apiKey: string;
  /** Defaults to the global `fetch`. */
  fetchFn?: typeof fetch;
  maxDocumentsPerRequest?: number;
  maxRequestBytes?: number;
  maxAttempts?: number;
  retryBaseMs?: number;
  maxRetryMs?: number;
  /** Test seam: the wait between attempts. */
  sleepFn?: (ms: number) => Promise<void>;
  /** Test seam: the jitter source, in [0,1). */
  randomFn?: () => number;
}

/** Whether the write should be awaited until it is searchable. A foot-gun for bulk imports. */
export interface WriteOptions {
  waitForIndex?: boolean;
}

/** The ingestion verbs scoped to one index; each maps to exactly one route of the frozen contract. */
export interface IngestionIndexClient {
  readonly name: string;
  /**
   * Writes documents, splitting them into requests that stay under both server caps and adding the
   * answers up. Upsert is idempotent by contract — a document whose `id` exists is replaced — so a
   * retried or re-run batch cannot duplicate anything.
   *
   * Throws {@link XpSearchIngestionError} with `partialUpsert` set to what the earlier batches had
   * already written.
   */
  upsert(documents: Iterable<PushDocument>, options?: WriteOptions): Promise<UpsertResult>;
  /** Replaces some attributes of one document; a `null` value removes one. `_source` cannot change. */
  patch(id: string, attributes: Record<string, unknown>, options?: WriteOptions): Promise<UpsertResponse>;
  /** Deletes one document. */
  delete(id: string, options?: WriteOptions): Promise<DeleteResponse>;
  /** Deletes many documents by id, split into batches of at most `maxDocumentsPerRequest` ids. */
  deleteMany(ids: Iterable<string>, options?: WriteOptions): Promise<DeleteResult>;
  /** Deletes every document of one source, or every external document when no source is named. */
  clear(source?: string, options?: WriteOptions): Promise<DeleteResponse>;
  /** Triggers a rebuild; the pushed documents are replayed into the new index generation. */
  rebuild(): Promise<UpsertResponse>;
  /** Reads document counts by source, last write and health. */
  status(): Promise<IndexStatus>;
}

export interface IngestionClient {
  /** Lists every registered index and the schema pushed documents are validated against. */
  listIndexes(): Promise<IndexListResponse>;
  /** Returns the verbs scoped to one index. */
  index(name: string): IngestionIndexClient;
}

const RETRYABLE = new Set([408, 429]);
const encoder = new TextEncoder();
const byteLength = (value: unknown): number => encoder.encode(JSON.stringify(value)).length;
const waitQuery = (options: WriteOptions | undefined): string =>
  options?.waitForIndex === true ? 'waitForIndex=true' : '';

/** `{"documents":[]}` plus the longest `waitForIndex` form, rounded up. */
const ENVELOPE_BYTES = 48;

export function createIngestionClient(options: IngestionClientOptions): IngestionClient {
  const { endpoint, apiKey } = options;
  if (typeof endpoint !== 'string' || endpoint === '') throw new TypeError('endpoint is required.');
  if (typeof apiKey !== 'string' || apiKey === '') throw new TypeError('apiKey is required.');

  const settings = {
    maxDocumentsPerRequest: options.maxDocumentsPerRequest ?? INGESTION_DEFAULTS.maxDocumentsPerRequest,
    maxRequestBytes: options.maxRequestBytes ?? INGESTION_DEFAULTS.maxRequestBytes,
    maxAttempts: options.maxAttempts ?? INGESTION_DEFAULTS.maxAttempts,
    retryBaseMs: options.retryBaseMs ?? INGESTION_DEFAULTS.retryBaseMs,
    maxRetryMs: options.maxRetryMs ?? INGESTION_DEFAULTS.maxRetryMs,
  };
  const fetchFn = options.fetchFn ?? ((...args: Parameters<typeof fetch>) => globalThis.fetch(...args));
  const sleep = options.sleepFn ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  const random = options.randomFn ?? Math.random;
  const base = endpoint.endsWith('/') ? endpoint.slice(0, -1) : endpoint;

  /**
   * `Retry-After` when the server sent one, otherwise `base * 2^(attempt-1)` with half jitter
   * (a uniform factor in [0.5, 1)), capped at `maxRetryMs`.
   */
  const backoff = (attempt: number, retryAfter: number | undefined): number =>
    Math.min(
      retryAfter ?? settings.retryBaseMs * 2 ** (attempt - 1) * (0.5 + 0.5 * random()),
      settings.maxRetryMs
    );

  async function send<T>(method: string, path: string, query: string, body?: unknown): Promise<T> {
    const url = `${base}${path}${query === '' ? '' : `?${query}`}`;
    const init: RequestInit = {
      method,
      headers: {
        authorization: `Bearer ${apiKey}`,
        ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    };

    for (let attempt = 1; ; attempt++) {
      let response: Response;
      try {
        response = await fetchFn(url, init);
      } catch (error) {
        if (attempt >= settings.maxAttempts) {
          throw new XpSearchIngestionError(
            `${method} ${url} failed after ${attempt} attempt(s): ${error instanceof Error ? error.message : String(error)}`,
            { cause: error }
          );
        }
        await sleep(backoff(attempt, undefined));
        continue;
      }

      const text = await response.text();
      if (response.ok) return JSON.parse(text) as T;

      // 408, 429 and 5xx are worth another try; every other 4xx is the caller's own fault.
      if ((RETRYABLE.has(response.status) || response.status >= 500) && attempt < settings.maxAttempts) {
        await sleep(backoff(attempt, retryAfterMs(response)));
        continue;
      }

      throw failure(method, url, response.status, text);
    }
  }

  const routeOf = (index: string): string => `${INGESTION_ROUTE_PREFIX}/indexes/${encodeURIComponent(index)}`;

  return {
    listIndexes: () => send<IndexListResponse>('GET', `${INGESTION_ROUTE_PREFIX}/indexes`, ''),
    index(name: string): IngestionIndexClient {
      if (typeof name !== 'string' || name === '') throw new TypeError('An index name is required.');
      const route = routeOf(name);
      const documents = `${route}/documents`;

      return {
        name,
        async upsert(docs, writeOptions): Promise<UpsertResult> {
          const result: UpsertResult = { indexed: 0, failed: 0, batches: 0, errors: [], taskIds: [] };
          for (const batch of batches(docs, settings.maxDocumentsPerRequest, settings.maxRequestBytes)) {
            const request: UpsertRequest = { documents: batch };
            if (writeOptions?.waitForIndex === true) request.waitForIndex = true;
            let response: UpsertResponse;
            try {
              response = await send<UpsertResponse>('POST', documents, '', request);
            } catch (error) {
              if (error instanceof XpSearchIngestionError) error.partialUpsert = result;
              throw error;
            }
            result.batches += 1;
            result.indexed += response.indexed;
            result.failed += response.failed;
            result.errors.push(...(response.errors ?? []));
            if (response.taskId !== undefined && response.taskId !== null) result.taskIds.push(response.taskId);
          }
          return result;
        },
        patch(id, attributes, writeOptions) {
          if (Object.keys(attributes).length === 0) {
            throw new TypeError('At least one attribute to change is required.');
          }
          // The patch body IS the attribute bag: PatchRequest carries them as extension data.
          return send<UpsertResponse>('PATCH', `${documents}/${encodeURIComponent(id)}`, waitQuery(writeOptions), attributes);
        },
        delete: (id, writeOptions) =>
          send<DeleteResponse>('DELETE', `${documents}/${encodeURIComponent(id)}`, waitQuery(writeOptions)),
        async deleteMany(ids, writeOptions): Promise<DeleteResult> {
          const result: DeleteResult = { deleted: 0, batches: 0, taskIds: [] };
          for (const chunk of chunks(ids, settings.maxDocumentsPerRequest)) {
            const request: BatchDeleteRequest = { ids: chunk };
            const response = await send<DeleteResponse>('POST', `${documents}/delete`, waitQuery(writeOptions), request);
            result.batches += 1;
            result.deleted += response.deleted;
            if (response.taskId !== undefined && response.taskId !== null) result.taskIds.push(response.taskId);
          }
          return result;
        },
        clear: (source, writeOptions) =>
          send<DeleteResponse>(
            'POST',
            `${route}/clear`,
            [source === undefined ? '' : `source=${encodeURIComponent(source)}`, waitQuery(writeOptions)]
              .filter((part) => part !== '')
              .join('&')
          ),
        rebuild: () => send<UpsertResponse>('POST', `${route}/rebuild`, ''),
        status: () => send<IndexStatus>('GET', `${route}/status`, ''),
      };
    },
  };
}

function retryAfterMs(response: Response): number | undefined {
  const header = response.headers.get('retry-after');
  if (header === null) return undefined;
  const seconds = Number(header);
  if (Number.isFinite(seconds)) return Math.max(0, seconds * 1000);
  const date = Date.parse(header);
  return Number.isNaN(date) ? undefined : Math.max(0, date - Date.now());
}

function failure(method: string, url: string, status: number, body: string): XpSearchIngestionError {
  let problem: ProblemDetails | undefined;
  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === 'object' && parsed !== null && ('title' in parsed || 'detail' in parsed)) {
      problem = parsed as ProblemDetails;
    }
  } catch {
    // Not every failure is Problem Details (a proxy's HTML 502, for one); the raw body is kept.
  }
  const detail = problem?.detail ?? problem?.title ?? body.slice(0, 200);
  return new XpSearchIngestionError(`${method} ${url} answered ${status}${detail === '' ? '' : `: ${detail}`}`, {
    status,
    ...(problem === undefined ? {} : { problem }),
    body,
  });
}

/**
 * Splits the documents into batches under both caps. The size is measured on the serialized
 * documents plus the request envelope, which is what the server weighs. A single oversized document
 * still goes out alone: the server owns the limit and answers 413 naming it.
 */
function* batches(documents: Iterable<PushDocument>, maxCount: number, maxBytes: number): Generator<PushDocument[]> {
  let batch: PushDocument[] = [];
  let bytes = ENVELOPE_BYTES;
  for (const document of documents) {
    const size = byteLength(document) + 1;
    if (batch.length > 0 && (batch.length === maxCount || bytes + size > maxBytes)) {
      yield batch;
      batch = [];
      bytes = ENVELOPE_BYTES;
    }
    batch.push(document);
    bytes += size;
  }
  if (batch.length > 0) yield batch;
}

function* chunks(ids: Iterable<string>, maxCount: number): Generator<string[]> {
  let chunk: string[] = [];
  for (const id of ids) {
    chunk.push(id);
    if (chunk.length === maxCount) {
      yield chunk;
      chunk = [];
    }
  }
  if (chunk.length > 0) yield chunk;
}
