/**
 * The templating layer (spec 5.4): a tagged template that escapes by default, plus the two
 * helpers every widget template receives. No virtual DOM, no framework — a template produces a
 * trusted HTML string and `render` writes it with one `innerHTML` assignment.
 *
 * XSS model: everything interpolated into an `html` template is HTML-escaped, including the
 * value a template function returns as a plain string. Trusted markup has to be opted into,
 * either with `html.raw(value)` or by nesting another `html` result. Always quote attribute
 * values — `class="${x}"`, never `class=${x}` — because escaping covers quotes, not spaces.
 */
import type { Hit } from '../types';

const ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

/** HTML-escapes text for use in element content and in a quoted attribute value. */
export function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ESCAPES[character] ?? character);
}

/**
 * Markup that is trusted to be inserted as-is. A class rather than a plain object on purpose:
 * `instanceof` cannot be forged by a JSON payload that happens to carry the marker property.
 */
export class TemplateResult {
  constructor(readonly value: string) {}
  toString(): string {
    return this.value;
  }
}

/** What a template may return, and what may be interpolated into one. */
export type Renderable = TemplateResult | string | number | boolean | null | undefined | readonly Renderable[];

/** Resolves any renderable to trusted HTML, escaping everything that is not already trusted. */
export function toHtml(value: Renderable): string {
  if (value === null || value === undefined || typeof value === 'boolean') return '';
  if (value instanceof TemplateResult) return value.value;
  if (Array.isArray(value)) return (value as readonly Renderable[]).map(toHtml).join('');
  return escapeHtml(String(value));
}

function tag(strings: TemplateStringsArray, ...values: Renderable[]): TemplateResult {
  let out = strings[0] ?? '';
  for (let i = 0; i < values.length; i++) out += toHtml(values[i]) + (strings[i + 1] ?? '');
  return new TemplateResult(out);
}

/**
 * Tagged template producing escaped, trusted HTML:
 * ``html`<p>${untrusted}</p>` ``. Nest results, arrays of results, and use `html.raw` to opt a
 * string out of escaping.
 */
export const html = Object.assign(tag, {
  /** Marks a string as already-safe HTML. The one documented escape hatch (spec 5.7). */
  raw: (value: string): TemplateResult => new TemplateResult(String(value)),
});

/** Writes a template result into a container. One assignment, no diffing (spec 5.4). */
export function render(value: Renderable, container: Element): void {
  container.innerHTML = toHtml(value);
}

/**
 * The server's highlighted form of `field` (already HTML-encoded before the tags were inserted,
 * spec 4.6) with the shell class added to each `<mark>`; falls back to the escaped plain field.
 */
export function highlight<TItem extends Record<string, unknown>>(
  field: string,
  hit: Hit<TItem>
): TemplateResult {
  const marked = hit._highlights?.[field];
  if (typeof marked === 'string' && marked !== '') {
    return new TemplateResult(marked.replace(/<mark>/g, '<mark class="xps-highlight">'));
  }
  const plain = (hit as Record<string, unknown>)[field];
  return new TemplateResult(plain === null || plain === undefined ? '' : escapeHtml(String(plain)));
}

/** `Intl.NumberFormat` with the page's locale by default. */
export function formatNumber(value: number, locale?: string): string {
  return new Intl.NumberFormat(locale).format(value);
}

/** The helper bag every template receives as its last argument (spec 5.4). */
export interface TemplateHelpers {
  html: typeof html;
  highlight: typeof highlight;
  formatNumber: typeof formatNumber;
}

export const helpers: TemplateHelpers = { html, highlight, formatNumber };
