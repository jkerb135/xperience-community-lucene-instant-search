/**
 * `currentRefinements` and `clearRefinements` — both default renderers over
 * `connectCurrentRefinements` (spec 5.3, Phase 2.5). Markup:
 * `themes/fixtures/current-refinements.html`, `themes/fixtures/clear-refinements.html`.
 *
 * `clearRefinements` needs no connector of its own: the current-refinements connector already
 * publishes `canRefine` and `clearAll()`.
 */
import {
  connectCurrentRefinements,
  type CurrentRefinementItem,
} from '../connectors/currentRefinements';
import { html } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, idBase, renderKeepingFocus, resolveContainer } from './dom';

/** The attribute filters both widgets share, spelled out (they cannot be intersected in). */
type Scope = {
  /** Only show refinements on these attributes. Defaults to all of them. */
  includedAttributes?: string[];
  excludedAttributes?: string[];
  transformItems?: (items: CurrentRefinementItem[]) => CurrentRefinementItem[];
};

export type CurrentRefinementsWidgetParams = Scope & {
  container: string | HTMLElement;
  /** Display names per attribute: `{ contentType: 'Content type' }`. */
  attributeLabels?: Record<string, string>;
  /** Heading text. Screen-reader only. */
  title?: string;
};

export type ClearRefinementsWidgetParams = Scope & {
  container: string | HTMLElement;
  /** Button text. Defaults to "Clear filters". */
  label?: string;
};

/** "coffee" for a facet, "<= 50" for a numeric refinement. */
const valueOf = (item: CurrentRefinementItem): string =>
  item.type === 'numeric' ? `${item.operator} ${item.value}` : String(item.value);

export function currentRefinements(params: CurrentRefinementsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'currentRefinements');
  let root: HTMLElement | undefined;
  let ids = '';
  let items: CurrentRefinementItem[] = [];

  const widget = connectCurrentRefinements<CurrentRefinementsWidgetParams>(
    (options, isFirstRender) => {
      const { attributeLabels, title = 'Active filters' } = options.widgetParams;
      items = options.items;

      if (isFirstRender) {
        ids = idBase(container, 'current-refinements');
        root = createRoot(container, 'div', 'xps xps-current-refinements');
        root.addEventListener('click', (event) => {
          const target = event.target;
          if (!(target instanceof Element)) return;
          const at = target.closest<HTMLElement>('[data-xps-item]')?.dataset['xpsItem'];
          if (at !== undefined) items[Number(at)]?.refine();
        });
      }
      if (!root) return;

      root.classList.toggle('xps-current-refinements--empty', !options.canRefine);
      renderKeepingFocus(
        html`<h3 class="xps-current-refinements__title xps-sr-only" id="${ids}-title">${title}</h3>
  <ul class="xps-current-refinements__list" aria-labelledby="${ids}-title">${options.items.map(
    (item, at) => {
      const name = attributeLabels?.[item.attribute] ?? item.attribute;
      const value = valueOf(item);
      return html`<li class="xps-current-refinements__item"><span class="xps-chip"><span class="xps-chip__label"><span class="xps-chip__attribute">${name}</span> ${value}</span><button class="xps-chip__remove" type="button" aria-label="Remove filter ${name}: ${value}" data-xps-item="${at}"><span aria-hidden="true">&times;</span></button></span></li>`;
    }
  )}</ul>`,
        root
      );
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'currentRefinements';
  return widget;
}

export function clearRefinements(params: ClearRefinementsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'clearRefinements');
  let button: HTMLButtonElement | undefined;
  let clearAll: () => void = () => {};

  const widget = connectCurrentRefinements<ClearRefinementsWidgetParams>(
    (options, isFirstRender) => {
      clearAll = options.clearAll;
      if (isFirstRender) {
        const root = createRoot(container, 'div', 'xps xps-clear-refinements');
        // The button is never removed from the DOM, so pressing it does not destroy focus.
        button = container.ownerDocument.createElement('button');
        button.className = 'xps-button xps-clear-refinements__button';
        button.type = 'button';
        button.textContent = options.widgetParams.label ?? 'Clear filters';
        button.addEventListener('click', () => clearAll());
        root.appendChild(button);
      }
      const root = button?.parentElement;
      if (!button || !root) return;
      button.disabled = !options.canRefine;
      root.classList.toggle('xps-clear-refinements--disabled', !options.canRefine);
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'clearRefinements';
  return widget;
}
