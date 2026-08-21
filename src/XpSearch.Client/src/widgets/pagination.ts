/**
 * `pagination` — `withPagination` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/pagination.html`. A11y (spec 5.6): a labelled `<nav>`,
 * `aria-current="page"`, and disabled ends as `<span aria-disabled="true">` so a keyboard user
 * never lands on a control that does nothing.
 *
 * Every enabled control is a real link carrying `urlFor(page)`, so the result pages are
 * crawlable and open in a new tab; the click handler intercepts the plain-left-click case.
 */
import { withPagination } from '../behaviors/pagination';
import { html, type Renderable } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, renderKeepingFocus, resolveContainer } from './dom';

export type PaginationWidgetParams = {
  container: string | HTMLElement;
  /** Pages shown either side of the current one. Defaults to 3. */
  padding?: number;
  /** Caps the number of pages offered, for indexes where deep paging is not wanted. */
  maxPages?: number;
  /** Defaults to `true`. */
  showFirst?: boolean;
  /** Defaults to `true`. */
  showLast?: boolean;
  /** Overrides the screen-reader names of the four end controls. */
  labels?: { first?: string; previous?: string; next?: string; last?: string };
};

const GLYPHS = { first: '«', previous: '‹', next: '›', last: '»' };
const NAMES = {
  first: 'First page',
  previous: 'Previous page',
  next: 'Next page',
  last: 'Last page',
};

type End = keyof typeof GLYPHS;

const control = (
  kind: End,
  page: number,
  disabled: boolean,
  name: string,
  urlFor: (page: number) => string
): Renderable => {
  const body = html`<span aria-hidden="true">${GLYPHS[kind]}</span><span class="xps-sr-only">${name}</span>`;
  return html`<li class="xps-pagination__item xps-pagination__item--${kind}${
    disabled ? ' xps-pagination__item--disabled' : ''
  }">${
    disabled
      ? html`<span class="xps-pagination__link" aria-disabled="true">${body}</span>`
      : html`<a class="xps-pagination__link" href="${urlFor(page)}" data-xps-page="${page}">${body}</a>`
  }</li>`;
};

const pageItem = (
  page: number,
  current: boolean,
  urlFor: (page: number) => string
): Renderable =>
  html`<li class="xps-pagination__item xps-pagination__item--page${
    current ? ' xps-pagination__item--current' : ''
  }"><a class="xps-pagination__link" href="${urlFor(page)}" data-xps-page="${page}"${
    current ? html.raw(' aria-current="page"') : ''
  }><span class="xps-sr-only">Page </span>${page}</a></li>`;

const ellipsis = (): Renderable =>
  html`<li class="xps-pagination__item xps-pagination__item--ellipsis"><span class="xps-pagination__ellipsis" aria-hidden="true">…</span></li>`;

export function pagination(params: PaginationWidgetParams): Widget {
  const container = resolveContainer(params.container, 'pagination');
  let root: HTMLElement | undefined;
  let apply: (page: number) => void = () => {};

  const widget = withPagination<PaginationWidgetParams>(
    (options, isFirstRender) => {
      const { showFirst = true, showLast = true, labels } = options.params;
      apply = options.apply;

      if (isFirstRender) {
        root = createRoot(container, 'nav', 'xps xps-pagination');
        root.setAttribute('aria-label', 'Search results pages');
        root.addEventListener('click', (event) => {
          if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey) return;
          const target = event.target;
          if (!(target instanceof Element)) return;
          const page = target.closest<HTMLElement>('a[data-xps-page]')?.dataset['xpsPage'];
          if (page === undefined) return;
          event.preventDefault();
          apply(Number(page));
        });
      }
      if (!root) return;

      const { pages, current, totalPages, urlFor } = options;
      const first = pages[0] ?? 0;
      const last = pages[pages.length - 1] ?? 0;
      const items: Renderable[] = [];
      const name = (kind: End): string => labels?.[kind] ?? NAMES[kind];

      if (showFirst) items.push(control('first', 1, options.isFirstPage, name('first'), urlFor));
      items.push(
        control(
          'previous',
          Math.max(1, current - 1),
          options.isFirstPage,
          name('previous'),
          urlFor
        )
      );
      if (first > 1) {
        items.push(pageItem(1, false, urlFor));
        if (first > 2) items.push(ellipsis());
      }
      for (const page of pages) items.push(pageItem(page, page === current, urlFor));
      if (last < totalPages) {
        if (last < totalPages - 1) items.push(ellipsis());
        items.push(pageItem(totalPages, false, urlFor));
      }
      items.push(
        control(
          'next',
          Math.min(totalPages, current + 1),
          options.isLastPage,
          name('next'),
          urlFor
        )
      );
      if (showLast) {
        items.push(
          control('last', Math.max(1, totalPages), options.isLastPage, name('last'), urlFor)
        );
      }

      // One page (or none) is nothing to navigate: hide the whole control rather than render a
      // row of dead ends (MARKUP.md rule 3).
      root.hidden = !options.canApply;
      renderKeepingFocus(html`<ul class="xps-pagination__list">${items}</ul>`, root);
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'pagination';
  return widget;
}
