/**
 * `@yourco/xperience-search` — the core entry point (spec 5.2).
 * The connectors live at `@yourco/xperience-search/connectors`.
 */
import { xpsearch } from './instance';

export { xpsearch };
export default xpsearch;

export { SearchClient, SearchError } from './client';
export type { SearchClientOptions } from './client';
export {
  FIRST_PARTY_WIDGET_TYPES,
  getWidgetType,
  mountAll,
  registerWidgetType,
} from './bootstrap';
export type { MountConfig, MountWidgetFactory } from './bootstrap';
export { defaultRouteToState, defaultStateToRoute } from './routing';
export {
  clearRefinements,
  currentRefinements,
  hits,
  pagination,
  refinementList,
  searchBox,
  sortBy,
  stats,
  toggleRefinement,
} from './widgets';
export type {
  ClearRefinementsWidgetParams,
  CurrentRefinementsWidgetParams,
  HitsTemplates,
  HitsWidgetParams,
  PaginationWidgetParams,
  RefinementListWidgetParams,
  SearchBoxWidgetParams,
  SortByWidgetParams,
  StatsWidgetParams,
  ToggleRefinementWidgetParams,
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
  SearchRequest,
  SearchResponse,
  SuggestRequest,
  SuggestResponse,
  Suggestion,
} from './contract/generated';
export type {
  FacetOperator,
  Hit,
  InitOptions,
  InstantSearch,
  InstantSearch as XpSearch,
  NumericOperator,
  NumericRefinement,
  RenderArgs,
  RenderOptions,
  RoutingOptions,
  SearchEvents,
  SearchHelper,
  SearchResults,
  SearchState,
  SearchStatus,
  Widget,
  WidgetFactory,
  XpSearchOptions,
} from './types';
