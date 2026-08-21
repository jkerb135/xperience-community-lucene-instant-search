/**
 * The `.xps-mount` bootstrap (spec 7.1) and the custom-widget registry (spec 5.7).
 *
 * Markup a Page Builder widget emits:
 *   <div class="xps-mount"
 *        data-xps-widget="refinementList"
 *        data-xps-instance="search-1"
 *        data-xps-instance-config='{"index":"site-content","routing":true}'
 *        data-xps-config='{"attribute":"contentType","limit":10}'></div>
 *
 * Mounts are grouped by `data-xps-instance` (default `"default"`), one `xpsearch()` per group.
 * Nothing here throws: a misconfigured mount is a console error and a skipped widget.
 */
import { xpsearch } from './instance';
import type { InstantSearch, Widget, XpSearchOptions } from './types';
import { DEFAULT_WIDGETS } from './widgets';

/** Config from `data-xps-config`, plus the mount element the widget renders into. */
export type MountConfig = Record<string, unknown> & { container: HTMLElement };

export type MountWidgetFactory = (config: MountConfig) => Widget;

/**
 * Bare identifiers reserved for the widgets shipped with the library; everything else must be
 * namespaced with a dot. Registering one of these replaces the built-in of the same name.
 */
export const FIRST_PARTY_WIDGET_TYPES: readonly string[] = [
  'searchBox',
  'hits',
  'refinementList',
  'pagination',
  'stats',
  'sortBy',
  'autocomplete',
  'clearRefinements',
  'currentRefinements',
  'rangeSlider',
  'hierarchicalMenu',
  'infiniteHits',
  'toggleRefinement',
];

const registry = new Map<string, MountWidgetFactory>();

/**
 * Makes a widget factory resolvable by `data-xps-widget` (spec 5.7).
 * Third-party identifiers must contain a dot: `myCompany.ratingFilter`.
 */
export function registerWidgetType(id: string, factory: MountWidgetFactory): void {
  if (typeof id !== 'string' || id.length === 0) {
    throw new Error('registerWidgetType(id, factory): id must be a non-empty string.');
  }
  if (!id.includes('.') && !FIRST_PARTY_WIDGET_TYPES.includes(id)) {
    throw new Error(
      `registerWidgetType("${id}"): custom widget type identifiers must contain a dot, e.g. "myCompany.${id}". Bare names are reserved for the widgets shipped with @yourco/xperience-search.`
    );
  }
  if (typeof factory !== 'function') {
    throw new Error(`registerWidgetType("${id}", factory): factory must be a function.`);
  }
  registry.set(id, factory);
}

/** The factory that `id` resolves to — a registered one, or the built-in of that name. */
export function getWidgetType(id: string): MountWidgetFactory | undefined {
  return registry.get(id) ?? DEFAULT_WIDGETS[id];
}

const MOUNTED = 'xpsMounted';

/**
 * Scans `root` for `.xps-mount` elements and starts one search instance per
 * `data-xps-instance` group. Already-mounted elements are skipped, so calling it again after
 * inserting markup mounts only what is new.
 */
export function mountAll(root: ParentNode | undefined = globalThis.document): InstantSearch[] {
  if (!root) return [];
  const groups = new Map<string, HTMLElement[]>();
  for (const element of root.querySelectorAll<HTMLElement>('.xps-mount')) {
    if (element.dataset[MOUNTED] === 'true') continue;
    const id = element.dataset['xpsInstance'] || 'default';
    const group = groups.get(id) ?? [];
    group.push(element);
    groups.set(id, group);
  }

  const instances: InstantSearch[] = [];
  for (const [id, elements] of groups) {
    const options = readInstanceOptions(id, elements);
    if (!options) continue;
    const widgets: Widget[] = [];
    for (const element of elements) {
      const widget = buildWidget(element);
      if (widget) {
        widgets.push(widget);
        element.dataset[MOUNTED] = 'true';
      }
    }
    const instance = xpsearch(options);
    instance.addWidgets(widgets);
    instance.start();
    instances.push(instance);
  }
  return instances;
}

function readInstanceOptions(id: string, elements: HTMLElement[]): XpSearchOptions | undefined {
  // Instance options come from `data-xps-instance-config` on any mount in the group; the first
  // one that parses wins, so the Page Builder widgets can all emit it without agreeing on order.
  for (const element of elements) {
    const raw = element.dataset['xpsInstanceConfig'];
    if (!raw) continue;
    const parsed = parseJson(raw, element, 'data-xps-instance-config');
    if (parsed && typeof parsed['index'] === 'string' && parsed['index'] !== '') {
      return parsed as unknown as XpSearchOptions;
    }
  }
  console.error(
    `[xpsearch] mount group "${id}" has no usable data-xps-instance-config with an "index"; skipping ${elements.length} mount(s).`
  );
  return undefined;
}

function buildWidget(element: HTMLElement): Widget | undefined {
  const type = element.dataset['xpsWidget'];
  if (!type) {
    console.error('[xpsearch] .xps-mount element without data-xps-widget; skipping.', element);
    return undefined;
  }
  // A registered factory wins over the built-in of the same name, which is what makes
  // `registerWidgetType('hits', …)` a supported override rather than a collision.
  const factory = registry.get(type) ?? DEFAULT_WIDGETS[type];
  if (!factory) {
    console.error(
      `[xpsearch] unknown widget type "${type}"; skipping. Register it with registerWidgetType("${type}", factory) before the bootstrap runs.`,
      element
    );
    return undefined;
  }
  const config = element.dataset['xpsConfig']
    ? parseJson(element.dataset['xpsConfig'], element, 'data-xps-config')
    : {};
  if (!config) return undefined;
  try {
    return factory({ ...config, container: element });
  } catch (error) {
    console.error(`[xpsearch] widget type "${type}" failed to build; skipping.`, error, element);
    return undefined;
  }
}

function parseJson(
  raw: string,
  element: HTMLElement,
  attribute: string
): Record<string, unknown> | undefined {
  try {
    const parsed: unknown = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
    console.error(`[xpsearch] ${attribute} must be a JSON object; skipping.`, element);
  } catch (error) {
    console.error(`[xpsearch] ${attribute} is not valid JSON; skipping.`, error, element);
  }
  return undefined;
}
