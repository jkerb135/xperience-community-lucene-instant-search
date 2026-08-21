/**
 * `refinementList` — `connectRefinementList` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/refinement-list.html`. A11y: real checkboxes with labels (spec 5.6).
 *
 * The root is built once and patched afterwards, so typing in the facet search box or pressing
 * "show more" never destroys the control that has focus.
 */
import {
  connectRefinementList,
  type RefinementListItem,
  type RefinementListSortBy,
} from '../connectors/refinementList';
import { escapeHtml, html, render, type Renderable } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, idBase, resolveContainer } from './dom';

export type RefinementListWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** `'or'` (the default) ORs the selected values; `'and'` ANDs them. */
  operator?: 'and' | 'or';
  /** Values shown before "show more". Defaults to 10. */
  limit?: number;
  showMore?: boolean;
  /** Values shown after "show more". Defaults to 20. */
  showMoreLimit?: number;
  sortBy?: RefinementListSortBy[];
  transformItems?: (items: RefinementListItem[]) => RefinementListItem[];
  /** Heading text. Defaults to `attribute`. */
  label?: string;
  /** Adds an input that filters the rendered values client-side. */
  searchable?: boolean;
  searchablePlaceholder?: string;
  showMoreLabels?: { more?: string; less?: string };
};

/** `espresso` + `es` → `<mark class="xps-highlight">es</mark>presso`, escaped either side. */
function markMatch(label: string, needle: string): Renderable {
  const at = needle === '' ? -1 : label.toLowerCase().indexOf(needle.toLowerCase());
  if (at === -1) return label;
  return html.raw(
    escapeHtml(label.slice(0, at)) +
      `<mark class="xps-highlight">${escapeHtml(label.slice(at, at + needle.length))}</mark>` +
      escapeHtml(label.slice(at + needle.length))
  );
}

const itemHtml = (item: RefinementListItem, attribute: string, needle: string): Renderable => {
  // A value nobody can reach any more (count 0, not selected) is a disabled row, not a missing
  // one — the list keeps its shape between searches.
  const disabled = item.count === 0 && !item.isRefined;
  const modifiers = `${item.isRefined ? ' xps-refinement-list__item--selected' : ''}${
    disabled ? ' xps-refinement-list__item--disabled' : ''
  }`;
  return html`<li class="xps-refinement-list__item${modifiers}">
    <label class="xps-refinement-list__label">
      <input class="xps-refinement-list__checkbox" type="checkbox" name="${attribute}" value="${item.value}"${
        item.isRefined ? html.raw(' checked') : ''
      }${disabled ? html.raw(' disabled') : ''}>
      <span class="xps-refinement-list__value">${markMatch(item.label, needle)}</span>
      <span class="xps-refinement-list__count">${item.count}</span>
    </label>
  </li>`;
};

export function refinementList(params: RefinementListWidgetParams): Widget {
  const container = resolveContainer(params.container, 'refinementList');
  let root: HTMLElement | undefined;
  let listEl: HTMLElement | undefined;
  let noResults: HTMLElement | undefined;
  let showMore: HTMLButtonElement | undefined;
  let needle = '';
  let refine: (value: string) => void = () => {};
  let toggleShowMore: () => void = () => {};
  // Reassigned on every render: a listener registered on the first render must not paint from
  // the first render's items.
  let repaint: () => void = () => {};

  const widget = connectRefinementList<RefinementListWidgetParams>(
    (options, isFirstRender) => {
      const {
        attribute,
        label = attribute,
        searchable = false,
        searchablePlaceholder,
        showMore: withShowMore = false,
        showMoreLabels,
      } = options.widgetParams;
      refine = options.refine;
      toggleShowMore = options.toggleShowMore;

      if (isFirstRender) {
        const ids = idBase(container, `${attribute}`);
        root = createRoot(
          container,
          'div',
          `xps xps-refinement-list${searchable ? ' xps-refinement-list--searchable' : ''}`
        );
        render(
          html`<h3 class="xps-refinement-list__title" id="${ids}-title">${label}</h3>
  ${searchable
    ? html`<div class="xps-refinement-list__search">
      <label class="xps-sr-only" for="${ids}-search">Search in ${label}</label>
      <input class="xps-refinement-list__search-input" id="${ids}-search" type="search" value="" placeholder="${searchablePlaceholder ?? `Search in ${label}`}" autocomplete="off">
    </div>`
    : ''}
  <ul class="xps-refinement-list__list" aria-labelledby="${ids}-title"></ul>
  <p class="xps-refinement-list__no-results" role="status" hidden>No matching filters.</p>
  ${withShowMore
    ? html`<button class="xps-button xps-refinement-list__show-more" type="button" aria-expanded="false">${showMoreLabels?.more ?? 'Show more'}</button>`
    : ''}`,
          root
        );
        listEl = root.querySelector<HTMLElement>('.xps-refinement-list__list') ?? undefined;
        noResults = root.querySelector<HTMLElement>('.xps-refinement-list__no-results') ?? undefined;
        showMore = root.querySelector<HTMLButtonElement>('.xps-refinement-list__show-more') ?? undefined;

        root.addEventListener('change', (event) => {
          const target = event.target;
          if (target instanceof HTMLInputElement && target.type === 'checkbox') refine(target.value);
        });
        root.addEventListener('input', (event) => {
          const target = event.target;
          if (!(target instanceof HTMLInputElement) || target.type !== 'search') return;
          needle = target.value;
          repaint();
        });
        showMore?.addEventListener('click', () => toggleShowMore());
      }

      const paint = (): void => {
        if (!listEl || !noResults) return;
        const matching = needle
          ? options.items.filter((item) =>
              item.label.toLowerCase().includes(needle.toLowerCase())
            )
          : options.items;
        render(matching.map((item) => itemHtml(item, attribute, needle)), listEl);
        // MARKUP.md rule 3: optional parts toggle with `hidden`, they are not removed.
        listEl.hidden = matching.length === 0;
        noResults.hidden = matching.length > 0;
        if (showMore) {
          showMore.textContent = options.isShowingMore
            ? (showMoreLabels?.less ?? 'Show less')
            : (showMoreLabels?.more ?? 'Show more');
          showMore.setAttribute('aria-expanded', String(options.isShowingMore));
          showMore.disabled = !options.canToggleShowMore;
          showMore.classList.toggle(
            'xps-refinement-list__show-more--disabled',
            !options.canToggleShowMore
          );
        }
      };

      repaint = paint;
      paint();
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'refinementList';
  return widget;
}
