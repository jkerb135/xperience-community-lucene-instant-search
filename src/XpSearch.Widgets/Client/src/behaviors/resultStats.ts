import type { RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface ResultStatsRenderState {
  total: number;
  /** Server-side time of the last response, in milliseconds. */
  tookMs: number;
  query: string;
  /** One-based. */
  page: number;
  totalPages: number;
  pageSize: number;
  /** False before the first response, so a widget can render "" instead of "0 results". */
  hasResults: boolean;
}

/** Result counter (spec 5.7). */
export function withResultStats<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: ResultStatsRenderState & RenderOptions<TParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams> {
  return createBehavior<TParams, ResultStatsRenderState, never>({
    $$type: 'xps.resultStats',
    getRenderState(base) {
      const results = base.results;
      return {
        total: results?.total ?? 0,
        tookMs: results?.tookMs ?? 0,
        query: base.state.query,
        page: results?.page ?? base.state.page,
        totalPages: results?.totalPages ?? 0,
        pageSize: results?.pageSize ?? 0,
        hasResults: results !== null,
      };
    },
  })(renderFn, unmountFn);
}
