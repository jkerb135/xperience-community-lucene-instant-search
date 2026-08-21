/**
 * `searchBox` — `withSearchBox` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/search-box.html`. A11y: spec 5.6 — `role="search"`, an associated
 * label, `aria-label` on the reset button.
 */
import { withSearchBox } from '../behaviors/searchBox';
import { html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, idBase, resolveContainer } from './dom';

export type SearchBoxWidgetParams = {
  container: string | HTMLElement;
  /** Intercepts a query before it reaches the state (spec 5.3). */
  queryHook?: (query: string, search: (value: string) => void) => void;
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
};

export function searchBox(params: SearchBoxWidgetParams): Widget {
  const container = resolveContainer(params.container, 'searchBox');
  let root: HTMLElement | undefined;
  let input: HTMLInputElement | undefined;
  let reset: HTMLElement | undefined;
  let apply: (query: string) => void = () => {};
  let clear: () => void = () => {};

  const widget = withSearchBox<SearchBoxWidgetParams>(
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
      clear = options.clear;

      if (isFirstRender) {
        const ids = idBase(container, 'search-box');
        root = createRoot(container, 'form', 'xps xps-search-box');
        root.setAttribute('role', 'search');
        root.setAttribute('novalidate', '');
        render(
          html`<label class="xps-search-box__label${showLabel ? '' : ' xps-sr-only'}" for="${ids}-input">${label}</label>
  <div class="xps-search-box__field">
    <input class="xps-search-box__input" id="${ids}-input" type="search" name="q" value="" placeholder="${placeholder}" autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false">
    <span class="xps-search-box__loading xps-skeleton" aria-hidden="true"></span>
    <button class="xps-button xps-search-box__reset" type="reset" aria-label="Clear the search query" hidden><span aria-hidden="true">&times;</span></button>
    ${showSubmit
      ? html`<button class="xps-button xps-search-box__submit" type="submit" aria-label="Submit the search query"><span aria-hidden="true">&rarr;</span></button>`
      : ''}
  </div>`,
          root
        );
        input = root.querySelector<HTMLInputElement>('.xps-search-box__input') ?? undefined;
        reset = root.querySelector<HTMLElement>('.xps-search-box__reset') ?? undefined;

        root.addEventListener('input', () => apply(input?.value ?? ''));
        root.addEventListener('submit', (event) => {
          event.preventDefault();
          apply(input?.value ?? '');
        });
        root.addEventListener('reset', (event) => {
          // The native reset would restore the *initial* value, not an empty one.
          event.preventDefault();
          if (input) input.value = '';
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

  widget.$$type = 'searchBox';
  return widget;
}
