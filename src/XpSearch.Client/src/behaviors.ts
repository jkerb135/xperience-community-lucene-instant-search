/**
 * `@yourco/xperience-search/behaviors` — behaviour without rendering (spec 5.7).
 *
 * A behaviour is what other libraries call a connector: it computes the render state and the
 * verbs (`apply`, `urlFor`, `isActive`, `canApply`, `isStalled`) and leaves the markup to you.
 *
 * `withCategoryTree` is deliberately absent: a hierarchy needs a facet shape the contract does
 * not have (`FacetValue` is flat). See docs/internal/KNOWN-LIMITATIONS.md.
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
export { withLoadMore } from './behaviors/loadMore';
export type { LoadMoreBehaviorParams, LoadMoreRenderState } from './behaviors/loadMore';
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
export { withSuggestions } from './behaviors/suggestions';
export type {
  SuggestionsBehaviorParams,
  SuggestionsRenderState,
} from './behaviors/suggestions';
export type {
  SortSelectBehaviorParams,
  SortSelectItem,
  SortSelectRenderState,
} from './behaviors/sortSelect';
export type {
  RenderOptions,
  Result,
  Suggestion,
  SearchActions,
  SearchInstance,
  SearchResults,
  SearchState,
  Widget,
  WidgetFactory,
} from './types';
