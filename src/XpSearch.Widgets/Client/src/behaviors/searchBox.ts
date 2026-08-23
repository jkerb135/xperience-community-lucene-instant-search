import type { RenderOptions, SearchResults, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface SearchBoxBehaviorParams {
  /** Intercepts a query before it reaches the state, e.g. to strip or gate input. */
  queryHook?: (query: string, search: (value: string) => void) => void;
  /**
   * Defaults to `true`. Navigate to `response.redirect.url` when a redirect rule matched.
   * Only ever for a query the visitor submitted — see {@link SearchBoxRenderState.submit}.
   */
  followRedirects?: boolean;
  /** `window` by default; injectable for tests and SSR. */
  windowRef?: Window;
}

export interface SearchBoxRenderState {
  query: string;
  /** Searches for a query the visitor is still typing. Never follows a redirect rule. */
  apply(query: string): void;
  /**
   * Searches for a query the visitor submitted (Enter, or the submit button). Only this one
   * follows a redirect rule, so a visitor can always type past a pattern that redirects.
   */
  submit(query: string): void;
  clear(): void;
  /** True while a request has been running longer than `stalledSearchDelayMs`. */
  isStalled: boolean;
}

/** Remembers whether the response still in flight belongs to a submitted query. */
interface SearchBoxLocal {
  submitted: boolean;
  /** The response the redirect was already decided on. Identity, not value: a re-render with the
   * same response must not navigate twice, and the render that a state change schedules before the
   * request comes back still carries the previous one. */
  seen: SearchResults | null | undefined;
}

/** Query input (spec 5.7). */
export function withSearchBox<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SearchBoxRenderState & RenderOptions<TParams & SearchBoxBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SearchBoxBehaviorParams> {
  return createBehavior<TParams & SearchBoxBehaviorParams, SearchBoxRenderState, SearchBoxLocal>({
    $$type: 'xps.searchBox',
    createLocal: () => ({ submitted: false, seen: undefined }),
    getRenderState(base, params, context) {
      const run = (query: string, submitted: boolean): void => {
        context.local.submitted = submitted;
        base.actions.setQuery(query).search();
      };
      const start = (query: string, submitted: boolean): void => {
        if (params.queryHook) params.queryHook(query, (value) => run(value, submitted));
        else run(query, submitted);
      };

      if (base.results !== context.local.seen) {
        context.local.seen = base.results;
        const redirect = context.local.submitted ? base.results?.redirect : null;
        context.local.submitted = false;
        const target = params.windowRef ?? (typeof window === 'undefined' ? undefined : window);
        if (redirect && params.followRedirects !== false) target?.location.assign(redirect.url);
      }

      return {
        query: base.state.query,
        apply(query) {
          start(query, false);
        },
        submit(query) {
          start(query, true);
        },
        clear() {
          run('', false);
        },
        isStalled: base.search.status === 'stalled',
      };
    },
  })(renderFn, unmountFn);
}
