# Unit FZ-1 — Typo tolerance (fuzzy matching) as an index configuration

One per-index, admin-controlled toggle: **Typo tolerance**. When on, free-text query terms also
match near-spellings (bounded edit distance); when off, behaviour is byte-identical to today.
Default **OFF** — turning it on changes ranking/recall on every existing host, so it is opt-in,
exactly like RK-1's popularity boost. No contract change, no JS change, no widget change.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/fz-1`).

## 1. Storage + admin surface

- Per-index settings row mirroring RK-1's `XpSearchPopularityIndexInfo` pattern exactly: a new
  module class (suggest `XpSearchFuzzyIndexInfo`: guid, index name, enabled bool), registered in
  the same module installer, covered by the RK-2 `InfoCreationSiteTests` source-scan guard.
  It is an **index-wide setting, not a tuning row**: experiments test tuning; both variants see
  the same typo tolerance (same reasoning as ADR-0025 — one sentence in the guide, no new ADR).
- Admin: header command + callout on the **Synonyms** listing (typo tolerance is query
  understanding, like synonyms), mirroring `FieldWeightListing.TogglePopularityBoost` verbatim:
  toggle command, callout headline/content for both states, success messages. Same
  cross-index-refusal guard. Beware the known 'command not found' recurrence on re-annotated
  [PageCommand] overrides — make the command a plain method on the final listing class (the
  pattern XP-1b pinned by reflection test).

## 2. Query behaviour (BuildQueryStage)

When the index's toggle is on, `BuildQueryStage` makes each query term fuzzy on **both** paths —
the plain escaped parse and the synonym-slot expansion (each whitespace token inside each
alternative):

- Policy is fixed, not configurable (one toggle, zero knobs): token length ≤ 2 → exact;
  3–5 chars → max 1 edit; ≥ 6 → max 2 edits. All-digit tokens stay exact. First letter must
  match: set the parser's `FuzzyPrefixLength = 1` (precision + Lucene automaton cost).
- Mechanism (verify, don't trust): keep `QueryParserBase.Escape` on the user text, then append
  the live `~N` suffix per token so `MultiFieldQueryParser` builds per-field `FuzzyQuery`s while
  `Operator.AND` still requires every position. Exact hits outrank fuzzy ones for free —
  `FuzzyQuery` discounts by distance. If the parse-with-suffix route fights the escaping,
  build `FuzzyQuery` per field/term directly instead; behaviour above is the requirement, the
  parser trick is only the suggested lazy path.
- The setting reaches the stage the way tuning does — extend whatever read the pipeline already
  performs per request (do NOT add a second uncached DB hit per search). Read it in Core through
  a seam with an off-by-default no-Admin default, like `IRelevanceTuningSource`/
  `IPopularitySignalStore` do (Core alone = off).
- `Explain` requests get a `fuzzy:on` entry in `QueryExplanations` (beside the `weight:` entries).

## 3. Correctness edges (each one is a test or a STOP)

- **Caching:** `CachedSearchPipeline` computes its key before executing
  (`SearchCacheKey.Compute(request, queryText, groups, experiment, signal.Version)`). The fuzzy
  flag MUST participate — fold it into the key the way `signal.Version` is, so flipping the
  toggle can never serve stale results. The flag read at that point must be as cheap as the
  popularity signal read (cached with the same invalidation discipline as other tuning reads).
- **Highlighting:** `FuzzyQuery` is a `MultiTermQuery`; the highlighter must rewrite it against
  the reader or snippets silently stop highlighting the matched (misspelled) term. Verify with a
  real index fixture (a fuzzy-only hit must still return a highlighted snippet). If the current
  highlighter path cannot do it without contortions, STOP and report — do not ship fuzzy with
  broken snippets.
- **AND semantics:** "red sofaa" with typo tolerance must still require both positions; a
  document containing only "sofa" must not match. Test on the synonym-slot path too.
- **Redirect/boost rules stay exact:** rule condition matching (`MatchesAnalyzed`) is untouched —
  the existing KNOWN-LIMITATIONS entry on fuzzy rule matching stands as a separate future opt-in.
  Suggestions (prefix-based) untouched. No did-you-mean — future unit.

## 4. Docs

- Guide: add a "Typo tolerance" section to the relevance-tuning guide (what it matches, the fixed
  length policy in a 3-row table, why exact still ranks first, off-by-default rationale,
  experiments-see-one-value note, where the toggle lives — screenshot per the manifest workflow).
- CHANGELOG `[Unreleased]` (additive, no breaking tag); KNOWN-LIMITATIONS: fixed policy — no
  per-field or per-request override (upgrade path: a contract `fuzzy` request field if a client
  ever needs to opt out per query); anything else honest.
- Append host-pass items to the HW-11 checklist (next free §): toggle on for DancingGoatSample,
  misspelled query ("expresso" is taken by the seeded synonym — use e.g. "grinderr") returns the
  right results with highlighted snippets, toggle off restores exact-only, no stale cached page
  in between.

## Deliverables

- Code + tests: policy edges (2/3/5/6-char tokens, digits, prefix letter), both query paths,
  AND preservation, escaping/suffix interplay, cache-key participation, highlight-with-fuzzy
  fixture, toggle command + callout states, info-class registration guard.
- All C# suites green; JS untouched. Conventional commits on `unit/fz-1`; commit this spec file.

## Constraints

- Kentico docs MCP for any admin-UI API question; no new dependencies; no contract regeneration.
- Never touch `src/Components/Widgets/CardWidget/`.
