# Unit SG-1 — Mixed suggestions, recent searches, no-results recovery

PAUL plan 04-04. Acceptance = host-pass checklist items 85–86 flipping from KNOWN FAIL to
walkable, plus the HW-14 gap report items 1–3 closed. Source mocks: the autocomplete artboard
(grouped panel: recent searches + suggestions + pages) and the NO RESULTS–WITH RECOVERY
artboard ("Did you mean …?" + popular-search chips) of the approved kentico-violet mockup
(artifact 4ce0cf5e-4e3d-49a3-8340-be4e9c78da9c).

Library unit, worktree branch `unit/sg-1` (already created for you). Read
`docs/internal/agent-primer.md` first. ES-1 recently touched `results.ts`'s empty state, the
contract schema, and the journal call site — read what it did (`docs/internal/units/ES-1.md`
and the code) before editing the same places.

Three features, one coordinated contract regen. Background evidence:

- `DocumentSuggestService.cs:84` — `SuggestMode` is a hard either/or: `QuerySuggestions`
  returns early, `Documents` never consults the query log. The panel
  (`suggestionsPanel.ts:126-132`) already renders grouped output when a response mixes both
  (it infers group from `result` presence) — the server just never mixes. `groupLabels` is
  dead config today.
- `QuerySuggestionService.cs:64-99` — all-visitor popularity over the query log, prefix via
  `StartsWith`; note an EMPTY prefix matches everything, i.e. this service already computes
  "popular searches" for free. The query log is anonymous BY DESIGN (SY-1 investigation: no
  visitor correlator), so a server-side "your recent searches" is impossible without new
  tracking — recents are therefore CLIENT-SIDE (§3), the only honest per-visitor source.
- ES-1 gave `SearchRequest` a `probe` flag that skips the single `Journal()` call site —
  reuse it for every internal verification search this unit makes (§4).

## 1. Contract (the one coordinated regen)

Three additive members in `contract/xpsearch-api.schema.json`, then `contract:gen` + the C#
emitter, CHANGELOG'd per the established additive convention (see how ES-1 documented
`probe`). These are the ONLY contract changes:

- `Suggestion.group` — optional string enum `"query" | "document" | "recent"`. The server
  emits `"query"`/`"document"` (all modes, not just Mixed); `"recent"` exists for the
  client-side entries so one type spans the panel (the server never sends it — say so in the
  description). The panel prefers `group` and falls back to today's `result`-presence
  inference (old server, new client).
- `SearchResponse.didYouMean` — optional string: a corrected query the server verified has
  results. Present only when `totalHits == 0`, did-you-mean is enabled for the index, and a
  verified correction exists.
- `SearchResponse.popularSearches` — optional array of strings: the index's most-searched
  queries, most popular first. Present only when `totalHits == 0` and the host opted in.

Both response members are omitted (not null) otherwise, and never present on `probe: true`
responses (§4 — also prevents recursion).

## 2. Core: `SuggestMode.Mixed`

- Add `Mixed` to the `SuggestMode` enum (`XpSearchOptions.cs:9`): one response containing
  query suggestions AND document suggestions, queries first (matching the panel's visual
  order). Limit split: queries get up to `limit / 2` (integer division, minimum 1 when any
  exist), documents fill the remainder, and unused share backfills the other source — the
  response never exceeds `limit`. Deterministic; test the split incl. backfill both ways.
- `DocumentSuggestService` refactors from the early-return into per-source builders; every
  emitted `Suggestion` now carries `group`. Existing modes' behavior otherwise byte-stable
  (pin with existing tests).
- No `SuggestRequest` change — mode stays server-side per-index config, as its schema
  description already promises.

## 3. JS: recent searches (client-side, both panel consumers)

- Shared internal module (beside `suggestionsPanel.ts`): a recents store over
  `localStorage`, key `xps-recent:<index>`, capped at 5, case-insensitively deduped, most
  recent first, every read/write in try/catch (private windows throw — the feature silently
  disables). Nothing is ever sent to the server; say so in the guide.
- Recording: a submitted search (search box submit, suggestion picked) records the query;
  blank/whitespace never recorded.
- Rendering: recents appear as a third panel group ABOVE queries/documents, prefix-filtered
  by the current input; with an EMPTY input, focusing the field opens the panel showing
  recents alone (today an empty prefix keeps the panel closed — this is the one behavior
  change, and only when recents exist). Group header gets a small "Clear" control that
  empties the store and closes the group. `PanelOptions.groupLabels` gains `recent`
  (default label "Recent searches"); picking a recent runs that query exactly like picking a
  query suggestion.
- Applies to BOTH consumers (standalone `suggestions` widget + `searchBox` integrated
  panel) via the shared module; opt-out param `recentSearches: false` (default ON —
  [[feedback-default-is-the-design]]; the mockup shows them). PB widgets: a checkbox on the
  two widgets that expose suggestions (follow TH-3's `EnableSuggestions` property shape).
- STOP clause: if injecting recents means rewriting the suggestions *behaviour*'s transport
  or state machine (rather than composing at the widget/panel layer where the render state
  is already in hand), stop and report the design before proceeding.

## 4. Core: did-you-mean

- New per-index option `XpSearchIndexOptions.DidYouMean` (bool, default TRUE — the mockup
  shows it; checklist 85 must walk on the unconfigured demo).
- Engine: `DirectSpellChecker` from `Lucene.Net.Suggest` **4.8.0-beta00017** (same pinned
  family — add to `Directory.Packages.props` + the Core csproj; this dependency addition is
  explicitly authorized). It reads live index terms directly, no sidecar spell index. STOP
  clause: if it turns out a sidecar/maintenance structure is required after all, stop and
  report — do not build index-maintenance machinery.
- Placement: after the pipeline answers, when `TotalHits == 0 && request.Probe != true` and
  the option is on — correct the query's terms against the SAME field set the query searches
  (read what FZ-1 did in `BuildQueryStage` for the field handling), keep terms that already
  match, and VERIFY the corrected query by running it through the pipeline with
  `probe: true` (journal-skipped — analytics honesty is mandatory; assert no journal record
  from a verification search). Emit `didYouMean` only when the verified correction has
  `TotalHits > 0`; at most one verification search per request. Cap correction work
  sensibly (first suggestion per term).
- FZ-1 interplay: fuzzy-enabled indexes rarely reach zero hits on typos, so did-you-mean is
  naturally the fallback for the misses — one guide sentence, no special casing.
- Cache: the enriched response is cached as-is (60s TTL is fine); recovery members are part
  of the cached entry, and the cache key needs no change.

## 5. Core: popular searches on no-results

- New per-index option `XpSearchIndexOptions.PopularSearchesOnNoResults` (int count, default
  0 = OFF — checklist 86: "rendered only when the host enables it"; it exposes query-log
  text to anonymous visitors, so opt-in is deliberate).
- When `TotalHits == 0 && request.Probe != true` and count > 0: fill
  `popularSearches` from `IQuerySuggestionSource.SuggestAsync(index, "", count, ct)` — the
  empty prefix already returns the top queries (evidence above). No new endpoint, no new
  service; its existing cache makes this cheap.
- Same enrichment site as §4 (one small recovery step, not two).

## 6. JS: recovery rendering

- Surface `didYouMean` + `popularSearches` through the state layer to the results widget;
  `EmptyTemplateData` (`results.ts:23`) gains both as optionals so custom templates get
  them.
- `defaultEmpty`: "Did you mean **<correction>**?" — clicking runs the corrected query
  (sets the query and searches, same path as a query-suggestion pick). Below it, popular
  chips (reuse an existing chip/button class if one fits — check `activeFilters`' chips and
  `.xps-button--link` before inventing CSS) that run their query on click. Both render only
  when present in the response; the ES-1 counted-recovery and countless fallbacks are
  unchanged.
- SSR/Razor empty state stays the simple block (ES-1 precedent — recovery is client-side by
  design; one guide sentence).
- Themes: fixtures + MARKUP.md updated for the new panel group, clear control, did-you-mean
  line, and chips; `npm run build` + themes check green (contrast rules apply to any new
  colored element).

## 7. Verification

- C# suites green: Mixed split/backfill, `group` emission, did-you-mean (verified
  correction; zero-hit gate; option off; probe requests never enriched; verification search
  not journaled — assert on the journal seam), popular (opt-in gate, count, empty-prefix
  reuse), existing modes pinned.
- JS suites green: panel grouping via `group` with inference fallback, recents store
  (try/catch, dedupe, cap, clear, empty-input focus open, opt-out), did-you-mean click,
  chips click, `EmptyTemplateData` additions; e2e/mock server updated to emit the new
  members.
- `contract:check` + `docs:check` clean; CHANGELOG (additive contract members lead the
  entry); guides updated: autocomplete/suggest guide (Mixed mode, recents incl. the
  localStorage privacy sentence, groupLabels now live), empty-state section (recovery
  states + the two options), `migrating-from-algolia` ONLY via its template + map (ES-1
  fixed drift here — never hand-edit the generated page).
- KNOWN-LIMITATIONS: remove/amend the dead-groupLabels entry if present; grep for it.
- Checklist: append a NEW section **§S, items numbered 95+** (the last section is §R ending
  at 94 — a previous unit collided numbers by branching early; verify against the file in
  YOUR worktree and renumber if it moved): mixed grouped panel, recents record/persist/
  clear/opt-out, did-you-mean walk (misspelling with fuzzy OFF), popular chips walk after
  enabling the option on the demo index, analytics honesty check (no query-log rows from
  verification searches). Update items 85–86's KNOWN-FAIL lines to point at §S (permitted
  checklist edit).
- Host follow-up is NOT yours (demo wiring, bundle rebuild, option enablement — lead does
  it); note what the demo needs in your report.
- Commit this spec file with the unit (copy it from
  `docs/internal/units/SG-1.md` on main if your worktree predates it).

## Constraints

- Kentico docs MCP for any Xperience question. `Lucene.Net.Suggest` is the only new
  dependency. §1's three members are the ONLY contract changes. Core must not gain
  Admin/Page Builder dependencies. Never touch `src/Components/Widgets/CardWidget/`. Host
  is out of scope entirely.
