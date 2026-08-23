import type { RenderOptions, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface RangeBehaviorParams {
  attribute: string;
  /**
   * Bounds of the control. The JSON contract carries no numeric facet statistics, so both
   * must be supplied by the caller; without them the control cannot apply anything.
   */
  min?: number;
  max?: number;
}

export interface RangeRenderState {
  /** Current bounds, `undefined` where unset. */
  start: [number | undefined, number | undefined];
  range: { min: number | undefined; max: number | undefined };
  canApply: boolean;
  /** Applies `gte min` and `lte max`; pass `undefined` for an open end. */
  apply(bounds: [number | undefined, number | undefined]): void;
}

/** Numeric range (spec 5.7). */
export function withRange<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: RangeRenderState & RenderOptions<TParams & RangeBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & RangeBehaviorParams> {
  return createBehavior<TParams & RangeBehaviorParams, RangeRenderState, never>({
    $$type: 'xps.range',
    getRenderState(base, params) {
      const on = (operator: 'gte' | 'lte'): number | undefined =>
        base.state.filters.numeric.find(
          (n) => n.attribute === params.attribute && n.operator === operator
        )?.value;
      return {
        start: [on('gte') ?? params.min, on('lte') ?? params.max],
        range: { min: params.min, max: params.max },
        canApply:
          params.min !== undefined && params.max !== undefined && params.min < params.max,
        apply([lower, upper]) {
          const { actions } = base;
          if (lower === undefined || lower === params.min) {
            actions.removeNumericFilter(params.attribute, 'gte');
          } else {
            actions.setNumericFilter(params.attribute, 'gte', lower);
          }
          if (upper === undefined || upper === params.max) {
            actions.removeNumericFilter(params.attribute, 'lte');
          } else {
            actions.setNumericFilter(params.attribute, 'lte', upper);
          }
          actions.search();
        },
      };
    },
  })(renderFn, unmountFn);
}
