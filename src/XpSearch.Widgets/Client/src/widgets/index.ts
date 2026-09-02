/**
 * The widgets shipped with the library (spec 5.3). Every one of them is
 * `behaviour + default renderer` over the public behaviour API — the dogfooding rule of
 * spec 5.7 — and every one emits the markup contract in `themes/MARKUP.md`.
 */
// The DEFAULT_WIDGETS map lives in ./defaults, a chunk of its own: it references every widget
// as a value, so keeping it out of this barrel is what lets a bundler drop the widgets a
// consumer did not import (the package-check's "./widgets barrel" fixture pins that).
export { DEFAULT_WIDGETS } from './defaults';

export { clearFilters, activeFilters } from './activeFilters';
export type {
  ClearFiltersWidgetParams,
  ActiveFiltersWidgetParams,
} from './activeFilters';
export { results } from './results';
export type { ResultsTemplates, ResultsWidgetParams } from './results';
export { pagination } from './pagination';
export type { PaginationWidgetParams } from './pagination';
export { categoryTree } from './categoryTree';
export type { CategoryTreeWidgetParams } from './categoryTree';
export { facetList } from './facetList';
export type { FacetListWidgetParams } from './facetList';
export { filterSort } from './filterSort';
export type { FilterSortFacet, FilterSortWidgetParams } from './filterSort';
export { searchBox } from './searchBox';
export type { SearchBoxSuggestionsParams, SearchBoxWidgetParams } from './searchBox';
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

