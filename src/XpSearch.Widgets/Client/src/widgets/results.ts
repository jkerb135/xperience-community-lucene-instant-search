/**
 * `results` — `withResults` plus the default renderer (spec 5.3), the results live region
 * (spec 5.6) and client-side click tracking (spec 9.1).
 * Markup: `themes/fixtures/results.html`.
 */
import { withResults } from '../behaviors/results';
import {
  helpers,
  html,
  highlight,
  toHtml,
  type Renderable,
  type TemplateHelpers,
} from '../templates/html';
import type { Result, Widget } from '../types';
import { createRoot, resolveContainer, setAttr } from './dom';

export interface ResultsTemplates<TAttributes extends Record<string, unknown>> {
  item?: (result: Result<TAttributes>, helpers: TemplateHelpers) => Renderable;
  empty?: (data: { query: string }, helpers: TemplateHelpers) => Renderable;
  loading?: (helpers: TemplateHelpers) => Renderable;
}

export type ResultsWidgetParams<
  TAttributes extends Record<string, unknown> = Record<string, unknown>,
> = {
  container: string | HTMLElement;
  templates?: ResultsTemplates<TAttributes>;
  /** Client-side massaging escape hatch (spec 5.2). */
  transformItems?: (items: Array<Result<TAttributes>>) => Array<Result<TAttributes>>;
  /** Skeleton rows rendered while the first search is in flight. Defaults to 3. */
  loadingRows?: number;
  /** Attribute the default template reads the title from. Defaults to `title`. */
  titleAttribute?: string;
  /** Attribute the default template reads the link from. Defaults to `url`. */
  urlAttribute?: string;
  /**
   * Attributes the default template tries, in order, for the snippet; the first one with a value
   * wins. Defaults to `summary`, `content`, `excerpt`.
   */
  snippetAttributes?: string[];
};

/**
 * The attributes the default template reads. `title`, `url` and `contentType` are the names the
 * server projects the base fields of every document under; the snippet has no base field, so it
 * falls back through the ones a content type usually carries.
 */
const TITLE_ATTRIBUTE = 'title';
const URL_ATTRIBUTE = 'url';
const SNIPPET_ATTRIBUTES = ['summary', 'content', 'excerpt'];

/** The three options the default item template reads. Shared with `loadMore`, which reuses it. */
export interface ResultItemOptions {
  titleAttribute?: string;
  urlAttribute?: string;
  snippetAttributes?: string[];
}

/** The `xps-result` block of `themes/fixtures/results.html`. Exported for `loadMore` to reuse. */
export function defaultResultItem<TAttributes extends Record<string, unknown>>(
  result: Result<TAttributes>,
  params: ResultItemOptions = {}
): Renderable {
  const attributes = result.attributes as Record<string, unknown>;
  const image = typeof attributes['image'] === 'string' ? attributes['image'] : '';
  const urlAttribute = params.urlAttribute ?? URL_ATTRIBUTE;
  const url = typeof attributes[urlAttribute] === 'string' ? attributes[urlAttribute] : '#';
  const type = typeof attributes['contentType'] === 'string' ? attributes['contentType'] : '';
  const title = params.titleAttribute ?? TITLE_ATTRIBUTE;
  const field = (params.snippetAttributes ?? SNIPPET_ATTRIBUTES).find(
    (name) => highlight(name, result).value !== ''
  );
  return html`<article class="xps-result">
    ${image
      ? html`<div class="xps-result__media"><img class="xps-result__image" src="${image}" alt="" width="96" height="96"></div>`
      : ''}
    <div class="xps-result__body">
      <h3 class="xps-result__title"><a class="xps-result__link" href="${url}">${highlight(title, result)}</a></h3>
      ${field ? html`<p class="xps-result__snippet">${highlight(field, result)}</p>` : ''}
      ${type
        ? html`<ul class="xps-result__meta"><li class="xps-result__meta-item">${type}</li></ul>`
        : ''}
    </div>
  </article>`;
}

const defaultEmpty = ({ query }: { query: string }): Renderable =>
  query === ''
    ? html`<p>No results.</p><p>Try a different search term, or clear some filters.</p>`
    : html`<p>No results for <strong>${query}</strong>.</p><p>Try fewer words, or clear some filters.</p>`;

const skeleton = (): Renderable =>
  html`<article class="xps-result xps-result--skeleton" aria-hidden="true">
    <div class="xps-result__media"><span class="xps-skeleton xps-skeleton--block"></span></div>
    <div class="xps-result__body">
      <span class="xps-skeleton xps-skeleton--title"></span>
      <span class="xps-skeleton xps-skeleton--text"></span>
    </div>
  </article>`;

const list = (items: Renderable[]): Renderable =>
  html`<ol class="xps-results__list">${items.map(
    (item) => html`<li class="xps-results__item">${item}</li>`
  )}</ol>`;

export function results<TAttributes extends Record<string, unknown> = Record<string, unknown>>(
  params: ResultsWidgetParams<TAttributes>
): Widget {
  const container = resolveContainer(params.container, 'results');
  let root: HTMLElement | undefined;
  let status: HTMLElement | undefined;
  let shown: Array<Result<TAttributes>> = [];
  let send: (result: Result<TAttributes>) => void = () => {};

  const widget = withResults<TAttributes, ResultsWidgetParams<TAttributes>>(
    (options, isFirstRender) => {
      const templates = options.params.templates ?? {};
      const rows = options.params.loadingRows ?? 3;
      shown = options.items;
      send = (result) => options.sendEvent('click', result);

      if (isFirstRender) {
        root = createRoot(container, 'div', 'xps xps-results');
        // The live region is created once and only ever has its text updated, so a re-render
        // that does not change the count is not announced again (spec 5.6).
        status = container.ownerDocument.createElement('p');
        status.className = 'xps-results__status xps-sr-only';
        status.setAttribute('role', 'status');
        root.appendChild(status);
        root.addEventListener('click', (event) => {
          const target = event.target;
          if (!(target instanceof Element)) return;
          const item = target.closest('a')?.closest('.xps-results__item');
          const parent = item?.parentElement;
          if (!item || !parent) return;
          const result = shown[Array.prototype.indexOf.call(parent.children, item)];
          if (result) send(result);
        });
      }
      if (!root || !status) return;

      const query = options.state.query;
      const busy = options.search.status === 'loading' || options.search.status === 'stalled';
      const first = busy && options.results === null;
      const empty = options.results !== null && options.items.length === 0;

      let body: Renderable;
      let announcement: string;
      if (first) {
        body = templates.loading
          ? templates.loading(helpers)
          : list(Array.from({ length: rows }, skeleton));
        announcement = 'Searching…';
      } else if (empty) {
        body = html`<div class="xps-results__empty">${
          templates.empty ? templates.empty({ query }, helpers) : defaultEmpty({ query })
        }</div>`;
        announcement = query === '' ? 'No results.' : `No results for “${query}”`;
      } else if (options.items.length > 0) {
        body = list(
          options.items.map((result) =>
            templates.item ? templates.item(result, helpers) : defaultResultItem(result, options.params)
          )
        );
        const count = helpers.formatNumber(options.results?.total ?? options.items.length);
        announcement = query === '' ? `${count} results` : `${count} results for “${query}”`;
      } else {
        body = '';
        announcement = '';
      }

      root.classList.toggle('xps-results--empty', empty);
      root.classList.toggle('xps-results--loading', busy);
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

  widget.$$type = 'results';
  return widget;
}
