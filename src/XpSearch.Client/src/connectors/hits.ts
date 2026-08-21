import type { EventType, Hit, RenderOptions, SearchResults, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface HitsConnectorParams<TItem extends Record<string, unknown>> {
  /** Client-side massaging escape hatch (spec 5.2). */
  transformItems?: (items: Array<Hit<TItem>>) => Array<Hit<TItem>>;
}

export interface HitsRenderState<TItem extends Record<string, unknown>> {
  hits: Array<Hit<TItem>>;
  results: SearchResults<TItem> | null;
  /**
   * Analytics for a result (spec 9.1). `position` defaults to the hit's one-based position
   * across pages. Silently does nothing when the response carried no `queryId`.
   */
  sendEvent(eventType: EventType, hit: Hit<TItem>, position?: number): void;
}

/** Result list (spec 5.7). */
export function connectHits<
  TItem extends Record<string, unknown> = Record<string, unknown>,
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: HitsRenderState<TItem> & RenderOptions<TParams & HitsConnectorParams<TItem>>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & HitsConnectorParams<TItem>> {
  return createConnector<
    TParams & HitsConnectorParams<TItem>,
    HitsRenderState<TItem>,
    never
  >({
    $$type: 'xps.hits',
    getRenderState(base, params) {
      const results = base.results as SearchResults<TItem> | null;
      const raw = (results?.hits ?? []) as Array<Hit<TItem>>;
      const hits = params.transformItems ? params.transformItems(raw) : raw;
      return {
        hits,
        results,
        sendEvent(eventType, hit, position) {
          const at = position ?? indexOfHit(results, hits, hit);
          base.instantSearchInstance.sendEvent(eventType, hit.objectID, at);
        },
      };
    },
  })(renderFn, unmountFn);
}

function indexOfHit<TItem extends Record<string, unknown>>(
  results: SearchResults<TItem> | null,
  hits: Array<Hit<TItem>>,
  hit: Hit<TItem>
): number | undefined {
  const at = hits.findIndex((h) => h.objectID === hit.objectID);
  if (at === -1 || !results) return undefined;
  // One-based across pages, as EventRequest.position is defined (contract).
  return results.page * results.hitsPerPage + at + 1;
}
