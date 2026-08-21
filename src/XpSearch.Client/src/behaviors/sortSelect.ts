import { setSort } from '../state';
import type { RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface SortSelectItem {
  label: string;
  /** An index-configured sort key, or `"relevance"`. */
  value: string;
}

export interface SortSelectBehaviorParams {
  items: SortSelectItem[];
}

export interface SortSelectRenderState {
  options: SortSelectItem[];
  current: string;
  canApply: boolean;
  apply(value: string): void;
  urlFor(value: string): string;
}

/** Sort selector (spec 5.7). */
export function withSortSelect<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SortSelectRenderState & RenderOptions<TParams & SortSelectBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SortSelectBehaviorParams> {
  return createBehavior<TParams & SortSelectBehaviorParams, SortSelectRenderState, never>({
    $$type: 'xps.sortSelect',
    getRenderState(base, params) {
      const items = params.items ?? [];
      return {
        options: items,
        current: base.state.sort,
        canApply: items.length > 1,
        apply(value) {
          base.actions.setSort(value).search();
        },
        urlFor(value) {
          return base.search.urlFor(setSort(base.state, value));
        },
      };
    },
  })(renderFn, unmountFn);
}
