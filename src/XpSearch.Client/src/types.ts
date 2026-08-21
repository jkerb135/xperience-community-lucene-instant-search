/**
 * The public type surface of @yourco/xperience-search (spec 5.7, "Widget SDK contract").
 * Breaking anything in this file is a semver-major event.
 */
import type {
  EventType,
  Hit as WireHit,
  HighlightOptions,
  SearchRequest,
  SearchResponse,
} from './contract/generated';

export type { EventType, HighlightOptions, SearchRequest, SearchResponse };

/**
 * One search result, typed. `WireHit` is deliberately open (`objectID` plus an index signature),
 * so intersecting it with the caller's document shape gives `hit.title` without an `any`.
 */
export type Hit<TItem extends Record<string, unknown> = Record<string, unknown>> = WireHit & TItem;

/** A `SearchResponse` whose hits carry the caller's document shape. */
export interface SearchResults<TItem extends Record<string, unknown> = Record<string, unknown>>
  extends Omit<SearchResponse, 'hits'> {
  hits: Array<Hit<TItem>>;
}

/** The numeric operators a widget may refine with (spec 5.7). */
export type NumericOperator = '<' | '<=' | '=' | '>=' | '>';

/** One numeric refinement; serialized to the wire as `attribute` + `operator` + `value`. */
export interface NumericRefinement {
  attribute: string;
  operator: NumericOperator;
  value: number;
}

/** How the values selected on one attribute combine (spec 4.2: outer AND, inner OR). */
export type FacetOperator = 'and' | 'or';

/**
 * The search state. Pure, serializable, frozen — widgets read it and never write to it;
 * every mutation goes through {@link SearchHelper}.
 */
export interface SearchState {
  readonly query: string;
  /** Zero-based, like the wire contract. The default route mapping shows it one-based. */
  readonly page: number;
  readonly facetFilters: Readonly<Record<string, readonly string[]>>;
  readonly numericFilters: readonly NumericRefinement[];
  /** `"relevance"` (the default) or an index-configured sort key. */
  readonly sort: string;
  readonly hitsPerPage?: number;
}

/**
 * The only sanctioned way to mutate state (spec 5.7). Mutators are chainable and never search;
 * call {@link SearchHelper.search} when the state is where you want it.
 *
 * Members below `search()` are this implementation's proposed additions to the published SDK
 * contract — see ADR-0007.
 */
export interface SearchHelper {
  setQuery(q: string): SearchHelper;
  toggleFacetRefinement(attribute: string, value: string): SearchHelper;
  clearRefinements(attribute?: string): SearchHelper;
  setPage(page: number): SearchHelper;
  addNumericRefinement(attr: string, op: NumericOperator, value: number): SearchHelper;
  setSort(key: string): SearchHelper;
  search(): void;

  /** Current state. Frozen: assigning to it is a no-op (a TypeError under strict mode). */
  getState(): SearchState;
  /** Replaces any refinement on `attr` with the same operator. Used by `connectRange`. */
  setNumericRefinement(attr: string, op: NumericOperator, value: number): SearchHelper;
  /** Removes numeric refinements on `attr`, optionally narrowed to one operator. */
  removeNumericRefinement(attr: string, op?: NumericOperator): SearchHelper;
  setHitsPerPage(hitsPerPage: number | undefined): SearchHelper;
  /** Declares how the values of one attribute combine on the wire. Defaults to `'or'`. */
  setFacetOperator(attribute: string, operator: FacetOperator): SearchHelper;
}

/** Lifecycle events of a search instance (spec 5.7). */
export interface SearchEvents {
  render: { results: SearchResults | null; state: SearchState };
  error: {
    error: Error;
    phase: 'init' | 'render' | 'dispose' | 'search' | 'contract';
    widget?: string;
  };
  stateChange: { state: SearchState };
}

/** What a search instance is doing right now. `stalled` is a slow in-flight request. */
export type SearchStatus = 'idle' | 'loading' | 'stalled' | 'error';

/** Arguments of {@link Widget.init}. */
export interface InitOptions {
  state: SearchState;
  helper: SearchHelper;
  instantSearchInstance: InstantSearch;
  /** Alias of {@link InitOptions.instantSearchInstance}, spelled as in spec 5.7. */
  instantiate: InstantSearch;
}

/** Arguments of {@link Widget.render}. */
export interface RenderArgs {
  results: SearchResults | null;
  state: SearchState;
  helper: SearchHelper;
  instantSearchInstance: InstantSearch;
  isFirstRender: boolean;
}

/** A widget. Every member is optional; a widget that only renders is legal. */
export interface Widget {
  /** Identifier used in error messages and by the mount bootstrap. */
  $$type?: string;
  /** Contributes to the outgoing request. Applied in widget-add order (spec 5.7). */
  getSearchParameters?(state: SearchState): SearchState;
  /**
   * Contributes request fields that are not state — `facets`, `highlight`,
   * `attributesToRetrieve`. Applied in widget-add order, after `getSearchParameters`.
   */
  getRequestParameters?(request: SearchRequest): SearchRequest;
  init?(options: InitOptions): void;
  render?(options: RenderArgs): void;
  dispose?(): void;
}

/** Base render options handed to every connector's render function (spec 5.7). */
export interface RenderOptions<TParams> {
  widgetParams: TParams;
  results: SearchResults | null;
  state: SearchState;
  helper: SearchHelper;
  instantSearchInstance: InstantSearch;
}

/** A widget factory, as produced by every connector. */
export type WidgetFactory<TParams> = (widgetParams: TParams) => Widget;

/** The search instance returned by `xpsearch()`. Aliased as `XpSearch`. */
export interface InstantSearch {
  readonly state: SearchState;
  readonly results: SearchResults | null;
  readonly status: SearchStatus;
  readonly helper: SearchHelper;
  readonly index: string;
  addWidgets(widgets: Widget[]): InstantSearch;
  removeWidgets(widgets: Widget[]): InstantSearch;
  start(): InstantSearch;
  dispose(): void;
  on<K extends keyof SearchEvents>(
    event: K,
    handler: (payload: SearchEvents[K]) => void
  ): InstantSearch;
  off<K extends keyof SearchEvents>(
    event: K,
    handler: (payload: SearchEvents[K]) => void
  ): InstantSearch;
  /** URL for `state` under the active route mapping. Used by every connector's `createURL`. */
  createURL(state?: SearchState): string;
  /** Fire-and-forget analytics event, correlated with the last response's `queryId`. */
  sendEvent(eventType: EventType, objectID: string, position?: number): void;
}

/** `routing: { stateToRoute, routeToState }` (spec 5.5). */
export interface RoutingOptions {
  stateToRoute?(state: SearchState): Record<string, string | string[]>;
  routeToState?(route: Record<string, string[]>): Partial<SearchState>;
  /** `window` by default; injectable for tests. */
  windowRef?: Window;
}

/** Options of `xpsearch()` (spec 5.2). */
export interface XpSearchOptions {
  /** Required. Lucene index code name. */
  index: string;
  /** Search endpoint. Defaults to `QUERY_ROUTE` (`/api/xpsearch/query`). */
  endpoint?: string;
  /** Suggest endpoint. Defaults to `SUGGEST_ROUTE`. */
  suggestEndpoint?: string;
  /** Events endpoint. Defaults to `EVENTS_ROUTE`. */
  eventsEndpoint?: string;
  routing?: boolean | RoutingOptions;
  initialState?: Partial<SearchState>;
  /** Defaults to `true`. */
  searchOnInitialLoad?: boolean;
  /** Defaults to `150`. */
  debounceMs?: number;
  /** Facet attributes to always count, on top of those the widgets ask for. */
  facets?: string[];
  highlight?: HighlightOptions;
  attributesToRetrieve?: string[];
  language?: string;
  /** Extra request headers, e.g. an API key. */
  headers?: Record<string, string>;
  /** Injectable `fetch`, for tests and SSR. */
  fetchFn?: typeof fetch;
  /** Retries after a network error, 429 or 5xx. Defaults to `2`. */
  retries?: number;
  /** Base backoff in ms, doubled per attempt. Defaults to `200`. */
  retryDelayMs?: number;
  /** How long a request may run before `status` becomes `'stalled'`. Defaults to `200`. */
  stalledSearchDelayMs?: number;
}
