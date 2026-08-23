import type {
  EventType,
  RenderOptions,
  Result,
  SearchRedirect,
  SearchResults,
  WidgetFactory,
} from '../types';
import { createBehavior } from './internal';

export interface ResultsBehaviorParams<TAttributes extends Record<string, unknown>> {
  /** Client-side massaging escape hatch (spec 5.2). */
  transformItems?: (items: Array<Result<TAttributes>>) => Array<Result<TAttributes>>;
}

export interface ResultsRenderState<TAttributes extends Record<string, unknown>> {
  items: Array<Result<TAttributes>>;
  results: SearchResults<TAttributes> | null;
  /**
   * Where a redirect rule sends the visitor, or `null`. The results are still there: whether to
   * show them or a “Redirecting…” message is the renderer's call, and only `searchBox`
   * navigates.
   */
  redirect: SearchRedirect | null;
  /**
   * Analytics for a result (spec 9.1). `position` defaults to the result's one-based position
   * across pages. Silently does nothing when the response carried no `queryId`.
   */
  sendEvent(type: EventType, result: Result<TAttributes>, position?: number): void;
}

/** Result list (spec 5.7). */
export function withResults<
  TAttributes extends Record<string, unknown> = Record<string, unknown>,
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: ResultsRenderState<TAttributes> &
      RenderOptions<TParams & ResultsBehaviorParams<TAttributes>>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & ResultsBehaviorParams<TAttributes>> {
  return createBehavior<
    TParams & ResultsBehaviorParams<TAttributes>,
    ResultsRenderState<TAttributes>,
    never
  >({
    $$type: 'xps.results',
    getRenderState(base, params) {
      const results = base.results as SearchResults<TAttributes> | null;
      const raw = (results?.results ?? []) as Array<Result<TAttributes>>;
      const items = params.transformItems ? params.transformItems(raw) : raw;
      return {
        items,
        results,
        redirect: results?.redirect ?? null,
        sendEvent(type, result, position) {
          const at = position ?? positionOf(results, items, result);
          base.search.sendEvent(type, result.id, at);
        },
      };
    },
  })(renderFn, unmountFn);
}

function positionOf<TAttributes extends Record<string, unknown>>(
  results: SearchResults<TAttributes> | null,
  items: Array<Result<TAttributes>>,
  result: Result<TAttributes>
): number | undefined {
  const at = items.findIndex((candidate) => candidate.id === result.id);
  if (at === -1 || !results) return undefined;
  // One-based across pages, as EventRequest.position is defined (contract). `page` is one-based
  // too, so the first page contributes no offset.
  return (results.page - 1) * results.pageSize + at + 1;
}
