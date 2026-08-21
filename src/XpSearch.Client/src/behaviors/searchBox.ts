import type { RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface SearchBoxBehaviorParams {
  /** Intercepts a query before it reaches the state, e.g. to strip or gate input. */
  queryHook?: (query: string, search: (value: string) => void) => void;
}

export interface SearchBoxRenderState {
  query: string;
  apply(query: string): void;
  clear(): void;
  /** True while a request has been running longer than `stalledSearchDelayMs`. */
  isStalled: boolean;
}

/** Query input (spec 5.7). */
export function withSearchBox<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SearchBoxRenderState & RenderOptions<TParams & SearchBoxBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SearchBoxBehaviorParams> {
  return createBehavior<TParams & SearchBoxBehaviorParams, SearchBoxRenderState, never>({
    $$type: 'xps.searchBox',
    getRenderState(base, params) {
      const run = (query: string): void => {
        base.actions.setQuery(query).search();
      };
      return {
        query: base.state.query,
        apply(query) {
          if (params.queryHook) params.queryHook(query, run);
          else run(query);
        },
        clear() {
          run('');
        },
        isStalled: base.search.status === 'stalled',
      };
    },
  })(renderFn, unmountFn);
}
