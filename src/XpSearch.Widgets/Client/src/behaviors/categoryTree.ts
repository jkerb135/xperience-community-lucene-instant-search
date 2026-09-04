import { valueLabel } from '../labels';
import { clearFilters, facetValues, toggleFacet } from '../state';
import type { RenderOptions, SearchState, WidgetFactory } from '../types';
import { createBehavior, withFacetAttribute } from './internal';

/** One node of the tree. `children` is empty for a leaf. */
export interface CategoryTreeItem {
  /** The value to send back in `filters.facets`: the tag code name. */
  value: string;
  /** The text to display: the taxonomy tag title. */
  label: string;
  /** Documents carrying this value, descendants included — the server rolls the counts up. */
  count: number;
  /** The node's ancestors, root first, excluding the node itself. Empty at the root. */
  path: string[];
  /** True on the selected node and on every ancestor of it: the open path. */
  isActive: boolean;
  children: CategoryTreeItem[];
}

export interface CategoryTreeBehaviorParams {
  attribute: string;
  /** Nodes kept per level, most documents first. Defaults to 10. */
  limit?: number;
}

export interface CategoryTreeRenderState {
  /** The root nodes; every node carries its own children. */
  items: CategoryTreeItem[];
  /** The selected value, or `undefined` when the whole tree is open. */
  selected: string | undefined;
  /** Selects `value`, or clears the attribute when `value` is already selected. */
  apply(value: string): void;
  urlFor(value: string): string;
  /** True on the selected node and on every ancestor of it. */
  isActive(value: string): boolean;
  /** ARIA scaffolding (spec 5.7): false when there is nothing to navigate. */
  canApply: boolean;
}

const DEFAULT_LIMIT = 10;

/**
 * Taxonomy navigation (spec 5.7), over `FacetValue.path` (ADR-0018).
 *
 * The tree is assembled from the facet values alone: each value names its ancestors, and the
 * contract guarantees every ancestor named is itself one of the values. Selection is
 * single-value — a node replaces the attribute's filter rather than adding to it — because the
 * server rolls counts up, so selecting a parent already includes everything below it.
 */
export function withCategoryTree<
  TParams extends Record<string, unknown> = Record<string, unknown>,
>(
  renderFn: (
    renderOptions: CategoryTreeRenderState & RenderOptions<TParams & CategoryTreeBehaviorParams>,
    isFirstRender: boolean
  ) => void,
  unmountFn?: () => void
): WidgetFactory<TParams & CategoryTreeBehaviorParams> {
  return createBehavior<TParams & CategoryTreeBehaviorParams, CategoryTreeRenderState, never>({
    $$type: 'xps.categoryTree',
    routable: 'facet',
    prepareRequest: (request, params) => withFacetAttribute(request, params.attribute),
    getRenderState(base, params) {
      const values = base.results?.facets?.[params.attribute] ?? [];
      const selected = facetValues(base.state, params.attribute)[0];
      const open = openPath(values, selected);
      const nodes = values.map((value) => ({
        value: value.value,
        label: value.label,
        count: value.count,
        path: [...(value.path ?? [])],
        isActive: open.has(value.value),
        children: [] as CategoryTreeItem[],
      }));
      // A category that narrows the search to nothing is not carried by the response any more.
      // Keep it, at the root, with its zero: selecting it again is the way back out, and without
      // it the tree would be empty and the visitor stuck with Clear all (TH-7).
      if (selected !== undefined && !nodes.some((node) => node.value === selected)) {
        nodes.unshift({
          value: selected,
          // Named by the label memory, never by the stored code (TH-12).
          label: valueLabel(base.search, params.attribute, selected) ?? selected,
          count: 0,
          path: [],
          isActive: true,
          children: [],
        });
      }
      const items = build(nodes, params.limit ?? DEFAULT_LIMIT);

      // Selecting the open node again closes it: with one value at a time there is no other way
      // back to "all categories" from inside the tree.
      const next = (value: string): SearchState =>
        value === selected
          ? clearFilters(base.state, params.attribute)
          : toggleFacet(clearFilters(base.state, params.attribute), params.attribute, value);

      return {
        items,
        selected,
        canApply: items.length > 0,
        isActive: (value) => open.has(value),
        apply(value) {
          const actions = base.actions.clearFilters(params.attribute);
          (value === selected ? actions : actions.toggleFacet(params.attribute, value)).search();
        },
        urlFor: (value) => base.search.urlFor(next(value)),
      };
    },
  })(renderFn, unmountFn);
}

/** The selected value and its ancestors — the nodes a renderer marks `aria-current`. */
function openPath(
  values: readonly { value: string; path?: string[] }[],
  selected: string | undefined
): Set<string> {
  if (selected === undefined) return new Set();
  const node = values.find((value) => value.value === selected);
  return new Set([...(node?.path ?? []), selected]);
}

/** Hangs every node off its parent (the last entry of its path) and caps each level. */
function build(nodes: CategoryTreeItem[], limit: number): CategoryTreeItem[] {
  const byValue = new Map(nodes.map((node) => [node.value, node]));
  const roots: CategoryTreeItem[] = [];

  for (const node of nodes) {
    const parent = byValue.get(node.path[node.path.length - 1] ?? '');
    // A node whose parent is missing is shown at the root rather than dropped: the contract
    // promises the ancestor is there, and a stale index is no reason to lose a category.
    (parent ? parent.children : roots).push(node);
  }

  const cap = (level: CategoryTreeItem[]): CategoryTreeItem[] => {
    level.sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
    const kept = level.slice(0, Math.max(0, limit));
    for (const node of kept) node.children = cap(node.children);
    return kept;
  };

  return cap(roots);
}
