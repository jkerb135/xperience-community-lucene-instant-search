/**
 * `filterSort` — the mobile Filter & Sort bottom sheet (TH-2). A trigger button in the mount, and
 * a modal sheet appended to `document.body` on first open. Markup: `themes/fixtures/filter-sort.html`.
 *
 * It owns no behaviour of its own: it composes `withFacetList` once per configured attribute and
 * `withSortSelect`, and fans the widget lifecycle out to them (spec 5.7, the dogfooding rule).
 * Selections inside the sheet are PENDING — nothing refines until Apply, which replays the pending
 * toggles through the public `SearchActions` in one chain and searches once.
 */
import { withFacetList, type FacetListItem } from '../behaviors/facetList';
import { withSortSelect, type SortSelectItem } from '../behaviors/sortSelect';
import * as st from '../state';
import { html, render, type Renderable } from '../templates/html';
import type {
  RenderArgs,
  SearchActions,
  SearchInstance,
  SearchState,
  Widget,
} from '../types';
import { createRoot, resolveContainer, widgetId } from './dom';

/** One facet group in the sheet, in the order it is listed. */
export interface FilterSortFacet {
  attribute: string;
  /** Section heading. Defaults to `attribute`. */
  label?: string;
  /** Values listed. Defaults to the facet list's own default (10). */
  limit?: number;
}

export type FilterSortWidgetParams = {
  container: string | HTMLElement;
  facets: FilterSortFacet[];
  /** Same shape `sortSelect` takes. Omitted ⇒ no "Sort by" section. */
  sortOptions?: SortSelectItem[];
  /** Trigger and sheet heading. Defaults to "Filter & Sort". */
  label?: string;
  /**
   * Footer primary button. Defaults to `'Show {count} results'`. A `{count}` placeholder is
   * replaced with how many results the pending selection would return, from a debounced probe;
   * while that count is unknown it is dropped, with the space after it, leaving "Show results".
   */
  applyLabel?: string;
  /** Footer secondary button. Defaults to "Clear all". */
  clearLabel?: string;
  /** Accessible name of the close button. Defaults to "Close". */
  closeLabel?: string;
};

/** How long a pending change settles before the count preview is asked for. */
const PREVIEW_DEBOUNCE_MS = 250;
const FOCUSABLE = 'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled)';
const SEPARATOR = '\u0000';

export function filterSort(params: FilterSortWidgetParams): Widget {
  const container = resolveContainer(params.container, 'filterSort');
  const doc = container.ownerDocument;
  const facets = params.facets ?? [];
  const label = params.label ?? 'Filter & Sort';
  const applyLabel = params.applyLabel ?? 'Show {count} results';
  const clearLabel = params.clearLabel ?? 'Clear all';
  const closeLabel = params.closeLabel ?? 'Close';
  const sortOptions = params.sortOptions ?? [];

  /** Latest committed items per configured attribute, filled by the facet-list behaviours. */
  const items = new Map<string, FacetListItem[]>();
  let state: SearchState | undefined;
  let actions: SearchActions | undefined;
  let search: SearchInstance | undefined;

  // Pending selection: `pending` holds the values toggled since the sheet opened, `pendingClear`
  // is "Clear all" waiting for Apply. A row is checked when committed XOR pending-toggled.
  const pending = new Set<string>();
  let pendingClear = false;
  let pendingSort: string | undefined;

  // The apply button's live count. `previewSeq` is bumped by anything that invalidates an answer -
  // a newer pending change, Apply, closing the sheet - so a probe that lands afterwards is dropped.
  let previewCount: number | undefined;
  let previewTimer: ReturnType<typeof setTimeout> | undefined;
  let previewSeq = 0;

  let trigger: HTMLButtonElement | undefined;
  let badge: HTMLElement | undefined;
  let sheet: HTMLElement | undefined;
  let panel: HTMLElement | undefined;
  let previousOverflow = '';

  const key = (attribute: string, value: string): string => `${attribute}${SEPARATOR}${value}`;

  const committed = (attribute: string, value: string): boolean =>
    state?.filters.facets
      .find((facet) => facet.attribute === attribute)
      ?.values.includes(value) ?? false;

  const isChecked = (attribute: string, value: string): boolean => {
    const base = pendingClear ? false : committed(attribute, value);
    return pending.has(key(attribute, value)) ? !base : base;
  };

  /** Refinements the visitor can see on the trigger: committed values plus a non-default sort. */
  const refinementCount = (): number =>
    facets.reduce(
      (count, facet) =>
        count +
        (state?.filters.facets.find((entry) => entry.attribute === facet.attribute)?.values.length ??
          0),
      0
    ) + (sortOptions.length > 0 && state?.sort !== undefined && state.sort !== 'relevance' ? 1 : 0);

  // --- composed behaviours -------------------------------------------------
  // Each child is a real behaviour widget; the composite below drives its lifecycle. Nothing
  // here reaches past the render state the behaviours publish.
  const children: Widget[] = facets.map((facet) =>
    withFacetList<Record<string, unknown>>((options) => {
      items.set(facet.attribute, options.items);
    })({
      attribute: facet.attribute,
      ...(facet.limit === undefined ? {} : { limit: facet.limit }),
    })
  );

  if (sortOptions.length > 0) {
    children.push(
      withSortSelect<Record<string, unknown>>(() => {})({ items: sortOptions })
    );
  }

  // --- the apply button's live count ---------------------------------------

  /** The label, counted once a probe has answered and countless until then (and on failure). */
  const applyText = (): string =>
    previewCount === undefined
      ? applyLabel.replace('{count} ', '').replace('{count}', '')
      : applyLabel.split('{count}').join(String(previewCount));

  /** The state Apply would commit: the committed filters with the pending toggles applied. */
  const pendingState = (): SearchState => {
    let next = state ?? st.createState();
    if (pendingClear) for (const facet of facets) next = st.clearFilters(next, facet.attribute);
    for (const entry of pending) {
      const at = entry.indexOf(SEPARATOR);
      next = st.toggleFacet(next, entry.slice(0, at), entry.slice(at + 1));
    }
    return next;
  };

  /** Drops the debounced probe and discards whatever is already in flight. */
  const stopPreview = (): void => {
    if (previewTimer !== undefined) clearTimeout(previewTimer);
    previewTimer = undefined;
    previewSeq++;
  };

  const paintApply = (): void => {
    const button = panel?.querySelector('.xps-sheet__apply');
    if (button) button.textContent = applyText();
  };

  /**
   * Debounced "Show N results" preview of the pending selection: one probe, which the server never
   * journals, so ticking through the sheet leaves no trace in the analytics.
   */
  const preview = (): void => {
    stopPreview();
    const seq = previewSeq;
    previewTimer = setTimeout(() => {
      search?.probe({ filters: st.stateToWireFragment(pendingState()).filters }).then(
        ({ total }) => {
          // Superseded, applied or closed while it ran: the answer is about a selection that is
          // no longer pending.
          if (seq !== previewSeq || !panel) return;
          previewCount = total;
          paintApply();
        },
        // A failed probe leaves the countless label rather than an error in a footer button.
        () => {}
      );
    }, PREVIEW_DEBOUNCE_MS);
  };

  // --- the sheet -----------------------------------------------------------
  const id = (part: string): string => widgetId(container, 'filter-sort', part);

  const sectionHtml = (facet: FilterSortFacet): Renderable =>
    html`<section class="xps-sheet__section">
    <h3 class="xps-sheet__section-title">${facet.label ?? facet.attribute}</h3>
    <ul class="xps-sheet__values">${(items.get(facet.attribute) ?? []).map(
      (item) => html`<li class="xps-sheet__value">
        <label class="xps-sheet__value-label">
          <input class="xps-sheet__checkbox" type="checkbox" name="${facet.attribute}" value="${item.value}"${
            isChecked(facet.attribute, item.value) ? html.raw(' checked') : ''
          }>
          <span class="xps-sheet__value-text">${item.label}</span>
          <span class="xps-sheet__value-count">${item.count}</span>
        </label>
      </li>`
    )}</ul>
  </section>`;

  const build = (): HTMLElement => {
    const element = doc.createElement('div');
    element.className = 'xps xps-sheet';
    render(
      html`<div class="xps-sheet__backdrop"></div>
  <div class="xps-sheet__panel" role="dialog" aria-modal="true" aria-labelledby="${id('title')}">
    <header class="xps-sheet__header">
      <h2 class="xps-sheet__title" id="${id('title')}">${label}</h2>
      <button class="xps-sheet__close" type="button" aria-label="${closeLabel}"><span aria-hidden="true">&times;</span></button>
    </header>
    <div class="xps-sheet__body">${sortOptions.length > 0
      ? html`<section class="xps-sheet__section">
      <h3 class="xps-sheet__section-title" id="${id('sort')}">Sort by</h3>
      <div class="xps-sheet__pills" role="group" aria-labelledby="${id('sort')}">${sortOptions.map(
        (option) => {
          const selected = (pendingSort ?? state?.sort ?? 'relevance') === option.value;
          return html`<button class="xps-sheet__pill${
            selected ? ' xps-sheet__pill--selected' : ''
          }" type="button" aria-pressed="${String(selected)}" data-xps-sort="${option.value}">${option.label}</button>`;
        }
      )}</div>
    </section>`
      : ''}${facets.map(sectionHtml)}</div>
    <footer class="xps-sheet__footer">
      <button class="xps-button xps-sheet__clear" type="button">${clearLabel}</button>
      <button class="xps-button xps-button--primary xps-sheet__apply" type="button">${applyText()}</button>
    </footer>
  </div>`,
      element
    );
    doc.body.appendChild(element);

    element.addEventListener('click', (event) => {
      const target = event.target;
      if (!(target instanceof Element)) return;
      if (target.closest('.xps-sheet__backdrop')) close();
      else if (target.closest('.xps-sheet__close')) close();
      else if (target.closest('.xps-sheet__apply')) apply();
      else if (target.closest('.xps-sheet__clear')) clearAll();
      else {
        const pill = target.closest<HTMLElement>('[data-xps-sort]');
        if (pill) selectSort(pill.dataset['xpsSort'] ?? '');
      }
    });
    element.addEventListener('change', (event) => {
      const target = event.target;
      if (!(target instanceof HTMLInputElement) || target.type !== 'checkbox') return;
      const entry = key(target.name, target.value);
      if (pending.has(entry)) pending.delete(entry);
      else pending.add(entry);
      preview();
    });
    element.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        close();
        return;
      }
      if (event.key !== 'Tab' || !panel) return;
      // Focus trap: the sheet is modal, so Tab must never leave it for the page behind.
      const stops = [...panel.querySelectorAll<HTMLElement>(FOCUSABLE)];
      if (stops.length === 0) return;
      const first = stops[0] as HTMLElement;
      const last = stops[stops.length - 1] as HTMLElement;
      const active = doc.activeElement;
      if (event.shiftKey && (active === first || !panel.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      }
    });

    return element;
  };

  const selectSort = (value: string): void => {
    pendingSort = value;
    for (const pill of panel?.querySelectorAll<HTMLElement>('[data-xps-sort]') ?? []) {
      const selected = pill.dataset['xpsSort'] === value;
      pill.classList.toggle('xps-sheet__pill--selected', selected);
      pill.setAttribute('aria-pressed', String(selected));
    }
  };

  const clearAll = (): void => {
    pendingClear = true;
    pending.clear();
    for (const box of panel?.querySelectorAll<HTMLInputElement>('.xps-sheet__checkbox') ?? []) {
      box.checked = false;
    }
    preview();
  };

  const open = (): void => {
    if (sheet) sheet.remove();
    pending.clear();
    pendingClear = false;
    pendingSort = undefined;
    stopPreview();
    previewCount = undefined;
    sheet = build();
    panel = sheet.querySelector<HTMLElement>('.xps-sheet__panel') ?? undefined;
    previousOverflow = doc.body.style.overflow;
    doc.body.style.overflow = 'hidden';
    trigger?.setAttribute('aria-expanded', 'true');
    sheet.querySelector<HTMLButtonElement>('.xps-sheet__close')?.focus();
  };

  const close = (): void => {
    if (!sheet) return;
    sheet.remove();
    sheet = undefined;
    panel = undefined;
    stopPreview();
    previewCount = undefined;
    pending.clear();
    pendingClear = false;
    pendingSort = undefined;
    doc.body.style.overflow = previousOverflow;
    trigger?.setAttribute('aria-expanded', 'false');
    trigger?.focus();
  };

  const apply = (): void => {
    const sortChanged = pendingSort !== undefined && pendingSort !== state?.sort;
    if (!actions || (!pendingClear && pending.size === 0 && !sortChanged)) {
      close();
      return;
    }
    // One chain, one search: the state layer coalesces the renders each mutation would queue.
    let next = actions;
    if (pendingClear) for (const facet of facets) next = next.clearFilters(facet.attribute);
    for (const entry of pending) {
      const at = entry.indexOf(SEPARATOR);
      next = next.toggleFacet(entry.slice(0, at), entry.slice(at + 1));
    }
    if (sortChanged) next = next.setSort(pendingSort as string);
    next.search();
    close();
  };

  const paintTrigger = (): void => {
    if (!badge) return;
    const count = refinementCount();
    badge.textContent = String(count);
    badge.hidden = count === 0;
  };

  const widget: Widget = {
    $$type: 'filterSort',

    prepareRequest: (request) =>
      children.reduce((current, child) => child.prepareRequest?.(current) ?? current, request),

    init(options) {
      state = options.state;
      actions = options.actions;
      search = options.search;
      trigger = doc.createElement('button');
      trigger.className = 'xps-button xps-filter-sort__trigger';
      trigger.type = 'button';
      trigger.setAttribute('aria-haspopup', 'dialog');
      trigger.setAttribute('aria-expanded', 'false');
      render(
        html`<svg class="xps-filter-sort__icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false"><path d="M1 3h14l-5.5 6v4l-3 1.5V9z" fill="currentColor"/></svg>
  <span class="xps-filter-sort__label">${label}</span>
  <span class="xps-filter-sort__badge" hidden>0</span>`,
        trigger
      );
      badge = trigger.querySelector<HTMLElement>('.xps-filter-sort__badge') ?? undefined;
      trigger.addEventListener('click', () => (sheet ? close() : open()));
      createRoot(container, 'div', 'xps xps-filter-sort').appendChild(trigger);

      for (const child of children) child.init?.(options);
      paintTrigger();
    },

    render(options: RenderArgs) {
      state = options.state;
      actions = options.actions;
      search = options.search;
      for (const child of children) child.render?.(options);
      paintTrigger();
    },

    dispose() {
      for (const child of children) child.dispose?.();
      close();
      container.textContent = '';
    },
  };

  return widget;
}
