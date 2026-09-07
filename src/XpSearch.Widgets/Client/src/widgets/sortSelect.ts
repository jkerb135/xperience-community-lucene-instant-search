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
import {
  chevron,
  createRoot,
  isServerRendered,
  resolveContainer,
  takeOver,
  widgetId,
} from './dom';

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
  let root: HTMLElement | undefined;
  let select: HTMLSelectElement | undefined;
  let apply: (value: string) => void = () => {};

  const widget = withSortSelect<SortSelectWidgetParams>(
    (options, isFirstRender) => {
      const { label = 'Sort by', hideLabel = false } = options.params;
      apply = options.apply;

      if (isFirstRender) root = createRoot(container, 'div', 'xps xps-sort-select xps-select');
      if (!root) return;
      // Nothing has answered yet: whatever the server painted stays on screen (SK-1).
      if (options.results === null && isServerRendered(root)) return;
      takeOver(root);
      // Built once, on the render this widget first paints (SK-1).
      if (!select) {
        const id = widgetId(container, 'sort-select', 'select');
        render(
          html`<label class="xps-select__label${hideLabel ? ' xps-sr-only' : ''}" for="${id}">${label}</label>
  <span class="xps-select__field"><select class="xps-select__control" id="${id}" name="sort">${options.options.map(
    (item) => html`<option value="${item.value}">${item.label}</option>`
  )}</select>${chevron('xps-select__chevron')}</span>`,
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
