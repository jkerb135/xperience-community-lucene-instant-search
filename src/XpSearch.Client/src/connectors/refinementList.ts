import { toggleFacetRefinement } from '../state';
import type {
  EventType,
  FacetOperator,
  RenderOptions,
  WidgetFactory,
} from '../types';
import { createConnector, withFacet } from './internal';

/** How the item list is ordered. Applied left to right, first difference wins. */
export type RefinementListSortBy =
  | 'isRefined'
  | 'count:asc'
  | 'count:desc'
  | 'name:asc'
  | 'name:desc';

export interface RefinementListItem {
  label: string;
  value: string;
  count: number;
  isRefined: boolean;
}

export interface RefinementListConnectorParams {
  attribute: string;
  /** `'or'` (the default) puts the selected values in one ORed group; `'and'` ANDs them. */
  operator?: FacetOperator;
  /** Items shown before "show more". Defaults to 10. */
  limit?: number;
  showMore?: boolean;
  /** Items shown after "show more". Defaults to 20. */
  showMoreLimit?: number;
  sortBy?: RefinementListSortBy[];
  transformItems?: (items: RefinementListItem[]) => RefinementListItem[];
}

export interface RefinementListRenderState {
  items: RefinementListItem[];
  refine(value: string): void;
  createURL(value: string): string;
  /** ARIA scaffolding (spec 5.7): false when there is nothing to filter on. */
  canRefine: boolean;
  canToggleShowMore: boolean;
  isShowingMore: boolean;
  toggleShowMore(): void;
  sendEvent(eventType: EventType, objectID: string, position?: number): void;
}

const DEFAULT_SORT: RefinementListSortBy[] = ['isRefined', 'count:desc', 'name:asc'];

/** Facet list (spec 5.7). */
export function connectRefinementList<
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: RefinementListRenderState &
      RenderOptions<TParams & RefinementListConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & RefinementListConnectorParams> {
  return createConnector<
    TParams & RefinementListConnectorParams,
    RefinementListRenderState,
    { isShowingMore: boolean }
  >({
    $$type: 'xps.refinementList',
    createLocal: () => ({ isShowingMore: false }),
    init(params, options) {
      options.helper.setFacetOperator(params.attribute, params.operator ?? 'or');
    },
    getRequestParameters: (request, params) => withFacet(request, params.attribute),
    getRenderState(base, params, context) {
      const counts = base.results?.facets?.[params.attribute] ?? {};
      const refined = base.state.facetFilters[params.attribute] ?? [];
      const all: RefinementListItem[] = Object.entries(counts).map(([value, count]) => ({
        label: value,
        value,
        count,
        isRefined: refined.includes(value),
      }));
      // A refined value can drop out of the counts (an `and` group, or a stale response);
      // keep it visible so the control can be un-checked.
      for (const value of refined) {
        if (!all.some((item) => item.value === value)) {
          all.push({ label: value, value, count: 0, isRefined: true });
        }
      }
      sortItems(all, params.sortBy ?? DEFAULT_SORT);

      const limit = params.limit ?? 10;
      const showMoreLimit = params.showMoreLimit ?? 20;
      const cap = params.showMore && context.local.isShowingMore ? showMoreLimit : limit;
      const capped = all.slice(0, cap);
      const items = params.transformItems ? params.transformItems(capped) : capped;

      return {
        items,
        canRefine: items.length > 0,
        canToggleShowMore: Boolean(params.showMore) && all.length > limit,
        isShowingMore: context.local.isShowingMore,
        toggleShowMore() {
          context.local.isShowingMore = !context.local.isShowingMore;
          context.rerender();
        },
        refine(value) {
          base.helper.toggleFacetRefinement(params.attribute, value).search();
        },
        createURL(value) {
          return base.instantSearchInstance.createURL(
            toggleFacetRefinement(base.state, params.attribute, value)
          );
        },
        sendEvent(eventType, objectID, position) {
          base.instantSearchInstance.sendEvent(eventType, objectID, position);
        },
      };
    },
  })(renderFn, unmountFn);
}

function sortItems(items: RefinementListItem[], sortBy: RefinementListSortBy[]): void {
  items.sort((a, b) => {
    for (const criterion of sortBy) {
      const delta = compare(a, b, criterion);
      if (delta !== 0) return delta;
    }
    return 0;
  });
}

function compare(a: RefinementListItem, b: RefinementListItem, by: RefinementListSortBy): number {
  switch (by) {
    case 'isRefined':
      return Number(b.isRefined) - Number(a.isRefined);
    case 'count:asc':
      return a.count - b.count;
    case 'count:desc':
      return b.count - a.count;
    case 'name:asc':
      return a.label.localeCompare(b.label);
    case 'name:desc':
      return b.label.localeCompare(a.label);
  }
}
