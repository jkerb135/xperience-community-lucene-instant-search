/**
 * `SearchClient` — the transport layer of spec 5.1: debounce, in-flight cancellation, retry,
 * contract-version checking, and fire-and-forget analytics.
 */
import {
  API_VERSION,
  API_VERSION_HEADER,
  EVENTS_ROUTE,
  QUERY_ROUTE,
  SUGGEST_ROUTE,
} from './contract/constants';
import type {
  EventRequest,
  SearchRequest,
  SearchResponse,
  SuggestRequest,
  SuggestResponse,
} from './contract/generated';

/** A failed search. `status` is the HTTP status when the server answered at all. */
export class SearchError extends Error {
  readonly status?: number;
  readonly body?: unknown;
  constructor(message: string, status?: number, body?: unknown) {
    super(message);
    this.name = 'SearchError';
    this.status = status;
    this.body = body;
  }
}

export interface SearchClientOptions {
  endpoint?: string;
  suggestEndpoint?: string;
  eventsEndpoint?: string;
  /** Trailing debounce applied to {@link SearchClient.search}. Defaults to 150ms. */
  debounceMs?: number;
  headers?: Record<string, string>;
  fetchFn?: typeof fetch;
  /** Retries after a network error, 429 or 5xx. Never after another 4xx. Defaults to 2. */
  retries?: number;
  /** Base backoff in ms, doubled per attempt. Defaults to 200. */
  retryDelayMs?: number;
  /** Called for contract-version mismatches and swallowed event failures. */
  onError?: (error: Error) => void;
}

const JSON_HEADERS = { 'content-type': 'application/json' };

export class SearchClient {
  readonly #options: Required<Omit<SearchClientOptions, 'onError' | 'headers' | 'fetchFn'>> & {
    headers: Record<string, string>;
    fetchFn: typeof fetch;
    onError?: (error: Error) => void;
  };
  #timer: ReturnType<typeof setTimeout> | undefined;
  #controller: AbortController | undefined;
  /** Monotonic id of the newest requested search; older answers are dropped. */
  #sequence = 0;
  #pending: Array<{ sequence: number; resolve: (r: SearchResponse | null) => void }> = [];
  #reportedVersions = new Set<string>();

  constructor(options: SearchClientOptions = {}) {
    this.#options = {
      endpoint: options.endpoint ?? QUERY_ROUTE,
      suggestEndpoint: options.suggestEndpoint ?? SUGGEST_ROUTE,
      eventsEndpoint: options.eventsEndpoint ?? EVENTS_ROUTE,
      debounceMs: options.debounceMs ?? 150,
      retries: options.retries ?? 2,
      retryDelayMs: options.retryDelayMs ?? 200,
      headers: options.headers ?? {},
      fetchFn: options.fetchFn ?? ((...args) => globalThis.fetch(...args)),
      onError: options.onError,
    };
  }

  /**
   * Debounced search. Resolves with the response, or with `null` when this call was superseded
   * by a newer one — a stale answer never reaches a widget. Rejects only on a real failure.
   */
  search(request: SearchRequest): Promise<SearchResponse | null> {
    const sequence = ++this.#sequence;
    // Everything still queued is stale the moment a newer search is asked for.
    const superseded = this.#pending;
    this.#pending = [];
    for (const p of superseded) p.resolve(null);
    if (this.#timer !== undefined) clearTimeout(this.#timer);

    return new Promise<SearchResponse | null>((resolve, reject) => {
      this.#pending.push({ sequence, resolve });
      this.#timer = setTimeout(() => {
        this.#timer = undefined;
        this.#pending = this.#pending.filter((p) => p.sequence !== sequence);
        this.#controller?.abort();
        const controller = new AbortController();
        this.#controller = controller;
        this.#send<SearchResponse>(this.#options.endpoint, request, controller.signal).then(
          (response) => resolve(sequence === this.#sequence ? response : null),
          (error: unknown) => {
            if (controller.signal.aborted || sequence !== this.#sequence) resolve(null);
            else reject(error);
          }
        );
      }, this.#options.debounceMs);
    });
  }

  /** Autocomplete. Not debounced and not cancelled — `connectAutocomplete` owns that policy. */
  suggest(request: SuggestRequest): Promise<SuggestResponse> {
    return this.#send<SuggestResponse>(this.#options.suggestEndpoint, request);
  }

  /**
   * Analytics. Fire-and-forget and never throws: click tracking must never break search
   * (spec 9.1).
   */
  sendEvent(event: EventRequest): void {
    try {
      void this.#send<unknown>(this.#options.eventsEndpoint, event).catch((error: unknown) => {
        this.#options.onError?.(error instanceof Error ? error : new Error(String(error)));
      });
    } catch (error) {
      this.#options.onError?.(error instanceof Error ? error : new Error(String(error)));
    }
  }

  /** Cancels the in-flight request and any debounced one. */
  dispose(): void {
    if (this.#timer !== undefined) clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#controller?.abort();
    for (const p of this.#pending) p.resolve(null);
    this.#pending = [];
  }

  async #send<T>(url: string, body: unknown, signal?: AbortSignal): Promise<T> {
    const { fetchFn, headers, retries, retryDelayMs } = this.#options;
    let lastError: Error = new SearchError(`No response from ${url}`);
    for (let attempt = 0; attempt <= retries; attempt++) {
      if (attempt > 0) {
        await new Promise((r) => setTimeout(r, retryDelayMs * 2 ** (attempt - 1)));
        if (signal?.aborted) throw new SearchError('Aborted');
      }
      let response: Response;
      try {
        response = await fetchFn(url, {
          method: 'POST',
          headers: { ...JSON_HEADERS, ...headers },
          body: JSON.stringify(body),
          ...(signal ? { signal } : {}),
        });
      } catch (error) {
        if (signal?.aborted) throw error;
        lastError = error instanceof Error ? error : new Error(String(error));
        continue; // network error: retry
      }
      this.#checkVersion(response);
      if (response.ok) {
        // 202 Accepted with an empty body is the contract for /events.
        return response.status === 202 ? (undefined as T) : ((await response.json()) as T);
      }
      const detail = await response.text().catch(() => '');
      lastError = new SearchError(
        `${url} answered ${response.status}${detail ? `: ${detail.slice(0, 200)}` : ''}`,
        response.status,
        detail
      );
      // 5xx and 429 are worth another try; every other 4xx is the caller's fault.
      if (response.status < 500 && response.status !== 429) throw lastError;
    }
    throw lastError;
  }

  /** A contract mismatch is surfaced, not thrown: the response may still be usable (spec 4.2). */
  #checkVersion(response: Response): void {
    const version = response.headers.get(API_VERSION_HEADER) ?? '';
    if (version === API_VERSION || this.#reportedVersions.has(version)) return;
    this.#reportedVersions.add(version);
    this.#options.onError?.(
      new SearchError(
        `Contract version mismatch: this client speaks ${API_VERSION_HEADER} ${API_VERSION}, the server answered ${version === '' ? '(no header)' : version}.`
      )
    );
  }
}
