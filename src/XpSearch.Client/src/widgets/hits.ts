/**
 * `hits` — `connectHits` plus the default renderer (spec 5.3), the results live region
 * (spec 5.6) and client-side click tracking (spec 9.1).
 * Markup: `themes/fixtures/hits.html`.
 */
import { connectHits } from '../connectors/hits';
import {
  helpers,
  html,
  highlight,
  toHtml,
  type Renderable,
  type TemplateHelpers,
} from '../templates/html';
import type { Hit, Widget } from '../types';
import { createRoot, resolveContainer, setAttr } from './dom';

export interface HitsTemplates<TItem extends Record<string, unknown>> {
  item?: (hit: Hit<TItem>, helpers: TemplateHelpers) => Renderable;
  empty?: (data: { query: string }, helpers: TemplateHelpers) => Renderable;
  loading?: (helpers: TemplateHelpers) => Renderable;
}

export type HitsWidgetParams<TItem extends Record<string, unknown> = Record<string, unknown>> = {
  container: string | HTMLElement;
  templates?: HitsTemplates<TItem>;
  /** Client-side massaging escape hatch (spec 5.2). */
  transformItems?: (items: Array<Hit<TItem>>) => Array<Hit<TItem>>;
  /** Skeleton rows rendered while the first search is in flight. Defaults to 3. */
  loadingRows?: number;
};

/** The snippet falls back through the fields a Kentico index usually carries. */
const SNIPPET_FIELDS = ['content', 'summary', 'excerpt'];

function defaultItem<TItem extends Record<string, unknown>>(hit: Hit<TItem>): Renderable {
  const record = hit as Record<string, unknown>;
  const image = typeof record['image'] === 'string' ? record['image'] : '';
  const url = typeof record['url'] === 'string' ? record['url'] : '#';
  const type = typeof record['contentType'] === 'string' ? record['contentType'] : '';
  const field = SNIPPET_FIELDS.find((name) => highlight(name, hit).value !== '');
  return html`<article class="xps-hit">
    ${image
      ? html`<div class="xps-hit__media"><img class="xps-hit__image" src="${image}" alt="" width="96" height="96"></div>`
      : ''}
    <div class="xps-hit__body">
      <h3 class="xps-hit__title"><a class="xps-hit__link" href="${url}">${highlight('title', hit)}</a></h3>
      ${field ? html`<p class="xps-hit__snippet">${highlight(field, hit)}</p>` : ''}
      ${type
        ? html`<ul class="xps-hit__meta"><li class="xps-hit__meta-item">${type}</li></ul>`
        : ''}
    </div>
  </article>`;
}

const defaultEmpty = ({ query }: { query: string }): Renderable =>
  query === ''
    ? html`<p>No results.</p><p>Try a different search term, or clear some filters.</p>`
    : html`<p>No results for <strong>${query}</strong>.</p><p>Try fewer words, or clear some filters.</p>`;

const skeleton = (): Renderable =>
  html`<article class="xps-hit xps-hit--skeleton" aria-hidden="true">
    <div class="xps-hit__media"><span class="xps-skeleton xps-skeleton--block"></span></div>
    <div class="xps-hit__body">
      <span class="xps-skeleton xps-skeleton--title"></span>
      <span class="xps-skeleton xps-skeleton--text"></span>
    </div>
  </article>`;

const list = (items: Renderable[]): Renderable =>
  html`<ol class="xps-hits__list">${items.map(
    (item) => html`<li class="xps-hits__item">${item}</li>`
  )}</ol>`;

export function hits<TItem extends Record<string, unknown> = Record<string, unknown>>(
  params: HitsWidgetParams<TItem>
): Widget {
  const container = resolveContainer(params.container, 'hits');
  let root: HTMLElement | undefined;
  let status: HTMLElement | undefined;
  let shown: Array<Hit<TItem>> = [];
  let send: (hit: Hit<TItem>) => void = () => {};

  const widget = connectHits<TItem, HitsWidgetParams<TItem>>(
    (options, isFirstRender) => {
      const templates = options.widgetParams.templates ?? {};
      const rows = options.widgetParams.loadingRows ?? 3;
      shown = options.hits;
      send = (hit) => options.sendEvent('click', hit);

      if (isFirstRender) {
        root = createRoot(container, 'div', 'xps xps-hits');
        // The live region is created once and only ever has its text updated, so a re-render
        // that does not change the count is not announced again (spec 5.6).
        status = container.ownerDocument.createElement('p');
        status.className = 'xps-hits__status xps-sr-only';
        status.setAttribute('role', 'status');
        root.appendChild(status);
        root.addEventListener('click', (event) => {
          const target = event.target;
          if (!(target instanceof Element)) return;
          const item = target.closest('a')?.closest('.xps-hits__item');
          const parent = item?.parentElement;
          if (!item || !parent) return;
          const hit = shown[Array.prototype.indexOf.call(parent.children, item)];
          if (hit) send(hit);
        });
      }
      if (!root || !status) return;

      const query = options.state.query;
      const busy =
        options.instantSearchInstance.status === 'loading' ||
        options.instantSearchInstance.status === 'stalled';
      const first = busy && options.results === null;
      const empty = options.results !== null && options.hits.length === 0;

      let body: Renderable;
      let announcement: string;
      if (first) {
        body = templates.loading
          ? templates.loading(helpers)
          : list(Array.from({ length: rows }, skeleton));
        announcement = 'Searching…';
      } else if (empty) {
        body = html`<div class="xps-hits__empty">${
          templates.empty ? templates.empty({ query }, helpers) : defaultEmpty({ query })
        }</div>`;
        announcement = query === '' ? 'No results.' : `No results for “${query}”`;
      } else if (options.hits.length > 0) {
        body = list(
          options.hits.map((hit) =>
            templates.item ? templates.item(hit, helpers) : defaultItem(hit)
          )
        );
        const count = helpers.formatNumber(options.results?.nbHits ?? options.hits.length);
        announcement = query === '' ? `${count} results` : `${count} results for “${query}”`;
      } else {
        body = '';
        announcement = '';
      }

      root.classList.toggle('xps-hits--empty', empty);
      root.classList.toggle('xps-hits--loading', busy);
      setAttr(root, 'aria-busy', busy);
      // Replace everything after the status element, so the live region survives the render.
      while (root.lastChild && root.lastChild !== status) root.removeChild(root.lastChild);
      root.insertAdjacentHTML('beforeend', toHtml(body));
      if (status.textContent !== announcement) status.textContent = announcement;
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'hits';
  return widget;
}
