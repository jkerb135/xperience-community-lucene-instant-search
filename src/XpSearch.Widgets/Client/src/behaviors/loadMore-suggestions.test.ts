/**
 * The two Phase 2.5 behaviours: accumulation across pages, and the autocomplete policy
 * (debounce, latest-response-wins, the keyboard state machine).
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { API_VERSION_HEADER, SUGGEST_ROUTE } from '../contract/constants';
import type {
  SearchRequest,
  SearchResponse,
  SuggestResponse,
  Suggestion,
} from '../contract/generated';
import { createSearch } from '../instance';
import type { RenderOptions, SearchInstance } from '../types';
import { withLoadMore, type LoadMoreRenderState } from './loadMore';
import { withSuggestions, type SuggestionsRenderState } from './suggestions';

/** `Array.prototype.at` is newer than the `lib` this package compiles against. */
const last = <T>(items: T[]): T | undefined => items[items.length - 1];

const live: SearchInstance[] = [];
afterEach(() => {
  while (live.length > 0) live.pop()?.dispose();
});

const PAGE_SIZE = 2;
const TOTAL = 5;

/** Answers page `n` of a five-document corpus, so the ids say which page they came from. */
function page(request: SearchRequest): SearchResponse {
  const at = request.page ?? 1;
  const ids = [1, 2, 3, 4, 5].slice((at - 1) * PAGE_SIZE, at * PAGE_SIZE);
  return {
    results: ids.map((n) => ({ id: `${request.query ?? ''}-doc-${n}`, attributes: { title: `#${n}` } })),
    facets: {},
    page: at,
    pageSize: PAGE_SIZE,
    total: TOTAL,
    totalPages: Math.ceil(TOTAL / PAGE_SIZE),
    tookMs: 1,
    redirect: null,
    queryId: 'q-1',
  };
}

type LoadMoreRender = LoadMoreRenderState<Record<string, unknown>> &
  RenderOptions<Record<string, unknown>>;

async function mountLoadMore(): Promise<{ renders: LoadMoreRender[]; search: SearchInstance }> {
  const fetchFn = (async (_url: string, init: RequestInit) =>
    new Response(JSON.stringify(page(JSON.parse(String(init.body)) as SearchRequest)), {
      status: 200,
      headers: { [API_VERSION_HEADER]: '1' },
    })) as unknown as typeof fetch;
  const renders: LoadMoreRender[] = [];
  const search = createSearch({
    index: 'site-content',
    debounceMs: 0,
    fetchFn,
    initialState: { pageSize: PAGE_SIZE },
  });
  live.push(search);
  search.addWidgets([withLoadMore((options) => renders.push(options))({})]);
  search.start();
  await vi.waitFor(() => expect(last(renders)?.items.length).toBe(PAGE_SIZE));
  return { renders, search };
}

describe('withLoadMore', () => {
  it('appends the next page instead of replacing it', async () => {
    const { renders } = await mountLoadMore();
    const first = last(renders)!;
    expect(first.total).toBe(TOTAL);
    expect(first.isExhausted).toBe(false);

    first.loadMore();
    await vi.waitFor(() => expect(last(renders)?.items.length).toBe(4));
    expect(last(renders)!.items.map((r) => r.id)).toEqual([
      '-doc-1',
      '-doc-2',
      '-doc-3',
      '-doc-4',
    ]);
    // Appending, not rebuilding: the renderer must not be told to throw the list away.
    expect(last(renders)!.generation).toBe(first.generation);
  });

  it('is exhausted on the last page and loads no further', async () => {
    const { renders, search } = await mountLoadMore();
    last(renders)!.loadMore();
    await vi.waitFor(() => expect(last(renders)?.items.length).toBe(4));
    last(renders)!.loadMore();
    await vi.waitFor(() => expect(last(renders)?.items.length).toBe(TOTAL));

    const final = last(renders)!;
    expect(final.isExhausted).toBe(true);
    const before = renders.length;
    final.loadMore();
    expect(search.state.page).toBe(3);
    expect(renders.length).toBe(before);
  });

  it('resets the list on any state change but the next page', async () => {
    const { renders } = await mountLoadMore();
    last(renders)!.loadMore();
    await vi.waitFor(() => expect(last(renders)?.items.length).toBe(4));
    const generation = last(renders)!.generation;

    last(renders)!.actions.setQuery('espresso').setPage(1).search();
    await vi.waitFor(() => expect(last(renders)?.items[0]?.id).toBe('espresso-doc-1'));
    expect(last(renders)!.items.length).toBe(PAGE_SIZE);
    expect(last(renders)!.generation).toBe(generation + 1);
  });
});

type SuggestionsRender = SuggestionsRenderState & RenderOptions<Record<string, unknown>>;

/** A `/suggest` stub whose answers are resolved by hand, one deferred per call. */
function suggestHarness(params: Record<string, unknown> = {}): {
  renders: SuggestionsRender[];
  answer: (at: number, suggestions: Array<string | Suggestion>) => Promise<void>;
  calls: string[];
} {
  const calls: string[] = [];
  const pending: Array<(response: SuggestResponse) => void> = [];
  const fetchFn = (async (url: string, init: RequestInit) => {
    if (!String(url).endsWith(SUGGEST_ROUTE)) {
      return new Response(JSON.stringify(page(JSON.parse(String(init.body)) as SearchRequest)), {
        status: 200,
        headers: { [API_VERSION_HEADER]: '1' },
      });
    }
    calls.push((JSON.parse(String(init.body)) as { query: string }).query);
    return new Promise<Response>((resolve) => {
      pending.push((response) =>
        resolve(
          new Response(JSON.stringify(response), {
            status: 200,
            headers: { [API_VERSION_HEADER]: '1' },
          })
        )
      );
    });
  }) as unknown as typeof fetch;

  const renders: SuggestionsRender[] = [];
  const search = createSearch({ index: 'site-content', debounceMs: 0, fetchFn });
  live.push(search);
  search.addWidgets([
    withSuggestions((options) => renders.push(options))({ resultsUrl: '/search', ...params }),
  ]);
  search.start();
  return {
    renders,
    calls,
    answer: async (at, suggestions) => {
      pending[at]?.({
        suggestions: suggestions.map((one) => (typeof one === 'string' ? { text: one } : one)),
      });
      // One turn for the fetch promise, one for the JSON body.
      await vi.advanceTimersByTimeAsync(0);
      await vi.advanceTimersByTimeAsync(0);
    },
  };
}

describe('withSuggestions', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('debounces, and asks for nothing below the minimum query length', async () => {
    vi.useFakeTimers();
    const { renders, calls } = suggestHarness({ debounceMs: 150, minQueryLength: 3 });

    last(renders)!.setQuery('e');
    last(renders)!.setQuery('es');
    await vi.advanceTimersByTimeAsync(300);
    expect(calls).toEqual([]);

    last(renders)!.setQuery('esp');
    await vi.advanceTimersByTimeAsync(100);
    expect(calls).toEqual([]); // still inside the debounce window
    await vi.advanceTimersByTimeAsync(100);
    expect(calls).toEqual(['esp']);
  });

  it('drops the answer to a keystroke the visitor has typed past', async () => {
    vi.useFakeTimers();
    const { renders, answer, calls } = suggestHarness({ debounceMs: 10 });

    last(renders)!.setQuery('esp');
    await vi.advanceTimersByTimeAsync(10);
    last(renders)!.setQuery('espresso');
    await vi.advanceTimersByTimeAsync(10);
    expect(calls).toEqual(['esp', 'espresso']);

    // The newer answer arrives first; the older one must not overwrite it.
    await answer(1, ['espresso machine']);
    expect(last(renders)!.suggestions.map((s) => s.text)).toEqual(['espresso machine']);
    await answer(0, ['esperanto']);
    expect(last(renders)!.suggestions.map((s) => s.text)).toEqual(['espresso machine']);
    expect(last(renders)!.isOpen).toBe(true);
  });

  it('moves the active option, wraps, and closes on escape', async () => {
    vi.useFakeTimers();
    const { renders, answer } = suggestHarness({ debounceMs: 0 });
    last(renders)!.setQuery('esp');
    await vi.advanceTimersByTimeAsync(0);
    await answer(0, ['one', 'two', 'three']);

    const at = (): number => last(renders)!.activeIndex;
    expect(at()).toBe(-1);
    last(renders)!.move(1);
    expect(at()).toBe(0);
    last(renders)!.move(-1);
    expect(at()).toBe(2); // wraps
    last(renders)!.move('first');
    expect(at()).toBe(0);
    last(renders)!.move('last');
    expect(at()).toBe(2);

    last(renders)!.close();
    expect(last(renders)!.isOpen).toBe(false);
    expect(at()).toBe(-1);
    // Closed, the suggestions are still there — reopening must not re-fetch.
    expect(last(renders)!.suggestions.length).toBe(3);
  });

  it('searches for a query suggestion and navigates to a document suggestion', async () => {
    vi.useFakeTimers();
    const assign = vi.fn();
    const { renders, answer } = suggestHarness({
      debounceMs: 0,
      windowRef: { location: { assign } } as unknown as Window,
    });
    last(renders)!.setQuery('esp');
    await vi.advanceTimersByTimeAsync(0);
    await answer(0, ['espresso']);

    // A query suggestion has no url: picking it searches for its text.
    last(renders)!.select(0);
    expect(assign).not.toHaveBeenCalled();
    expect(last(renders)!.query).toBe('espresso');
    await vi.advanceTimersByTimeAsync(10);
    expect(last(renders)!.state.query).toBe('espresso');

    last(renders)!.setQuery('roc');
    await vi.advanceTimersByTimeAsync(0);
    // A document suggestion carries the page it stands for: picking it navigates.
    await answer(1, [{ text: 'Rocket', url: '/products/rocket', result: { id: 'r', attributes: {} } }]);
    last(renders)!.select(0);
    expect(assign).toHaveBeenCalledWith('/products/rocket');
  });

  it('submits to the results page when one is configured', async () => {
    vi.useFakeTimers();
    const assign = vi.fn();
    const { renders } = suggestHarness({
      debounceMs: 0,
      resultsUrl: '/search',
      windowRef: { location: { assign } } as unknown as Window,
    });
    last(renders)!.setQuery('espresso');
    // No window in this environment, so the router builds against its own default origin.
    expect(last(renders)!.seeAllUrl).toBe('http://localhost/search?q=espresso');
    last(renders)!.submit();
    expect(assign).toHaveBeenCalledWith('http://localhost/search?q=espresso');
  });
});
