/**
 * `@yourco/xperience-search/behaviors` — behaviour without rendering (spec 5.7).
 *
 * A behaviour is what other libraries call a connector: it computes the render state and the
 * verbs (`apply`, `urlFor`, `isActive`, `canApply`, `isStalled`) and leaves the markup to you.
 *
 * `withSuggestions` and `withCategoryTree` are deliberately absent: they depend on `/suggest`
 * behaviour and on hierarchical facet semantics that are not decided yet. See
 * docs/internal/KNOWN-LIMITATIONS.md.
 */
export { withActiveFilters } from './behaviors/activeFilters';
export type {
  ActiveFilterItem,
  ActiveFiltersBehaviorParams,
  ActiveFiltersRenderState,
} from './behaviors/activeFilters';
export { withFacetList } from './behaviors/facetList';
export type {
  FacetListBehaviorParams,
  FacetListItem,
  FacetListRenderState,
  FacetListSortBy,
} from './behaviors/facetList';
export { withPagination } from './behaviors/pagination';
export type {
  PaginationBehaviorParams,
  PaginationRenderState,
} from './behaviors/pagination';
export { withRange } from './behaviors/range';
export type { RangeBehaviorParams, RangeRenderState } from './behaviors/range';
export { withResults } from './behaviors/results';
export type { ResultsBehaviorParams, ResultsRenderState } from './behaviors/results';
export { withResultStats } from './behaviors/resultStats';
export type { ResultStatsRenderState } from './behaviors/resultStats';
export { withSearchBox } from './behaviors/searchBox';
export type { SearchBoxBehaviorParams, SearchBoxRenderState } from './behaviors/searchBox';
export { withSortSelect } from './behaviors/sortSelect';
export type {
  SortSelectBehaviorParams,
  SortSelectItem,
  SortSelectRenderState,
} from './behaviors/sortSelect';
export type {
  RenderOptions,
  Result,
  SearchActions,
  SearchInstance,
  SearchResults,
  SearchState,
  Widget,
  WidgetFactory,
} from './types';
