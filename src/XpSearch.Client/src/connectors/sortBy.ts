import { setSort } from '../state';
import type { RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface SortByItem {
  label: string;
  /** An index-configured sort key, or `"relevance"`. */
  value: string;
}

export interface SortByConnectorParams {
  items: SortByItem[];
}

export interface SortByRenderState {
  options: SortByItem[];
  currentRefinement: string;
  canRefine: boolean;
  refine(value: string): void;
  createURL(value: string): string;
}

/** Sort selector (spec 5.7). */
export function connectSortBy<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SortByRenderState & RenderOptions<TParams & SortByConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SortByConnectorParams> {
  return createConnector<TParams & SortByConnectorParams, SortByRenderState, never>({
    $$type: 'xps.sortBy',
    getRenderState(base, params) {
      const items = params.items ?? [];
      return {
        options: items,
        currentRefinement: base.state.sort,
        canRefine: items.length > 1,
        refine(value) {
          base.helper.setSort(value).search();
        },
        createURL(value) {
          return base.instantSearchInstance.createURL(setSort(base.state, value));
        },
      };
    },
  })(renderFn, unmountFn);
}
