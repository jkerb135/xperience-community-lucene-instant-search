# Unit EX-2 — §10.7 worked example 2: a computed relevance field

PAUL plan 04-02: spec §10.7 asks for two worked extensibility examples in the sample project.
Example 1 (linked items flattened into the parent document) ships —
`docs/guides/indexing-strategy.md` § "Linked items" walks Dancing Goat's real
`FlattenLinkedItems` registration. Example 2 is "a computed relevance field (e.g. popularity
from the analytics store in §9.2, fed back into ranking)". This unit builds it FOR REAL on the
sample host and documents it as a guide walkthrough of that real code
([[feedback-docs-wiki-ready]] / [[feedback-default-is-the-design]]: guides walk shipped code,
never pseudo-samples).

Hybrid unit, HW-13 style: host changes are file-by-file (the host `F:\Personal\CommunityProjects\src`
is NOT a git repo — report them; never commit there), library changes (guide + checklist +
this spec) go on worktree branch `unit/ex-2` (already created for you). Read
`docs/internal/agent-primer.md` first.

## Context you must hold before designing

- RK-1 already ships THIS EXACT SIGNAL as a no-code feature: `PopularityBoostStage`
  (`src/XpSearch.Core/Pipeline/Stages/PopularityBoostStage.cs`, order 750, bounded SHOULD
  clause on clicked ids, per-index admin toggle — guide `docs/guides/popularity-boosts.md`).
  The worked example is NOT a replacement and must not read as one: its point is the
  PATTERN — compute any signal of your own at index time and feed it into ranking — using
  click data only because every site already has it. The guide section must open with that
  framing and link popularity-boosts.md ("want exactly this without code? toggle it on").
- The two extension surfaces this example exercises are the sanctioned ones
  ([[feedback-extend-via-plugins]]): the indexing strategy (`ContributeAsync` + IX-1's
  `indexing.AddField` declaration — the guide's "Adding fields of your own" two-step) and a
  custom pipeline stage (`ISearchStage` registered with `services.AddXpSearchStage<T>()` —
  read `XpSearchServiceCollectionExtensions.cs:131` for the idiom and pick a slot that
  doesn't collide with shipped stages; look at how orders are assigned).
- Click data lives in the query log: `IQueryLogStore` rows carry `LogClickedResultID`
  (RK-1/RK-2). Document ids are `WebPageItemGUID:lang`. The demo index is
  `DancingGoatSample`; seeded click rows exist from earlier passes, and more can be seeded
  through `POST /api/xpsearch/events` (click events with a real `queryId`; the query-log
  click write is NOT consent-gated).

## 1. Host: the computed field

In `src/Search/` (pattern-match `DancingGoatSearchIndexingStrategy`, which already
contributes `image`/`path` via the AddField two-step):

- Compute a per-document click count over the last 30 days from `IQueryLogStore` at
  indexing time, and write it as a numeric field (name it something honest like `clicks`),
  declared with `indexing.AddField(...)` in `Program.cs`: retrievable (so the walkthrough
  can SHOW it on a raw hit), sortable, not searchable, not facetable. Compute the counts
  ONCE per mapping scope, not one store read per document — cache in the strategy the way
  its existing per-scope state works.
- Register a `"popular"` sort key on it (`options.Indexes[...].SortKeys` — the existing
  `newest`/`price_asc` lines are the idiom), so the field is immediately useful with zero
  further code and the demo's sort dropdown can offer it (do NOT change the live page's PB
  config — note it as an owner option).

## 2. Host: the custom ranking stage

- A host-owned `ISearchStage` (in `src/Search/`) that reads the indexed `clicks` field and
  adds a bounded boost to the query — mirror `PopularityBoostStage`'s mechanics (SHOULD
  clause beside the built query, bounded factor, no-op when the signal is empty or the
  request is sorted by something other than relevance — check what the shipped stage
  gates on and match it). Registered in the host with `AddXpSearchStage`.
- Guard against double-boosting in the demo: the built-in popularity toggle is currently ON
  for `DancingGoatSample` (flipped during an earlier host pass). The walkthrough's
  verification runs with the built-in OFF (flip it in the admin — you have no browser, so
  flip it by updating the info row the way the toggle stores it, or report it as a lead
  step if that is not reachable programmatically); the guide states plainly that running
  both stacks two bounded boosts and you'd normally pick one.

## 3. Guide

- New section in `docs/guides/indexing-strategy.md` after the AddField material: "Worked
  example: a computed relevance field" — the REAL host code (strategy computation, AddField
  line, sort key, the stage, its registration), what each piece is for, the staleness
  ceiling (the field refreshes when a document is re-indexed or the index rebuilds — a
  scheduled rebuild is the operational answer; one sentence, and a KNOWN-LIMITATIONS entry
  is NOT needed since the guide states it), and the built-in-vs-custom framing from the
  context section above.
- Cross-link: popularity-boosts.md gains one line pointing at the worked example for teams
  that want the index-time pattern for their own signals.
- CHANGELOG: docs entry (no library code changes expected — if you find yourself needing a
  library change, STOP and report instead).

## 4. Verification

- Rebuild the index so the field exists: easiest programmatic route is the ingestion
  rebuild endpoint (`POST /api/xpsearch/admin/indexes/DancingGoatSample/rebuild`) — the dev
  API key's plaintext is recoverable from `CMS_EventLog` (the 2026-08-31 seeder warning
  row; SELECT it, do not delete/reissue — the classifier blocks the delete). You may
  stop/rebuild/restart the host (`Stop-Process -Name CommunityProjects`; `dotnet build
  CommProjects.sln` from the umbrella root; `dotnet run --no-build --project
  src/CommunityProjects.csproj` in background). Leave the host RUNNING.
- Show in your report: a raw `/api/xpsearch/query` hit with the `clicks` attribute
  populated for a clicked document; `sort=popular` ordering by it; and a relevance query
  whose ordering demonstrably moves with the stage on (seed clicks via `/events` onto a
  document that ranks mid-list for some query, re-index that document or rebuild, show
  before/after order). If the built-in toggle could not be flipped programmatically, say
  so and show the demo with it on, noting the composition.
- All four+1 C# suites still green (you should not have touched the library — run them to
  prove it); `docs:check` clean if the guide edits touch generated material (they should
  not).
- Checklist: append a new section numbered after the current last item (§U ends at 107 on
  main — verify in YOUR worktree): the raw-hit field, the popular sort, the before/after
  ranking demo, and restoring the built-in toggle to ON afterwards (the owner's HW-11 item
  45 flow expects it — leave the demo in the state you found it unless the walkthrough
  says otherwise, and SAY which state you left it in).
- Commit guide + checklist + this spec on `unit/ex-2` (copy the spec from
  `docs/internal/units/EX-2.md` on main if your worktree predates it). Host changes:
  file-by-file in the report + `src/Search/README.md` updated (it documents the search
  sample's parts).

## Constraints

- NO library code changes (docs only in the library repo; STOP clause above). Kentico docs
  MCP mandatory for any Xperience API question. Never touch
  `src/Components/Widgets/CardWidget/`. No new dependencies. The guide walks real,
  verified code — no invented samples.
