/**
 * The `.xps-mount` bootstrap (spec 7.1) and the custom-widget registry (spec 5.7).
 *
 * Markup a Page Builder widget emits:
 *   <div class="xps-mount"
 *        data-xps-widget="facetList"
 *        data-xps-instance="search-1"
 *        data-xps-instance-config='{"index":"site-content","routing":true}'
 *        data-xps-config='{"attribute":"contentType","limit":10}'></div>
 *
 * Mounts are grouped by `data-xps-instance` (default `"default"`), one `createSearch()` per group.
 * The group's `data-xps-instance-config` objects are merged, so an option only one widget knows -
 * the Page Builder results widget's page size and fields - applies wherever the editor placed it.
 * Nothing here throws: a misconfigured mount is a console error and a skipped widget.
 */
import { createSearch } from './instance';
import type { SearchInstance, Widget, XpSearchOptions } from './types';
import { DEFAULT_WIDGETS } from './widgets';

/** Config from `data-xps-config`, plus the mount element the widget renders into. */
export type MountConfig = Record<string, unknown> & { container: HTMLElement };

export type MountWidgetFactory = (config: MountConfig) => Widget;

/** One field of a {@link MountConfigSpec}. A trailing `?` makes the field optional. */
export type MountFieldSpec =
  | 'string'
  | 'string?'
  | 'number'
  | 'number?'
  | 'boolean'
  | 'boolean?';

/** What {@link readMountConfig} expects each config key to be. */
export type MountConfigSpec = Record<string, MountFieldSpec>;

type MountFieldType<TSpec extends MountFieldSpec> = TSpec extends `string${string}`
  ? string
  : TSpec extends `number${string}`
    ? number
    : boolean;

type OptionalKeys<TSpec extends MountConfigSpec> = {
  [K in keyof TSpec]: TSpec[K] extends `${string}?` ? K : never;
}[keyof TSpec];

/** The narrowed shape a {@link MountConfigSpec} describes. */
export type MountConfigOf<TSpec extends MountConfigSpec> = {
  [K in Exclude<keyof TSpec, OptionalKeys<TSpec>>]: MountFieldType<TSpec[K]>;
} & {
  [K in OptionalKeys<TSpec>]?: MountFieldType<TSpec[K]>;
};

/**
 * Narrows the untyped values of a `data-xps-config` to the shape a widget factory needs.
 *
 * A mount config is a **trust boundary**: the JSON is whatever an editor typed into the widget
 * dialog, so every value arrives as `unknown` and has to be checked before use. A missing or
 * wrong-typed required key throws an `Error` naming the key; the bootstrap turns that into one
 * `console.error` and skips the widget, leaving the rest of the page working.
 *
 * An empty string counts as absent: an editor who leaves a text field blank has not configured it.
 *
 * ```ts
 * const { attribute, label } = readMountConfig(config, { attribute: 'string', label: 'string?' });
 * ```
 */
export function readMountConfig<TSpec extends MountConfigSpec>(
  config: MountConfig,
  spec: TSpec
): MountConfigOf<TSpec> {
  const out: Record<string, unknown> = {};
  for (const [key, field] of Object.entries(spec) as [string, MountFieldSpec][]) {
    const optional = field.endsWith('?');
    const kind = optional ? field.slice(0, -1) : field;
    const raw = config[key];
    const value = raw === '' || raw === null ? undefined : raw;

    if (value === undefined) {
      if (!optional) {
        throw new Error(`data-xps-config: "${key}" is required and must be a ${kind}.`);
      }
      continue;
    }
    if (typeof value !== kind) {
      throw new Error(
        `data-xps-config: "${key}" must be a ${kind}, got ${typeof value} (${JSON.stringify(value)}).`
      );
    }
    if (kind === 'number' && !Number.isFinite(value)) {
      throw new Error(`data-xps-config: "${key}" must be a finite number, got ${String(value)}.`);
    }
    out[key] = value;
  }
  return out as MountConfigOf<TSpec>;
}

/**
 * Bare identifiers reserved for the widgets shipped with the library; everything else must be
 * namespaced with a dot. Registering one of these replaces the built-in of the same name.
 */
export const FIRST_PARTY_WIDGET_TYPES: readonly string[] = [
  'searchBox',
  'results',
  'facetList',
  'pagination',
  'resultStats',
  'sortSelect',
  'clearFilters',
  'activeFilters',
  'toggleFilter',
  // Reserved for the Phase 2.5 widgets, so a project cannot take one of these names first.
  'suggestions',
  'rangeFilter',
  'categoryTree',
  'loadMore',
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
export function mountAll(root: ParentNode | undefined = globalThis.document): SearchInstance[] {
  if (!root) return [];
  const groups = new Map<string, HTMLElement[]>();
  for (const element of root.querySelectorAll<HTMLElement>('.xps-mount')) {
    if (element.dataset[MOUNTED] === 'true') continue;
    const id = element.dataset['xpsInstance'] || 'default';
    const group = groups.get(id) ?? [];
    group.push(element);
    groups.set(id, group);
  }

  const instances: SearchInstance[] = [];
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
    const instance = createSearch(options);
    instance.addWidgets(widgets);
    instance.start();
    instances.push(instance);
  }
  return instances;
}

function readInstanceOptions(id: string, elements: HTMLElement[]): XpSearchOptions | undefined {
  // Instance options are merged across every mount of the group rather than taken from the first one,
  // so a widget that contributes an option the others cannot know (`initialState.pageSize`, `fields`)
  // works wherever an editor dropped it. The first definition of a key wins; a later mount that
  // disagrees is a warning, never a silent override.
  const merged: Record<string, unknown> = {};
  const conflicts = new Set<string>();
  for (const element of elements) {
    const raw = element.dataset['xpsInstanceConfig'];
    if (!raw) continue;
    const parsed = parseJson(raw, element, 'data-xps-instance-config');
    if (!parsed) continue;
    for (const [key, value] of Object.entries(parsed)) {
      if (!(key in merged)) {
        merged[key] = value;
      } else if (!conflicts.has(key) && JSON.stringify(merged[key]) !== JSON.stringify(value)) {
        conflicts.add(key);
        console.warn(
          `[xpsearch] instance "${id}" has conflicting data-xps-instance-config values for "${key}"; keeping the first one. Every widget of one instance must agree.`,
          element
        );
      }
    }
  }

  if (typeof merged['index'] === 'string' && merged['index'] !== '') {
    return merged as unknown as XpSearchOptions;
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
  // `registerWidgetType('results', …)` a supported override rather than a collision.
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
