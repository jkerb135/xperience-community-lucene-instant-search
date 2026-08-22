/**
 * The widgets shipped with the library (spec 5.3). Every one of them is
 * `behaviour + default renderer` over the public behaviour API — the dogfooding rule of
 * spec 5.7 — and every one emits the markup contract in `themes/MARKUP.md`.
 */
import type { MountConfig, MountWidgetFactory } from '../bootstrap';
import type { Widget } from '../types';
import { clearFilters, activeFilters } from './activeFilters';
import { results } from './results';
import { pagination } from './pagination';
import { facetList } from './facetList';
import { searchBox } from './searchBox';
import { sortSelect } from './sortSelect';
import { resultStats } from './resultStats';
import { toggleFilter } from './toggleFilter';
import { rangeFilter } from './rangeFilter';
import { loadMore } from './loadMore';
import { suggestions } from './suggestions';

export { clearFilters, activeFilters } from './activeFilters';
export type {
  ClearFiltersWidgetParams,
  ActiveFiltersWidgetParams,
} from './activeFilters';
export { results } from './results';
export type { ResultsTemplates, ResultsWidgetParams } from './results';
export { pagination } from './pagination';
export type { PaginationWidgetParams } from './pagination';
export { facetList } from './facetList';
export type { FacetListWidgetParams } from './facetList';
export { searchBox } from './searchBox';
export type { SearchBoxWidgetParams } from './searchBox';
export { sortSelect } from './sortSelect';
export type { SortSelectWidgetParams } from './sortSelect';
export { resultStats } from './resultStats';
export type { ResultStatsWidgetParams } from './resultStats';
export { toggleFilter } from './toggleFilter';
export type { ToggleFilterWidgetParams } from './toggleFilter';
export { rangeFilter } from './rangeFilter';
export type { RangeFilterWidgetParams } from './rangeFilter';
export { loadMore } from './loadMore';
export type { LoadMoreWidgetParams } from './loadMore';
export { suggestions } from './suggestions';
export type { SuggestionsWidgetParams } from './suggestions';

/**
 * `data-xps-config` is JSON: its shape is only known at runtime, so the one cast in this file
 * is where the untyped mount configuration meets the typed widget parameters. A missing or
 * wrong option surfaces as the widget's own error, isolated by the instance (spec 5.7).
 */
const fromMount =
  <TParams extends { container: string | HTMLElement }>(
    factory: (params: TParams) => Widget
  ): MountWidgetFactory =>
  (config: MountConfig): Widget =>
    factory(config as unknown as TParams);

/**
 * Resolved by `data-xps-widget` unless `registerWidgetType` overrode the name (spec 7.1).
 * `categoryTree` is the one reserved name with no widget behind it: see
 * docs/internal/KNOWN-LIMITATIONS.md.
 */
export const DEFAULT_WIDGETS: Readonly<Record<string, MountWidgetFactory>> = {
  searchBox: fromMount(searchBox),
  results: fromMount(results),
  facetList: fromMount(facetList),
  pagination: fromMount(pagination),
  resultStats: fromMount(resultStats),
  sortSelect: fromMount(sortSelect),
  clearFilters: fromMount(clearFilters),
  activeFilters: fromMount(activeFilters),
  toggleFilter: fromMount(toggleFilter),
  rangeFilter: fromMount(rangeFilter),
  loadMore: fromMount(loadMore),
  suggestions: fromMount(suggestions),
};
