/**
 * The four DOM chores every default renderer shares. Nothing here is widget-specific.
 * The markup rules referenced below live in `themes/MARKUP.md`.
 */
import { render, type Renderable } from '../templates/html';

/** `container` accepts a CSS selector or an element (spec 5.2). */
export function resolveContainer(
  container: string | HTMLElement | undefined,
  widget: string
): HTMLElement {
  const node = typeof container === 'string' ? document.querySelector(container) : container;
  if (!node || typeof (node as HTMLElement).appendChild !== 'function') {
    throw new Error(
      `${widget}({ container }): no element found for ${JSON.stringify(container ?? null)}.`
    );
  }
  return node as HTMLElement;
}

const usedIds = new Set<string>();
/** container -> widget name -> the `xps-{instance}-{widget}` prefix already handed out for it. */
const bases = new WeakMap<HTMLElement, Map<string, string>>();

/**
 * The element id of one part of one widget, following MARKUP.md rule 4:
 * `xps-{instance}-{widget}-{part}`.
 *
 * The instance is the mount's `data-xps-instance`, else the container's own `id`, else
 * `default` — a Page Builder mount element carries no `id`, so `data-xps-instance` is the only
 * thing that distinguishes two instances on one page. Ids must be unique across the page, so the
 * second widget of the same name in the same instance gets `-2` appended to the `{widget}`
 * segment (`xps-search-1-sort-select-2-select`), the third `-3`, and so on.
 *
 * Every part of the same widget in the same container shares one prefix, so calling this once per
 * part is safe and a re-render does not renumber anything.
 */
export function widgetId(container: HTMLElement, widget: string, part: string): string {
  let perWidget = bases.get(container);
  if (!perWidget) {
    perWidget = new Map<string, string>();
    bases.set(container, perWidget);
  }
  let base = perWidget.get(widget);
  if (base === undefined) {
    const instance = container.dataset['xpsInstance'] || container.id || 'default';
    const prefix = `xps-${instance}-${widget}`;
    base = prefix;
    for (let n = 2; usedIds.has(base); n++) base = `${prefix}-${n}`;
    usedIds.add(base);
    perWidget.set(widget, base);
  }
  return `${base}-${part}`;
}

/**
 * Empties `container` and puts the widget root inside it — the mount element itself is never the
 * root, so an unhydrated `.xps-mount` stays unstyled (MARKUP.md, "Page Builder mount").
 */
export function createRoot(container: HTMLElement, tagName: string, className: string): HTMLElement {
  container.textContent = '';
  const root = container.ownerDocument.createElement(tagName);
  root.className = className;
  container.appendChild(root);
  return root;
}

/**
 * Re-renders `root` and puts focus back on the control that had it, matched by its rendered
 * text. Used where the whole widget is rebuilt (pagination, chips): without it, activating a
 * control with the keyboard drops focus to the body.
 */
export function renderKeepingFocus(value: Renderable, root: HTMLElement): void {
  const active = root.ownerDocument.activeElement;
  const focused = active instanceof HTMLElement && root.contains(active) ? active.textContent : null;
  render(value, root);
  if (focused === null) return;
  for (const candidate of root.querySelectorAll<HTMLElement>('a[href], button, input, select')) {
    if (candidate.textContent === focused) {
      candidate.focus();
      return;
    }
  }
}

/** Sets or removes a boolean attribute. */
export function setAttr(element: Element, name: string, on: boolean, value = 'true'): void {
  if (on) element.setAttribute(name, value);
  else element.removeAttribute(name);
}
