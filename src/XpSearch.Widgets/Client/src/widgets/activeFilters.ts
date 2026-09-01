/**
 * `activeFilters` and `clearFilters` — both default renderers over
 * `withActiveFilters` (spec 5.3, Phase 2.5). Markup:
 * `themes/fixtures/active-filters.html`, `themes/fixtures/clear-filters.html`.
 *
 * `clearFilters` needs no behaviour of its own: `withActiveFilters` already publishes
 * `canApply` and `clearAll()`.
 */
import {
  withActiveFilters,
  type ActiveFilterItem,
} from '../behaviors/activeFilters';
import { html } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, renderKeepingFocus, resolveContainer, widgetId } from './dom';

/** The attribute filters both widgets share, spelled out (they cannot be intersected in). */
type Scope = {
  /** Only show filters on these attributes. Defaults to all of them. */
  includedAttributes?: string[];
  excludedAttributes?: string[];
  transformItems?: (items: ActiveFilterItem[]) => ActiveFilterItem[];
};

export type ActiveFiltersWidgetParams = Scope & {
  container: string | HTMLElement;
  /** Display names per attribute: `{ contentType: 'Content type' }`. */
  attributeLabels?: Record<string, string>;
  /** Heading text. Screen-reader only. */
  title?: string;
  /** Keep the chips on one row that scrolls sideways instead of wrapping. */
  scroll?: boolean;
};

export type ClearFiltersWidgetParams = Scope & {
  container: string | HTMLElement;
  /** Button text. Defaults to "Clear all". */
  label?: string;
};

/** "coffee" for a facet, "lte 50" for a numeric filter. */
const valueOf = (item: ActiveFilterItem): string =>
  item.type === 'numeric' ? `${item.operator} ${item.value}` : String(item.value);

export function activeFilters(params: ActiveFiltersWidgetParams): Widget {
  const container = resolveContainer(params.container, 'activeFilters');
  let root: HTMLElement | undefined;
  let items: ActiveFilterItem[] = [];

  const widget = withActiveFilters<ActiveFiltersWidgetParams>(
    (options, isFirstRender) => {
      const { attributeLabels, title = 'Active filters', scroll = false } = options.params;
      items = options.items;

      if (isFirstRender) {
        root = createRoot(
          container,
          'div',
          `xps xps-active-filters${scroll ? ' xps-active-filters--scroll' : ''}`
        );
        root.addEventListener('click', (event) => {
          const target = event.target;
          if (!(target instanceof Element)) return;
          const at = target.closest<HTMLElement>('[data-xps-item]')?.dataset['xpsItem'];
          if (at !== undefined) items[Number(at)]?.apply();
        });
      }
      if (!root) return;

      root.classList.toggle('xps-active-filters--empty', !options.canApply);
      renderKeepingFocus(
        html`<h3 class="xps-active-filters__title xps-sr-only" id="${widgetId(container, 'active-filters', 'title')}">${title}</h3>
  <ul class="xps-active-filters__list" aria-labelledby="${widgetId(container, 'active-filters', 'title')}">${options.items.map(
    (item, at) => {
      const name = attributeLabels?.[item.attribute] ?? item.attribute;
      const value = valueOf(item);
      return html`<li class="xps-active-filters__item"><span class="xps-chip"><span class="xps-chip__label"><span class="xps-chip__attribute">${name}</span> ${value}</span><button class="xps-chip__remove" type="button" aria-label="Remove filter ${name}: ${value}" data-xps-item="${at}"><span aria-hidden="true">&times;</span></button></span></li>`;
    }
  )}</ul>`,
        root
      );
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'activeFilters';
  return widget;
}

export function clearFilters(params: ClearFiltersWidgetParams): Widget {
  const container = resolveContainer(params.container, 'clearFilters');
  let button: HTMLButtonElement | undefined;
  let clearAll: () => void = () => {};

  const widget = withActiveFilters<ClearFiltersWidgetParams>(
    (options, isFirstRender) => {
      clearAll = options.clearAll;
      if (isFirstRender) {
        const root = createRoot(container, 'div', 'xps xps-clear-filters');
        // The button is never removed from the DOM, so pressing it does not destroy focus.
        button = container.ownerDocument.createElement('button');
        button.className = 'xps-button xps-button--link xps-clear-filters__button';
        button.type = 'button';
        button.textContent = options.params.label ?? 'Clear all';
        button.addEventListener('click', () => clearAll());
        root.appendChild(button);
      }
      const root = button?.parentElement;
      if (!button || !root) return;
      button.disabled = !options.canApply;
      root.classList.toggle('xps-clear-filters--disabled', !options.canApply);
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'clearFilters';
  return widget;
}
