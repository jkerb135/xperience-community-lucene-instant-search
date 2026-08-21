/**
 * `xpsearch()` — the search instance: widget lifecycle, error isolation, the event bus,
 * routing, and the glue between `SearchState` and `SearchClient` (spec 5.1, 5.2, 5.5, 5.7).
 */
import { SearchClient } from './client';
import { createRouter } from './routing';
import * as st from './state';
import type {
  EventType,
  FacetOperator,
  InstantSearch,
  NumericOperator,
  RoutingOptions,
  SearchEvents,
  SearchHelper,
  SearchRequest,
  SearchResults,
  SearchState,
  SearchStatus,
  Widget,
  XpSearchOptions,
} from './types';

type Handler = (payload: never) => void;

export function xpsearch(options: XpSearchOptions): InstantSearch {
  if (!options?.index) throw new Error('xpsearch({ index }) is required.');

  const routingOptions: RoutingOptions =
    typeof options.routing === 'object' && options.routing !== null ? options.routing : {};
  const routingEnabled = Boolean(options.routing);
  const router = createRouter({ ...routingOptions, enabled: routingEnabled });

  const store = st.createStore(
    st.createState({ ...options.initialState, ...(routingEnabled ? router.read() : {}) })
  );

  const widgets: Widget[] = [];
  const rendered = new WeakSet<Widget>();
  const listeners = new Map<keyof SearchEvents, Set<Handler>>();
  const facetOperators: Record<string, FacetOperator> = {};

  let results: SearchResults | null = null;
  let status: SearchStatus = 'idle';
  let started = false;
  let disposed = false;
  let renderQueued = false;
  let stalledTimer: ReturnType<typeof setTimeout> | undefined;
  let unlistenPop: (() => void) | undefined;

  const emit = <K extends keyof SearchEvents>(event: K, payload: SearchEvents[K]): void => {
    for (const handler of [...(listeners.get(event) ?? [])]) {
      (handler as (p: SearchEvents[K]) => void)(payload);
    }
  };

  const report = (
    error: unknown,
    phase: SearchEvents['error']['phase'],
    widget?: string
  ): void => {
    const wrapped = error instanceof Error ? error : new Error(String(error));
    // Spec 5.7: log it, keep the rest of the page working.
    console.error(`[xpsearch] ${phase}${widget ? ` in widget "${widget}"` : ''}:`, wrapped);
    emit('error', { error: wrapped, phase, ...(widget === undefined ? {} : { widget }) });
  };

  const label = (widget: Widget, at: number): string => widget.$$type ?? `widget[${at}]`;

  /** Runs one widget hook in isolation: a thrower never stops the widgets after it. */
  const safely = <T>(widget: Widget, at: number, phase: SearchEvents['error']['phase'], fn: () => T): T | undefined => {
    try {
      return fn();
    } catch (error) {
      report(error, phase, label(widget, at));
      return undefined;
    }
  };

  const client = new SearchClient({
    ...(options.endpoint === undefined ? {} : { endpoint: options.endpoint }),
    ...(options.suggestEndpoint === undefined ? {} : { suggestEndpoint: options.suggestEndpoint }),
    ...(options.eventsEndpoint === undefined ? {} : { eventsEndpoint: options.eventsEndpoint }),
    ...(options.debounceMs === undefined ? {} : { debounceMs: options.debounceMs }),
    ...(options.headers === undefined ? {} : { headers: options.headers }),
    ...(options.fetchFn === undefined ? {} : { fetchFn: options.fetchFn }),
    ...(options.retries === undefined ? {} : { retries: options.retries }),
    ...(options.retryDelayMs === undefined ? {} : { retryDelayMs: options.retryDelayMs }),
    onError: (error) => report(error, 'contract'),
  });

  const buildRequest = (): SearchRequest => {
    let state = store.get();
    widgets.forEach((widget, at) => {
      if (!widget.getSearchParameters) return;
      const next = safely(widget, at, 'render', () => widget.getSearchParameters!(state));
      if (next) state = st.createState(next);
    });
    let request: SearchRequest = {
      index: options.index,
      ...st.stateToWireFragment(state, facetOperators),
      ...(options.facets === undefined ? {} : { facets: options.facets }),
      ...(options.highlight === undefined ? {} : { highlight: options.highlight }),
      ...(options.attributesToRetrieve === undefined
        ? {}
        : { attributesToRetrieve: options.attributesToRetrieve }),
      ...(options.language === undefined ? {} : { language: options.language }),
    };
    widgets.forEach((widget, at) => {
      if (!widget.getRequestParameters) return;
      const next = safely(widget, at, 'render', () => widget.getRequestParameters!(request));
      if (next) request = next;
    });
    if (request.facets) request.facets = [...new Set(request.facets)];
    return request;
  };

  const renderWidgets = (): void => {
    const state = store.get();
    widgets.forEach((widget, at) => {
      if (!widget.render) return;
      const isFirstRender = !rendered.has(widget);
      rendered.add(widget);
      safely(widget, at, 'render', () =>
        widget.render!({ results, state, helper, instantSearchInstance: instance, isFirstRender })
      );
    });
    emit('render', { results, state });
  };

  /** Coalesces the renders a chained helper call would otherwise trigger one per mutation. */
  const scheduleRender = (): void => {
    if (renderQueued || !started || disposed) return;
    renderQueued = true;
    queueMicrotask(() => {
      renderQueued = false;
      if (!disposed) renderWidgets();
    });
  };

  const setState = (next: SearchState): void => {
    const previous = store.get();
    store.set(next);
    emit('stateChange', { state: next });
    if (routingEnabled) {
      try {
        router.write(next, previous);
      } catch (error) {
        report(error, 'search');
      }
    }
    scheduleRender();
  };

  const runSearch = (): void => {
    if (disposed) return;
    status = 'loading';
    if (stalledTimer !== undefined) clearTimeout(stalledTimer);
    stalledTimer = setTimeout(() => {
      // A slow request is a rendering concern: `isSearchStalled` drives spinners (spec 5.7).
      if (status === 'loading') {
        status = 'stalled';
        scheduleRender();
      }
    }, options.stalledSearchDelayMs ?? 200);

    client.search(buildRequest()).then(
      (response) => {
        if (disposed || response === null) return; // superseded: a newer search owns the UI
        if (stalledTimer !== undefined) clearTimeout(stalledTimer);
        status = 'idle';
        results = response as SearchResults;
        renderWidgets();
      },
      (error: unknown) => {
        if (disposed) return;
        if (stalledTimer !== undefined) clearTimeout(stalledTimer);
        status = 'error';
        report(error, 'search');
        scheduleRender();
      }
    );
  };

  const helper: SearchHelper = {
    setQuery(q) {
      setState(st.setQuery(store.get(), q));
      return helper;
    },
    toggleFacetRefinement(attribute, value) {
      setState(st.toggleFacetRefinement(store.get(), attribute, value));
      return helper;
    },
    clearRefinements(attribute) {
      setState(st.clearRefinements(store.get(), attribute));
      return helper;
    },
    setPage(page) {
      setState(st.setPage(store.get(), page));
      return helper;
    },
    addNumericRefinement(attr, op, value) {
      setState(st.addNumericRefinement(store.get(), attr, op, value));
      return helper;
    },
    setSort(key) {
      setState(st.setSort(store.get(), key));
      return helper;
    },
    search() {
      runSearch();
    },
    getState: () => store.get(),
    setNumericRefinement(attr: string, op: NumericOperator, value: number) {
      setState(st.setNumericRefinement(store.get(), attr, op, value));
      return helper;
    },
    removeNumericRefinement(attr: string, op?: NumericOperator) {
      setState(st.removeNumericRefinement(store.get(), attr, op));
      return helper;
    },
    setHitsPerPage(hitsPerPage) {
      setState(st.setHitsPerPage(store.get(), hitsPerPage));
      return helper;
    },
    setFacetOperator(attribute, operator) {
      // Not part of the state: it is how the values of one attribute combine on the wire.
      facetOperators[attribute] = operator;
      return helper;
    },
  };

  const initWidget = (widget: Widget, at: number): void => {
    safely(widget, at, 'init', () =>
      widget.init?.({
        state: store.get(),
        helper,
        instantSearchInstance: instance,
        instantiate: instance,
      })
    );
  };

  const instance: InstantSearch = {
    get state() {
      return store.get();
    },
    get results() {
      return results;
    },
    get status() {
      return status;
    },
    helper,
    index: options.index,

    addWidgets(added) {
      for (const widget of added) {
        widgets.push(widget);
        if (started) initWidget(widget, widgets.length - 1);
      }
      if (started && added.length > 0) {
        scheduleRender();
        runSearch();
      }
      return instance;
    },

    removeWidgets(removed) {
      for (const widget of removed) {
        const at = widgets.indexOf(widget);
        if (at === -1) continue;
        safely(widget, at, 'dispose', () => widget.dispose?.());
        widgets.splice(at, 1);
      }
      if (started && removed.length > 0) runSearch();
      return instance;
    },

    start() {
      if (started) return instance;
      started = true;
      widgets.forEach(initWidget);
      unlistenPop = router.listen((route) => {
        // Back/forward: the URL is the state, and it must produce a fresh search.
        store.set(st.createState(route));
        emit('stateChange', { state: store.get() });
        renderWidgets();
        runSearch();
      });
      if (options.searchOnInitialLoad === false) renderWidgets();
      else runSearch();
      return instance;
    },

    dispose() {
      disposed = true;
      widgets.forEach((widget, at) => safely(widget, at, 'dispose', () => widget.dispose?.()));
      widgets.length = 0;
      unlistenPop?.();
      if (stalledTimer !== undefined) clearTimeout(stalledTimer);
      client.dispose();
      listeners.clear();
      started = false;
    },

    on(event, handler) {
      const set = listeners.get(event) ?? new Set<Handler>();
      set.add(handler as Handler);
      listeners.set(event, set);
      return instance;
    },

    off(event, handler) {
      listeners.get(event)?.delete(handler as Handler);
      return instance;
    },

    createURL(state) {
      return router.createURL(state ?? store.get());
    },

    sendEvent(eventType: EventType, objectID: string, position?: number) {
      const queryId = results?.queryId;
      // Without a queryId there is nothing to correlate the event with, so drop it silently.
      if (!queryId) return;
      client.sendEvent({
        eventType,
        objectID,
        queryId,
        index: options.index,
        ...(position === undefined ? {} : { position }),
      });
    },
  };

  return instance;
}

/** The transport behind an instance, for `connectAutocomplete` and direct callers. */
export { SearchClient } from './client';
