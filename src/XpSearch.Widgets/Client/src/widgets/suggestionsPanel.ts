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

/** The empty state's footer: only the two actions that still apply with nothing to navigate. */
const EMPTY_HINTS = html`<span class="xps-suggestions__hints" aria-hidden="true"><kbd class="xps-suggestions__key">&crarr;</kbd> search <kbd class="xps-suggestions__key">esc</kbd> close</span>`;

/**
 * The leading glyph of a row, on the 24px grid, in `currentColor` so a re-skin needs no asset:
 * a clock for a recent search, a magnifier for a query suggestion. A document row has none — its
 * accent title and its meta line are what set it apart (design board `Autocomplete.dc.html`).
 */
const ICON_OPEN =
  '<svg class="xps-suggestions__option-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">';
const ICONS = {
  recent: `${ICON_OPEN}<circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>`,
  query: `${ICON_OPEN}<circle cx="11" cy="11" r="7"></circle><path d="M20 20l-4.2-4.2"></path></svg>`,
  document: '',
} as const;

/** The X of a recent row's remove control. */
const REMOVE_ICON = `${ICON_OPEN}<path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>`;

/** The empty state's glyph: the magnifier with a minus in it (design board `Autocomplete.dc.html`). */
const EMPTY_ICON =
  '<svg class="xps-suggestions__empty-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="11" cy="11" r="7"></circle><path d="M20 20l-4.2-4.2"></path><path d="M8 11h6"></path></svg>';

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
    // The recents' own controls are handled by `recentSearches.ts`, whatever order the two
    // listeners were bound in: removing an entry must not also pick it.
    if (target.closest('[data-xps-recent-remove]') !== null) return;
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
   * The index's suggestion mode. In `mixed` the panel can answer from either source, so every
   * non-empty group is labelled — a lone "Pages" group still says Pages, per the design board. The
   * single-source modes have nothing to distinguish, so their list stays header-less (TH-11).
   */
  mode?: 'documents' | 'querySuggestions' | 'mixed';
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
  { groupLabels, mode, hints = false }: PanelOptions = {}
): void {
  const { input, panel, id } = parts;
  const { query, activeIndex, isOpen } = api;

  input.setAttribute('aria-expanded', String(isOpen));
  panel.hidden = !isOpen;

  // Grouped only when the panel actually shows more than one source: with one source the wrapper
  // would be a group of one, which is noise to a screen reader (MARKUP.md, "suggestions"). Two
  // exceptions: the recents, whose group carries the Clear control, and `mode: 'mixed'`, where the
  // panel could have answered from either source — there a lone group still says which one it is
  // (design board `Autocomplete.dc.html`, TH-11).
  const all: Option[] = api.suggestions.map((suggestion, at) => ({ suggestion, at }));
  const present = GROUPS.map((group) => ({
    ...group,
    options: all.filter((option) => groupOf(option.suggestion) === group.key),
  })).filter((group) => group.options.length > 0);
  const grouped = present.length > 1 || present[0]?.key === 'recent' || (mode === 'mixed' && present.length > 0);
  const ordered = grouped ? present.flatMap((group) => group.options) : all;

  /** Ids are the *visual* position; `data-xps-suggestion` is the behaviour's own index. */
  const optionHtml = (option: Option, visual: number, tag: 'li' | 'div'): Renderable => {
    const active = option.at === activeIndex;
    const group = groupOf(option.suggestion);
    const meta = option.suggestion.result?.attributes['contentType'];
    const inner = html`${html.raw(ICONS[group])}<span class="xps-suggestions__option-title">${markMatch(option.suggestion.text, query)}</span>${
      typeof meta === 'string' && meta !== ''
        ? html`<span class="xps-suggestions__option-meta">${meta}</span>`
        : ''
    }`;
    const row = html.raw(
      `<${tag} class="xps-suggestions__option xps-suggestions__option--${group}${active ? ' xps-suggestions__option--active' : ''}"` +
        ` role="option" id="${escapeHtml(id(`option-${visual}`))}" aria-selected="${active}"` +
        ` data-xps-suggestion="${option.at}">${toHtml(inner)}</${tag}>`
    );
    // The remove control sits beside the option, and is deliberately NOT a focusable control: a
    // listbox owns nothing but options and groups, and a focusable element anywhere in that subtree
    // is either swallowed by an option's accessible name or an unallowed child of the listbox
    // (axe `nested-interactive` / `aria-required-children`, both measured). It is a pointer
    // affordance; the keyboard and assistive-tech path is Delete on the active row, plus the
    // group's Clear. See docs/internal/KNOWN-LIMITATIONS.md.
    return group === 'recent'
      ? html`<div class="xps-suggestions__row">${row}<span class="xps-suggestions__option-remove" data-xps-recent-remove="${option.suggestion.text}" title="Remove from recent searches" aria-hidden="true">${html.raw(REMOVE_ICON)}</span></div>`
      : row;
  };

  let visual = 0;
  const label = (key: (typeof GROUPS)[number]['key'], fallback: string): string =>
    (key === 'recent' ? groupLabels?.recent : key === 'query' ? groupLabels?.suggestions : groupLabels?.documents) ??
    fallback;

  const body = grouped
    ? present.map(
        (group) => html`<li class="xps-suggestions__group" role="group" aria-labelledby="${id(`group-${group.id}`)}">
      ${group.key === 'recent'
        ? ''
        : html`<div class="xps-suggestions__group-title" id="${id(`group-${group.id}`)}">${label(group.key, group.label)}</div>`}
      ${group.options.map((option) => optionHtml(option, visual++, 'div'))}
    </li>`
      )
    : ordered.map((option) => optionHtml(option, visual++, 'li'));

  /**
   * The recents' heading row and its Clear control render ABOVE the listbox, not inside it: a
   * listbox owns options and groups and nothing else, and a button in there is an unallowed child
   * (axe `aria-required-children`). The recents are always the first group, so the row still reads
   * as their heading, and `aria-labelledby` names the group across the boundary.
   */
  const recentGroup = present.find((group) => group.key === 'recent');
  const header =
    grouped && recentGroup !== undefined
      ? html`<div class="xps-suggestions__group-header"><div class="xps-suggestions__group-title" id="${id('group-recent')}">${label('recent', recentGroup.label)}</div><button class="xps-button xps-button--link xps-suggestions__group-clear" type="button" data-xps-recent-clear aria-label="Clear recent searches">Clear</button></div>`
      : '';

  render(
    html`${header}<ul class="xps-suggestions__list" id="${id('listbox')}" role="listbox" aria-label="Search suggestions">${body}</ul>
    ${isOpen && ordered.length === 0
      ? html`<div class="xps-suggestions__empty" role="status">${html.raw(EMPTY_ICON)}<div class="xps-suggestions__empty-title">No suggestions for &ldquo;${query}&rdquo;</div><div class="xps-suggestions__empty-hint">Press Enter to search anyway, or try a different spelling.</div></div><div class="xps-suggestions__footer">${EMPTY_HINTS}</div>`
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
