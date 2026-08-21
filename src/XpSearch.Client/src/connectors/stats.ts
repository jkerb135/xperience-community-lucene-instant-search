import type { RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface StatsRenderState {
  nbHits: number;
  /** Server-side time of the last response. Spelled as Algolia does; the wire field is
   * `processingTimeMs` (contract). */
  processingTimeMS: number;
  query: string;
  /** Zero-based. */
  page: number;
  nbPages: number;
  hitsPerPage: number;
  /** False before the first response, so a widget can render "" instead of "0 results". */
  hasResults: boolean;
}

/** Result counter (spec 5.7). */
export function connectStats<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: StatsRenderState & RenderOptions<TParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams> {
  return createConnector<TParams, StatsRenderState, never>({
    $$type: 'xps.stats',
    getRenderState(base) {
      const results = base.results;
      return {
        nbHits: results?.nbHits ?? 0,
        processingTimeMS: results?.processingTimeMs ?? 0,
        query: base.state.query,
        page: results?.page ?? base.state.page,
        nbPages: results?.nbPages ?? 0,
        hitsPerPage: results?.hitsPerPage ?? 0,
        hasResults: results !== null,
      };
    },
  })(renderFn, unmountFn);
}
