import { facetValues, toggleFacet } from '../state';
import type { EventType, FacetOperator, RenderOptions, WidgetFactory } from '../types';
import { createBehavior, withFacetAttribute } from './internal';

/** How the item list is ordered. Applied left to right, first difference wins. */
export type FacetListSortBy =
  | 'isActive'
  | 'count:asc'
  | 'count:desc'
  | 'name:asc'
  | 'name:desc';

export interface FacetListItem {
  /** The text to display: the taxonomy tag title when the server has one. */
  label: string;
  /** The value to send back in `filters.facets`: the tag code name. */
  value: string;
  count: number;
  isActive: boolean;
}

export interface FacetListBehaviorParams {
  attribute: string;
  /** `'or'` (the default) matches any selected value; `'and'` requires all of them. */
  operator?: FacetOperator;
  /** Items shown before "show more". Defaults to 10. */
  limit?: number;
  showMore?: boolean;
  /** Items shown after "show more". Defaults to 20. */
  showMoreLimit?: number;
  sortBy?: FacetListSortBy[];
  transformItems?: (items: FacetListItem[]) => FacetListItem[];
}

export interface FacetListRenderState {
  items: FacetListItem[];
  apply(value: string): void;
  urlFor(value: string): string;
  /** ARIA scaffolding (spec 5.7): false when there is nothing to filter on. */
  canApply: boolean;
  canToggleShowMore: boolean;
  isShowingMore: boolean;
  toggleShowMore(): void;
  sendEvent(type: EventType, resultId: string, position?: number): void;
}

const DEFAULT_SORT: FacetListSortBy[] = ['isActive', 'count:desc', 'name:asc'];

/** Facet list (spec 5.7). */
export function withFacetList<
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: FacetListRenderState & RenderOptions<TParams & FacetListBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & FacetListBehaviorParams> {
  return createBehavior<
    TParams & FacetListBehaviorParams,
    FacetListRenderState,
    { isShowingMore: boolean }
  >({
    $$type: 'xps.facetList',
    createLocal: () => ({ isShowingMore: false }),
    init(params, options) {
      options.actions.setFacetOperator(params.attribute, params.operator ?? 'or');
    },
    prepareRequest: (request, params) => withFacetAttribute(request, params.attribute),
    getRenderState(base, params, context) {
      const values = base.results?.facets?.[params.attribute] ?? [];
      const active = facetValues(base.state, params.attribute);
      const all: FacetListItem[] = values.map((value) => ({
        // The label comes from the server: for a taxonomy dimension it is the tag title, so a
        // facet list never displays a code name.
        label: value.label,
        value: value.value,
        count: value.count,
        isActive: active.includes(value.value),
      }));
      // A selected value can drop out of the counts (an `and` entry, or a stale response);
      // keep it visible so the control can be un-checked.
      for (const value of active) {
        if (!all.some((item) => item.value === value)) {
          all.push({ label: value, value, count: 0, isActive: true });
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
        canApply: items.length > 0,
        canToggleShowMore: Boolean(params.showMore) && all.length > limit,
        isShowingMore: context.local.isShowingMore,
        toggleShowMore() {
          context.local.isShowingMore = !context.local.isShowingMore;
          context.rerender();
        },
        apply(value) {
          base.actions.toggleFacet(params.attribute, value).search();
        },
        urlFor(value) {
          return base.search.urlFor(toggleFacet(base.state, params.attribute, value));
        },
        sendEvent(type, resultId, position) {
          base.search.sendEvent(type, resultId, position);
        },
      };
    },
  })(renderFn, unmountFn);
}

function sortItems(items: FacetListItem[], sortBy: FacetListSortBy[]): void {
  items.sort((a, b) => {
    for (const criterion of sortBy) {
      const delta = compare(a, b, criterion);
      if (delta !== 0) return delta;
    }
    return 0;
  });
}

function compare(a: FacetListItem, b: FacetListItem, by: FacetListSortBy): number {
  switch (by) {
    case 'isActive':
      return Number(b.isActive) - Number(a.isActive);
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
