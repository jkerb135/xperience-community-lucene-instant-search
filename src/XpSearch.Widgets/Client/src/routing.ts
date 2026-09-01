/**
 * URL routing (spec 5.5): state <-> query params, back/forward through `popstate`, and the
 * shared `urlFor` every behaviour hands to its renderer so links stay crawlable.
 */
import { FIRST_PAGE } from './state';
import type { NumericFilter, NumericOperator, RoutingOptions, SearchState } from './types';

/** Params the default mapping owns. A facet attribute with one of these names cannot be routed. */
const QUERY_PARAM = 'q';
const PAGE_PARAM = 'page';
const SORT_PARAM = 'sort';
const RESERVED = new Set([QUERY_PARAM, PAGE_PARAM, SORT_PARAM]);

/** `price lte 50` is routed as `price_lte=50`; the suffix is the contract's own operator name. */
const OPERATORS = new Set<string>(['lt', 'lte', 'eq', 'ne', 'gte', 'gt']);

/**
 * Default state -> params mapping. `page` is one-based in state and in the URL; defaults (empty
 * query, first page, `relevance`) are omitted so a pristine URL stays clean. Facet values are
 * `encodeURIComponent`-escaped before being comma-joined, so a value that itself contains a
 * comma round-trips. An `and` attribute keeps its operator in a `<attribute>_op` param.
 */
export function defaultStateToRoute(state: SearchState): Record<string, string | string[]> {
  const route: Record<string, string | string[]> = {};
  if (state.query !== '') route[QUERY_PARAM] = state.query;
  if (state.page > FIRST_PAGE) route[PAGE_PARAM] = String(state.page);
  if (state.sort !== 'relevance') route[SORT_PARAM] = state.sort;
  for (const facet of state.filters.facets) {
    if (facet.values.length === 0) continue;
    route[facet.attribute] = facet.values.map(encodeURIComponent).join(',');
    if (facet.operator === 'and') route[`${facet.attribute}_op`] = 'and';
  }
  for (const numeric of state.filters.numeric) {
    const key = `${numeric.attribute}_${numeric.operator}`;
    const value = String(numeric.value);
    const existing = route[key];
    route[key] = existing === undefined ? value : [...(Array.isArray(existing) ? existing : [existing]), value];
  }
  return route;
}

/**
 * The filter attributes the page can route, collected from the widgets on it. Sets are read at
 * hydration time, so a widget added before `start()` counts; one added after does not.
 */
export interface RoutableAttributes {
  facets: ReadonlySet<string>;
  numeric: ReadonlySet<string>;
}

/**
 * Default params -> state mapping; the inverse of {@link defaultStateToRoute}.
 *
 * With `routable` supplied, only filters on those attributes are adopted: a page carries foreign
 * query params (Kentico's `uh`, `utm_*`, `gclid`) that are not filters and that the API rejects.
 * Without it every non-reserved param is taken as a filter, as before.
 */
export function defaultRouteToState(
  route: Record<string, string[]>,
  routable?: RoutableAttributes
): Partial<SearchState> {
  const values = new Map<string, string[]>();
  const operators = new Map<string, 'and' | 'or'>();
  const numeric: NumericFilter[] = [];
  for (const [key, raw] of Object.entries(route)) {
    if (RESERVED.has(key) || raw.length === 0) continue;
    const separator = key.lastIndexOf('_');
    const suffix = separator > 0 ? key.slice(separator + 1) : '';
    const attribute = key.slice(0, separator);
    if (suffix === 'op') {
      if ((raw[0] === 'and' || raw[0] === 'or') && (routable?.facets.has(attribute) ?? true)) {
        operators.set(attribute, raw[0]);
      }
      continue;
    }
    if (OPERATORS.has(suffix) && raw.every((v) => v !== '' && !Number.isNaN(Number(v)))) {
      if (routable && !routable.numeric.has(attribute)) continue;
      for (const value of raw) {
        numeric.push({
          attribute,
          operator: suffix as NumericOperator,
          value: Number(value),
        });
      }
      continue;
    }
    if (routable && !routable.facets.has(key)) continue;
    const parsed = raw
      .flatMap((value) => value.split(','))
      .filter((value) => value !== '')
      .map(decodeURIComponent);
    if (parsed.length > 0) values.set(key, parsed);
  }
  const facets = [...values].map(([attribute, selected]) => {
    const operator = operators.get(attribute);
    return { attribute, values: selected, ...(operator === undefined ? {} : { operator }) };
  });
  const page = Number(route[PAGE_PARAM]?.[0]);
  const hasFilters = facets.length > 0 || numeric.length > 0;
  // Absent params are left out rather than defaulted, so `initialState` still applies to them.
  return {
    ...(route[QUERY_PARAM]?.[0] === undefined ? {} : { query: route[QUERY_PARAM][0] }),
    ...(Number.isFinite(page) && page > FIRST_PAGE ? { page } : {}),
    ...(route[SORT_PARAM]?.[0] === undefined ? {} : { sort: route[SORT_PARAM][0] }),
    ...(hasFilters ? { filters: { facets, numeric } } : {}),
  };
}

export interface Router {
  /** Only the params actually present in the URL; the caller decides the defaults. */
  read(): Partial<SearchState>;
  /** Writes `state` to the address bar. Replaces when only the query changed, pushes otherwise. */
  write(state: SearchState, previous: SearchState): void;
  urlFor(state: SearchState): string;
  listen(onPop: (state: Partial<SearchState>) => void): () => void;
}

interface RouterOptions extends RoutingOptions {
  /** When false the router still builds URLs (for `urlFor`) but never touches history. */
  enabled: boolean;
  /** Filters the default mapping may adopt. A custom `routeToState` bypasses it. */
  routable?: RoutableAttributes;
}

export function createRouter(options: RouterOptions): Router {
  const win = options.windowRef ?? (typeof window === 'undefined' ? undefined : window);
  const stateToRoute = options.stateToRoute ?? defaultStateToRoute;
  const routeToState =
    options.routeToState ??
    ((route: Record<string, string[]>): Partial<SearchState> =>
      defaultRouteToState(route, options.routable));
  // Params any mapping has produced so far. Anything else in the URL belongs to the page
  // (utm_*, tracking ids, ...) and is preserved across writes.
  const owned = new Set<string>(RESERVED);

  const toURL = (state: SearchState): URL => {
    const url = new URL(win?.location.href ?? 'http://localhost/');
    const route = stateToRoute(state);
    for (const key of Object.keys(route)) owned.add(key);
    for (const key of owned) url.searchParams.delete(key);
    for (const [key, value] of Object.entries(route)) {
      for (const one of Array.isArray(value) ? value : [value]) url.searchParams.append(key, one);
    }
    url.searchParams.sort();
    return url;
  };

  const read = (): Partial<SearchState> => {
    if (!win) return {};
    const params = new URL(win.location.href).searchParams;
    const route: Record<string, string[]> = {};
    for (const key of params.keys()) route[key] = params.getAll(key);
    return routeToState(route);
  };

  return {
    read,
    urlFor(state) {
      return toURL(state).toString();
    },
    write(state, previous) {
      if (!enabledHistory(win, options.enabled)) return;
      const url = toURL(state).toString();
      if (url === win!.location.href) return;
      const onlyQueryChanged =
        state.query !== previous.query &&
        state.page === previous.page &&
        state.sort === previous.sort &&
        JSON.stringify(state.filters) === JSON.stringify(previous.filters);
      // Typing into the search box should not fill the back stack with one entry per keystroke.
      if (onlyQueryChanged) win!.history.replaceState(null, '', url);
      else win!.history.pushState(null, '', url);
    },
    listen(onPop) {
      if (!enabledHistory(win, options.enabled)) return () => {};
      const handler = (): void => onPop(read());
      win!.addEventListener('popstate', handler);
      return () => win!.removeEventListener('popstate', handler);
    },
  };
}

function enabledHistory(win: Window | undefined, enabled: boolean): win is Window {
  return enabled && win !== undefined && typeof win.history !== 'undefined';
}
