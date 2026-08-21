/**
 * `SearchState` — the observable, serializable store of spec 5.1.
 * Every function here is pure: it returns a new frozen state, never mutates the one given.
 */
import type {
  FacetOperator,
  NumericOperator,
  NumericRefinement,
  SearchRequest,
  SearchState,
} from './types';

/** Freezes a state deeply enough that a widget cannot mutate it by accident. */
function freeze(state: SearchState): SearchState {
  for (const values of Object.values(state.facetFilters)) Object.freeze(values);
  Object.freeze(state.facetFilters);
  for (const numeric of state.numericFilters) Object.freeze(numeric);
  Object.freeze(state.numericFilters);
  return Object.freeze(state);
}

/** Builds a complete, frozen state from a partial one. */
export function createState(partial: Partial<SearchState> = {}): SearchState {
  const facetFilters: Record<string, string[]> = {};
  for (const [attribute, values] of Object.entries(partial.facetFilters ?? {})) {
    if (values && values.length > 0) facetFilters[attribute] = [...values];
  }
  return freeze({
    query: partial.query ?? '',
    page: partial.page ?? 0,
    facetFilters,
    numericFilters: (partial.numericFilters ?? []).map((n) => ({ ...n })),
    sort: partial.sort ?? 'relevance',
    ...(partial.hitsPerPage === undefined ? {} : { hitsPerPage: partial.hitsPerPage }),
  });
}

/** A refinement change always returns to the first page; only `setPage` moves the page. */
function withRefinement(state: SearchState, changes: Partial<SearchState>): SearchState {
  return createState({ ...state, page: 0, ...changes });
}

export function setQuery(state: SearchState, query: string): SearchState {
  return withRefinement(state, { query });
}

export function setPage(state: SearchState, page: number): SearchState {
  return createState({ ...state, page: Math.max(0, Math.trunc(page)) });
}

export function setSort(state: SearchState, sort: string): SearchState {
  return withRefinement(state, { sort });
}

export function setHitsPerPage(state: SearchState, hitsPerPage: number | undefined): SearchState {
  return withRefinement(state, { hitsPerPage });
}

export function toggleFacetRefinement(
  state: SearchState,
  attribute: string,
  value: string
): SearchState {
  const current = state.facetFilters[attribute] ?? [];
  const next = current.includes(value)
    ? current.filter((v) => v !== value)
    : [...current, value];
  const facetFilters = { ...state.facetFilters, [attribute]: next };
  if (next.length === 0) delete facetFilters[attribute];
  return withRefinement(state, { facetFilters });
}

export function setFacetValues(
  state: SearchState,
  attribute: string,
  values: readonly string[]
): SearchState {
  const facetFilters = { ...state.facetFilters, [attribute]: [...values] };
  if (values.length === 0) delete facetFilters[attribute];
  return withRefinement(state, { facetFilters });
}

/** Clears every refinement, or every refinement on one attribute (facet and numeric alike). */
export function clearRefinements(state: SearchState, attribute?: string): SearchState {
  if (attribute === undefined) {
    return withRefinement(state, { facetFilters: {}, numericFilters: [] });
  }
  const facetFilters = { ...state.facetFilters };
  delete facetFilters[attribute];
  return withRefinement(state, {
    facetFilters,
    numericFilters: state.numericFilters.filter((n) => n.attribute !== attribute),
  });
}

export function addNumericRefinement(
  state: SearchState,
  attribute: string,
  operator: NumericOperator,
  value: number
): SearchState {
  return withRefinement(state, {
    numericFilters: [...state.numericFilters, { attribute, operator, value }],
  });
}

/** Like `addNumericRefinement`, but replaces an existing bound on the same attribute+operator. */
export function setNumericRefinement(
  state: SearchState,
  attribute: string,
  operator: NumericOperator,
  value: number
): SearchState {
  const kept = state.numericFilters.filter(
    (n) => !(n.attribute === attribute && n.operator === operator)
  );
  return withRefinement(state, { numericFilters: [...kept, { attribute, operator, value }] });
}

export function removeNumericRefinement(
  state: SearchState,
  attribute: string,
  operator?: NumericOperator
): SearchState {
  return withRefinement(state, {
    numericFilters: state.numericFilters.filter(
      (n) => n.attribute !== attribute || (operator !== undefined && n.operator !== operator)
    ),
  });
}

export function isFacetRefined(state: SearchState, attribute: string, value: string): boolean {
  return (state.facetFilters[attribute] ?? []).includes(value);
}

/** True when two states would produce the same request. */
export function statesEqual(a: SearchState, b: SearchState): boolean {
  return JSON.stringify(stateToWireFragment(a)) === JSON.stringify(stateToWireFragment(b));
}

/** The part of a `SearchRequest` that comes from state alone. */
export function stateToWireFragment(
  state: SearchState,
  facetOperators: Readonly<Record<string, FacetOperator>> = {}
): Pick<
  SearchRequest,
  'query' | 'page' | 'facetFilters' | 'numericFilters' | 'sort' | 'hitsPerPage'
> {
  // Outer array is ANDed, each inner array is ORed (spec 4.2). An `or` attribute contributes one
  // inner array holding all of its values; an `and` attribute contributes one array per value.
  const facetFilters: string[][] = [];
  for (const [attribute, values] of Object.entries(state.facetFilters)) {
    if (values.length === 0) continue;
    const encoded = values.map((value) => `${attribute}:${value}`);
    if ((facetOperators[attribute] ?? 'or') === 'and') {
      for (const one of encoded) facetFilters.push([one]);
    } else {
      facetFilters.push(encoded);
    }
  }
  const numericFilters = state.numericFilters.map(
    (n) => `${n.attribute}${n.operator}${n.value}`
  );
  return {
    query: state.query,
    page: state.page,
    sort: state.sort,
    ...(facetFilters.length > 0 ? { facetFilters } : {}),
    ...(numericFilters.length > 0 ? { numericFilters } : {}),
    ...(state.hitsPerPage === undefined ? {} : { hitsPerPage: state.hitsPerPage }),
  };
}

/** Reads a wire request fragment back into state — the inverse of `stateToWireFragment`. */
export function stateFromWireFragment(request: Partial<SearchRequest>): SearchState {
  const facetFilters: Record<string, string[]> = {};
  for (const group of request.facetFilters ?? []) {
    for (const entry of group) {
      const separator = entry.indexOf(':');
      if (separator <= 0) continue;
      const attribute = entry.slice(0, separator);
      const value = entry.slice(separator + 1);
      (facetFilters[attribute] ??= []).push(value);
    }
  }
  const numericFilters: NumericRefinement[] = [];
  for (const entry of request.numericFilters ?? []) {
    const parsed = parseNumericFilter(entry);
    if (parsed) numericFilters.push(parsed);
  }
  return createState({
    query: request.query,
    page: request.page,
    sort: request.sort,
    facetFilters,
    numericFilters,
    hitsPerPage: request.hitsPerPage,
  });
}

/** Parses `price<=50` into its parts. Returns `null` for anything off-grammar (spec 4.2). */
export function parseNumericFilter(filter: string): NumericRefinement | null {
  const match = /^([A-Za-z_][\w.]*)\s*(<=|>=|<|>|=)\s*(-?\d+(?:\.\d+)?)$/.exec(filter);
  if (!match) return null;
  return {
    attribute: match[1]!,
    operator: match[2] as NumericOperator,
    value: Number(match[3]),
  };
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
