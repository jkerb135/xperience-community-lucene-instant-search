/**
 * `sortSelect` — `withSortSelect` plus the default renderer (spec 5.3): a native `<select>` with an
 * associated label, built on the shared `xps-select` block. Markup: `themes/fixtures/sort-select.html`.
 *
 * The select is built once and only its `value` is patched, so changing the sort does not blow
 * away the element the user is interacting with.
 */
import { withSortSelect, type SortSelectItem } from '../behaviors/sortSelect';
import { html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer, widgetId } from './dom';

export type SortSelectWidgetParams = {
  container: string | HTMLElement;
  items: SortSelectItem[];
  /** Defaults to "Sort by". */
  label?: string;
  /** Hide the label from sighted users. It stays associated with the select. */
  hideLabel?: boolean;
};

export function sortSelect(params: SortSelectWidgetParams): Widget {
  const container = resolveContainer(params.container, 'sortSelect');
  let select: HTMLSelectElement | undefined;
  let apply: (value: string) => void = () => {};

  const widget = withSortSelect<SortSelectWidgetParams>(
    (options, isFirstRender) => {
      const { label = 'Sort by', hideLabel = false } = options.params;
      apply = options.apply;

      if (isFirstRender) {
        const id = widgetId(container, 'sort-select', 'select');
        const root = createRoot(container, 'div', 'xps xps-sort-select xps-select');
        render(
          html`<label class="xps-select__label${hideLabel ? ' xps-sr-only' : ''}" for="${id}">${label}</label>
  <select class="xps-select__control" id="${id}" name="sort">${options.options.map(
    (item) => html`<option value="${item.value}">${item.label}</option>`
  )}</select>`,
          root
        );
        select = root.querySelector<HTMLSelectElement>('.xps-select__control') ?? undefined;
        select?.addEventListener('change', () => apply(select?.value ?? ''));
      }
      if (!select) return;
      if (select.value !== options.current) select.value = options.current;
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'sortSelect';
  return widget;
}
