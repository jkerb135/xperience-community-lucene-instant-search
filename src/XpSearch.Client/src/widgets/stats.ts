/**
 * `stats` — `connectStats` plus the default renderer (spec 5.3): "46 results in 14 ms".
 * Markup: `themes/fixtures/stats.html`. Deliberately *not* a live region — `hits` announces the
 * count, so a page carrying both widgets announces a change once (spec 5.6).
 */
import { connectStats, type StatsRenderState } from '../connectors/stats';
import { helpers, html, render, type Renderable, type TemplateHelpers } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer } from './dom';

export type StatsWidgetParams = {
  container: string | HTMLElement;
  templates?: {
    text?: (data: StatsRenderState, helpers: TemplateHelpers) => Renderable;
  };
  /** Shown before the first response. */
  emptyText?: string;
};

const defaultText = (data: StatsRenderState, tools: TemplateHelpers): Renderable =>
  html`${tools.formatNumber(data.nbHits)} results in <span class="xps-stats__time">${tools.formatNumber(
    data.processingTimeMS
  )}&nbsp;ms</span>`;

export function stats(params: StatsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'stats');
  let root: HTMLElement | undefined;

  const widget = connectStats<StatsWidgetParams>(
    (options, isFirstRender) => {
      if (isFirstRender) root = createRoot(container, 'div', 'xps xps-stats');
      if (!root) return;
      const template = options.widgetParams.templates?.text ?? defaultText;
      const empty = !options.hasResults;
      root.classList.toggle('xps-stats--empty', empty);
      render(
        html`<span class="xps-stats__text">${
          empty ? (options.widgetParams.emptyText ?? 'Type to search.') : template(options, helpers)
        }</span>`,
        root
      );
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'stats';
  return widget;
}
