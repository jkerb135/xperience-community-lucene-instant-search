/**
 * `categoryTree` — `withCategoryTree` plus the default renderer (spec 5.3).
 * Markup: `themes/fixtures/category-tree.html`. A11y (spec 5.6): a labelled `<nav>` of nested
 * lists, `aria-current="true"` on every node of the open path, and a value nobody can reach as
 * `<span aria-disabled="true">` rather than a dead link.
 *
 * Every enabled node is a real link carrying `urlFor(value)`, so the filtered pages are
 * crawlable and open in a new tab; the click handler intercepts the plain-left-click case.
 */
import { withCategoryTree, type CategoryTreeItem } from '../behaviors/categoryTree';
import { html, type Renderable } from '../templates/html';
import type { Widget } from '../types';
import { createRoot, renderKeepingFocus, resolveContainer } from './dom';

export type CategoryTreeWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** Nodes shown per level, most documents first. Defaults to 10. */
  limit?: number;
  /** Heading and `aria-label` text. Defaults to `attribute`. */
  label?: string;
};

const node = (item: CategoryTreeItem, level: number, urlFor: (value: string) => string): Renderable => {
  const disabled = item.count === 0 && !item.isActive;
  const modifiers = `${item.children.length > 0 ? ' xps-category-tree__item--parent' : ''}${
    item.isActive ? ' xps-category-tree__item--selected' : ''
  }${disabled ? ' xps-category-tree__item--disabled' : ''}`;
  const body = html`<span class="xps-category-tree__value">${item.label}</span>
        <span class="xps-category-tree__count">${item.count}</span>`;

  return html`<li class="xps-category-tree__item${modifiers}">${
    disabled
      ? html`<span class="xps-category-tree__link" aria-disabled="true">${body}</span>`
      : html`<a class="xps-category-tree__link" href="${urlFor(item.value)}" data-xps-value="${item.value}"${
          item.isActive ? html.raw(' aria-current="true"') : ''
        }>${body}</a>`
  }${item.children.length > 0 ? list(item.children, level + 1, urlFor) : ''}</li>`;
};

const list = (
  items: CategoryTreeItem[],
  level: number,
  urlFor: (value: string) => string
): Renderable =>
  html`<ul class="xps-category-tree__list xps-category-tree__list--lvl${level}">${items.map(
    (item) => node(item, level, urlFor)
  )}</ul>`;

export function categoryTree(params: CategoryTreeWidgetParams): Widget {
  const container = resolveContainer(params.container, 'categoryTree');
  let root: HTMLElement | undefined;
  let apply: (value: string) => void = () => {};

  const widget = withCategoryTree<CategoryTreeWidgetParams>(
    (options, isFirstRender) => {
      const { attribute, label = attribute } = options.params;
      apply = options.apply;

      if (isFirstRender) {
        root = createRoot(container, 'nav', 'xps xps-category-tree');
        root.addEventListener('click', (event) => {
          if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey) return;
          const target = event.target;
          if (!(target instanceof Element)) return;
          const value = target.closest<HTMLElement>('a[data-xps-value]')?.dataset['xpsValue'];
          if (value === undefined) return;
          event.preventDefault();
          apply(value);
        });
      }
      if (!root) return;

      root.setAttribute('aria-label', label);
      // Nothing to navigate is nothing to render: hide the control rather than leave a bare
      // heading behind (MARKUP.md rule 3).
      root.hidden = !options.canApply;
      renderKeepingFocus(
        html`<h3 class="xps-category-tree__title">${label}</h3>
  ${list(options.items, 0, options.urlFor)}`,
        root
      );
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'categoryTree';
  return widget;
}
