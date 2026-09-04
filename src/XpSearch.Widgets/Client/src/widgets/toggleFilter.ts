/**
 * `toggleFilter` — one boolean facet, over `withFacetList` (spec 5.3, Phase 2.5).
 * Markup: `themes/fixtures/toggle-filter.html`. A real checkbox, never a styled div
 * (spec 5.6).
 *
 * No behaviour of its own: a toggle is a facet list narrowed to a single value, and
 * `withFacetList` already publishes the count and the active flag for it.
 */
import { withFacetList } from '../behaviors/facetList';
import { valueLabel } from '../labels';
import { html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer } from './dom';

export type ToggleFilterWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** The facet value the checkbox filters on. Defaults to `"true"`. */
  value?: string;
  /** Visible text. Defaults to `attribute`. */
  label?: string;
  /** Hide the facet count. */
  showCount?: boolean;
};

/** High enough that the toggled value is never cut off by the behaviour's default limit. */
const ALL_VALUES = 1000;

export function toggleFilter(params: ToggleFilterWidgetParams): Widget {
  const container = resolveContainer(params.container, 'toggleFilter');
  let root: HTMLElement | undefined;
  let checkbox: HTMLInputElement | undefined;
  let count: HTMLElement | undefined;
  let text: HTMLElement | undefined;
  let apply: (value: string) => void = () => {};

  const widget = withFacetList<ToggleFilterWidgetParams>(
    (options, isFirstRender) => {
      const { attribute, value = 'true', showCount = true } = options.params;
      const item = options.items.find((candidate) => candidate.value === value);
      apply = options.apply;
      // No `label`: the visitor reads the value the server named, never the attribute code (TH-12).
      const label =
        options.params.label ||
        item?.label ||
        valueLabel(options.search, attribute, value) ||
        value;

      if (isFirstRender) {
        root = createRoot(container, 'div', 'xps xps-toggle-filter');
        render(
          html`<label class="xps-toggle-filter__label">
    <input class="xps-toggle-filter__checkbox" type="checkbox" name="${attribute}" value="${value}">
    <span class="xps-toggle-filter__value"></span>
    <span class="xps-toggle-filter__count"${showCount ? '' : html.raw(' hidden')}></span>
  </label>`,
          root
        );
        checkbox = root.querySelector<HTMLInputElement>('.xps-toggle-filter__checkbox') ?? undefined;
        count = root.querySelector<HTMLElement>('.xps-toggle-filter__count') ?? undefined;
        text = root.querySelector<HTMLElement>('.xps-toggle-filter__value') ?? undefined;
        checkbox?.addEventListener('change', () => apply(value));
      }
      if (!root || !checkbox || !count || !text) return;
      // Patched, not baked in: the first render happens before the response that names the value.
      if (text.textContent !== label) text.textContent = label;

      const active = item?.isActive ?? false;
      // Nothing to switch on: no document carries the value and it is not already selected.
      const canApply = active || (item?.count ?? 0) > 0;
      checkbox.checked = active;
      checkbox.disabled = !canApply;
      count.textContent = String(item?.count ?? 0);
      root.classList.toggle('xps-toggle-filter--disabled', !canApply);
    },
    () => {
      container.textContent = '';
    }
  )({ ...params, limit: ALL_VALUES });

  widget.$$type = 'toggleFilter';
  return widget;
}
