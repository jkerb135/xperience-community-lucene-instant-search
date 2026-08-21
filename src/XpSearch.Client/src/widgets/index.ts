/**
 * The widgets shipped with the library (spec 5.3). Every one of them is
 * `connector + default renderer` over the public connector API — the dogfooding rule of
 * spec 5.7 — and every one emits the markup contract in `themes/MARKUP.md`.
 */
import type { MountConfig, MountWidgetFactory } from '../bootstrap';
import type { Widget } from '../types';
import { clearRefinements, currentRefinements } from './currentRefinements';
import { hits } from './hits';
import { pagination } from './pagination';
import { refinementList } from './refinementList';
import { searchBox } from './searchBox';
import { sortBy } from './sortBy';
import { stats } from './stats';
import { toggleRefinement } from './toggleRefinement';

export { clearRefinements, currentRefinements } from './currentRefinements';
export type {
  ClearRefinementsWidgetParams,
  CurrentRefinementsWidgetParams,
} from './currentRefinements';
export { hits } from './hits';
export type { HitsTemplates, HitsWidgetParams } from './hits';
export { pagination } from './pagination';
export type { PaginationWidgetParams } from './pagination';
export { refinementList } from './refinementList';
export type { RefinementListWidgetParams } from './refinementList';
export { searchBox } from './searchBox';
export type { SearchBoxWidgetParams } from './searchBox';
export { sortBy } from './sortBy';
export type { SortByWidgetParams } from './sortBy';
export { stats } from './stats';
export type { StatsWidgetParams } from './stats';
export { toggleRefinement } from './toggleRefinement';
export type { ToggleRefinementWidgetParams } from './toggleRefinement';

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
  hits: fromMount(hits),
  refinementList: fromMount(refinementList),
  pagination: fromMount(pagination),
  stats: fromMount(stats),
  sortBy: fromMount(sortBy),
  clearRefinements: fromMount(clearRefinements),
  currentRefinements: fromMount(currentRefinements),
  toggleRefinement: fromMount(toggleRefinement),
};
