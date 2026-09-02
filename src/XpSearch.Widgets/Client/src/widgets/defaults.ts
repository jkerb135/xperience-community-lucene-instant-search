/**
 * The default widget map `mountAll` resolves `data-xps-widget` names through. A chunk of its
 * own on purpose: it references every widget as a value, so bundling it into the `./widgets`
 * barrel would drag all fourteen widgets into any bundle that imports one of them.
 */
import type { MountConfig, MountWidgetFactory } from '../bootstrap';
import type { Widget } from '../types';
import { clearFilters, activeFilters } from './activeFilters';
import { results } from './results';
import { pagination } from './pagination';
import { categoryTree } from './categoryTree';
import { facetList } from './facetList';
import { filterSort } from './filterSort';
import { searchBox } from './searchBox';
import { sortSelect } from './sortSelect';
import { resultStats } from './resultStats';
import { toggleFilter } from './toggleFilter';
import { rangeFilter } from './rangeFilter';
import { loadMore } from './loadMore';
import { suggestions } from './suggestions';

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

/** Resolved by `data-xps-widget` unless `registerWidgetType` overrode the name (spec 7.1). */
export const DEFAULT_WIDGETS: Readonly<Record<string, MountWidgetFactory>> = {
  searchBox: fromMount(searchBox),
  results: fromMount(results),
  facetList: fromMount(facetList),
  filterSort: fromMount(filterSort),
  categoryTree: fromMount(categoryTree),
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
