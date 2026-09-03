/**
 * `results` — `withResults` plus the default renderer (spec 5.3), the results live region
 * (spec 5.6) and client-side click tracking (spec 9.1).
 * Markup: `themes/fixtures/results.html`.
 */
import {
  withResults,
  type ResultsBehaviorParams,
  type ResultsRenderState,
} from '../behaviors/results';
import {
  helpers,
  html,
  highlight,
  toHtml,
  type Renderable,
  type TemplateHelpers,
} from '../templates/html';
import type { RenderOptions, Result, Widget } from '../types';
import { createRoot, resolveContainer, setAttr } from './dom';

/** What `templates.empty` receives: the query, and whether filters are narrowing it. */
export interface EmptyTemplateData {
  query: string;
  /** Whether any facet or numeric filter is applied — the empty state's "or clear them" case. */
  hasRefinements: boolean;
  /** Clears every filter and searches. The same action `activeFilters`' Clear all uses. */
  clearRefinements(): void;
  /**
   * How many results the same query returns with no filters at all, from a debounced probe
   * (`SearchInstance.probe`). Only ever set while `hasRefinements`, and only once the probe has
   * answered with a number above zero: `undefined` covers "still in flight", "the probe failed"
   * and "there is nothing behind the filters either", all of which the countless copy is the
   * honest answer to.
   */
  unfilteredCount?: number;
  /**
   * A corrected spelling of the query that the server verified returns results (`SearchResponse
   * .didYouMean`). Absent unless the index has did-you-mean on and a correction was found.
   */
  didYouMean?: string;
  /**
   * The index's most-searched queries, most popular first (`SearchResponse.popularSearches`).
   * Absent unless the host opted the index in.
   */
  popularSearches?: string[];
}

/**
 * A template can make any element run a query by giving it this attribute: the widget's own click
 * handler reads it, so a custom empty state gets the same recovery clicks the default one has.
 */
export const RECOVER_ATTRIBUTE = 'data-xps-recover';

export interface ResultsTemplates<TAttributes extends Record<string, unknown>> {
  item?: (result: Result<TAttributes>, helpers: TemplateHelpers) => Renderable;
  empty?: (data: EmptyTemplateData, helpers: TemplateHelpers) => Renderable;
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
  /** Attribute the default template reads the breadcrumb path from. Defaults to `path`. */
  pathAttribute?: string;
};

/**
 * The attributes the default template reads. `title`, `url` and `contentType` are the names the
 * server projects the base fields of every document under; the snippet has no base field, so it
 * falls back through the ones a content type usually carries.
 */
const TITLE_ATTRIBUTE = 'title';
const URL_ATTRIBUTE = 'url';
const PATH_ATTRIBUTE = 'path';
const SNIPPET_ATTRIBUTES = ['summary', 'content', 'excerpt'];

/**
 * The document glyph the media slot falls back to for a `fileType` result with no image. Kept
 * byte-identical in `_Result.cshtml` and `ServerRenderedResults.DefaultCard`; `card-parity.test.ts`
 * fails if one of the three moves.
 */
const FILE_ICON =
  '<svg class="xps-result__icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M14 2H7a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7z"></path><path d="M14 2v5h5"></path></svg>';

/** The options the default item template reads. Shared with `loadMore`, which reuses it. */
export interface ResultItemOptions {
  titleAttribute?: string;
  urlAttribute?: string;
  snippetAttributes?: string[];
  pathAttribute?: string;
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
  const fileType = typeof attributes['fileType'] === 'string' ? attributes['fileType'] : '';
  const pathAttribute = params.pathAttribute ?? PATH_ATTRIBUTE;
  const path = typeof attributes[pathAttribute] === 'string' ? attributes[pathAttribute] : '';
  const title = params.titleAttribute ?? TITLE_ATTRIBUTE;
  const field = (params.snippetAttributes ?? SNIPPET_ATTRIBUTES).find(
    (name) => highlight(name, result).value !== ''
  );
  return html`<article class="xps-result">
    ${image
      ? html`<div class="xps-result__media"><img class="xps-result__image" src="${image}" alt="" width="96" height="96"></div>`
      : fileType
        ? html`<div class="xps-result__media">${html.raw(FILE_ICON)}</div>`
        : ''}
    <div class="xps-result__body">
      <h3 class="xps-result__title"><a class="xps-result__link" href="${url}">${highlight(title, result)}</a></h3>
      ${path ? html`<p class="xps-result__path">${path}</p>` : ''}
      ${field ? html`<p class="xps-result__snippet">${highlight(field, result)}</p>` : ''}
      ${type
        ? html`<ul class="xps-result__meta"><li class="xps-result__meta-item xps-result__type">${type}</li></ul>`
        : ''}
    </div>
  </article>`;
}

/** The button the empty state's Clear filters is delegated through (see the root click handler). */
export const CLEAR_CLASS = 'xps-results__clear';

/** How long a filtered empty state waits before probing for the unfiltered count. */
const PROBE_DEBOUNCE_MS = 250;

/** The bit of `SearchInstance` the probe needs — anything that can ask for a count. */
type Prober = { probe(overrides: { filters: undefined }): Promise<{ total: number }> };

/**
 * The filtered empty state's "there are N results without them" count, shared by `results` and by
 * `loadMore` (TH-7): one debounced unfiltered probe per query, its answer remembered.
 *
 * `probedQuery` is the query the count belongs to and the staleness test with it: the unfiltered
 * total depends on the query and on nothing else, so an answer for a query that is no longer the
 * current one is thrown away, and a query already probed is never probed again.
 */
export function createUnfilteredProbe(): {
  count(search: Prober, query: string, repaint: () => void): number | undefined;
  dispose(): void;
} {
  let probedQuery: string | undefined;
  let probedCount: number | undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;

  return {
    count(search, query, repaint) {
      if (probedQuery !== query) {
        probedQuery = query;
        probedCount = undefined;
        if (timer !== undefined) clearTimeout(timer);
        timer = setTimeout(() => {
          search.probe({ filters: undefined }).then(({ total }) => {
            if (probedQuery !== query) return; // stale: the query moved on while the probe ran
            // Zero is not worth saying — "clearing shows 0 results" is not a recovery. The
            // countless copy stands, and so it does when the probe fails.
            if (total > 0) {
              probedCount = total;
              repaint();
            }
          }, () => {});
        }, PROBE_DEBOUNCE_MS);
      }
      return probedQuery === query ? probedCount : undefined;
    },
    dispose() {
      if (timer !== undefined) clearTimeout(timer);
      probedQuery = undefined;
      probedCount = undefined;
    },
  };
}

/**
 * The magnifier-with-minus above the empty-state copy: nothing found, on the 24px grid, in
 * `currentColor` so a re-skin needs no new asset.
 */
const EMPTY_ICON =
  '<svg class="xps-results__empty-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="11" cy="11" r="7"></circle><path d="M8 11h6"></path><path d="m20 20-4.35-4.35"></path></svg>';

/** "1 result" / "7 results" — the count and its noun, as both the copy and the button read it. */
const resultCount = (total: number): string =>
  `${helpers.formatNumber(total)} result${total === 1 ? '' : 's'}`;

/** The two ways out of a dead end the server offers (SG-1); both render only when it offered them. */
const recovery = ({ didYouMean, popularSearches }: EmptyTemplateData): Renderable =>
  html`${
    didYouMean === undefined
      ? ''
      : html`<p class="xps-results__did-you-mean">Did you mean <button class="xps-button xps-button--link xps-results__correction" type="button" data-xps-recover="${didYouMean}"><strong>${didYouMean}</strong></button>?</p>`
  }${
    popularSearches === undefined || popularSearches.length === 0
      ? ''
      : html`<div class="xps-results__popular">
      <p class="xps-results__popular-title">Popular searches</p>
      <ul class="xps-results__popular-list">${popularSearches.map(
        (search) => html`<li class="xps-results__popular-item"><button class="xps-button xps-chip xps-results__popular-button" type="button" data-xps-recover="${search}">${search}</button></li>`
      )}</ul>
    </div>`
  }`;

/** The headline of the empty state, in the four shapes the two variants take (design board). */
const headline = (query: string, withFilters: boolean): Renderable =>
  html`<p class="xps-results__empty-title">${
    query === ''
      ? withFilters
        ? 'No results with these filters.'
        : 'No results.'
      : html`No results for &ldquo;${query}&rdquo;${withFilters ? ' with these filters' : ''}`
  }</p>`;

/**
 * The default empty state, shared by `results` and by `loadMore`. Kept in step with
 * `ServerRenderedResults`' first paint (`card-parity.test.ts`).
 */
export const defaultEmpty = (data: EmptyTemplateData): Renderable => {
  const { query, hasRefinements, unfilteredCount } = data;
  const icon = html.raw(EMPTY_ICON);
  const recover = recovery(data);
  if (hasRefinements) {
    const counted = unfilteredCount !== undefined && unfilteredCount > 0;
    const clear = html`<button class="xps-button xps-button--primary ${CLEAR_CLASS}" type="button">${
      counted ? `Clear filters and show ${resultCount(unfilteredCount)}` : 'Clear filters'
    }</button>`;
    const without = counted
      ? html`<p>There are <strong>${resultCount(unfilteredCount)}</strong> without them.</p>`
      : '';
    return html`${icon}${headline(query, true)}${without}${clear}${recover}`;
  }
  return html`${icon}${headline(query, false)}${recover}`;
};

/** The empty state in its block, which is what both widgets put in the DOM. */
export const emptyState = (data: EmptyTemplateData): Renderable =>
  html`<div class="xps-results__empty">${defaultEmpty(data)}</div>`;

const skeleton = (): Renderable =>
  html`<article class="xps-result xps-result--skeleton" aria-hidden="true">
    <div class="xps-result__media"><span class="xps-skeleton xps-skeleton--block"></span></div>
    <div class="xps-result__body">
      <span class="xps-skeleton xps-skeleton--title"></span>
      <span class="xps-skeleton xps-skeleton--text"></span>
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
  let clearRefinements: () => void = () => {};
  /** Runs a query the empty state offered as a way out: the correction, or a popular search. */
  let recover: (query: string) => void = () => {};

  /** The unfiltered count of the filtered empty state (ES-1), shared with `loadMore`. */
  const probe = createUnfilteredProbe();

  type PaintOptions = ResultsRenderState<TAttributes> &
    RenderOptions<ResultsWidgetParams<TAttributes> & ResultsBehaviorParams<TAttributes>>;

  const paint = (options: PaintOptions, isFirstRender: boolean): void => {
    const templates = options.params.templates ?? {};
    const rows = options.params.loadingRows ?? 3;
    shown = options.items;
    send = (result) => options.sendEvent('click', result);
    clearRefinements = () => {
      options.actions.clearFilters().search();
    };
    recover = (query) => {
      options.actions.setQuery(query).search();
    };

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
        if (target.closest(`.${CLEAR_CLASS}`)) {
          clearRefinements();
          return;
        }
        const offer = target.closest<HTMLElement>(`[${RECOVER_ATTRIBUTE}]`);
        if (offer) {
          recover(offer.dataset['xpsRecover'] ?? '');
          return;
        }
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
      const filters = options.state.filters;
      const hasRefinements =
        filters.numeric.length > 0 || filters.facets.some((facet) => facet.values.length > 0);
      const counted = hasRefinements
        ? probe.count(options.search, query, () => paint(options, false))
        : undefined;
      const data: EmptyTemplateData = {
        query,
        hasRefinements,
        clearRefinements,
        ...(counted === undefined ? {} : { unfilteredCount: counted }),
        ...(options.results?.didYouMean === undefined ? {} : { didYouMean: options.results.didYouMean }),
        ...(options.results?.popularSearches === undefined
          ? {}
          : { popularSearches: options.results.popularSearches }),
      };
      body = templates.empty
        ? html`<div class="xps-results__empty">${templates.empty(data, helpers)}</div>`
        : emptyState(data);
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
  };

  const widget = withResults<TAttributes, ResultsWidgetParams<TAttributes>>(paint, () => {
    probe.dispose();
    container.textContent = '';
  })(params);

  widget.$$type = 'results';
  return widget;
}
