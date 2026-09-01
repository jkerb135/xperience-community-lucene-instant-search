/**
 * `searchBox` — `withSearchBox` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/search-box.html`. A11y: spec 5.6 — `role="search"`, an associated
 * label, `aria-label` on the reset button.
 *
 * With `params.suggestions` the widget also runs `withSuggestions` over its own input, so one
 * field carries the query, the URL routing and the autocomplete popup; the panel and the
 * combobox keyboard model come from `suggestionsPanel.ts`, shared with the standalone
 * `suggestions` widget.
 */
import { withSearchBox } from '../behaviors/searchBox';
import { withSuggestions, type SuggestionsRenderState } from '../behaviors/suggestions';
import { html, render } from '../templates/html';
import type { RenderOptions, Widget } from '../types';
import { createRoot, resolveContainer, widgetId } from './dom';
import { createRecents, recentsStorage, type Recents } from './recentSearches';
import { bindCombobox, renderPanel } from './suggestionsPanel';

/**
 * The magnifier inside the field. Decoration: the label already names the input, so repeating
 * "search" here would only be announced twice.
 */
const SEARCH_ICON =
  '<svg class="xps-search-box__icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="11" cy="11" r="7"></circle><path d="m20 20-3.6-3.6"></path></svg>';

/** The request-shaping options of the integrated popup; the field itself is the search box's. */
export type SearchBoxSuggestionsParams = {
  /** Trailing debounce before `/suggest` is called. Defaults to 150 ms. */
  debounceMs?: number;
  /** Shortest query that asks for suggestions. Defaults to 1. */
  minQueryLength?: number;
  /** `SuggestRequest.limit`. Defaults to 5. */
  limit?: number;
  /** `SuggestRequest.language`. Defaults to the instance's `language`. */
  language?: string;
  /** Group headings, used whenever the panel shows more than one source. */
  groupLabels?: { suggestions?: string; documents?: string; recent?: string };
  /**
   * Defaults to `true`. Remember what this visitor searched for and offer it back as the panel's
   * first group. The list lives in their browser's `localStorage` and is never sent anywhere.
   */
  recentSearches?: boolean;
};

export type SearchBoxWidgetParams = {
  container: string | HTMLElement;
  /** Intercepts a query before it reaches the state (spec 5.3). */
  queryHook?: (query: string, search: (value: string) => void) => void;
  /**
   * Defaults to `true`. Navigate when a submitted query matched a redirect rule. Typing never
   * redirects, so a visitor can search for something that merely contains the rule's pattern.
   */
  followRedirects?: boolean;
  /** `window` by default; injectable for tests and SSR. */
  windowRef?: Window;
  placeholder?: string;
  /** Text of the always-rendered `<label>`. */
  label?: string;
  /** Show the label to sighted users. It is `xps-sr-only` by default. */
  showLabel?: boolean;
  /** Defaults to `true`. The button is always in the DOM and `hidden` while the query is empty. */
  showReset?: boolean;
  /** Defaults to `false`. When false the button is not rendered at all. */
  showSubmit?: boolean;
  autofocus?: boolean;
  /**
   * Set to turn the input into an autocomplete combobox. Picking a suggestion searches in place —
   * the search box belongs on the results page. For a header or landing-page field that navigates
   * to a results page, use the standalone `suggestions` widget instead.
   */
  suggestions?: SearchBoxSuggestionsParams;
};

export function searchBox(params: SearchBoxWidgetParams): Widget {
  const container = resolveContainer(params.container, 'searchBox');
  const id = (part: string): string => widgetId(container, 'search-box', part);
  const popup = params.suggestions;
  let root: HTMLElement | undefined;
  let input: HTMLInputElement | undefined;
  let reset: HTMLElement | undefined;
  let panel: HTMLElement | undefined;
  let apply: (query: string) => void = () => {};
  let submit: (query: string) => void = () => {};
  let clear: () => void = () => {};
  /** The current suggestions render state, recents composed in. Listeners are bound once; they must not see an older one. */
  let suggest: SuggestionsRenderState | undefined;
  let recents: Recents | undefined;
  /** Re-runs the last popup render, for a change only the recents know about. */
  let repaint: () => void = () => {};

  const box = withSearchBox<SearchBoxWidgetParams>(
    (options, isFirstRender) => {
      const {
        placeholder = 'Search…',
        label = 'Search this site',
        showLabel = false,
        showReset = true,
        showSubmit = false,
        autofocus = false,
      } = options.params;
      apply = options.apply;
      submit = options.submit;
      clear = options.clear;

      if (isFirstRender) {
        root = createRoot(container, 'form', 'xps xps-search-box');
        root.setAttribute('role', 'search');
        root.setAttribute('novalidate', '');
        render(
          html`<label class="xps-search-box__label${showLabel ? '' : ' xps-sr-only'}" for="${id('input')}">${label}</label>
  <div class="xps-search-box__field">
    ${html.raw(SEARCH_ICON)}
    <input class="xps-search-box__input" id="${id('input')}" type="search" name="q" value="" placeholder="${placeholder}" autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false"${
      popup
        ? html` role="combobox" aria-expanded="false" aria-controls="${id('listbox')}" aria-autocomplete="list"`
        : ''
    }>
    <span class="xps-search-box__loading xps-skeleton" aria-hidden="true"></span>
    <button class="xps-button xps-search-box__reset" type="reset" aria-label="Clear the search query" hidden><span aria-hidden="true">&times;</span></button>
    ${showSubmit
      ? html`<button class="xps-button xps-search-box__submit" type="submit" aria-label="Submit the search query"><span aria-hidden="true">&rarr;</span></button>`
      : ''}
  </div>${popup ? html`<div class="xps-suggestions__panel" hidden></div>` : ''}`,
          root
        );
        input = root.querySelector<HTMLInputElement>('.xps-search-box__input') ?? undefined;
        reset = root.querySelector<HTMLElement>('.xps-search-box__reset') ?? undefined;
        panel = root.querySelector<HTMLElement>('.xps-suggestions__panel') ?? undefined;

        root.addEventListener('input', () => {
          const value = input?.value ?? '';
          // The popup asks `/suggest` for what was typed; `apply` is still what searches, so
          // `queryHook` keeps the last word on the query. Both searches the two start collapse
          // into one request inside the client's debounce.
          suggest?.setQuery(value);
          apply(value);
        });
        root.addEventListener('submit', (event) => {
          event.preventDefault();
          // The search box's own submit bypasses the popup's, so this is where a submitted query
          // is remembered. `close()` resets the popup, so record before it.
          recents?.record(input?.value ?? '');
          suggest?.close();
          submit(input?.value ?? '');
        });
        root.addEventListener('reset', (event) => {
          // The native reset would restore the *initial* value, not an empty one.
          event.preventDefault();
          if (input) input.value = '';
          suggest?.clear();
          clear();
          input?.focus();
        });
        if (autofocus) input?.focus();
      }

      if (!root || !input || !reset) return;
      root.classList.toggle('xps-search-box--stalled', options.isStalled);
      // Only assign when it actually differs: assigning moves the caret to the end.
      if (input.value !== options.query) input.value = options.query;
      reset.hidden = !showReset || options.query === '';
    },
    () => {
      container.textContent = '';
    }
  )(params);

  box.$$type = 'searchBox';
  if (!popup) return box;

  const drawPopup = (
    options: SuggestionsRenderState & RenderOptions<Record<string, unknown>>,
    isFirstRender: boolean
  ): void => {
    if (!root || !input || !panel) {
      suggest = options;
      return;
    }
    repaint = () => drawPopup(options, false);
    if (isFirstRender) {
      if (popup.recentSearches !== false) {
        recents = createRecents({
          index: options.search.index,
          storage: recentsStorage(params.windowRef),
          repaint: () => repaint(),
        });
        recents.bind(input, panel);
      }
      // Enter with no active option goes through the search box's own submit, which is the
      // only path that follows a redirect rule.
      bindCombobox({ input, panel, id }, () => suggest, () => submit(input?.value ?? ''));
    }
    // Picking a recent runs it the way picking a query suggestion does: the field takes the text,
    // the pending `/suggest` call is dropped, and the box searches for it (never a redirect —
    // only a query the visitor submitted follows one).
    const view = recents
      ? recents.wrap(options, (text) => {
          suggest?.setQuery(text);
          suggest?.close();
          apply(text);
        })
      : options;
    suggest = view;

    root.classList.toggle('xps-suggestions--open', view.isOpen);
    // Searching in place means no "see all" link, but the keyboard hints are still worth showing.
    renderPanel({ input, panel, id }, view, {
      ...(popup.groupLabels === undefined ? {} : { groupLabels: popup.groupLabels }),
      hints: true,
    });
  };

  const combobox = withSuggestions<Record<string, unknown>>(
    (options, isFirstRender) => drawPopup(options, isFirstRender),
    () => {
      // Drops a debounced call that has not fired yet and makes an in-flight answer stale.
      suggest?.close();
    }
  )({ ...popup, ...(params.windowRef === undefined ? {} : { windowRef: params.windowRef }) });

  // One widget to the instance, two behaviours behind it: the input is rendered by the search box
  // and driven as a combobox by `withSuggestions`. The search box goes first, because the popup
  // renders into the field it has just built.
  return {
    $$type: 'searchBox',
    init(options) {
      box.init?.(options);
      combobox.init?.(options);
    },
    render(options) {
      box.render?.(options);
      combobox.render?.(options);
    },
    dispose() {
      // Before the search box empties the container: closing re-renders the panel.
      combobox.dispose?.();
      box.dispose?.();
    },
  };
}
