/**
 * `sortBy` — `connectSortBy` plus the default renderer (spec 5.3): a native `<select>` with an
 * associated label. Markup: `themes/fixtures/sort-by.html`.
 *
 * The select is built once and only its `value` is patched, so changing the sort does not blow
 * away the element the user is interacting with.
 */
import { connectSortBy, type SortByItem } from '../connectors/sortBy';
import { html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, idBase, resolveContainer } from './dom';

export type SortByWidgetParams = {
  container: string | HTMLElement;
  items: SortByItem[];
  /** Defaults to "Sort by". */
  label?: string;
  /** Hide the label from sighted users. It stays associated with the select. */
  hideLabel?: boolean;
};

export function sortBy(params: SortByWidgetParams): Widget {
  const container = resolveContainer(params.container, 'sortBy');
  let select: HTMLSelectElement | undefined;
  let refine: (value: string) => void = () => {};

  const widget = connectSortBy<SortByWidgetParams>(
    (options, isFirstRender) => {
      const { label = 'Sort by', hideLabel = false } = options.widgetParams;
      refine = options.refine;

      if (isFirstRender) {
        const ids = idBase(container, 'sort-by');
        const root = createRoot(container, 'div', 'xps xps-sort-by');
        render(
          html`<label class="xps-sort-by__label${hideLabel ? ' xps-sr-only' : ''}" for="${ids}-select">${label}</label>
  <select class="xps-sort-by__select" id="${ids}-select" name="sort">${options.options.map(
    (item) => html`<option value="${item.value}">${item.label}</option>`
  )}</select>`,
          root
        );
        select = root.querySelector<HTMLSelectElement>('.xps-sort-by__select') ?? undefined;
        select?.addEventListener('change', () => refine(select?.value ?? ''));
      }
      if (!select) return;
      if (select.value !== options.currentRefinement) select.value = options.currentRefinement;
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'sortBy';
  return widget;
}
