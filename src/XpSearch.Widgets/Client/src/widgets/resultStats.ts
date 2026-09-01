/**
 * `resultStats` — `withResultStats` plus the default renderer (spec 5.3): "46 results in 14 ms".
 * Markup: `themes/fixtures/result-stats.html`. Deliberately *not* a live region — `results` announces the
 * count, so a page carrying both widgets announces a change once (spec 5.6).
 */
import { withResultStats, type ResultStatsRenderState } from '../behaviors/resultStats';
import {
  escapeHtml,
  helpers,
  html,
  render,
  type Renderable,
  type TemplateHelpers,
} from '../templates/html';
import type { Widget } from '../types';
import { createRoot, resolveContainer } from './dom';

export type ResultStatsWidgetParams = {
  container: string | HTMLElement;
  templates?: {
    text?: (data: ResultStatsRenderState, helpers: TemplateHelpers) => Renderable;
  };
  /**
   * A plain-text alternative to `templates.text`, for callers that can only supply a string - the
   * Page Builder stats widget's "Text template" property. Placeholders: `{total}`, `{tookMs}`,
   * `{query}`, `{page}`, `{totalPages}`. The template and every substituted value are escaped, so
   * markup typed into it is shown, not rendered; `{total}` is emphasised with a
   * `<strong class="xps-result-stats__total">`. `templates.text` wins when both are given.
   */
  textTemplate?: string;
  /** Shown before the first response. */
  emptyText?: string;
};

const PLACEHOLDER = /\{(total|tookMs|query|page|totalPages)\}/g;

/**
 * The template and every substituted value are escaped; the only markup that survives is the
 * `<strong>` this puts around `{total}`, so the count reads as the emphasised number of the
 * design. A template without `{total}` produces plain text, as before.
 */
const fromTextTemplate = (
  template: string,
  data: ResultStatsRenderState,
  tools: TemplateHelpers
): Renderable =>
  html.raw(
    escapeHtml(template).replace(PLACEHOLDER, (_match, key: string) =>
      key === 'total'
        ? `<strong class="xps-result-stats__total">${escapeHtml(tools.formatNumber(data.total))}</strong>`
        : escapeHtml(
            key === 'query'
              ? data.query
              : tools.formatNumber(data[key as 'tookMs' | 'page' | 'totalPages'])
          )
    )
  );

/** "14 results for “espresso” (8 ms)", or without the query part when nothing has been typed. */
const defaultText = (data: ResultStatsRenderState, tools: TemplateHelpers): Renderable =>
  html`<strong class="xps-result-stats__total">${tools.formatNumber(
    data.total
  )}</strong> results${
    data.query ? html` for “${data.query}”` : ''
  } (<span class="xps-result-stats__time">${tools.formatNumber(data.tookMs)}&nbsp;ms</span>)`;

export function resultStats(params: ResultStatsWidgetParams): Widget {
  const container = resolveContainer(params.container, 'resultStats');
  let root: HTMLElement | undefined;

  const widget = withResultStats<ResultStatsWidgetParams>(
    (options, isFirstRender) => {
      if (isFirstRender) root = createRoot(container, 'div', 'xps xps-result-stats');
      if (!root) return;
      const { textTemplate } = options.params;
      const template: (data: ResultStatsRenderState, tools: TemplateHelpers) => Renderable =
        options.params.templates?.text ??
        (textTemplate ? (data, tools) => fromTextTemplate(textTemplate, data, tools) : defaultText);
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
