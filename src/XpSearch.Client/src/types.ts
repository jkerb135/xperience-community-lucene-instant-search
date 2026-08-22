/**
 * The public type surface of @yourco/xperience-search (spec 5.7, "Widget SDK contract").
 * Breaking anything in this file is a semver-major event.
 */
import type {
  EventType,
  FacetFilter,
  FacetOperator,
  FacetValue,
  HighlightOptions,
  NumericFilter,
  NumericOperator,
  Result as WireResult,
  SearchRedirect,
  SearchRequest,
  SearchResponse,
} from './contract/generated';

export type {
  EventType,
  FacetFilter,
  FacetOperator,
  FacetValue,
  HighlightOptions,
  NumericFilter,
  NumericOperator,
  SearchRedirect,
  SearchRequest,
  SearchResponse,
};

/**
 * One search result, typed. The wire result is closed, so the caller's document shape is applied
 * to `attributes` alone: `result.attributes.title` is typed, and no field of theirs can ever
 * shadow `id`, `score`, `highlights` or `ranking`.
 */
export type Result<TAttributes extends Record<string, unknown> = Record<string, unknown>> = Omit<
  WireResult,
  'attributes'
> & { attributes: TAttributes };

/** A `SearchResponse` whose results carry the caller's document shape. */
export interface SearchResults<TAttributes extends Record<string, unknown> = Record<string, unknown>>
  extends Omit<SearchResponse, 'results'> {
  results: Array<Result<TAttributes>>;
}

/** The refinements held in state, shaped exactly as they go on the wire (contract §4.2). */
export interface StateFilters {
  /** One entry per attribute, ANDed together. */
  readonly facets: readonly FacetFilter[];
  /** Numeric comparisons, ANDed together. */
  readonly numeric: readonly NumericFilter[];
}

/**
 * The search state. Pure, serializable, frozen — widgets read it and never write to it;
 * every mutation goes through {@link SearchActions}.
 */
export interface SearchState {
  readonly query: string;
  /** One-based, like the wire contract and like the default route mapping. */
  readonly page: number;
  readonly filters: StateFilters;
  /** `"relevance"` (the default) or an index-configured sort key. */
  readonly sort: string;
  readonly pageSize?: number;
}

/**
 * The only sanctioned way to mutate state (spec 5.7). Mutators are chainable and never search;
 * call {@link SearchActions.search} when the state is where you want it.
 */
export interface SearchActions {
  setQuery(query: string): SearchActions;
  toggleFacet(attribute: string, value: string): SearchActions;
  /** Clears every filter, or every filter on one attribute (facet and numeric alike). */
  clearFilters(attribute?: string): SearchActions;
  setPage(page: number): SearchActions;
  /** Sets the bound on `attribute` for `operator`, replacing any existing one. */
  setNumericFilter(attribute: string, operator: NumericOperator, value: number): SearchActions;
  /** Removes numeric filters on `attribute`, optionally narrowed to one operator. */
  removeNumericFilter(attribute: string, operator?: NumericOperator): SearchActions;
  setSort(key: string): SearchActions;
  setPageSize(pageSize: number | undefined): SearchActions;
  /** Declares how the values selected on one attribute combine. Defaults to `'or'`. */
  setFacetOperator(attribute: string, operator: FacetOperator): SearchActions;
  /** Current state. Frozen: assigning to it is a no-op (a TypeError under strict mode). */
  getState(): SearchState;
  search(): void;
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
  actions: SearchActions;
  search: SearchInstance;
}

/** Arguments of {@link Widget.render}. */
export interface RenderArgs {
  results: SearchResults | null;
  state: SearchState;
  actions: SearchActions;
  search: SearchInstance;
  isFirstRender: boolean;
}

/** A widget. Every member is optional; a widget that only renders is legal. */
export interface Widget {
  /** Identifier used in error messages and by the mount bootstrap. */
  $$type?: string;
  /** Contributes to the outgoing state. Applied in widget-add order (spec 5.7). */
  prepareState?(state: SearchState): SearchState;
  /**
   * Contributes request fields that are not state — `facets`, `highlight`, `fields`.
   * Applied in widget-add order, after `prepareState`.
   */
  prepareRequest?(request: SearchRequest): SearchRequest;
  init?(options: InitOptions): void;
  render?(options: RenderArgs): void;
  dispose?(): void;
}

/** Base render options handed to every behaviour's render function (spec 5.7). */
export interface RenderOptions<TParams> {
  params: TParams;
  results: SearchResults | null;
  state: SearchState;
  actions: SearchActions;
  search: SearchInstance;
}

/** A widget factory, as produced by every behaviour. */
export type WidgetFactory<TParams> = (params: TParams) => Widget;

/** The search instance returned by `createSearch()`. */
export interface SearchInstance {
  readonly state: SearchState;
  readonly results: SearchResults | null;
  readonly status: SearchStatus;
  readonly actions: SearchActions;
  readonly index: string;
  addWidgets(widgets: Widget[]): SearchInstance;
  removeWidgets(widgets: Widget[]): SearchInstance;
  start(): SearchInstance;
  dispose(): void;
  on<K extends keyof SearchEvents>(
    event: K,
    handler: (payload: SearchEvents[K]) => void
  ): SearchInstance;
  off<K extends keyof SearchEvents>(
    event: K,
    handler: (payload: SearchEvents[K]) => void
  ): SearchInstance;
  /** URL for `state` under the active route mapping. Used by every behaviour's `urlFor`. */
  urlFor(state?: SearchState): string;
  /** Fire-and-forget analytics event, correlated with the last response's `queryId`. */
  sendEvent(type: EventType, resultId: string, position?: number): void;
}

/** `routing: { stateToRoute, routeToState }` (spec 5.5). */
export interface RoutingOptions {
  stateToRoute?(state: SearchState): Record<string, string | string[]>;
  routeToState?(route: Record<string, string[]>): Partial<SearchState>;
  /** `window` by default; injectable for tests. */
  windowRef?: Window;
}

/** Options of `createSearch()` (spec 5.2). */
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
  /** Document fields to project into `result.attributes`. */
  fields?: string[];
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
