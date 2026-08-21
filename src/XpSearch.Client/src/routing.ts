/**
 * URL routing (spec 5.5): state <-> query params, back/forward through `popstate`, and the
 * shared `createURL` every connector hands to its renderer so links stay crawlable.
 */
import type { NumericOperator, NumericRefinement, RoutingOptions, SearchState } from './types';

/** Params the default mapping owns. A facet attribute with one of these names cannot be routed. */
const QUERY_PARAM = 'q';
const PAGE_PARAM = 'page';
const SORT_PARAM = 'sort';
const RESERVED = new Set([QUERY_PARAM, PAGE_PARAM, SORT_PARAM]);

/** `price<=50` is routed as `price_lte=50`; the suffix is what makes the URL readable. */
const OPERATOR_SUFFIX: Record<NumericOperator, string> = {
  '<': 'lt',
  '<=': 'lte',
  '=': 'eq',
  '>=': 'gte',
  '>': 'gt',
};
const SUFFIX_OPERATOR = new Map<string, NumericOperator>(
  Object.entries(OPERATOR_SUFFIX).map(([operator, suffix]) => [suffix, operator as NumericOperator])
);

/**
 * Default state -> params mapping. `page` is one-based in the URL and zero-based in state;
 * defaults (empty query, first page, `relevance`) are omitted so a pristine URL stays clean.
 * Facet values are `encodeURIComponent`-escaped before being comma-joined, so a value that
 * itself contains a comma round-trips.
 */
export function defaultStateToRoute(state: SearchState): Record<string, string | string[]> {
  const route: Record<string, string | string[]> = {};
  if (state.query !== '') route[QUERY_PARAM] = state.query;
  if (state.page > 0) route[PAGE_PARAM] = String(state.page + 1);
  if (state.sort !== 'relevance') route[SORT_PARAM] = state.sort;
  for (const [attribute, values] of Object.entries(state.facetFilters)) {
    if (values.length > 0) route[attribute] = values.map(encodeURIComponent).join(',');
  }
  for (const numeric of state.numericFilters) {
    const key = `${numeric.attribute}_${OPERATOR_SUFFIX[numeric.operator]}`;
    const value = String(numeric.value);
    const existing = route[key];
    route[key] = existing === undefined ? value : [...(Array.isArray(existing) ? existing : [existing]), value];
  }
  return route;
}

/** Default params -> state mapping; the inverse of {@link defaultStateToRoute}. */
export function defaultRouteToState(route: Record<string, string[]>): Partial<SearchState> {
  const facetFilters: Record<string, string[]> = {};
  const numericFilters: NumericRefinement[] = [];
  for (const [key, values] of Object.entries(route)) {
    if (RESERVED.has(key) || values.length === 0) continue;
    const separator = key.lastIndexOf('_');
    const operator = separator > 0 ? SUFFIX_OPERATOR.get(key.slice(separator + 1)) : undefined;
    if (operator !== undefined && values.every((v) => v !== '' && !Number.isNaN(Number(v)))) {
      for (const value of values) {
        numericFilters.push({ attribute: key.slice(0, separator), operator, value: Number(value) });
      }
      continue;
    }
    const parsed = values
      .flatMap((value) => value.split(','))
      .filter((value) => value !== '')
      .map(decodeURIComponent);
    if (parsed.length > 0) facetFilters[key] = parsed;
  }
  const page = Number(route[PAGE_PARAM]?.[0]);
  // Absent params are left out rather than defaulted, so `initialState` still applies to them.
  return {
    ...(route[QUERY_PARAM]?.[0] === undefined ? {} : { query: route[QUERY_PARAM][0] }),
    ...(Number.isFinite(page) && page > 1 ? { page: page - 1 } : {}),
    ...(route[SORT_PARAM]?.[0] === undefined ? {} : { sort: route[SORT_PARAM][0] }),
    ...(Object.keys(facetFilters).length > 0 ? { facetFilters } : {}),
    ...(numericFilters.length > 0 ? { numericFilters } : {}),
  };
}

export interface Router {
  /** Only the params actually present in the URL; the caller decides the defaults. */
  read(): Partial<SearchState>;
  /** Writes `state` to the address bar. Replaces when only the query changed, pushes otherwise. */
  write(state: SearchState, previous: SearchState): void;
  createURL(state: SearchState): string;
  listen(onPop: (state: Partial<SearchState>) => void): () => void;
}

interface RouterOptions extends RoutingOptions {
  /** When false the router still builds URLs (for `createURL`) but never touches history. */
  enabled: boolean;
}

export function createRouter(options: RouterOptions): Router {
  const win = options.windowRef ?? (typeof window === 'undefined' ? undefined : window);
  const stateToRoute = options.stateToRoute ?? defaultStateToRoute;
  const routeToState = options.routeToState ?? defaultRouteToState;
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
    createURL(state) {
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
        JSON.stringify(state.facetFilters) === JSON.stringify(previous.facetFilters) &&
        JSON.stringify(state.numericFilters) === JSON.stringify(previous.numericFilters);
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
