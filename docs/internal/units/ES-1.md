# Unit ES-1 — Probe requests + counted recovery states + skeleton parity

PAUL plan 1.1-02. Acceptance = host-pass checklist §Q items 82–84 flipping from KNOWN FAIL to
pass. Source mocks: NO RESULTS–FILTERS ACTIVE ("There are **7 results** without them" +
"Clear filters and show 7 results") and LOADING–FIRST SEARCH (skeleton matches the card
layout), plus TH-2's sheet apply button ("Show N results" live preview) which the same
mechanism unblocks.

Library unit, worktree branch `unit/es-1`. Read `docs/internal/agent-primer.md` first.
DISPATCH NOTE: branches from post-FZ-1 main — FZ-1 touched `BuildQueryStage`, the cache key
and `CachedSearchPipeline`; read what it did there before editing the same files.

## 1. Contract: `probe` on `SearchRequest` (the one coordinated contract change)

- Add optional boolean `probe` (default false) to the contract schema; regenerate
  (`contract:gen` + the C# emitter) — additive, CHANGELOG'd, no version bump beyond the
  established additive convention (check how PB-6 documented `queryId`'s reuse and follow it).
- Server semantics (`CachedSearchPipeline` + `SearchRequestJournal` seam): a probe request is
  answered normally (pipeline, rules, cache participation all unchanged — a cached probe is
  fine and cheap) but is NEVER journaled: no query-log row, no search activity, no experiment
  exposure recording if any happens at journal time. Implement at the journal call sites, not
  scattered. C# tests: probe → `ISearchRequestJournal.Record` not called, on both the cache-hit
  and cache-miss paths; non-probe unchanged.
- Analytics honesty is the entire point — a probe must be invisible to every report,
  suggestion miner, and popularity signal (they all read the journal's outputs, so the journal
  skip covers them; assert the query-log enqueue specifically).

## 2. JS: a public probe capability on the instance

TH-2's report found widgets cannot construct a matching `SearchClient` (the instance exposes
`index` but not endpoint/headers/fetchFn). Fix the gap properly, once:

- `SearchInstance` gains `probe(overrides): Promise<{ total: number }>` (name/shape may follow
  existing naming conventions — check `types.ts` idiom): runs ONE request through the
  instance's own client with `probe: true`, built from the current committed state with the
  caller's overrides applied (e.g. `{ filters: none }` for the unfiltered count, or a pending
  filter set for the sheet). It never touches instance state, never renders, never journals
  (server-side per §1), and is NOT debounced itself — callers debounce.
- Documented in the JS guide (`js-client.md` / widget-reference, wherever instance methods
  live), typed, exported types updated.

## 3. Consumers

- **Filtered empty state** (`results.ts` `defaultEmpty`, TH-1's refined variant): when
  `hasRefinements`, fire a ~250ms-debounced unfiltered probe; render "There are **N results**
  without them." and the button text "Clear filters and show N results". Stale-probe results
  discarded (query/state changed since issue); probe failure or N=0 → today's countless copy
  and button (never an error, and N=0 must NOT claim "0 results" — countless fallback).
  `templates.empty` data gains the optional count so custom templates get it too.
- **filterSort sheet** (TH-2): the apply button becomes the live "Show {count} results"
  preview the original TH-2 spec described — debounced probe on each pending change using
  committed+pending filters, stale discard, in-flight probe discarded at Apply, fallback to
  countless label. Restore the `{count}` placeholder handling in `applyLabel` (default label
  becomes the counted form; PB `ApplyLabel` docs updated).
- Remove the two KNOWN-LIMITATIONS entries this obsoletes (TH-1's unfiltered-count ceiling,
  TH-2's no-live-count entry) and TH-2's "second, smaller gap" note about unreachable
  transport if it lives anywhere citable.

## 4. Visual edge-state parity (same artboards)

- **Empty-state icon**: the magnifier-with-minus glyph above the copy (inline SVG, 24px grid,
  `currentColor`, `aria-hidden`), both empty variants, muted color in the default theme.
- **Skeleton parity**: the first-search skeleton rows gain the media square beside the text
  lines, matching the thumbnail card's layout (`themes` fixtures + MARKUP.md updated; behavior
  unchanged — first-search-only, refinements dim).
- Razor/SSR note: server-rendered empty stays the simple block (recovery states are
  client-side by design — one guide sentence, not a limitation entry).

## 5. Verification

- C# suites green incl. the new journal-skip tests; JS suite: probe method (state untouched,
  probe:true on the wire), both consumers' debounce/stale/fallback/zero cases, `{count}`
  label, icon + skeleton fixtures; `contract:check`/`docs:check` regenerated and clean;
  themes build + check green.
- CHANGELOG (additive contract member leads the entry); guides updated (empty-state section,
  sheet section, instance probe method); checklist §Q items 82–84 KNOWN-FAIL lines updated to
  walkable (permitted checklist edit).
- Host follow-up is NOT yours: the demo picks this up via bundle rebuild (lead does it) —
  note it in the report.

## Constraints

- Kentico docs MCP for any Xperience question. No new dependencies. The probe flag is the
  ONLY contract change. Never touch `src/Components/Widgets/CardWidget/` (host is out of
  scope entirely for this unit).
