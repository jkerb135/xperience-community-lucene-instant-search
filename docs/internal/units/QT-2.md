# Unit QT-2 — Query tester as a diff, with per-stage scores

Owner decisions 2026-09-03: the redesigned query tester prototype is the **source of truth** for the
page; where the library cannot supply what the prototype shows, the library is extended. The
pipeline **emits the score after every scoring stage** ("the more information given to the end
user the better"). The sample host gets **demo tuning rules** so every change type the page can
show is visible on a real run.

Prototype (interactive, read it in a browser): `docs/internal/design/QueryTester.dc.html`
(published canvas https://claude.ai/code/artifact/e7790310-b04d-44e3-b7c7-68727468d6d0). Every
region of it maps to a stock `@kentico/xperience-admin-components` component (ADR-0020 rule); the
mapping is in §B.3. Its sample data is invented — the demo rules in §C recreate it for real.

Read `docs/internal/agent-primer.md`, then ADR-0020 (admin page design) and ADR-0027 (page
commands). Work only in your worktree (branch `unit/qt-2`).

This unit has three slices. **A (Core) is built and reviewed first**; B (Admin) and C (host)
build on A's merged types. One implementer does A then B in this worktree; C is dispatched
separately against the umbrella host (`F:\Personal\CommunityProjects\src\Search`, no git).

---

## Why the pipeline has to change (read before A)

`ProjectResponseStage` reports `ranking.baseScore = scored.Score` — the **final** score. Every
boost (field weights in `BuildQueryStage`, boost rules in `BoostRulesStage`, `PopularityBoostStage`,
the host's `ClicksBoostStage`) is folded into `SearchContext.BaseQuery` *before*
`ExecuteSearchStage` runs, so there is no pre-boost score anywhere; the tester today shows
`score == baseScore` on every row. Pins and buries (`PinnedAndBuriedStage`) move documents after
the search and never touch a score.

So "the score after each stage" cannot be read off the search. It is computed: each scoring stage
leaves a **checkpoint** (a label and the `Query` as it stood after that stage), and after the
search a new stage asks Lucene to `Explain(checkpointQuery, docId)` for every document on the
page. `IndexSearcher.Explain(...).Value` is exactly the score that query gives that document.
For a page of ≤ 50 hits and ≤ 6 checkpoints that is a few hundred cheap explains, and it only
runs when `explain=true` (the tester always sets it; visitors never do).

---

## A. Core — score checkpoints, score steps, applied rules

### A.1 `ScoredDocument` carries the Lucene doc id

`ScoredDocument(Document Document, float Score, int DocId)`. Set it in
`ExecuteSearchStage` (from `scoreDoc.Doc`) and in `PinnedAndBuriedStage.Load` (from
`hits.ScoreDocs[0].Doc`). Fix every constructor site (tests included).

### A.2 Checkpoints on the context

```csharp
/// <summary>The query as it stood after one scoring stage, so the score it alone would give a document can be explained afterwards (QT-2).</summary>
/// <param name="Stage">What the user sees: "Lucene score", "Field weights", "rule:Espresso grinders", "Popularity boost", "Clicks boost".</param>
/// <param name="Query">The query at that point. Never mutated afterwards — stages replace <see cref="SearchContext.BaseQuery"/>, they do not edit it.</param>
/// <param name="RuleId">The tuning rule this checkpoint belongs to, when it is one rule's boost.</param>
public sealed record ScoreCheckpoint(string Stage, Query Query, int? RuleId = null);

// SearchContext
public IList<ScoreCheckpoint> ScoreCheckpoints { get; } = new List<ScoreCheckpoint>();
```

Who pushes, in order:

| Stage | Checkpoint | Condition |
| --- | --- | --- |
| `BuildQueryStage` | `"Lucene score"` = the text query built with **no** field weights, wrapped in the same filters as `BaseQuery` | always |
| `BuildQueryStage` | `"Field weights"` = `BaseQuery` as built today | only when at least one weight ≠ 1.0 |
| `BoostRulesStage` | one per boost rule that wrapped the query, `Stage = RuleSelection.Explain(rule)`, `RuleId = rule.Id` | per applied `RuleAction.Boost` |
| `PopularityBoostStage` | `"Popularity boost"` | when it wraps |
| host `ClicksBoostStage` (§C) | `"Clicks boost"` | when it wraps |

`BuildQueryStage` today weights fields while it builds; refactor so the builder takes the weight
map as a parameter and is called twice when weights exist (once with an empty map). Building a
query is in-memory; the cost is nil. Verify that `BaseQuery` is byte-for-byte what it was before
(existing `BuildQueryStage` tests must pass unchanged).

### A.3 `ScoreBreakdownStage` — new, `SearchStageOrder.ScoreBreakdown = 850`

Between `Execute` (800) and `PinnedAndBuried` (900). Registered wherever the other Core stages are.
No-op unless `context.Request.Explain == true`. Inside one searcher lease (the pattern of
`ExecuteSearchStage` / `PinnedAndBuriedStage.Load`), for every `ScoredDocument` on the page and every
checkpoint in order: `searcher.Explain(checkpoint.Query, doc.DocId).Value` →

```csharp
public sealed record ScoreStep(string Stage, double Score, int? RuleId = null);

// SearchContext, keyed by result id (ProjectResponseStage.ResolveResultId)
public IDictionary<string, List<ScoreStep>> ScoreSteps { get; } = new Dictionary<string, List<ScoreStep>>(StringComparer.Ordinal);
```

Skip a step whose score equals the previous step's (within 1e-6) — a boost rule that did not
match this document is not a step for it. **Always** keep the first step ("Lucene score").
Invariant to test: the last step's score equals `ScoredDocument.Score` within 1e-3 for a plain
search. If facet drill-down (`DrillSideways`) perturbs it, record the delta in
`KNOWN-LIMITATIONS.md` (symbol, what, ceiling, upgrade path) rather than hiding it.

### A.4 Rules that touched a document

```csharp
public sealed record AppliedRule(int RuleId, string Name, string Effect); // Effect: "boost" | "pin" | "bury" | "hide"

// SearchContext, keyed by result id
public IDictionary<string, List<AppliedRule>> AppliedRules { get; } = new Dictionary<string, List<AppliedRule>>(StringComparer.Ordinal);
```

- `ScoreBreakdownStage` adds `"boost"` for every kept step whose checkpoint has a `RuleId`.
- `PinnedAndBuriedStage` adds `"pin"` (and appends a `ScoreStep($"{RuleSelection.Explain(rule)} → #{position}", currentScore, rule.Id)` to the target's steps, creating the list with a `"Lucene score"` step first when the document was injected — use the injected document's own `Score`), `"bury"`, and `"hide"` for the documents it moves, injects, or removes. A removed document is not on the page, so its entry is informational only; keep it, the admin side may use it later.
- `ProjectResponseStage.Explain` is unchanged (the string lines stay for API consumers).

### A.5 Contract (additive, no breaking)

`contract/xpsearch-api.schema.json` → `RankingInfo` gains

```json
"steps": {
  "type": "array",
  "description": "Score after each scoring stage, in application order, present only with explain. The first entry is the raw Lucene score; the last equals score. Stages that did not change this result's score are omitted.",
  "items": { "type": "object", "additionalProperties": false, "required": ["stage", "score"],
    "properties": { "stage": { "type": "string" }, "score": { "type": "number" } } }
}
```

Regenerate every emission (`src/XpSearch.Widgets/Client`: `npm run contract:gen`, then
`contract:check` must pass — CL-1 recorded which files it writes). `ProjectResponseStage` fills
`Ranking.Steps` from `ScoreSteps[id]` and sets `Ranking.BaseScore` to the **first step's score** —
that is what its documentation ("Lucene score before any boost rule was applied") has promised
since spec §4.2. CHANGELOG: `**Fixed (core):** ranking.baseScore was the final score; it is now the
raw Lucene score` plus `**Added (core):** ranking.steps …`. Consumers of `baseScore` in this repo:
the query tester diff (B) and any JS client reader — grep and update.

### A.6 Cache

`explain` is already part of the response identity (check `SearchCacheKey.Compute`; if it is not,
add it — an explained response must never be served to a request that did not ask). Nothing else.

### A.7 Tests (Core suite)

- `ScoreBreakdownStageTests`: with a boost rule targeting one document → that document has steps
  `["Lucene score", "rule:…"]` and an `AppliedRule("boost")`; a document the rule does not target has
  the single step; last step == score; stage is a no-op without `explain`; `DocId` set on every
  page document; injected pin → steps present with the pin step last; order constant 850 and
  registration (`StageOrderingTests` precedent).
- `BuildQueryStage`: unweighted checkpoint pushed always, weighted only when weights exist; existing
  query-shape tests untouched.
- Contract: `contract:check` clean; the C# generated file diff is only the new member.

---

## B. Admin — the page rebuilt to the prototype

### B.1 Models (`QueryTesterModels.cs`)

- `QueryTesterHit` gains `IReadOnlyList<ScoreStep> Steps` (reuse the Core record) and
  `IReadOnlyList<HitRule> Rules` where `HitRule(int Id, string Name, string Effect)`.
- `QueryTesterSideResult` gains `IReadOnlyDictionary<string, IReadOnlyList<AppliedRule>> AppliedRules`;
  `QueryTesterSearch.CaptureExplanationsStage` captures `context.AppliedRules` next to the
  query explanations (it already runs last).
- `QueryTesterDiff.Compare` copies `Ranking.Steps` and the applied rules onto each hit;
  `BaseScore` now comes from `Ranking.BaseScore` unchanged (it is the true base after A).
- `QueryTesterClientProperties` unchanged.

### B.2 Commands (`QueryTesterPage.cs`)

All `[PageCommand(Permission = SystemPermissions.VIEW)]`; each name added to
`PageCommandDiscoveryTests`.

| Command | Request | Navigates to |
| --- | --- | --- |
| `CreateRule` | `{ Query }` | the seeded create page, exactly as `AnalyticsDashboardPage.CreateRule` does (`RuleSeed.Encode(IndexName, query)` → `ZeroResultRuleCreatePage`) |
| `PinResult` | `{ Query, TargetId, Position }` | the same seeded create page, with a **Pin** action pre-filled |
| `BuryResult` | `{ Query, TargetId }` | the same, with a **Bury** action pre-filled |
| `OpenRule` | `{ RuleId }` | the index-scoped rule edit page for that rule (find the `RuleBuilderPage` edit registration and compose its route with `IndexScope.Route`; refuse with an error response when the rule belongs to another index) |

`RuleSeed` grows an optional action: encode `index\nquery\naction\ntarget\nposition`, where the
old two-segment form still decodes (existing tests stay green). `ZeroResultRuleCreatePage.SeedFor`
adds the `RuleActionDto` for `pin`/`bury` when present. Round-trip tests for both forms.

### B.3 Client template (`Client/src/query-tester/QueryTesterTemplate.tsx`)

Rebuild to `docs/internal/design/QueryTester.dc.html`. Region → component:

| Region | Component(s) |
| --- | --- |
| Header card | `Card` `headline="Query tester"`; index name + tuning + stage count as `muted` text right of the headline |
| Query row | `Input` (`markAsRequired`, placeholder `e.g. espresso`, submit on Enter), `Select` Language inline, `Button` primary **Run** (`inProgress` while running, disabled when empty) |
| Simulate-as | tertiary `Button` with `Icon` toggling a drawer (`Divider` + a `Row` of `Select`s: Contact group, Tuning = Live / Variant B when an experiment exists, Results per side 10/25/50). The **applied** choices render as `Tag`s (sky blue) beside the button at all times |
| Recent | up to 5 `Button` tertiary chips from `localStorage` key `xpsearch.query-tester.recent.<index>` (SG-1 `recentSearches` precedent, cap 5, most recent first); clicking one runs it |
| Verdict | `Callout` `type=QuickTip` `placement=OnPaper`, subheadline `Verdict for ‘{query}’`, headline = one of: `Tuning changed N of M results` / `Tuning made no difference to this query`; body = tally (`1 moved up, 1 added, 1 moved down`) + `Select a row to see how its score was built.` or the no-difference hint; `actionButton` = secondary `Button` **Create a rule for this query** → `CreateRule` |
| Pipeline | `Card` (headline S **Pipeline**) with the query as a dark `Tag`, then for each query-level explanation line an `Icon` arrow + clickable `Tag` (sky blue when selected); the selected line's full text in a mono block below. Lines come from `withRules.queryExplanations` |
| Results card | `Card` headline `Results for ‘{query}’`, stats line (tuned / raw / changed counts, `tookMs` both sides), `Checkbox` **Only changes** (diff view only), `NameToggleButtons` **Diff** / **Side by side** |
| Diff table | `Table` with columns Tuned #, Raw #, Change (`Icon` + `Tag`, the existing `changes` map), Result (title + mono url), Score (final; second line `base x.xxx` / `+Δ vs base` / `not in raw`), Why (this hit's `boosts` joined with ` · `). Rows clickable, selected state; unchanged rows low-emphasis; **Only changes** hides them |
| Side by side | two `Table`s as today (With tuning / Without tuning), unchanged rows without a tag |
| Row detail | `SidePanel` `size=Stackable`, headline = title, mono url under it; a change `Tag` reading `Moved up · raw #3 → tuned #2` (or `Added · not in raw ranking → tuned #3`); **How the score was built** = two-column list of `steps` (last row bold); **Rules that touched this result** = one bordered row per `rule` with an **Open rule** tertiary `Button` → `OpenRule`, or `None. Only the query-level stages apply.`; footer: secondary **Bury for ‘{query}’** → `BuryResult`, primary **Pin for ‘{query}’** → `PinResult` (position = the hit's tuned position, or 1 for a raw-only hit) |
| Error | the existing `Callout` `FriendlyWarning` with **Open status** |
| Narrow (`useMediaBreakpoints().sm`) | the diff table drops the Why column; `SidePanel` `size=Full`; everything else stacks |

Rules that bind the whole page:
- The diff table is the default view; the page never shows both views at once.
- No hand-rolled markup where a component exists (ADR-0020). Text treatments come from
  `src/theme.ts` only; add a treatment there if the board needs one the file lacks (name it).
- Icons are `Icon` with `xp-` names (the `changes` map already has them).
- `npm run build` with strict TypeScript is the client check; `npm run typecheck` too.

### B.4 Tests (Admin suite)

`QueryTesterDiffTests` (steps + rules carried, base from `Ranking.BaseScore`),
`QueryTesterPageTests` (four commands navigate to the expected paths; `OpenRule` refuses a
foreign-index rule), `RuleSeedTests` (two-segment and five-segment round trips),
`PageCommandDiscoveryTests` (+4 names). Existing `QueryTesterSearchTests` extended for the
captured applied rules.

### B.5 Docs (wiki-ready, per `feedback-docs-wiki-ready`)

- `docs/guides/relevance-tuning.md` "Query tester" section rewritten as a walkthrough of the
  default page: the four moves (ask → verdict → why then what → drill in and act), the change
  markers, what a score step is, and the three ways out (create a rule, pin, bury, open rule).
- `docs/guides/admin-ui-tour.md` query tester entry updated.
- `docs/guides/search-api.md`: `ranking.steps` documented beside `ranking.boosts`, and the
  `baseScore` fix called out.
- The pipeline-extension guide (wherever `AddXpSearchStage` is documented — EX-2 touched it): a
  paragraph "Leave a score checkpoint" showing the one-liner a boosting stage adds.
- New `docs/adr/0028-query-tester-as-diff.md`: the prototype is the spec; one list two rankings;
  per-stage scores by explain-against-checkpoints; Callout/SidePanel mapping; `baseScore` fix.
- `docs/internal/screenshot-manifest.md`: mark the query tester rows STALE; list them in the report.
- CHANGELOG `[Unreleased]`: `**Changed (admin):** query tester redesigned …`, `**Added (admin):**
  pin / bury / open-rule from a result …`.

---

## C. Host — demo rules that make every change type visible (separate dispatch)

Host = `F:\Personal\CommunityProjects\src\Search` (umbrella, **not** git; edit files in place; the
host `ProjectReference`s the library's main worktree, so A must be merged first).

- `ClicksBoostStage.cs`: push `new ScoreCheckpoint("Clicks boost", context.BaseQuery)` right after
  it wraps the query (one line).
- New `DemoTuningRuleSeeder : IHostedService`, registered **Development only** next to
  `DevIngestionKeySeeder`. Idempotent: skip when any rule named with the `Demo:` prefix exists on
  the demo index. Creates, for query `espresso` on `DancingGoatSearchIndexingStrategy.INDEX_NAME`,
  through the same `XpSearchRuleInfo` provider the admin uses (set EVERY installer-declared field —
  primer "Creating an Info object"):
  1. `Demo: Espresso grinders` — **Pin** the product whose raw rank is 3 to position 2 (→ *Moved up*
     for it, *Moved down* for the displaced one).
  2. `Demo: Espresso accessories` — **Boost** ×3 with a filter expression that matches a product
     *not* on the raw first page (→ *Added*). Pick the expression from the index's real attributes.
  3. `Demo: Hide out of stock` — **Bury** the raw #5 product (→ *Removed*).
  4. `Demo: Espresso for coffee lovers` — **Pin** scoped to the `CoffeeLovers` contact group (or the
     group AN-3 created), so *Simulate as* shows a rule firing only for members.
  5. `Demo: Espresso wording` — a **RemoveWord** rule on `espresso` removing a word that is not in
     the query (harmless), so the pipeline trail shows a query-level rule.
  Target ids are resolved at seed time by searching the index for the product titles through
  `ILuceneIndexAccessor`; if the index has no documents yet (first start queues a rebuild), log a
  warning and do nothing — the next start seeds. Titles are chosen from the live index at seed
  time, not hardcoded.
- `README.md` of `src/Search`: a "Demo tuning rules" paragraph.
- Host-pass checklist `docs/internal/host-pass-hw11-checklist-2026-08-26.md` (library repo): new
  section **§AA — QT-2 query tester as a diff** with items 132–139: verdict + counts; pipeline
  trail click; Only changes; Side by side; row → SidePanel steps (Lucene score ≠ final on the
  boosted row) and Open rule lands on the rule; Pin → seeded create page with the action; Bury →
  same; Simulate as CoffeeLovers → the group rule fires and the applied tag reads the group.
- Then build the umbrella: `dotnet build CommProjects.sln` from `F:\Personal\CommunityProjects`
  must be 0 errors; rebuild the admin client (`src/XpSearch.Admin/Client`: `npm run build`).

---

## Constraints (all slices)

- Contract change is additive only; `contract:check` gates it. No change to the string `boosts`.
- No new dependencies. No new abstractions beyond the three records and the one stage named above.
- Lazy-senior rules: intentional shortcuts go to `docs/internal/KNOWN-LIMITATIONS.md`, never code
  comments.
- Conventional commits, one per slice on `unit/qt-2`:
  `feat(core): score checkpoints, per-stage score steps and applied rules on the response (QT-2a)`,
  `feat(admin): query tester rebuilt as a diff with score steps, pin/bury/open-rule commands (QT-2b)`.
- Report per slice: files changed, the design decisions you had to make that the spec left open
  (with the option you took and why), every suite's count line (Core, Admin, Ingestion, Widgets,
  Client), `contract:check` output, screenshot rows marked stale, the commit hash.
