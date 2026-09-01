/**
 * `suggestions` — `withSuggestions` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/suggestions.html`. The panel and the combobox keyboard model live in
 * `suggestionsPanel.ts`, shared with `searchBox`'s integrated suggestions; this file renders the
 * widget's own field around them.
 */
import { withSuggestions, type SuggestionsRenderState } from '../behaviors/suggestions';
import { html, render } from '../templates/html';
import type { RenderOptions, Widget } from '../types';
import { createRoot, resolveContainer, widgetId } from './dom';
import { createRecents, recentsStorage, type Recents } from './recentSearches';
import { bindCombobox, renderPanel } from './suggestionsPanel';

export type SuggestionsWidgetParams = {
  container: string | HTMLElement;
  /** Trailing debounce before `/suggest` is called. Defaults to 150 ms. */
  debounceMs?: number;
  /** Shortest query that asks for suggestions. Defaults to 1. */
  minQueryLength?: number;
  /** `SuggestRequest.limit`. Defaults to 5. */
  limit?: number;
  /** `SuggestRequest.language`. Defaults to the instance's `language`. */
  language?: string;
  /**
   * The full results page. When set, the form posts there and submitting navigates instead of
   * searching in place; when unset the widget searches its own instance as the visitor types.
   */
  resultsUrl?: string;
  /** `window` by default; injectable for tests and SSR. */
  windowRef?: Window;
  /**
   * Accepted so the Page Builder mount can pass it through, and otherwise unused: which of the
   * two an index answers with is server-side configuration, not a request field (contract §4.4).
   */
  mode?: 'documents' | 'querySuggestions' | 'mixed';
  placeholder?: string;
  /** Text of the always-rendered `<label>`. */
  label?: string;
  /** Show the label to sighted users. It is `xps-sr-only` by default. */
  showLabel?: boolean;
  /** Group headings, used whenever the panel shows more than one source. */
  groupLabels?: { suggestions?: string; documents?: string; recent?: string };
  /**
   * Defaults to `true`. Remember what this visitor searched for and offer it back as the panel's
   * first group. The list lives in their browser's `localStorage` and is never sent anywhere.
   */
  recentSearches?: boolean;
};

/** What `withSuggestions` hands the renderer: the behaviour's state plus the base render options. */
type SuggestionsRenderOptions = SuggestionsRenderState & RenderOptions<SuggestionsWidgetParams>;

export function suggestions(params: SuggestionsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'suggestions');
  const id = (part: string): string => widgetId(container, 'suggestions', part);
  let root: HTMLElement | undefined;
  let input: HTMLInputElement | undefined;
  let panel: HTMLElement | undefined;
  let reset: HTMLElement | undefined;
  /** The current render state, recents composed in. Listeners are bound once and must never see an older one. */
  let api: SuggestionsRenderState | undefined;
  let recents: Recents | undefined;
  /** Re-runs the last render, for a change only the recents know about (focus, Clear, arrow keys). */
  let repaint: () => void = () => {};

  const draw = (options: SuggestionsRenderOptions, isFirstRender: boolean): void => {
    const {
      placeholder = 'Search…',
      label = 'Search this site',
      showLabel = false,
      resultsUrl,
      groupLabels,
      recentSearches,
    } = options.params;
    repaint = () => draw(options, false);

    if (isFirstRender) {
      root = createRoot(container, 'div', 'xps xps-suggestions');
      render(
        html`<form class="xps-suggestions__form" role="search"${
          resultsUrl === undefined ? '' : html` action="${resultsUrl}" method="get"`
        } novalidate>
    <label class="xps-suggestions__label${showLabel ? '' : ' xps-sr-only'}" for="${id('input')}">${label}</label>
    <div class="xps-suggestions__field">
      <input class="xps-suggestions__input" id="${id('input')}" type="text" name="q" value="" role="combobox" aria-expanded="false" aria-controls="${id('listbox')}" aria-autocomplete="list" autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false" placeholder="${placeholder}">
      <button class="xps-button xps-suggestions__reset" type="reset" aria-label="Clear the search query" hidden><span aria-hidden="true">&times;</span></button>
    </div>
  </form>
  <div class="xps-suggestions__panel" hidden></div>`,
        root
      );
      input = root.querySelector<HTMLInputElement>('.xps-suggestions__input') ?? undefined;
      panel = root.querySelector<HTMLElement>('.xps-suggestions__panel') ?? undefined;
      reset = root.querySelector<HTMLElement>('.xps-suggestions__reset') ?? undefined;

      if (recentSearches !== false && input && panel) {
        recents = createRecents({
          index: options.search.index,
          storage: recentsStorage(options.params.windowRef),
          repaint: () => repaint(),
        });
        recents.bind(input, panel);
      }
      if (input && panel) bindCombobox({ input, panel, id }, () => api);
      input?.addEventListener('input', () => api?.setQuery(input?.value ?? ''));
      root.addEventListener('submit', (event) => {
        event.preventDefault();
        if (!api) return;
        if (api.activeIndex >= 0) api.select(api.activeIndex);
        else api.submit();
      });
      root.addEventListener('reset', (event) => {
        // The native reset restores the *initial* value, not an empty one.
        event.preventDefault();
        if (input) input.value = '';
        api?.clear();
        input?.focus();
      });
    }
    // Picking a recent runs it exactly the way picking a query suggestion does: the field takes
    // the text, the pending `/suggest` call is dropped, and the instance searches for it.
    const view = recents
      ? recents.wrap(options, (text) => {
          api?.setQuery(text);
          api?.close();
          options.actions.setQuery(text).search();
        })
      : options;
    api = view;

    if (!root || !input || !panel || !reset) return;

    const { query } = options;
    root.classList.toggle('xps-suggestions--open', view.isOpen);
    // Only assign when it differs: assigning moves the caret to the end.
    if (input.value !== query) input.value = query;
    reset.hidden = query === '';

    // No `hints`: this widget shows the footer only when it has a "see all" link to put in it.
    renderPanel({ input, panel, id }, view, { ...(groupLabels === undefined ? {} : { groupLabels }) });
  };

  const widget = withSuggestions<SuggestionsWidgetParams>(
    (options, isFirstRender) => draw(options, isFirstRender),
    () => {
      // Drops a debounced call that has not fired yet and makes an in-flight answer stale.
      api?.close();
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'suggestions';
  return widget;
}
