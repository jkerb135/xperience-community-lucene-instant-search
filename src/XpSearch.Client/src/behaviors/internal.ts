/**
 * The plumbing every behaviour shares (spec 5.7): build a widget from a render function, call
 * it once on `init` with `isFirstRender: true` and again after every response.
 * Internal — not exported from the package entry points.
 */
import type {
  InitOptions,
  RenderArgs,
  RenderOptions,
  SearchRequest,
  SearchState,
  Widget,
  WidgetFactory,
} from '../types';

/** Per-widget scratch space plus a way to re-render without a state change (`showMore`). */
export interface BehaviorContext<TLocal> {
  local: TLocal;
  rerender(): void;
}

export interface BehaviorSpec<TParams, TExtra, TLocal> {
  $$type: string;
  createLocal?(): TLocal;
  prepareState?(state: SearchState, params: TParams): SearchState;
  prepareRequest?(request: SearchRequest, params: TParams): SearchRequest;
  init?(params: TParams, options: InitOptions): void;
  getRenderState(
    base: RenderOptions<TParams>,
    params: TParams,
    context: BehaviorContext<TLocal>
  ): TExtra;
  dispose?(params: TParams): void;
}

export type BehaviorRenderer<TParams, TExtra> = (
  renderOptions: TExtra & RenderOptions<TParams>,
  isFirstRender: boolean
) => void;

export function createBehavior<TParams, TExtra, TLocal = Record<string, never>>(
  spec: BehaviorSpec<TParams, TExtra, TLocal>
): (
  renderFn: BehaviorRenderer<TParams, TExtra>,
  unmountFn?: () => void
) => WidgetFactory<TParams> {
  return (renderFn, unmountFn) =>
    (params: TParams): Widget => {
      const local = (spec.createLocal?.() ?? {}) as TLocal;
      let lastBase: RenderOptions<TParams> | null = null;

      const call = (base: RenderOptions<TParams>, isFirstRender: boolean): void => {
        lastBase = base;
        const context: BehaviorContext<TLocal> = {
          local,
          rerender: () => {
            if (lastBase) call(lastBase, false);
          },
        };
        renderFn({ ...base, ...spec.getRenderState(base, params, context) }, isFirstRender);
      };

      return {
        $$type: spec.$$type,
        ...(spec.prepareState
          ? { prepareState: (state: SearchState) => spec.prepareState!(state, params) }
          : {}),
        ...(spec.prepareRequest
          ? { prepareRequest: (request: SearchRequest) => spec.prepareRequest!(request, params) }
          : {}),
        init(options: InitOptions) {
          spec.init?.(params, options);
          call(
            {
              params,
              results: null,
              state: options.state,
              actions: options.actions,
              search: options.search,
            },
            true
          );
        },
        render(options: RenderArgs) {
          call(
            {
              params,
              results: options.results,
              state: options.state,
              actions: options.actions,
              search: options.search,
            },
            false
          );
        },
        dispose() {
          spec.dispose?.(params);
          unmountFn?.();
        },
      };
    };
}

/** Adds attributes to `request.facets` without dropping what other widgets asked for. */
export function withFacetAttribute(request: SearchRequest, attribute: string): SearchRequest {
  return { ...request, facets: [...(request.facets ?? []), attribute] };
}
