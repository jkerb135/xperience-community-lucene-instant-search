import { attributeUnit, displayValue } from '../labels';
import { clearFilters, removeNumericFilter, toggleFacet } from '../state';
import { formatNumber } from '../templates/html';
import type { NumericOperator, RenderOptions, SearchInstance, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface ActiveFilterItem {
  attribute: string;
  type: 'facet' | 'numeric';
  /** The facet value, or the number of a numeric filter (its lower bound when it has both). */
  value: string | number;
  /** The operator of a one-ended numeric filter; absent on a bounded range and on facets. */
  operator?: NumericOperator;
  /**
   * Ready-to-display text for the VALUE, as a visitor reads it (TH-12): `"Hot tips"` for a
   * facet, `"up to 50"` or `"50 – 200 USD"` for a numeric one. The attribute's own name is
   * `attribute` plus the display registry — a renderer decides whether to put it in front.
   */
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

/**
 * A numeric refinement as a sentence, never as an operator (TH-12): `up to 200`, `from 50`,
 * `50 – 200`. The unit is the one the range filter on the attribute shows under its own inputs,
 * in the same place: once, at the end.
 */
function numericSentence(
  search: SearchInstance,
  attribute: string,
  from: number | undefined,
  to: number | undefined
): string {
  const unit = attributeUnit(search, attribute);
  const suffix = unit === undefined ? '' : ` ${unit}`;
  const sentence =
    from !== undefined && to !== undefined
      ? `${formatNumber(from)} – ${formatNumber(to)}`
      : from !== undefined
        ? `from ${formatNumber(from)}`
        : `up to ${formatNumber(to as number)}`;
  return `${sentence}${suffix}`;
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
            label: displayValue(base.search, facet.attribute, value),
            apply: () => {
              base.actions.toggleFacet(facet.attribute, value).search();
            },
            urlFor: () => base.search.urlFor(toggleFacet(base.state, facet.attribute, value)),
          });
        }
      }
      // One chip per attribute, not per bound: "from 50" and "up to 200" are the two ends of one
      // range to a visitor, and removing it removes both (TH-12).
      for (const attribute of new Set(
        base.state.filters.numeric.map((numeric) => numeric.attribute)
      )) {
        if (!included(attribute)) continue;
        const bounds = base.state.filters.numeric.filter((n) => n.attribute === attribute);
        const from = bounds.find((n) => n.operator === 'gte' || n.operator === 'gt')?.value;
        const to = bounds.find((n) => n.operator === 'lte' || n.operator === 'lt')?.value;
        const single = bounds.length === 1 ? bounds[0]?.operator : undefined;
        items.push({
          attribute,
          type: 'numeric',
          value: from ?? (to as number),
          ...(single === undefined ? {} : { operator: single }),
          label: numericSentence(base.search, attribute, from, to),
          apply: () => {
            base.actions.removeNumericFilter(attribute).search();
          },
          urlFor: () => base.search.urlFor(removeNumericFilter(base.state, attribute)),
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
