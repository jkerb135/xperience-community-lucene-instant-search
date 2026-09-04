/**
 * The display names the widgets share (TH-12): what a filter code is called on screen.
 *
 * Two sources, both remembered per search instance so that two instances on one page never mix
 * taxonomies:
 *
 * - **the registry** — what a filtering widget declares about the attribute it owns (its `label`,
 *   a range's `unit`), so `activeFilters`, which owns no attribute, can name one;
 * - **the label memory** — the `label` the server sends with every facet value. It is never
 *   cleared by a response that lacks the attribute, so a refinement that returns nothing still
 *   has a name to show (the response that named it is long gone).
 *
 * Nothing here ever prints a stored code where a name was configured, and no surface may fall
 * back to the attribute code: a widget with no `label` shows the value alone and warns the
 * developer once.
 */
import type { SearchInstance, SearchResults } from './types';

/** What a filtering widget declares about the attribute it owns. */
export interface AttributeDisplay {
  /** The widget's `label`, empty/undefined when the developer set none. */
  label?: string | undefined;
  /** A range filter's `unit`: "USD", "kg". */
  unit?: string | undefined;
}

interface Memory {
  /** attribute -> value -> what the server called it, plus its ancestry. */
  values: Map<string, Map<string, { label: string; path: readonly string[] }>>;
  attributes: Map<string, AttributeDisplay>;
  warned: Set<string>;
  /** The response already folded in: rendering N widgets must not merge it N times. */
  last: SearchResults | null;
}

const memories = new WeakMap<SearchInstance, Memory>();

/**
 * The heading of a filter group whose widget declares no `label`. A group has to be called
 * something, and the one thing it must never be called is the attribute's field code.
 */
export const UNNAMED_GROUP = 'Filters';

/** Between the segments of a taxonomy path: a single right angle quote, spaced. */
const PATH_SEPARATOR = ' › ';

function memoryOf(search: SearchInstance): Memory {
  let memory = memories.get(search);
  if (!memory) {
    memory = { values: new Map(), attributes: new Map(), warned: new Set(), last: null };
    memories.set(search, memory);
  }
  return memory;
}

/** Folds a response's facet labels into the memory. Called for every render, cheap after the first. */
export function rememberFacetLabels(search: SearchInstance, results: SearchResults | null): void {
  const memory = memoryOf(search);
  if (results === null || results === memory.last) return;
  memory.last = results;
  for (const [attribute, values] of Object.entries(results.facets ?? {})) {
    let known = memory.values.get(attribute);
    if (!known) {
      known = new Map();
      memory.values.set(attribute, known);
    }
    for (const value of values ?? []) {
      known.set(value.value, { label: value.label ?? value.value, path: value.path ?? [] });
    }
  }
}

/**
 * Seeds the memory from the server-rendered first paint (FC-1): the `data-xps-labels` object the
 * results mount carries, `attribute -> value -> label`, for the values the visitor arrived
 * filtering by. A **trust boundary** — the attribute is markup — so anything that is not a string
 * label is ignored, and a value a response already named is never overwritten.
 */
export function seedValueLabels(search: SearchInstance, seed: Record<string, unknown>): void {
  const memory = memoryOf(search);
  for (const [attribute, values] of Object.entries(seed)) {
    if (!values || typeof values !== 'object' || Array.isArray(values)) continue;
    let known = memory.values.get(attribute);
    if (!known) {
      known = new Map();
      memory.values.set(attribute, known);
    }
    for (const [value, label] of Object.entries(values as Record<string, unknown>)) {
      if (typeof label === 'string' && label !== '' && !known.has(value)) {
        // No path: the seed names the selected values, and the first response brings the ancestry.
        known.set(value, { label, path: [] });
      }
    }
  }
}

/** What a filtering widget declares about its own attribute. */
export function declareAttribute(
  search: SearchInstance,
  attribute: string,
  display: AttributeDisplay
): void {
  const memory = memoryOf(search);
  const current = memory.attributes.get(attribute) ?? {};
  memory.attributes.set(attribute, {
    label: display.label || current.label,
    unit: display.unit || current.unit,
  });
}

/** The unit the range filter on `attribute` displays its own inputs with, if there is one. */
export function attributeUnit(search: SearchInstance, attribute: string): string | undefined {
  return memoryOf(search).attributes.get(attribute)?.unit || undefined;
}

/**
 * What to call `attribute` on screen: the caller's own `label` first, then whatever the widget
 * that owns the attribute declared. Never the attribute code — `undefined` means "no name", and
 * the caller renders the value without a prefix.
 */
export function attributeLabel(
  search: SearchInstance,
  attribute: string,
  explicit?: string
): string | undefined {
  return explicit || memoryOf(search).attributes.get(attribute)?.label || undefined;
}

/**
 * {@link attributeLabel}, plus a one-time developer warning naming the property to set. For the
 * surfaces that would otherwise have printed the attribute code.
 */
export function attributeLabelOrWarn(
  search: SearchInstance,
  attribute: string,
  widget: string,
  explicit?: string
): string | undefined {
  const name = attributeLabel(search, attribute, explicit);
  const memory = memoryOf(search);
  if (name === undefined && !memory.warned.has(attribute)) {
    memory.warned.add(attribute);
    console.warn(
      `[xpsearch] ${widget}: no "label" for attribute "${attribute}", so its name is left off the UI — a visitor must never read a field code. Set the widget's Label property.`
    );
  }
  return name;
}

/** The label the server last sent for one facet value, `undefined` when it never sent one. */
export function valueLabel(
  search: SearchInstance,
  attribute: string,
  value: string
): string | undefined {
  return memoryOf(search).values.get(attribute)?.get(value)?.label;
}

/**
 * A facet value as a visitor should read it: the remembered label, and for a value nested in a
 * taxonomy the whole open path, `Sweet › Acidy`, each segment named by its own remembered
 * label. Falls back to the stored value verbatim — never prettified, never guessed.
 */
export function displayValue(search: SearchInstance, attribute: string, value: string): string {
  const known = memoryOf(search).values.get(attribute);
  const entry = known?.get(value);
  if (entry === undefined) return value;
  if (entry.path.length === 0) return entry.label;
  return [...entry.path.map((step) => known?.get(step)?.label ?? step), entry.label].join(
    PATH_SEPARATOR
  );
}
