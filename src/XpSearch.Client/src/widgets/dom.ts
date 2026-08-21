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
let anonymous = 0;

/**
 * The `xps-{instance}-{widget}` id prefix of MARKUP.md rule 4. The instance is the mount's
 * `data-xps-instance` or the container's id; ids must be unique across the page, so a repeat
 * gets a numeric suffix.
 */
export function idBase(container: HTMLElement, widget: string): string {
  const instance = container.dataset['xpsInstance'] || container.id || `s${++anonymous}`;
  const base = `xps-${instance}-${widget}`;
  let unique = base;
  for (let n = 2; usedIds.has(unique); n++) unique = `${base}-${n}`;
  usedIds.add(unique);
  return unique;
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
