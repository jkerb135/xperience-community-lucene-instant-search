import { FIRST_PAGE } from '../state';
import type { RenderOptions, Suggestion, WidgetFactory } from '../types';
import { createBehavior } from './internal';

export interface SuggestionsBehaviorParams {
  /** Trailing debounce before `/suggest` is called. Defaults to 150 ms. */
  debounceMs?: number;
  /** Shortest query that asks for suggestions at all. Defaults to 1. */
  minQueryLength?: number;
  /** `SuggestRequest.limit`. Defaults to 5, the server's own default. */
  limit?: number;
  /** `SuggestRequest.language`. Defaults to the instance's `language`. */
  language?: string;
  /**
   * The full results page. When set, submitting navigates there with the query in the URL, and
   * typing does **not** search in place — the widget is a header search box. When unset the
   * widget searches its own instance as the visitor types.
   */
  resultsUrl?: string;
  /** `window` by default; injectable for tests and SSR. */
  windowRef?: Window;
}

export interface SuggestionsRenderState {
  /** What is in the input: the visitor's typing, not `state.query`, until one is picked. */
  query: string;
  suggestions: Suggestion[];
  isOpen: boolean;
  /** Index into `suggestions` of the active option, or `-1` for none. */
  activeIndex: number;
  /** A `/suggest` call is in flight. */
  isLoading: boolean;
  /** Where "see all results" goes, or `null` when no `resultsUrl` is configured. */
  seeAllUrl: string | null;
  /** The visitor typed. Debounced, and the answer to an older keystroke is dropped. */
  setQuery(query: string): void;
  /** Moves the active option: an offset (wrapping), or one of the two ends. */
  move(to: number | 'first' | 'last'): void;
  /** Picks a suggestion: follows its `url`, or searches for its `text` when it has none. */
  select(index: number): void;
  /** Enter with no active option: goes to `resultsUrl`, or searches in place. */
  submit(): void;
  close(): void;
  clear(): void;
}

interface SuggestionsLocal {
  /** `null` until the visitor types: `state.query` is the initial value. */
  query: string | null;
  suggestions: Suggestion[];
  isOpen: boolean;
  activeIndex: number;
  isLoading: boolean;
  /** Newest requested suggestion; an older answer that arrives later is dropped. */
  sequence: number;
  timer: ReturnType<typeof setTimeout> | undefined;
}

/**
 * Autocomplete (spec 5.7). The transport is `SearchInstance.suggest`, i.e. `SearchClient.suggest`.
 *
 * A renderer must call `close()` from its unmount function: that is what drops a debounced call
 * that has not fired yet, and what makes the answer to an in-flight one stale.
 */
export function withSuggestions<TParams extends Record<string, unknown> = Record<string, unknown>>(
  renderFn: (
    renderOptions: SuggestionsRenderState & RenderOptions<TParams & SuggestionsBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & SuggestionsBehaviorParams> {
  return createBehavior<TParams & SuggestionsBehaviorParams, SuggestionsRenderState, SuggestionsLocal>(
    {
      $$type: 'xps.suggestions',
      createLocal: () => ({
        query: null,
        suggestions: [],
        isOpen: false,
        activeIndex: -1,
        isLoading: false,
        sequence: 0,
        timer: undefined,
      }),
      getRenderState(base, params, context) {
        const state = context.local;
        const query = state.query ?? base.state.query;
        const inPlace = params.resultsUrl === undefined;
        const win = params.windowRef ?? (typeof window === 'undefined' ? undefined : window);

        /** The results-page URL for `query`, under the instance's own route mapping. */
        const seeAllUrl = ((): string | null => {
          if (params.resultsUrl === undefined) return null;
          const from = new URL(base.search.urlFor({ ...base.state, query, page: FIRST_PAGE }));
          const target = new URL(params.resultsUrl, from);
          target.search = from.search;
          return target.toString();
        })();

        /** Nothing in flight is worth rendering any more. */
        const cancel = (): void => {
          if (state.timer !== undefined) clearTimeout(state.timer);
          state.timer = undefined;
          state.sequence++;
          state.isLoading = false;
        };

        const closeUp = (): void => {
          state.isOpen = false;
          state.activeIndex = -1;
        };

        const fetchSuggestions = (value: string): void => {
          const sequence = ++state.sequence;
          state.isLoading = true;
          base.search
            .suggest({
              query: value,
              limit: params.limit ?? 5,
              ...(params.language === undefined ? {} : { language: params.language }),
            })
            .then(
              (response) => {
                // Latest response wins: an answer to a keystroke the visitor has typed past is
                // never allowed to repopulate the list.
                if (sequence !== state.sequence) return;
                state.isLoading = false;
                state.suggestions = response?.suggestions ?? [];
                state.isOpen = true;
                state.activeIndex = -1;
                context.rerender();
              },
              () => {
                if (sequence !== state.sequence) return;
                // A failed autocomplete closes the popup and leaves the search box working.
                state.isLoading = false;
                state.suggestions = [];
                closeUp();
                context.rerender();
              }
            );
        };

        return {
          query,
          suggestions: state.suggestions,
          isOpen: state.isOpen,
          activeIndex: state.activeIndex,
          isLoading: state.isLoading,
          seeAllUrl,

          setQuery(value) {
            state.query = value;
            cancel();
            if (inPlace) base.actions.setQuery(value).search();
            if (value.length < (params.minQueryLength ?? 1)) {
              state.suggestions = [];
              closeUp();
            } else {
              state.timer = setTimeout(() => {
                state.timer = undefined;
                fetchSuggestions(value);
              }, params.debounceMs ?? 150);
            }
            context.rerender();
          },

          move(to) {
            const count = state.suggestions.length;
            if (count === 0) return;
            state.isOpen = true;
            if (to === 'first') state.activeIndex = 0;
            else if (to === 'last') state.activeIndex = count - 1;
            else if (state.activeIndex === -1) state.activeIndex = to > 0 ? 0 : count - 1;
            else state.activeIndex = (state.activeIndex + to + count) % count;
            context.rerender();
          },

          select(index) {
            const suggestion = state.suggestions[index];
            if (!suggestion) return;
            cancel();
            state.query = suggestion.text;
            closeUp();
            // A document suggestion carries the page it stands for; a query suggestion is a
            // query, and searching for it is the whole point.
            if (suggestion.url !== undefined && suggestion.url !== '') {
              win?.location.assign(suggestion.url);
              context.rerender();
              return;
            }
            base.actions.setQuery(suggestion.text).search();
            context.rerender();
          },

          submit() {
            cancel();
            closeUp();
            if (seeAllUrl !== null) {
              win?.location.assign(seeAllUrl);
              context.rerender();
              return;
            }
            base.actions.setQuery(query).search();
            context.rerender();
          },

          close() {
            cancel();
            closeUp();
            context.rerender();
          },

          clear() {
            cancel();
            state.query = '';
            state.suggestions = [];
            closeUp();
            if (inPlace) base.actions.setQuery('').search();
            context.rerender();
          },
        };
      },
    }
  )(renderFn, unmountFn);
}
