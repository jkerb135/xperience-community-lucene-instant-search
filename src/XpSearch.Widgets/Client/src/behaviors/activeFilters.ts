import { clearFilters, removeNumericFilter, toggleFacet } from '../state';
import type { NumericOperator, RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface ActiveFilterItem {
  attribute: string;
  type: 'facet' | 'numeric';
  /** The facet value, or the number of a numeric filter. */
  value: string | number;
  operator?: NumericOperator;
  /** Ready-to-display text: `"coffee"` for a facet, `"price lte 50"` for a numeric one. */
  label: string;
  /** Removes this filter and searches. */
  apply(): void;
  urlFor(): string;
}

export interface ActiveFiltersBehaviorParams {
  /** Only show filters on these attributes. Defaults to all of them. */
  includedAttributes?: string[];
  excludedAttributes?: string[];
  transformItems?: (items: ActiveFilterItem[]) => ActiveFilterItem[];
}

export interface ActiveFiltersRenderState {
  items: ActiveFilterItem[];
  canApply: boolean;
  /** Clears every filter at once, and searches. */
  clearAll(): void;
  clearAllUrl(): string;
}

/** Removable filter chips (spec 5.7). The query itself is not listed as a filter. */
export function withActiveFilters<
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: ActiveFiltersRenderState &
      RenderOptions<TParams & ActiveFiltersBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & ActiveFiltersBehaviorParams> {
  return createBehavior<
    TParams & ActiveFiltersBehaviorParams,
    ActiveFiltersRenderState,
    never
  >({
    $$type: 'xps.activeFilters',
    getRenderState(base, params) {
      const included = (attribute: string): boolean =>
        (!params.includedAttributes || params.includedAttributes.includes(attribute)) &&
        !(params.excludedAttributes ?? []).includes(attribute);

      const items: ActiveFilterItem[] = [];
      for (const facet of base.state.filters.facets) {
        if (!included(facet.attribute)) continue;
        for (const value of facet.values) {
          items.push({
            attribute: facet.attribute,
            type: 'facet',
            value,
            label: value,
            apply: () => {
              base.actions.toggleFacet(facet.attribute, value).search();
            },
            urlFor: () => base.search.urlFor(toggleFacet(base.state, facet.attribute, value)),
          });
        }
      }
      for (const numeric of base.state.filters.numeric) {
        if (!included(numeric.attribute)) continue;
        items.push({
          attribute: numeric.attribute,
          type: 'numeric',
          value: numeric.value,
          operator: numeric.operator,
          label: `${numeric.attribute} ${numeric.operator} ${numeric.value}`,
          apply: () => {
            base.actions.removeNumericFilter(numeric.attribute, numeric.operator).search();
          },
          urlFor: () =>
            base.search.urlFor(
              removeNumericFilter(base.state, numeric.attribute, numeric.operator)
            ),
        });
      }

      const shown = params.transformItems ? params.transformItems(items) : items;
      return {
        items: shown,
        canApply: shown.length > 0,
        clearAll() {
          base.actions.clearFilters().search();
        },
        clearAllUrl: () => base.search.urlFor(clearFilters(base.state)),
      };
    },
  })(renderFn, unmountFn);
}
