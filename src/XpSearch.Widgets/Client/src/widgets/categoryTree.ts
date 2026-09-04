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
import { attributeLabelOrWarn, declareAttribute, UNNAMED_GROUP } from '../labels';
import { html, type Renderable } from '../templates/html';
import type { Widget } from '../types';
import { chevron, createRoot, renderKeepingFocus, resolveContainer, widgetId } from './dom';

export type CategoryTreeWidgetParams = {
  container: string | HTMLElement;
  attribute: string;
  /** Nodes shown per level, most documents first. Defaults to 10. */
  limit?: number;
  /** Heading and `aria-label` text. Defaults to `attribute`. */
  label?: string;
  /**
   * The title is a disclosure button that folds the tree away. On by default; pass `false` for a
   * tree that is always open. Collapsed state is local to the render — never persisted, never in
   * the URL.
   */
  collapsible?: boolean;
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
  // Local to this render: a collapsed group is a viewing preference, not search state.
  let collapsed = false;

  const widget = withCategoryTree<CategoryTreeWidgetParams>(
    (options, isFirstRender) => {
      const { attribute, collapsible = true } = options.params;
      const bodyId = widgetId(container, attribute, 'body');
      apply = options.apply;
      // This widget owns the attribute, so its heading is what every other widget calls it (TH-12).
      declareAttribute(options.search, attribute, { label: options.params.label });
      const label =
        attributeLabelOrWarn(options.search, attribute, 'categoryTree', options.params.label) ??
        UNNAMED_GROUP;

      if (isFirstRender) {
        root = createRoot(container, 'nav', 'xps xps-category-tree');
        root.addEventListener('click', (event) => {
          if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey) return;
          const target = event.target;
          if (!(target instanceof Element)) return;
          const toggle = target.closest<HTMLButtonElement>('.xps-category-tree__toggle');
          if (toggle) {
            collapsed = !collapsed;
            toggle.setAttribute('aria-expanded', String(!collapsed));
            const body = root?.querySelector<HTMLElement>('.xps-category-tree__body');
            if (body) body.hidden = collapsed;
            return;
          }
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
        html`<h3 class="xps-category-tree__title">${
          collapsible
            ? html`<button class="xps-category-tree__toggle" type="button" aria-expanded="${String(!collapsed)}" aria-controls="${bodyId}"><span class="xps-category-tree__toggle-label">${label}</span>${chevron('xps-category-tree__chevron')}</button>`
            : label
        }</h3>
  <div class="xps-category-tree__body" id="${bodyId}">${list(options.items, 0, options.urlFor)}</div>`,
        root
      );
      // The body is rebuilt on every render, so the collapsed state is re-applied here.
      const body = root.querySelector<HTMLElement>('.xps-category-tree__body');
      if (body) body.hidden = collapsible && collapsed;
    },
    () => {
      container.textContent = '';
    }
  )(params);

  widget.$$type = 'categoryTree';
  return widget;
}
