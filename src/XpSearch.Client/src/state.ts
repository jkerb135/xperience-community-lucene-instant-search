/**
 * `SearchState` — the observable, serializable store of spec 5.1.
 * Every function here is pure: it returns a new frozen state, never mutates the one given.
 */
import type {
  FacetFilter,
  FacetOperator,
  NumericFilter,
  NumericOperator,
  SearchRequest,
  SearchState,
  StateFilters,
} from './types';

/** The first page. State, the wire and the URL all count pages from one. */
export const FIRST_PAGE = 1;

/** Freezes a state deeply enough that a widget cannot mutate it by accident. */
function freeze(state: SearchState): SearchState {
  for (const facet of state.filters.facets) {
    Object.freeze(facet.values);
    Object.freeze(facet);
  }
  for (const numeric of state.filters.numeric) Object.freeze(numeric);
  Object.freeze(state.filters.facets);
  Object.freeze(state.filters.numeric);
  Object.freeze(state.filters);
  return Object.freeze(state);
}

/**
 * Copies the filters, dropping the entries that carry nothing at all. An entry with no values
 * survives only when it declares a non-default operator: that is a widget saying "my attribute
 * is `and`" before the visitor has picked anything, and it must outlive an empty selection.
 */
function copyFilters(filters: Partial<StateFilters> | undefined): StateFilters {
  const facets: FacetFilter[] = [];
  for (const facet of filters?.facets ?? []) {
    if (facet.values.length > 0 || facet.operator === 'and') {
      facets.push({
        attribute: facet.attribute,
        values: [...facet.values],
        ...(facet.operator === undefined ? {} : { operator: facet.operator }),
      });
    }
  }
  return { facets, numeric: (filters?.numeric ?? []).map((n) => ({ ...n })) };
}

/** Builds a complete, frozen state from a partial one. */
export function createState(partial: Partial<SearchState> = {}): SearchState {
  return freeze({
    query: partial.query ?? '',
    page: partial.page ?? FIRST_PAGE,
    filters: copyFilters(partial.filters),
    sort: partial.sort ?? 'relevance',
    ...(partial.pageSize === undefined ? {} : { pageSize: partial.pageSize }),
  });
}

/** A filter change always returns to the first page; only `setPage` moves the page. */
function withFilter(state: SearchState, changes: Partial<SearchState>): SearchState {
  return createState({ ...state, page: FIRST_PAGE, ...changes });
}

/** The values currently selected on one attribute. */
export function facetValues(state: SearchState, attribute: string): readonly string[] {
  return state.filters.facets.find((facet) => facet.attribute === attribute)?.values ?? [];
}

/** The operator declared for one attribute, `'or'` when it has none. */
export function facetOperator(state: SearchState, attribute: string): FacetOperator {
  return state.filters.facets.find((facet) => facet.attribute === attribute)?.operator ?? 'or';
}

export function setQuery(state: SearchState, query: string): SearchState {
  return withFilter(state, { query });
}

export function setPage(state: SearchState, page: number): SearchState {
  return createState({ ...state, page: Math.max(FIRST_PAGE, Math.trunc(page)) });
}

export function setSort(state: SearchState, sort: string): SearchState {
  return withFilter(state, { sort });
}

export function setPageSize(state: SearchState, pageSize: number | undefined): SearchState {
  return withFilter(state, { pageSize });
}

/** Replaces one attribute's entry, dropping it when nothing is selected on it any more. */
function withFacet(
  state: SearchState,
  attribute: string,
  values: readonly string[],
  operator?: FacetOperator
): SearchState {
  const current = state.filters.facets.find((facet) => facet.attribute === attribute);
  const kept = state.filters.facets.filter((facet) => facet.attribute !== attribute);
  const chosen = operator ?? current?.operator;
  const facets = [
    ...kept,
    { attribute, values: [...values], ...(chosen === undefined ? {} : { operator: chosen }) },
  ];
  // createState drops the entry again when it selects nothing and declares no `and`.
  return withFilter(state, { filters: { ...state.filters, facets } });
}

export function toggleFacet(state: SearchState, attribute: string, value: string): SearchState {
  const current = facetValues(state, attribute);
  return withFacet(
    state,
    attribute,
    current.includes(value) ? current.filter((v) => v !== value) : [...current, value]
  );
}

export function setFacetValues(
  state: SearchState,
  attribute: string,
  values: readonly string[]
): SearchState {
  return withFacet(state, attribute, values);
}

/**
 * Declares how the values of one attribute combine. It is state, not a side channel: it goes on
 * the wire as `filters.facets[].operator` and belongs in the URL with the values it applies to.
 */
export function setFacetOperator(
  state: SearchState,
  attribute: string,
  operator: FacetOperator
): SearchState {
  return withFacet(state, attribute, facetValues(state, attribute), operator);
}

/** Clears every filter, or every filter on one attribute (facet and numeric alike). */
export function clearFilters(state: SearchState, attribute?: string): SearchState {
  if (attribute === undefined) {
    return withFilter(state, { filters: { facets: [], numeric: [] } });
  }
  return withFilter(state, {
    filters: {
      facets: state.filters.facets.filter((facet) => facet.attribute !== attribute),
      numeric: state.filters.numeric.filter((n) => n.attribute !== attribute),
    },
  });
}

/** Sets the bound on `attribute` for `operator`, replacing any existing one. */
export function setNumericFilter(
  state: SearchState,
  attribute: string,
  operator: NumericOperator,
  value: number
): SearchState {
  const kept = state.filters.numeric.filter(
    (n) => !(n.attribute === attribute && n.operator === operator)
  );
  return withFilter(state, {
    filters: { ...state.filters, numeric: [...kept, { attribute, operator, value }] },
  });
}

export function removeNumericFilter(
  state: SearchState,
  attribute: string,
  operator?: NumericOperator
): SearchState {
  return withFilter(state, {
    filters: {
      ...state.filters,
      numeric: state.filters.numeric.filter(
        (n) => n.attribute !== attribute || (operator !== undefined && n.operator !== operator)
      ),
    },
  });
}

export function isFacetActive(state: SearchState, attribute: string, value: string): boolean {
  return facetValues(state, attribute).includes(value);
}

/** True when two states would produce the same request. */
export function statesEqual(a: SearchState, b: SearchState): boolean {
  return JSON.stringify(stateToWireFragment(a)) === JSON.stringify(stateToWireFragment(b));
}

/** The part of a `SearchRequest` that comes from state alone. */
export function stateToWireFragment(
  state: SearchState
): Pick<SearchRequest, 'query' | 'page' | 'filters' | 'sort' | 'pageSize'> {
  const copied = copyFilters(state.filters);
  // An entry that selects nothing refines nothing; it exists in state only to carry its operator.
  const filters = { ...copied, facets: copied.facets.filter((facet) => facet.values.length > 0) };
  const hasFilters = filters.facets.length > 0 || filters.numeric.length > 0;
  return {
    query: state.query,
    page: state.page,
    sort: state.sort,
    ...(hasFilters
      ? {
          filters: {
            ...(filters.facets.length > 0 ? { facets: filters.facets as FacetFilter[] } : {}),
            ...(filters.numeric.length > 0 ? { numeric: filters.numeric as NumericFilter[] } : {}),
          },
        }
      : {}),
    ...(state.pageSize === undefined ? {} : { pageSize: state.pageSize }),
  };
}

/** Reads a wire request fragment back into state — the inverse of `stateToWireFragment`. */
export function stateFromWireFragment(request: Partial<SearchRequest>): SearchState {
  return createState({
    query: request.query,
    page: request.page,
    sort: request.sort,
    filters: {
      facets: request.filters?.facets ?? [],
      numeric: request.filters?.numeric ?? [],
    },
    pageSize: request.pageSize,
  });
}

/** The observable half of spec 5.1: hold a state, hand out changes. */
export interface Store {
  get(): SearchState;
  set(next: SearchState): void;
  subscribe(listener: (state: SearchState) => void): () => void;
}

export function createStore(initial: SearchState): Store {
  let state = initial;
  const listeners = new Set<(state: SearchState) => void>();
  return {
    get: () => state,
    set(next) {
      state = next;
      for (const listener of [...listeners]) listener(state);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}
