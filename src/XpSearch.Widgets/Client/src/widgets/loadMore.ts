/**
 * `loadMore` — `withLoadMore` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/load-more.html`. The items are the same `xps-result` template the
 * `results` widget renders.
 *
 * A11y (spec 5.6): the `<ol>` is **appended to, never rebuilt**, so scroll position and focus
 * survive a load; a live region announces how much of the list is on screen; and the button is
 * the keyboard path, always present and only ever `disabled` when there is nothing left to load.
 * The `IntersectionObserver` sentinel is the scroll path, an addition to that button and never a
 * replacement for it — where the API is missing (older browsers, jsdom) it is simply not used.
 *
 * Do not place `loadMore` and `pagination` in one instance: both own `state.page`, and paging
 * backwards throws the accumulated list away.
 */
import {
  withLoadMore,
  type LoadMoreBehaviorParams,
  type LoadMoreRenderState,
} from '../behaviors/loadMore';
import { helpers, html, toHtml, type Renderable, type TemplateHelpers } from '../templates/html';
import type { RenderOptions, Result, Widget } from '../types';
import { createRoot, resolveContainer } from './dom';
import {
  CLEAR_CLASS,
  RECOVER_ATTRIBUTE,
  createUnfilteredProbe,
  defaultResultItem,
  emptyState,
  type EmptyTemplateData,
} from './results';

export type LoadMoreWidgetParams<
  TAttributes extends Record<string, unknown> = Record<string, unknown>,
> = {
  container: string | HTMLElement;
  templates?: { item?: (result: Result<TAttributes>, helpers: TemplateHelpers) => Renderable };
  /** Client-side massaging escape hatch (spec 5.2). Applied per page, before accumulating. */
  transformItems?: (items: Array<Result<TAttributes>>) => Array<Result<TAttributes>>;
  /** Defaults to `true`. Loads the next page when the sentinel scrolls into view. */
  autoLoad?: boolean;
  /** Attribute the default template reads the title from. Defaults to `title`. */
  titleAttribute?: string;
  /** Attribute the default template reads the link from. Defaults to `url`. */
  urlAttribute?: string;
  /** Attributes the default template tries, in order, for the snippet. */
  snippetAttributes?: string[];
  /** Button text, loadable and exhausted. */
  labels?: { more?: string; exhausted?: string };
};

export function loadMore<TAttributes extends Record<string, unknown> = Record<string, unknown>>(
  params: LoadMoreWidgetParams<TAttributes>
): Widget {
  const container = resolveContainer(params.container, 'loadMore');
  let root: HTMLElement | undefined;
  let status: HTMLElement | undefined;
  let list: HTMLElement | undefined;
  let button: HTMLButtonElement | undefined;
  let observer: IntersectionObserver | undefined;
  /** What is already in the `<ol>`: the generation it belongs to and how much of it is painted. */
  let painted = { generation: -1, count: 0 };
  let shown: Array<Result<TAttributes>> = [];
  let load: () => void = () => {};
  let send: (result: Result<TAttributes>) => void = () => {};
  let clearRefinements: () => void = () => {};
  let recover: (query: string) => void = () => {};
  /** The empty state currently in the DOM, if any. Rebuilt whenever the render state changes. */
  let empty: HTMLElement | undefined;
  /** The same unfiltered probe the `results` widget counts its empty state with (TH-7). */
  const probe = createUnfilteredProbe();
  /** Re-runs the last render when only the probe's answer changed. */
  let repaint: () => void = () => {};

  type DrawOptions = LoadMoreRenderState<TAttributes> &
    RenderOptions<LoadMoreWidgetParams<TAttributes> & LoadMoreBehaviorParams<TAttributes>>;

  const draw = (options: DrawOptions, isFirstRender: boolean): void => {
    const { templates, labels, autoLoad = true } = options.params;
    repaint = () => draw(options, false);
    load = options.loadMore;
    shown = options.items;
    send = (result) => options.sendEvent('click', result);
    clearRefinements = () => {
      options.actions.clearFilters().search();
    };
    recover = (query) => {
      options.actions.setQuery(query).search();
    };

    if (isFirstRender) {
      root = createRoot(container, 'div', 'xps xps-load-more');
      root.insertAdjacentHTML(
        'beforeend',
        toHtml(html`<p class="xps-load-more__status xps-sr-only" role="status"></p>
  <ol class="xps-load-more__list"></ol>
  <div class="xps-load-more__sentinel" aria-hidden="true"></div>
  <button class="xps-button xps-load-more__load-more" type="button"></button>`)
      );
      status = root.querySelector<HTMLElement>('.xps-load-more__status') ?? undefined;
      list = root.querySelector<HTMLElement>('.xps-load-more__list') ?? undefined;
      button = root.querySelector<HTMLButtonElement>('.xps-load-more__load-more') ?? undefined;
      button?.addEventListener('click', () => load());
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
        const item = target.closest('a')?.closest('.xps-load-more__item');
        const parent = item?.parentElement;
        if (!item || !parent) return;
        const result = shown[Array.prototype.indexOf.call(parent.children, item)];
        if (result) send(result);
      });

      const sentinel = root.querySelector('.xps-load-more__sentinel');
      if (autoLoad && sentinel && typeof IntersectionObserver !== 'undefined') {
        observer = new IntersectionObserver((entries) => {
          if (entries.some((entry) => entry.isIntersecting)) load();
        });
        observer.observe(sentinel);
      }
    }
    if (!root || !status || !list || !button) return;

    if (painted.generation !== options.generation) {
      list.textContent = '';
      painted = { generation: options.generation, count: 0 };
    }
    const added = options.items.slice(painted.count);
    if (added.length > 0) {
      list.insertAdjacentHTML(
        'beforeend',
        toHtml(
          added.map(
            (result) =>
              html`<li class="xps-load-more__item">${
                templates?.item
                  ? templates.item(result, helpers)
                  : defaultResultItem(result, options.params)
              }</li>`
          )
        )
      );
      painted.count = options.items.length;
    }

    const count = helpers.formatNumber(options.items.length);
    const announcement =
      options.total === 0
        ? 'No results'
        : options.isExhausted
          ? `Showing all ${count} results`
          : `Showing ${count} of ${helpers.formatNumber(options.total)} results`;
    if (status.textContent !== announcement) status.textContent = announcement;

    // MB-1's mount-time swap puts `loadMore` where `results` would be, so it owes the same empty
    // state (TH-6): the same render, the same delegated recovery clicks, and since TH-7 the same
    // unfiltered probe behind "There are N results without them".
    const isEmpty = options.results !== null && options.items.length === 0;
    empty?.remove();
    empty = undefined;
    if (isEmpty) {
      const filters = options.state.filters;
      const hasRefinements =
        filters.numeric.length > 0 || filters.facets.some((facet) => facet.values.length > 0);
      const counted = hasRefinements
        ? probe.count(options.search, options.state.query, () => repaint())
        : undefined;
      const data: EmptyTemplateData = {
        query: options.state.query,
        hasRefinements,
        clearRefinements,
        ...(counted === undefined ? {} : { unfilteredCount: counted }),
        ...(options.results?.didYouMean === undefined
          ? {}
          : { didYouMean: options.results.didYouMean }),
        ...(options.results?.popularSearches === undefined
          ? {}
          : { popularSearches: options.results.popularSearches }),
      };
      list.insertAdjacentHTML('afterend', toHtml(emptyState(data)));
      empty = root.querySelector<HTMLElement>('.xps-results__empty') ?? undefined;
    }
    // Nothing to load and nothing to say about it: the control would only offer a dead end.
    button.hidden = isEmpty;

    root.classList.toggle('xps-load-more--exhausted', options.isExhausted);
    button.disabled = options.isExhausted;
    const text = options.isExhausted
      ? (labels?.exhausted ?? 'No more results')
      : (labels?.more ?? 'Load more results');
    if (button.textContent !== text) button.textContent = text;
  };

  const widget = withLoadMore<TAttributes, LoadMoreWidgetParams<TAttributes>>(
    (options, isFirstRender) => draw(options, isFirstRender),
    () => {
      observer?.disconnect();
      probe.dispose();
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'loadMore';
  return widget;
}
