/**
 * `@yourco/xperience-search/connectors` — behaviour without rendering (spec 5.7).
 *
 * `connectAutocomplete` and `connectHierarchicalMenu` are deliberately absent: they depend on
 * `/suggest` behaviour and on hierarchical facet semantics that are not decided yet. See
 * docs/internal/KNOWN-LIMITATIONS.md.
 */
export { connectCurrentRefinements } from './connectors/currentRefinements';
export type {
  CurrentRefinementItem,
  CurrentRefinementsConnectorParams,
  CurrentRefinementsRenderState,
} from './connectors/currentRefinements';
export { connectHits } from './connectors/hits';
export type { HitsConnectorParams, HitsRenderState } from './connectors/hits';
export { connectPagination } from './connectors/pagination';
export type {
  PaginationConnectorParams,
  PaginationRenderState,
} from './connectors/pagination';
export { connectRange } from './connectors/range';
export type { RangeConnectorParams, RangeRenderState } from './connectors/range';
export { connectRefinementList } from './connectors/refinementList';
export type {
  RefinementListConnectorParams,
  RefinementListItem,
  RefinementListRenderState,
  RefinementListSortBy,
} from './connectors/refinementList';
export { connectSearchBox } from './connectors/searchBox';
export type { SearchBoxConnectorParams, SearchBoxRenderState } from './connectors/searchBox';
export { connectSortBy } from './connectors/sortBy';
export type { SortByConnectorParams, SortByItem, SortByRenderState } from './connectors/sortBy';
export { connectStats } from './connectors/stats';
export type { StatsRenderState } from './connectors/stats';
export type {
  Hit,
  InstantSearch,
  RenderOptions,
  SearchHelper,
  SearchResults,
  SearchState,
  Widget,
  WidgetFactory,
} from './types';
