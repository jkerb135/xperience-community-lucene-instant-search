/**
 * `facetList` — `withFacetList` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/facet-list.html`. A11y: real checkboxes with labels (spec 5.6).
 *
 * The root is built once and patched afterwards, so typing in the facet search box or pressing
 * "show more" never destroys the control that has focus.
 */
import {
  withFacetList,
  type FacetListItem,
  type FacetListSortBy,
} from '../behaviors/facetList';
import { escapeHtml, html, render, type Renderable } from '../templates/html';
import type { Widget } from '../types';
import { chevron, createRoot, resolveContainer, widgetId } from './dom';

export type FacetListWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** `'or'` (the default) ORs the selected values; `'and'` ANDs them. */
  operator?: 'and' | 'or';
  /** Values shown before "show more". Defaults to 10. */
  limit?: number;
  showMore?: boolean;
  /** Values shown after "show more". Defaults to 20. */
  showMoreLimit?: number;
  sortBy?: FacetListSortBy[];
  transformItems?: (items: FacetListItem[]) => FacetListItem[];
  /** Heading text. Defaults to `attribute`. */
  label?: string;
  /** Adds an input that filters the rendered values client-side. */
  searchable?: boolean;
  searchablePlaceholder?: string;
  showMoreLabels?: { more?: string; less?: string };
  /**
   * The title is a disclosure button that folds the values away. On by default; pass `false`
   * for a group that is always open. Collapsed state is local to the render — it is never
   * persisted and never reaches the URL.
   */
  collapsible?: boolean;
};

/**
 * `espresso` + `es` → `<mark class="xps-highlight">es</mark>presso`, escaped either side.
 * Shared with `suggestions`, which marks the typed prefix the same way.
 */
export function markMatch(label: string, needle: string): Renderable {
  const at = needle === '' ? -1 : label.toLowerCase().indexOf(needle.toLowerCase());
  if (at === -1) return label;
  return html.raw(
    escapeHtml(label.slice(0, at)) +
      `<mark class="xps-highlight">${escapeHtml(label.slice(at, at + needle.length))}</mark>` +
      escapeHtml(label.slice(at + needle.length))
  );
}

const itemHtml = (item: FacetListItem, attribute: string, needle: string): Renderable => {
  // A value nobody can reach any more (count 0, not selected) is a disabled row, not a missing
  // one — the list keeps its shape between searches.
  const disabled = item.count === 0 && !item.isActive;
  // A value the visitor picked that this response has no hit for (TH-7): still checked, still
  // untickable, marked so the theme can quieten it.
  const empty = item.count === 0 && item.isActive;
  const modifiers = `${item.isActive ? ' xps-facet-list__item--selected' : ''}${
    disabled ? ' xps-facet-list__item--disabled' : ''
  }${empty ? ' xps-facet-list__item--empty' : ''}`;
  return html`<li class="xps-facet-list__item${modifiers}">
    <label class="xps-facet-list__label">
      <input class="xps-facet-list__checkbox" type="checkbox" name="${attribute}" value="${item.value}"${
        item.isActive ? html.raw(' checked') : ''
      }${disabled ? html.raw(' disabled') : ''}>
      <span class="xps-facet-list__value">${markMatch(item.label, needle)}</span>
      <span class="xps-facet-list__count">${item.count}</span>
    </label>
  </li>`;
};

export function facetList(params: FacetListWidgetParams): Widget {
  const container = resolveContainer(params.container, 'facetList');
  let root: HTMLElement | undefined;
  let listEl: HTMLElement | undefined;
  let noResults: HTMLElement | undefined;
  let showMore: HTMLButtonElement | undefined;
  let needle = '';
  let apply: (value: string) => void = () => {};
  let toggleShowMore: () => void = () => {};
  // Reassigned on every render: a listener registered on the first render must not paint from
  // the first render's items.
  let repaint: () => void = () => {};

  const widget = withFacetList<FacetListWidgetParams>(
    (options, isFirstRender) => {
      const {
        attribute,
        label = attribute,
        searchable = false,
        searchablePlaceholder,
        showMore: withShowMore = false,
        showMoreLabels,
        collapsible = true,
      } = options.params;
      apply = options.apply;
      toggleShowMore = options.toggleShowMore;

      if (isFirstRender) {
        const id = (part: string): string => widgetId(container, attribute, part);
        root = createRoot(
          container,
          'div',
          `xps xps-facet-list${searchable ? ' xps-facet-list--searchable' : ''}`
        );
        render(
          html`<h3 class="xps-facet-list__title" id="${id('title')}">${
            collapsible
              ? html`<button class="xps-facet-list__toggle" type="button" aria-expanded="true" aria-controls="${id('body')}"><span class="xps-facet-list__toggle-label">${label}</span>${chevron('xps-facet-list__chevron')}</button>`
              : label
          }</h3>
  <div class="xps-facet-list__body" id="${id('body')}">${searchable
    ? html`<div class="xps-facet-list__search">
      <label class="xps-sr-only" for="${id('search')}">Search in ${label}</label>
      <input class="xps-facet-list__search-input" id="${id('search')}" type="search" value="" placeholder="${searchablePlaceholder ?? `Search in ${label}`}" autocomplete="off">
    </div>`
    : ''}
  <ul class="xps-facet-list__list" aria-labelledby="${id('title')}"></ul>
  <p class="xps-facet-list__no-results" role="status" hidden>No matching filters.</p>
  ${withShowMore
    ? html`<button class="xps-button xps-facet-list__show-more" type="button" aria-expanded="false">${showMoreLabels?.more ?? 'Show more'}</button>`
    : ''}</div>`,
          root
        );
        listEl = root.querySelector<HTMLElement>('.xps-facet-list__list') ?? undefined;
        noResults = root.querySelector<HTMLElement>('.xps-facet-list__no-results') ?? undefined;
        showMore = root.querySelector<HTMLButtonElement>('.xps-facet-list__show-more') ?? undefined;

        root.addEventListener('change', (event) => {
          const target = event.target;
          if (target instanceof HTMLInputElement && target.type === 'checkbox') apply(target.value);
        });
        root.addEventListener('input', (event) => {
          const target = event.target;
          if (!(target instanceof HTMLInputElement) || target.type !== 'search') return;
          needle = target.value;
          repaint();
        });
        showMore?.addEventListener('click', () => toggleShowMore());

        const toggle = root.querySelector<HTMLButtonElement>('.xps-facet-list__toggle');
        const body = root.querySelector<HTMLElement>('.xps-facet-list__body');
        toggle?.addEventListener('click', () => {
          const open = toggle.getAttribute('aria-expanded') !== 'true';
          toggle.setAttribute('aria-expanded', String(open));
          if (body) body.hidden = !open;
        });
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
            'xps-facet-list__show-more--disabled',
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

  widget.$$type = 'facetList';
  return widget;
}
