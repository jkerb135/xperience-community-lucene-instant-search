/**
 * `resultStats` — `withResultStats` plus the default renderer (spec 5.3): "46 results in 14 ms".
 * Markup: `themes/fixtures/result-stats.html`. Deliberately *not* a live region — `results` announces the
 * count, so a page carrying both widgets announces a change once (spec 5.6).
 */
import { withResultStats, type ResultStatsRenderState } from '../behaviors/resultStats';
import { helpers, html, render, type Renderable, type TemplateHelpers } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer } from './dom';

export type ResultStatsWidgetParams = {
  container: string | HTMLElement;
  templates?: {
    text?: (data: ResultStatsRenderState, helpers: TemplateHelpers) => Renderable;
  };
  /** Shown before the first response. */
  emptyText?: string;
};

const defaultText = (data: ResultStatsRenderState, tools: TemplateHelpers): Renderable =>
  html`${tools.formatNumber(data.total)} results in <span class="xps-result-stats__time">${tools.formatNumber(
    data.tookMs
  )}&nbsp;ms</span>`;

export function resultStats(params: ResultStatsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'resultStats');
  let root: HTMLElement | undefined;

  const widget = withResultStats<ResultStatsWidgetParams>(
    (options, isFirstRender) => {
      if (isFirstRender) root = createRoot(container, 'div', 'xps xps-result-stats');
      if (!root) return;
      const template = options.params.templates?.text ?? defaultText;
      const empty = !options.hasResults;
      root.classList.toggle('xps-result-stats--empty', empty);
      render(
        html`<span class="xps-result-stats__text">${
          empty ? (options.params.emptyText ?? 'Type to search.') : template(options, helpers)
        }</span>`,
        root
      );
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'resultStats';
  return widget;
}
