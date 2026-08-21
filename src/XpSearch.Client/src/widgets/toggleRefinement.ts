/**
 * `toggleRefinement` — one boolean facet, over `connectRefinementList` (spec 5.3, Phase 2.5).
 * Markup: `themes/fixtures/toggle-refinement.html`. A real checkbox, never a styled div
 * (spec 5.6).
 *
 * No connector of its own: a toggle is a refinement list narrowed to a single value, and the
 * connector already publishes the count and the refined flag for it.
 */
import { connectRefinementList } from '../connectors/refinementList';
import { html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer } from './dom';

export type ToggleRefinementWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** The facet value the checkbox refines on. Defaults to `"true"`. */
  value?: string;
  /** Visible text. Defaults to `attribute`. */
  label?: string;
  /** Hide the facet count. */
  showCount?: boolean;
};

/** High enough that the toggled value is never cut off by the connector's default limit. */
const ALL_VALUES = 1000;

export function toggleRefinement(params: ToggleRefinementWidgetParams): Widget {
  const container = resolveContainer(params.container, 'toggleRefinement');
  let root: HTMLElement | undefined;
  let checkbox: HTMLInputElement | undefined;
  let count: HTMLElement | undefined;
  let refine: (value: string) => void = () => {};

  const widget = connectRefinementList<ToggleRefinementWidgetParams>(
    (options, isFirstRender) => {
      const {
        attribute,
        value = 'true',
        label = attribute,
        showCount = true,
      } = options.widgetParams;
      const item = options.items.find((candidate) => candidate.value === value);
      refine = options.refine;

      if (isFirstRender) {
        root = createRoot(container, 'div', 'xps xps-toggle-refinement');
        render(
          html`<label class="xps-toggle-refinement__label">
    <input class="xps-toggle-refinement__checkbox" type="checkbox" name="${attribute}" value="${value}">
    <span class="xps-toggle-refinement__value">${label}</span>
    <span class="xps-toggle-refinement__count"${showCount ? '' : html.raw(' hidden')}></span>
  </label>`,
          root
        );
        checkbox = root.querySelector<HTMLInputElement>('.xps-toggle-refinement__checkbox') ?? undefined;
        count = root.querySelector<HTMLElement>('.xps-toggle-refinement__count') ?? undefined;
        checkbox?.addEventListener('change', () => refine(value));
      }
      if (!root || !checkbox || !count) return;

      const refined = item?.isRefined ?? false;
      // Nothing to switch on: no document carries the value and it is not already refined.
      const canRefine = refined || (item?.count ?? 0) > 0;
      checkbox.checked = refined;
      checkbox.disabled = !canRefine;
      count.textContent = String(item?.count ?? 0);
      root.classList.toggle('xps-toggle-refinement--disabled', !canRefine);
    },
    () => {
      container.textContent = '';
    }
  )({ ...params, limit: ALL_VALUES });

  widget.$$type = 'toggleRefinement';
  return widget;
}
