import { setPage } from '../state';
import type { RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface PaginationConnectorParams {
  /** Pages shown either side of the current one. Defaults to 3. */
  padding?: number;
  /** Caps `nbPages`, for indexes where deep paging is not wanted. */
  totalPages?: number;
}

export interface PaginationRenderState {
  /** Zero-based page numbers to render, current page included. */
  pages: number[];
  /** Zero-based, like `SearchState.page`. */
  currentRefinement: number;
  nbPages: number;
  nbHits: number;
  isFirstPage: boolean;
  isLastPage: boolean;
  canRefine: boolean;
  refine(page: number): void;
  createURL(page: number): string;
}

/** Page controls (spec 5.7). */
export function connectPagination<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: PaginationRenderState & RenderOptions<TParams & PaginationConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & PaginationConnectorParams> {
  return createConnector<TParams & PaginationConnectorParams, PaginationRenderState, never>({
    $$type: 'xps.pagination',
    getRenderState(base, params) {
      const nbPages = Math.min(base.results?.nbPages ?? 0, params.totalPages ?? Infinity);
      const current = base.state.page;
      const padding = params.padding ?? 3;
      const start = Math.max(0, Math.min(current - padding, nbPages - (padding * 2 + 1)));
      const end = Math.min(nbPages - 1, Math.max(current + padding, padding * 2));
      const pages: number[] = [];
      for (let page = start; page <= end; page++) pages.push(page);
      return {
        pages,
        currentRefinement: current,
        nbPages,
        nbHits: base.results?.nbHits ?? 0,
        isFirstPage: current === 0,
        isLastPage: nbPages === 0 || current >= nbPages - 1,
        canRefine: nbPages > 1,
        refine(page) {
          base.helper.setPage(page).search();
        },
        createURL(page) {
          return base.instantSearchInstance.createURL(setPage(base.state, page));
        },
      };
    },
  })(renderFn, unmountFn);
}
