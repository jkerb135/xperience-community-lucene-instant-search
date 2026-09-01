/*
 * The keyboard half of the action reorder, to the WAI-ARIA drag pattern: space or enter on the grip
 * lifts a row, the arrow keys move it, space or enter drops it, escape puts it back where it was.
 *
 * It lives apart from ActionRow because this - the bounds, the landing index and what each step
 * announces - is the part worth a test. The HTML5 drag events themselves are not testable outside a
 * browser, so they stay in the components.
 */

/** A row that has been lifted and not yet dropped. */
export interface Grab {
  /** Where the row sat when it was lifted, so escape can put it back. */
  readonly from: number;
  /** Where it sits now, after any arrow-key moves. */
  readonly at: number;
}

export const lift = (index: number): Grab => ({ from: index, at: index });

/** Moves a lifted row one place, refusing to walk off either end of the list. */
export const step = (grab: Grab, by: 1 | -1, count: number): Grab => {
  const at = grab.at + by;

  return at < 0 || at >= count ? grab : { from: grab.from, at };
};

/**
 * The index a row dragged from `from` lands on when it is dropped into the gap before row `gap`
 * (`gap` runs 0..count, one past the last row meaning "at the end"). Taking the row out first shifts
 * every gap below it up by one, which is the whole of the arithmetic.
 */
export const landing = (from: number, gap: number): number => (gap > from ? gap - 1 : gap);

/** The grip's accessible name: what it reorders, and where that currently sits. */
export const gripLabel = (label: string, index: number, count: number): string =>
  `Reorder ${label}, ${index + 1} of ${count}`;

/**
 * What the polite live region says. A screen reader gets no other clue that the list re-ordered,
 * because the rows themselves are silent when they swap.
 */
export const announce = {
  grabbed: (label: string, at: number, count: number): string =>
    `${label} grabbed, position ${at + 1} of ${count}. Use the up and down arrow keys to move it, space or enter to drop it, escape to cancel.`,
  moved: (label: string, at: number, count: number): string => `${label} moved to position ${at + 1} of ${count}.`,
  dropped: (label: string, at: number, count: number): string => `${label} dropped at position ${at + 1} of ${count}.`,
  cancelled: (label: string, at: number, count: number): string =>
    `Reorder cancelled. ${label} is back at position ${at + 1} of ${count}.`,
};
