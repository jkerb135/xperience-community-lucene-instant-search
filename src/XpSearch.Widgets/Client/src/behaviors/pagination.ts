import { FIRST_PAGE, setPage } from '../state';
import type { RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface PaginationBehaviorParams {
  /** Pages shown either side of the current one. Defaults to 3. */
  padding?: number;
  /** Caps `totalPages`, for indexes where deep paging is not wanted. */
  maxPages?: number;
}

export interface PaginationRenderState {
  /** One-based page numbers to render, current page included. */
  pages: number[];
  /** One-based, like `SearchState.page`. */
  current: number;
  totalPages: number;
  total: number;
  isFirstPage: boolean;
  isLastPage: boolean;
  canApply: boolean;
  apply(page: number): void;
  urlFor(page: number): string;
}

/** Page controls (spec 5.7). */
export function withPagination<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: PaginationRenderState & RenderOptions<TParams & PaginationBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & PaginationBehaviorParams> {
  return createBehavior<TParams & PaginationBehaviorParams, PaginationRenderState, never>({
    $$type: 'xps.pagination',
    getRenderState(base, params) {
      const totalPages = Math.min(base.results?.totalPages ?? 0, params.maxPages ?? Infinity);
      const current = base.state.page;
      const padding = params.padding ?? 3;
      const start = Math.max(FIRST_PAGE, Math.min(current - padding, totalPages - padding * 2));
      const end = Math.min(totalPages, Math.max(current + padding, padding * 2 + FIRST_PAGE));
      const pages: number[] = [];
      for (let page = start; page <= end; page++) pages.push(page);
      return {
        pages,
        current,
        totalPages,
        total: base.results?.total ?? 0,
        isFirstPage: current === FIRST_PAGE,
        isLastPage: totalPages === 0 || current >= totalPages,
        canApply: totalPages > 1,
        apply(page) {
          base.actions.setPage(page).search();
        },
        urlFor(page) {
          return base.search.urlFor(setPage(base.state, page));
        },
      };
    },
  })(renderFn, unmountFn);
}
