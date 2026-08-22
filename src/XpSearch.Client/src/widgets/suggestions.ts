/**
 * `suggestions` — `withSuggestions` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/suggestions.html`. A11y (spec 5.6): the WAI-ARIA APG
 * combobox-with-listbox pattern — DOM focus never leaves the input, the active option is named
 * by `aria-activedescendant`, and the listbox element always exists so `aria-controls` cannot
 * dangle. Keyboard: Down/Up move, Home/End jump to the ends, Enter activates, Escape closes and
 * then clears, Tab closes and moves on.
 */
import { withSuggestions, type SuggestionsRenderState } from '../behaviors/suggestions';
import { escapeHtml, html, render, toHtml, type Renderable } from '../templates/html';
import type { Suggestion, Widget } from '../types';
import { createRoot, resolveContainer, setAttr, widgetId } from './dom';
import { markMatch } from './facetList';

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
  mode?: 'documents' | 'querySuggestions';
  placeholder?: string;
  /** Text of the always-rendered `<label>`. */
  label?: string;
  /** Show the label to sighted users. It is `xps-sr-only` by default. */
  showLabel?: boolean;
  /** Group headings, used only when a response mixes query suggestions with documents. */
  groupLabels?: { suggestions?: string; documents?: string };
};

/** One suggestion plus the index the behaviour knows it by, which grouping reorders away from. */
interface Option {
  suggestion: Suggestion;
  at: number;
}

export function suggestions(params: SuggestionsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'suggestions');
  const id = (part: string): string => widgetId(container, 'suggestions', part);
  let root: HTMLElement | undefined;
  let input: HTMLInputElement | undefined;
  let panel: HTMLElement | undefined;
  let reset: HTMLElement | undefined;
  /** The current render state. Listeners are bound once and must never see an older one. */
  let api: SuggestionsRenderState | undefined;

  const widget = withSuggestions<SuggestionsWidgetParams>(
    (options, isFirstRender) => {
      const {
        placeholder = 'Search…',
        label = 'Search this site',
        showLabel = false,
        resultsUrl,
        groupLabels,
      } = options.params;
      api = options;

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

        input?.addEventListener('input', () => api?.setQuery(input?.value ?? ''));
        input?.addEventListener('keydown', (event) => {
          if (!api) return;
          switch (event.key) {
            case 'ArrowDown':
            case 'ArrowUp':
              event.preventDefault();
              api.move(event.key === 'ArrowDown' ? 1 : -1);
              break;
            case 'Home':
            case 'End':
              // Only while the popup is open: otherwise Home/End belong to the caret.
              if (!api.isOpen) break;
              event.preventDefault();
              api.move(event.key === 'Home' ? 'first' : 'last');
              break;
            case 'Enter':
              // Implicit form submission is not enough: the active option has to win over it,
              // and a form with a single field submits on Enter in some browsers only.
              event.preventDefault();
              if (api.activeIndex >= 0) api.select(api.activeIndex);
              else api.submit();
              break;
            case 'Escape':
              // First press closes the popup, a second one clears the input (APG).
              if (api.isOpen) api.close();
              else if ((input?.value ?? '') !== '') {
                if (input) input.value = '';
                api.clear();
              }
              break;
            case 'Tab':
              api.close();
              break;
            default:
          }
        });
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
        // Picking an option with the mouse must not blur the input first: the blur would close
        // the popup out from under the click.
        panel?.addEventListener('mousedown', (event) => event.preventDefault());
        panel?.addEventListener('click', (event) => {
          const target = event.target;
          if (!(target instanceof Element)) return;
          const at = target.closest<HTMLElement>('[data-xps-suggestion]')?.dataset['xpsSuggestion'];
          if (at !== undefined) api?.select(Number(at));
        });
        input?.addEventListener('blur', () => api?.close());
      }
      if (!root || !input || !panel || !reset) return;

      const { query, activeIndex, isOpen } = options;
      root.classList.toggle('xps-suggestions--open', isOpen);
      // Only assign when it differs: assigning moves the caret to the end.
      if (input.value !== query) input.value = query;
      input.setAttribute('aria-expanded', String(isOpen));
      reset.hidden = query === '';
      panel.hidden = !isOpen;

      // Grouped only when a response actually mixes the two sources: with one source the wrapper
      // would be a group of one, which is noise to a screen reader (MARKUP.md, "suggestions").
      const all: Option[] = options.suggestions.map((suggestion, at) => ({ suggestion, at }));
      const queries = all.filter((option) => option.suggestion.result === undefined);
      const documents = all.filter((option) => option.suggestion.result !== undefined);
      const grouped = queries.length > 0 && documents.length > 0;
      const ordered = grouped ? [...queries, ...documents] : all;

      /** Ids are the *visual* position; `data-xps-suggestion` is the behaviour's own index. */
      const optionHtml = (option: Option, visual: number, tag: 'li' | 'div'): Renderable => {
        const active = option.at === activeIndex;
        const meta = option.suggestion.result?.attributes['contentType'];
        const inner = html`<span class="xps-suggestions__option-title">${markMatch(option.suggestion.text, query)}</span>${
          typeof meta === 'string' && meta !== ''
            ? html`<span class="xps-suggestions__option-meta">${meta}</span>`
            : ''
        }`;
        return html.raw(
          `<${tag} class="xps-suggestions__option${active ? ' xps-suggestions__option--active' : ''}"` +
            ` role="option" id="${escapeHtml(id(`option-${visual}`))}" aria-selected="${active}"` +
            ` data-xps-suggestion="${option.at}">${toHtml(inner)}</${tag}>`
        );
      };

      let visual = 0;
      const group = (key: string, label: string, options_: Option[]): Renderable =>
        html`<li class="xps-suggestions__group" role="group" aria-labelledby="${id(`group-${key}`)}">
      <div class="xps-suggestions__group-title" id="${id(`group-${key}`)}">${label}</div>
      ${options_.map((option) => optionHtml(option, visual++, 'div'))}
    </li>`;

      const body = grouped
        ? [
            group('suggestions', groupLabels?.suggestions ?? 'Suggestions', queries),
            group('pages', groupLabels?.documents ?? 'Pages', documents),
          ]
        : ordered.map((option) => optionHtml(option, visual++, 'li'));

      render(
        html`<ul class="xps-suggestions__list" id="${id('listbox')}" role="listbox" aria-label="Search suggestions">${body}</ul>
    ${isOpen && ordered.length === 0
      ? html`<p class="xps-suggestions__empty" role="status">No suggestions for &ldquo;${query}&rdquo;.</p>`
      : ''}
    ${options.seeAllUrl !== null && ordered.length > 0
      ? html`<div class="xps-suggestions__footer"><a class="xps-suggestions__see-all" href="${options.seeAllUrl}">See all results for &ldquo;${query}&rdquo;</a></div>`
      : ''}`,
        panel
      );

      const active = ordered.findIndex((option) => option.at === activeIndex);
      setAttr(input, 'aria-activedescendant', active >= 0, active >= 0 ? id(`option-${active}`) : '');
    },
    () => {
      // Drops a debounced call that has not fired yet and makes an in-flight answer stale.
      api?.close();
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'suggestions';
  return widget;
}
