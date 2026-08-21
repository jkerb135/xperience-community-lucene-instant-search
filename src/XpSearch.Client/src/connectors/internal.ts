/**
 * The plumbing every connector shares (spec 5.7): build a widget from a render function, call
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
export interface ConnectorContext<TLocal> {
  local: TLocal;
  rerender(): void;
}

export interface ConnectorSpec<TParams, TExtra, TLocal> {
  $$type: string;
  createLocal?(): TLocal;
  getSearchParameters?(state: SearchState, params: TParams): SearchState;
  getRequestParameters?(request: SearchRequest, params: TParams): SearchRequest;
  init?(params: TParams, options: InitOptions): void;
  getRenderState(
    base: RenderOptions<TParams>,
    params: TParams,
    context: ConnectorContext<TLocal>
  ): TExtra;
  dispose?(params: TParams): void;
}

export type ConnectorRenderer<TParams, TExtra> = (
  renderOptions: TExtra & RenderOptions<TParams>,
  isFirstRender: boolean
) => void;

export function createConnector<TParams, TExtra, TLocal = Record<string, never>>(
  spec: ConnectorSpec<TParams, TExtra, TLocal>
): (
  renderFn: ConnectorRenderer<TParams, TExtra>,
  unmountFn?: () => void
) => WidgetFactory<TParams> {
  return (renderFn, unmountFn) =>
    (widgetParams: TParams): Widget => {
      const local = (spec.createLocal?.() ?? {}) as TLocal;
      let lastBase: RenderOptions<TParams> | null = null;

      const call = (base: RenderOptions<TParams>, isFirstRender: boolean): void => {
        lastBase = base;
        const context: ConnectorContext<TLocal> = {
          local,
          rerender: () => {
            if (lastBase) call(lastBase, false);
          },
        };
        renderFn({ ...base, ...spec.getRenderState(base, widgetParams, context) }, isFirstRender);
      };

      return {
        $$type: spec.$$type,
        ...(spec.getSearchParameters
          ? { getSearchParameters: (state: SearchState) => spec.getSearchParameters!(state, widgetParams) }
          : {}),
        ...(spec.getRequestParameters
          ? {
              getRequestParameters: (request: SearchRequest) =>
                spec.getRequestParameters!(request, widgetParams),
            }
          : {}),
        init(options: InitOptions) {
          spec.init?.(widgetParams, options);
          call(
            {
              widgetParams,
              results: null,
              state: options.state,
              helper: options.helper,
              instantSearchInstance: options.instantSearchInstance,
            },
            true
          );
        },
        render(options: RenderArgs) {
          call(
            {
              widgetParams,
              results: options.results,
              state: options.state,
              helper: options.helper,
              instantSearchInstance: options.instantSearchInstance,
            },
            false
          );
        },
        dispose() {
          spec.dispose?.(widgetParams);
          unmountFn?.();
        },
      };
    };
}

/** Adds attributes to `request.facets` without dropping what other widgets asked for. */
export function withFacet(request: SearchRequest, attribute: string): SearchRequest {
  return { ...request, facets: [...(request.facets ?? []), attribute] };
}
