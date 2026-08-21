import type { RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface SearchBoxConnectorParams {
  /** Intercepts a refinement before it reaches the state, e.g. to strip or gate input. */
  queryHook?: (query: string, search: (value: string) => void) => void;
}

export interface SearchBoxRenderState {
  query: string;
  refine(query: string): void;
  clear(): void;
  /** True while a request has been running longer than `stalledSearchDelayMs`. */
  isSearchStalled: boolean;
}

/** Query input (spec 5.7). */
export function connectSearchBox<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SearchBoxRenderState & RenderOptions<TParams & SearchBoxConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SearchBoxConnectorParams> {
  return createConnector<TParams & SearchBoxConnectorParams, SearchBoxRenderState, never>({
    $$type: 'xps.searchBox',
    getRenderState(base, params) {
      const apply = (query: string): void => {
        base.helper.setQuery(query).search();
      };
      return {
        query: base.state.query,
        refine(query) {
          if (params.queryHook) params.queryHook(query, apply);
          else apply(query);
        },
        clear() {
          apply('');
        },
        isSearchStalled: base.instantSearchInstance.status === 'stalled',
      };
    },
  })(renderFn, unmountFn);
}
