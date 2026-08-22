/**
 * `rangeFilter` — `withRange` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/range-filter.html`. A11y (spec 5.6): two native
 * `<input type="range">` controls rather than a custom drag widget, so the control is
 * keyboard-operable and announced by a screen reader with no extra code.
 *
 * The bounds are `params.min`/`max`: the JSON contract carries no numeric facet statistics, so
 * there is nowhere for a server-computed range to arrive (docs/internal/KNOWN-LIMITATIONS.md).
 * Without them the control renders disabled instead of pretending to filter.
 */
import { withRange } from '../behaviors/range';
import { formatNumber, html, render } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer, widgetId } from './dom';

export type RangeFilterWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** Lower bound of the control. Required to filter: see the note above. */
  min?: number;
  /** Upper bound of the control. Required to filter: see the note above. */
  max?: number;
  /** Step of both the sliders and the number inputs. Defaults to 1. */
  step?: number;
  /** Heading text. Defaults to `attribute`. */
  label?: string;
  /** Visible labels of the two number inputs. */
  labels?: { from?: string; to?: string };
};

export function rangeFilter(params: RangeFilterWidgetParams): Widget {
  const container = resolveContainer(params.container, 'rangeFilter');
  let root: HTMLElement | undefined;
  let values: HTMLElement | undefined;
  /** [range-min, range-max, input-min, input-max], in that order. */
  let controls: HTMLInputElement[] = [];
  let apply: (bounds: [number, number]) => void = () => {};
  let paint: () => void = () => {};

  const widget = withRange<RangeFilterWidgetParams>(
    (options, isFirstRender) => {
      const { attribute, label = options.params.attribute, step = 1, labels } = options.params;
      const { min, max } = options.range;
      const enabled = options.canApply;

      if (isFirstRender) {
        const id = (part: string): string => widgetId(container, attribute, part);
        root = createRoot(container, 'div', 'xps xps-range-filter');
        render(
          html`<h3 class="xps-range-filter__title" id="${id('title')}">${label}</h3>
  <div class="xps-range-filter__track" role="group" aria-labelledby="${id('title')}">
    <label class="xps-sr-only" for="${id('range-min')}">Minimum ${label}</label>
    <input class="xps-range-filter__range xps-range-filter__range--min" id="${id('range-min')}" type="range" aria-describedby="${id('values')}">
    <label class="xps-sr-only" for="${id('range-max')}">Maximum ${label}</label>
    <input class="xps-range-filter__range xps-range-filter__range--max" id="${id('range-max')}" type="range" aria-describedby="${id('values')}">
  </div>
  <div class="xps-range-filter__inputs">
    <label class="xps-range-filter__input-label" for="${id('input-min')}">${labels?.from ?? 'From'}</label>
    <input class="xps-range-filter__input" id="${id('input-min')}" type="number" inputmode="numeric">
    <span class="xps-range-filter__separator" aria-hidden="true">&ndash;</span>
    <label class="xps-range-filter__input-label" for="${id('input-max')}">${labels?.to ?? 'To'}</label>
    <input class="xps-range-filter__input" id="${id('input-max')}" type="number" inputmode="numeric">
  </div>
  <p class="xps-range-filter__values" id="${id('values')}"></p>`,
          root
        );
        controls = [
          ...root.querySelectorAll<HTMLInputElement>('.xps-range-filter__range, .xps-range-filter__input'),
        ];
        values = root.querySelector<HTMLElement>('.xps-range-filter__values') ?? undefined;

        // `input` mirrors the two halves of the control while dragging; `change` is the commit,
        // so a drag or a held arrow key produces one search, not one per pixel.
        const edit = (event: Event, commit: boolean): void => {
          const target = event.target;
          if (!(target instanceof HTMLInputElement) || controls.length < 4) return;
          const [rangeMin, rangeMax, inputMin, inputMax] = controls as [
            HTMLInputElement,
            HTMLInputElement,
            HTMLInputElement,
            HTMLInputElement,
          ];
          const isMin = target === rangeMin || target === inputMin;
          const low = Number(rangeMin.min);
          const high = Number(rangeMin.max);
          let value = Number(target.value);
          if (!Number.isFinite(value)) value = isMin ? low : high;
          value = Math.min(Math.max(value, low), high);
          // Neither end may cross the other, whichever control was moved.
          value = isMin
            ? Math.min(value, Number(rangeMax.value))
            : Math.max(value, Number(rangeMin.value));
          for (const control of isMin ? [rangeMin, inputMin] : [rangeMax, inputMax]) {
            control.value = String(value);
          }
          paint();
          if (commit) apply([Number(rangeMin.value), Number(rangeMax.value)]);
        };
        root.addEventListener('input', (event) => edit(event, false));
        root.addEventListener('change', (event) => edit(event, true));
      }
      if (!root || !values || controls.length < 4) return;

      apply = options.apply;
      const [lower, upper] = options.start;
      root.classList.toggle('xps-range-filter--disabled', !enabled);
      for (const [at, control] of controls.entries()) {
        control.disabled = !enabled;
        if (!enabled) {
          control.value = '';
          continue;
        }
        control.min = String(min);
        control.max = String(max);
        control.step = String(step);
        const next = String((at % 2 === 0 ? lower : upper) ?? (at % 2 === 0 ? min : max));
        // Only assign when it differs: assigning moves the caret in the number input.
        if (control.value !== next) control.value = next;
      }

      paint = (): void => {
        if (!values) return;
        const text = enabled
          ? `${formatNumber(Number(controls[0]?.value))} to ${formatNumber(Number(controls[1]?.value))}`
          : `No ${label.toLowerCase()} range in these results.`;
        if (values.textContent !== text) values.textContent = text;
      };
      paint();
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'rangeFilter';
  return widget;
}
