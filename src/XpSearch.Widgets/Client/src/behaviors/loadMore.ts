import { FIRST_PAGE } from '../state';
import type {
  EventType,
  RenderOptions,
  Result,
  SearchResults,
  SearchState,
  WidgetFactory,
} from '../types';
import { createBehavior } from './internal';

export interface LoadMoreBehaviorParams<TAttributes extends Record<string, unknown>> {
  /** Client-side massaging escape hatch (spec 5.2). Applied per page, before accumulating. */
  transformItems?: (items: Array<Result<TAttributes>>) => Array<Result<TAttributes>>;
}

export interface LoadMoreRenderState<TAttributes extends Record<string, unknown>> {
  /** Every result loaded so far, in load order. */
  items: Array<Result<TAttributes>>;
  total: number;
  /** No page left to load — every result of the current search is in `items`. */
  isExhausted: boolean;
  isLoading: boolean;
  /**
   * Bumped whenever the accumulated list was thrown away rather than appended to. A renderer
   * that appends compares it with the one it last painted and rebuilds when it differs.
   */
  generation: number;
  /** Asks for the next page. A no-op while one is in flight, or when exhausted. */
  loadMore(): void;
  /** Analytics for a result (spec 9.1); `position` defaults to its place in `items`. */
  sendEvent(type: EventType, result: Result<TAttributes>, position?: number): void;
}

/** What the accumulated list belongs to: any change but the page starts a new one. */
const fingerprint = (state: SearchState): string =>
  JSON.stringify([state.query, state.sort, state.filters, state.pageSize]);

interface LoadMoreLocal<TAttributes extends Record<string, unknown>> {
  items: Array<Result<TAttributes>>;
  /** The last page merged in; 0 before the first response. */
  page: number;
  key: string;
  /** Response identity, so re-rendering the same response never appends it twice. */
  seen: SearchResults | null | undefined;
  generation: number;
}

/** Endless results (spec 5.7): the pages of one search, accumulated instead of replaced. */
export function withLoadMore<
  TAttributes extends Record<string, unknown> = Record<string, unknown>,
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: LoadMoreRenderState<TAttributes> &
      RenderOptions<TParams & LoadMoreBehaviorParams<TAttributes>>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & LoadMoreBehaviorParams<TAttributes>> {
  return createBehavior<
    TParams & LoadMoreBehaviorParams<TAttributes>,
    LoadMoreRenderState<TAttributes>,
    LoadMoreLocal<TAttributes>
  >({
    $$type: 'xps.loadMore',
    createLocal: () => ({ items: [], page: 0, key: '', seen: undefined, generation: 0 }),
    getRenderState(base, params, context) {
      const local = context.local;
      const results = base.results as SearchResults<TAttributes> | null;

      if (results !== local.seen) {
        local.seen = results;
        if (results) {
          const raw = results.results;
          const page = params.transformItems ? params.transformItems(raw) : raw;
          const key = fingerprint(base.state);
          if (key === local.key && results.page === local.page + 1) {
            local.items = [...local.items, ...page];
          } else {
            // A new query, a new refinement, or a jump to an unrelated page: the list a visitor
            // was reading no longer describes the same search, so it is replaced, not extended.
            local.items = page;
            local.generation++;
          }
          local.key = key;
          local.page = results.page;
        }
      }

      const total = results?.total ?? 0;
      const isExhausted = local.items.length >= total;
      const isLoading = base.search.status === 'loading' || base.search.status === 'stalled';

      return {
        items: local.items,
        total,
        isExhausted,
        isLoading,
        generation: local.generation,
        loadMore() {
          if (isExhausted || isLoading) return;
          base.actions.setPage(Math.max(local.page, FIRST_PAGE) + 1).search();
        },
        sendEvent(type, result, position) {
          const at = position ?? local.items.indexOf(result) + 1;
          base.search.sendEvent(type, result.id, at > 0 ? at : undefined);
        },
      };
    },
  })(renderFn, unmountFn);
}
