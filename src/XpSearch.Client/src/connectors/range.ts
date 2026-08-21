import type { RenderOptions, WidgetFactory } from '../types';
import { createConnector } from './internal';

export interface RangeConnectorParams {
  attribute: string;
  /**
   * Bounds of the control. The JSON contract carries no numeric facet statistics, so both
   * must be supplied by the caller; without them the control cannot refine.
   */
  min?: number;
  max?: number;
}

export interface RangeRenderState {
  /** Current bounds, `undefined` where unset. */
  start: [number | undefined, number | undefined];
  range: { min: number | undefined; max: number | undefined };
  canRefine: boolean;
  /** Applies `>= min` and `<= max`; pass `undefined` for an open end. */
  refine(bounds: [number | undefined, number | undefined]): void;
}

/** Numeric range (spec 5.7). */
export function connectRange<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: RangeRenderState & RenderOptions<TParams & RangeConnectorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & RangeConnectorParams> {
  return createConnector<TParams & RangeConnectorParams, RangeRenderState, never>({
    $$type: 'xps.range',
    getRenderState(base, params) {
      const on = (operator: '>=' | '<='): number | undefined =>
        base.state.numericFilters.find(
          (n) => n.attribute === params.attribute && n.operator === operator
        )?.value;
      return {
        start: [on('>=') ?? params.min, on('<=') ?? params.max],
        range: { min: params.min, max: params.max },
        canRefine:
          params.min !== undefined && params.max !== undefined && params.min < params.max,
        refine([lower, upper]) {
          const { helper } = base;
          if (lower === undefined || lower === params.min) {
            helper.removeNumericRefinement(params.attribute, '>=');
          } else {
            helper.setNumericRefinement(params.attribute, '>=', lower);
          }
          if (upper === undefined || upper === params.max) {
            helper.removeNumericRefinement(params.attribute, '<=');
          } else {
            helper.setNumericRefinement(params.attribute, '<=', upper);
          }
          helper.search();
        },
      };
    },
  })(renderFn, unmountFn);
}
