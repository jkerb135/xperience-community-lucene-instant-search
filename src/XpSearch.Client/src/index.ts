/**
 * `@yourco/xperience-search` — the core entry point (spec 5.2).
 * The behaviours live at `@yourco/xperience-search/behaviors`.
 */
import { createSearch } from './instance';

export { createSearch };
export default createSearch;

export { SearchClient, SearchError } from './client';
export type { SearchClientOptions } from './client';
export {
  FIRST_PARTY_WIDGET_TYPES,
  getWidgetType,
  mountAll,
  readMountConfig,
  registerWidgetType,
} from './bootstrap';
export type {
  MountConfig,
  MountConfigOf,
  MountConfigSpec,
  MountFieldSpec,
  MountWidgetFactory,
} from './bootstrap';
export { widgetId } from './widgets/dom';
export { defaultRouteToState, defaultStateToRoute } from './routing';
export {
  activeFilters,
  clearFilters,
  facetList,
  loadMore,
  pagination,
  rangeFilter,
  results,
  resultStats,
  searchBox,
  sortSelect,
  suggestions,
  toggleFilter,
} from './widgets';
export type {
  ActiveFiltersWidgetParams,
  ClearFiltersWidgetParams,
  FacetListWidgetParams,
  LoadMoreWidgetParams,
  PaginationWidgetParams,
  RangeFilterWidgetParams,
  ResultsTemplates,
  ResultsWidgetParams,
  ResultStatsWidgetParams,
  SearchBoxWidgetParams,
  SortSelectWidgetParams,
  SuggestionsWidgetParams,
  ToggleFilterWidgetParams,
} from './widgets';
export { escapeHtml, formatNumber, highlight, html, render, TemplateResult } from './templates/html';
export type { Renderable, TemplateHelpers } from './templates/html';
export {
  API_VERSION,
  API_VERSION_HEADER,
  EVENTS_ROUTE,
  QUERY_ROUTE,
  SUGGEST_ROUTE,
} from './contract/constants';
export type {
  EventRequest,
  EventType,
  HighlightOptions,
  RankingInfo,
  SearchRedirect,
  SearchRequest,
  SearchResponse,
  SuggestRequest,
  SuggestResponse,
  Suggestion,
} from './contract/generated';
export type {
  FacetFilter,
  FacetOperator,
  FacetValue,
  InitOptions,
  NumericFilter,
  NumericOperator,
  RenderArgs,
  RenderOptions,
  Result,
  RoutingOptions,
  SearchActions,
  SearchEvents,
  SearchInstance,
  SearchResults,
  SearchState,
  SearchStatus,
  StateFilters,
  Widget,
  WidgetFactory,
  XpSearchOptions,
} from './types';
