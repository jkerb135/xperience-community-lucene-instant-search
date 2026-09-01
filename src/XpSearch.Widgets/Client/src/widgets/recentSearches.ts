/**
 * Recent searches, shared by the standalone `suggestions` widget and by `searchBox`'s integrated
 * panel. Internal: not exported from the package entry points.
 *
 * They are entirely client-side. The query log the server suggests from is anonymous by design — it
 * has no visitor correlator — so "your recent searches" can only honestly come from the visitor's own
 * browser. Nothing here is ever sent anywhere: the list lives in `localStorage` under
 * `xps-recent:<index>` and is read straight back into the panel.
 *
 * Every storage access is guarded: a private window can throw on `localStorage` itself, and a
 * quota-full or disabled store throws on write. The feature then silently does nothing, which is the
 * only acceptable failure mode for a convenience.
 */
import type { SuggestionsRenderState } from '../behaviors/suggestions';
import type { Suggestion } from '../types';

/** How many are kept. Enough to be useful, few enough that the panel stays a panel. */
const CAP = 5;

const keyFor = (index: string): string => `xps-recent:${index}`;

/** `window.localStorage` when it is usable, `undefined` when the browser refuses to hand it over. */
export function recentsStorage(windowRef?: Window): Storage | undefined {
  try {
    const win = windowRef ?? (typeof window === 'undefined' ? undefined : window);
    return win?.localStorage ?? undefined;
  } catch {
    return undefined;
  }
}

export function readRecents(storage: Storage | undefined, index: string): string[] {
  try {
    const raw = storage?.getItem(keyFor(index));
    if (raw === null || raw === undefined) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((entry): entry is string => typeof entry === 'string' && entry !== '').slice(0, CAP);
  } catch {
    // Someone else's key, hand-edited JSON, a quota error: an unreadable store has no recents.
    return [];
  }
}

export function recordRecent(storage: Storage | undefined, index: string, query: string): void {
  const text = query.trim();
  if (text === '') return;
  const kept = readRecents(storage, index).filter(
    (entry) => entry.toLowerCase() !== text.toLowerCase()
  );
  try {
    storage?.setItem(keyFor(index), JSON.stringify([text, ...kept].slice(0, CAP)));
  } catch {
    /* full, disabled, or private: the search still happened, which is what mattered */
  }
}

export function clearRecents(storage: Storage | undefined, index: string): void {
  try {
    storage?.removeItem(keyFor(index));
  } catch {
    /* nothing to clear if the store cannot be touched */
  }
}

export interface RecentsOptions {
  index: string;
  storage: Storage | undefined;
  /** Re-renders the panel after a change only the recents know about (focus, clear, arrow keys). */
  repaint(): void;
}

/**
 * The panel-layer half: it composes recents into the render state the suggestions *behaviour*
 * produces, and never touches that behaviour's transport or state machine. When no recent matches
 * the current input, everything but the recording is delegated straight through, so a widget with
 * an empty store behaves exactly as it did before.
 */
export interface Recents {
  /** Binds the focus-to-open and the group's Clear control. Call once, on the first render. */
  bind(input: HTMLInputElement, panel: HTMLElement): void;
  /** Records a query the visitor actually searched for. */
  record(query: string): void;
  /**
   * Wraps the behaviour's render state so the panel sees the recents as a third group.
   * `pick` runs a recent, exactly the way the widget runs a picked query suggestion.
   */
  wrap(api: SuggestionsRenderState, pick: (query: string) => void): SuggestionsRenderState;
}

/** Which source an entry came from: the server's own label, or the pre-SG-1 inference from `result`. */
export const groupOf = (suggestion: Suggestion): 'recent' | 'query' | 'document' =>
  suggestion.group === 'recent' || suggestion.group === 'query' || suggestion.group === 'document'
    ? suggestion.group
    : suggestion.result === undefined
      ? 'query'
      : 'document';

export function createRecents({ index, storage, repaint }: RecentsOptions): Recents {
  /** The field has focus, so the panel may open on the recents alone. */
  let focused = false;
  /** Active option over the *merged* list, owned here whenever a recent is showing. */
  let active = -1;

  const reset = (): void => {
    focused = false;
    active = -1;
  };

  return {
    bind(input, panel) {
      input.addEventListener('focus', () => {
        focused = true;
        repaint();
      });
      panel.addEventListener('click', (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        if (target.closest('[data-xps-recent-clear]') === null) return;
        clearRecents(storage, index);
        reset();
        repaint();
      });
    },

    record(query) {
      recordRecent(storage, index, query);
    },

    wrap(api, pick) {
      const prefix = api.query.trim().toLowerCase();
      const entries: Suggestion[] = readRecents(storage, index)
        .filter((text) => text.toLowerCase().startsWith(prefix))
        .map((text) => ({ text, group: 'recent' }));
      /** Whether the recents own the panel's highlight and openness this render. */
      const owns = entries.length > 0;
      const merged = owns ? [...entries, ...api.suggestions] : api.suggestions;
      if (active >= merged.length) active = -1;

      return {
        ...api,
        suggestions: merged,
        isOpen: api.isOpen || (owns && focused),
        activeIndex: owns ? active : api.activeIndex,

        move(to) {
          if (!owns) {
            api.move(to);
            return;
          }
          const count = merged.length;
          if (count === 0) return;
          focused = true;
          if (to === 'first') active = 0;
          else if (to === 'last') active = count - 1;
          else if (active === -1) active = to > 0 ? 0 : count - 1;
          else active = (active + to + count) % count;
          repaint();
        },

        select(at) {
          if (owns && at < entries.length) {
            const text = entries[at]?.text ?? '';
            reset();
            recordRecent(storage, index, text);
            pick(text);
            return;
          }
          const index_ = owns ? at - entries.length : at;
          const picked = api.suggestions[index_];
          reset();
          // A document suggestion is a link to a page, not a search anyone ran, so only the
          // suggestions that become a query are remembered.
          if (picked && groupOf(picked) === 'query') recordRecent(storage, index, picked.text);
          api.select(index_);
        },

        submit() {
          recordRecent(storage, index, api.query);
          reset();
          api.submit();
        },

        close() {
          reset();
          api.close();
        },

        clear() {
          reset();
          api.clear();
        },
      };
    },
  };
}
