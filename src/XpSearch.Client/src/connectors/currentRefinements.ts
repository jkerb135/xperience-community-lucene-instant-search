import { clearRefinements, removeNumericRefinement, toggleFacetRefinement } from '../state';
import type { NumericOperator, RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface CurrentRefinementItem {
  attribute: string;
  type: 'facet' | 'numeric';
  /** The facet value, or the number of a numeric refinement. */
  value: string | number;
  operator?: NumericOperator;
  /** Ready-to-display text: `"coffee"` for a facet, `"price <= 50"` for a numeric one. */
  label: string;
  /** Removes this refinement and searches. */
  refine(): void;
  createURL(): string;
}

export interface CurrentRefinementsConnectorParams {
  /** Only show refinements on these attributes. Defaults to all of them. */
  includedAttributes?: string[];
  excludedAttributes?: string[];
  transformItems?: (items: CurrentRefinementItem[]) => CurrentRefinementItem[];
}

export interface CurrentRefinementsRenderState {
  items: CurrentRefinementItem[];
  canRefine: boolean;
  /** Clears every refinement at once, and searches. */
  clearAll(): void;
  createClearAllURL(): string;
}

/** Removable filter chips (spec 5.7). The query itself is not listed as a refinement. */
export function connectCurrentRefinements<
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: CurrentRefinementsRenderState &
      RenderOptions<TParams & CurrentRefinementsConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & CurrentRefinementsConnectorParams> {
  return createConnector<
    TParams & CurrentRefinementsConnectorParams,
    CurrentRefinementsRenderState,
    never
  >({
    $$type: 'xps.currentRefinements',
    getRenderState(base, params) {
      const included = (attribute: string): boolean =>
        (!params.includedAttributes || params.includedAttributes.includes(attribute)) &&
        !(params.excludedAttributes ?? []).includes(attribute);

      const items: CurrentRefinementItem[] = [];
      for (const [attribute, values] of Object.entries(base.state.facetFilters)) {
        if (!included(attribute)) continue;
        for (const value of values) {
          items.push({
            attribute,
            type: 'facet',
            value,
            label: value,
            refine: () => {
              base.helper.toggleFacetRefinement(attribute, value).search();
            },
            createURL: () =>
              base.instantSearchInstance.createURL(
                toggleFacetRefinement(base.state, attribute, value)
              ),
          });
        }
      }
      for (const numeric of base.state.numericFilters) {
        if (!included(numeric.attribute)) continue;
        items.push({
          attribute: numeric.attribute,
          type: 'numeric',
          value: numeric.value,
          operator: numeric.operator,
          label: `${numeric.attribute} ${numeric.operator} ${numeric.value}`,
          refine: () => {
            base.helper.removeNumericRefinement(numeric.attribute, numeric.operator).search();
          },
          createURL: () =>
            base.instantSearchInstance.createURL(
              removeNumericRefinement(base.state, numeric.attribute, numeric.operator)
            ),
        });
      }

      const shown = params.transformItems ? params.transformItems(items) : items;
      return {
        items: shown,
        canRefine: shown.length > 0,
        clearAll() {
          base.helper.clearRefinements().search();
        },
        createClearAllURL: () =>
          base.instantSearchInstance.createURL(clearRefinements(base.state)),
      };
    },
  })(renderFn, unmountFn);
}
