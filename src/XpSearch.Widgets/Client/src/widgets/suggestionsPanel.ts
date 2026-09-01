/**
 * The autocomplete popup, shared by the standalone `suggestions` widget and by `searchBox` with
 * its `suggestions` param group. Internal: not exported from the package entry points.
 *
 * It owns the WAI-ARIA APG combobox-with-listbox pattern
 * (https://www.w3.org/WAI/ARIA/apg/patterns/combobox/) over an input the *caller* renders — DOM
 * focus never leaves that input, the active option is named by `aria-activedescendant`, and the
 * listbox element always exists so `aria-controls` cannot dangle. Both widgets therefore emit the
 * same `.xps-suggestions__*` panel; only the field around it differs.
 */
import type { SuggestionsRenderState } from '../behaviors/suggestions';
import { escapeHtml, html, render, toHtml, type Renderable } from '../templates/html';
import type { Suggestion } from '../types';
import { setAttr } from './dom';
import { markMatch } from './facetList';
import { groupOf } from './recentSearches';

/**
 * Decoration only, and hidden from assistive tech: the combobox pattern already conveys the
 * keyboard model through the roles and `aria-activedescendant`, so repeating it here would be
 * announced twice. Hidden on a touch keyboard by `shell.css`.
 */
const KEYBOARD_HINTS = html`<span class="xps-suggestions__hints" aria-hidden="true"><kbd class="xps-suggestions__key">&uarr;</kbd><kbd class="xps-suggestions__key">&darr;</kbd> navigate <kbd class="xps-suggestions__key">&crarr;</kbd> select <kbd class="xps-suggestions__key">esc</kbd> close</span>`;

/** The two elements the pattern spans, and the widget's own id scheme (MARKUP.md rule 4). */
export interface ComboboxParts {
  input: HTMLInputElement;
  panel: HTMLElement;
  id(part: string): string;
}

/** One suggestion plus the index the behaviour knows it by, which grouping reorders away from. */
interface Option {
  suggestion: Suggestion;
  at: number;
}

/**
 * Binds the keyboard and pointer half of the pattern. Call once, on the first render: `state()`
 * is read on every event so a listener never sees a stale render state.
 *
 * `onSubmit` replaces Enter-with-no-active-option, for a caller whose own submit does more than
 * the behaviour's (the search box follows redirect rules).
 */
export function bindCombobox(
  parts: ComboboxParts,
  state: () => SuggestionsRenderState | undefined,
  onSubmit?: () => void
): void {
  const { input, panel } = parts;

  input.addEventListener('keydown', (event) => {
    const api = state();
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
        else if (onSubmit) onSubmit();
        else api.submit();
        break;
      case 'Escape':
        // First press closes the popup, a second one clears the input (APG).
        if (api.isOpen) api.close();
        else if (input.value !== '') {
          input.value = '';
          api.clear();
        }
        break;
      case 'Tab':
        api.close();
        break;
      default:
    }
  });
  // Picking an option with the mouse must not blur the input first: the blur would close
  // the popup out from under the click.
  panel.addEventListener('mousedown', (event) => event.preventDefault());
  panel.addEventListener('click', (event) => {
    const target = event.target;
    if (!(target instanceof Element)) return;
    const at = target.closest<HTMLElement>('[data-xps-suggestion]')?.dataset['xpsSuggestion'];
    if (at !== undefined) state()?.select(Number(at));
  });
  input.addEventListener('blur', () => state()?.close());
}

/** The three sources the panel can show, in the order the artboard stacks them. */
const GROUPS = [
  { key: 'recent', id: 'recent', label: 'Recent searches' },
  { key: 'query', id: 'suggestions', label: 'Suggestions' },
  { key: 'document', id: 'pages', label: 'Pages' },
] as const;

export interface PanelOptions {
  /** Group headings, used whenever the panel shows more than one source (or any recent search). */
  groupLabels?: { suggestions?: string; documents?: string; recent?: string };
  /**
   * Render the footer even without a "see all" link, for its keyboard hints. A consumer that
   * searches in place — the search box — has no results page to link to but still shows them.
   */
  hints?: boolean;
}

/**
 * Renders the popup and the input's live combobox attributes. Everything else about the field —
 * its value, its reset button, the root modifier — belongs to the widget that renders it.
 */
export function renderPanel(
  parts: ComboboxParts,
  api: SuggestionsRenderState,
  { groupLabels, hints = false }: PanelOptions = {}
): void {
  const { input, panel, id } = parts;
  const { query, activeIndex, isOpen } = api;

  input.setAttribute('aria-expanded', String(isOpen));
  panel.hidden = !isOpen;

  // Grouped only when the panel actually shows more than one source: with one source the wrapper
  // would be a group of one, which is noise to a screen reader (MARKUP.md, "suggestions"). The
  // recents are the exception — their group carries the Clear control, so it is always labelled.
  const all: Option[] = api.suggestions.map((suggestion, at) => ({ suggestion, at }));
  const present = GROUPS.map((group) => ({
    ...group,
    options: all.filter((option) => groupOf(option.suggestion) === group.key),
  })).filter((group) => group.options.length > 0);
  const grouped = present.length > 1 || present[0]?.key === 'recent';
  const ordered = grouped ? present.flatMap((group) => group.options) : all;

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
  const label = (key: (typeof GROUPS)[number]['key'], fallback: string): string =>
    (key === 'recent' ? groupLabels?.recent : key === 'query' ? groupLabels?.suggestions : groupLabels?.documents) ??
    fallback;

  const body = grouped
    ? present.map(
        (group) => html`<li class="xps-suggestions__group" role="group" aria-labelledby="${id(`group-${group.id}`)}">
      ${group.key === 'recent'
        ? html`<div class="xps-suggestions__group-header"><div class="xps-suggestions__group-title" id="${id(`group-${group.id}`)}">${label(group.key, group.label)}</div><button class="xps-button xps-button--link xps-suggestions__group-clear" type="button" data-xps-recent-clear aria-label="Clear recent searches">Clear</button></div>`
        : html`<div class="xps-suggestions__group-title" id="${id(`group-${group.id}`)}">${label(group.key, group.label)}</div>`}
      ${group.options.map((option) => optionHtml(option, visual++, 'div'))}
    </li>`
      )
    : ordered.map((option) => optionHtml(option, visual++, 'li'));

  render(
    html`<ul class="xps-suggestions__list" id="${id('listbox')}" role="listbox" aria-label="Search suggestions">${body}</ul>
    ${isOpen && ordered.length === 0
      ? html`<p class="xps-suggestions__empty" role="status">No suggestions for &ldquo;${query}&rdquo;.</p>`
      : ''}
    ${(api.seeAllUrl !== null || hints) && ordered.length > 0
      ? html`<div class="xps-suggestions__footer">${KEYBOARD_HINTS}${
          api.seeAllUrl === null
            ? ''
            : html`<a class="xps-suggestions__see-all" href="${api.seeAllUrl}">See all results for &ldquo;${query}&rdquo;</a>`
        }</div>`
      : ''}`,
    panel
  );

  const active = ordered.findIndex((option) => option.at === activeIndex);
  setAttr(input, 'aria-activedescendant', active >= 0, active >= 0 ? id(`option-${active}`) : '');
}
