# Unit XP-1b — Experiments admin: pages, draft editing, report, Promote/Discard

Second half of XP-1 (amendment `docs/spec/amendments/2026-08-25-experiments.md` — the authority;
read it first). XP-1a (merged, ADR-0024) built everything below the UI:

- `XpSearch.Admin/Tuning/ExperimentService.cs`: `IExperimentService` — create-with-clone,
  `SetSplitAsync` (Draft only), start, conclude(promote|discard) with cache eviction;
  `ExperimentRules` state machine (Draft→Running→Concluded, one non-concluded per index).
- `Persistence/XpSearchExperimentInfo.cs` (`ExperimentState`, `ExperimentOutcome`), nullable
  `…ExperimentID` on the four tuning infos (NULL = live), `Tuning/VariantScope.cs` = the ONE
  row-scoping seam, `InfoRunningExperimentSource` (cached running lookup).
- Query log rows carry `LogExperimentID`/`LogVariant` while an experiment runs; clicks correlate
  by queryId as before.
- The four tuning listing pages currently EXCLUDE draft rows outright.

Read `docs/internal/agent-primer.md` (the `[PageCommand]` and `RoutingContentPlaceholder`
gotchas are real and yours this time). Work only in this worktree (branch `unit/xp-1b`).

## 1. Experiments section (index-scoped tuning sidebar, after Analytics)

- **Listing** page: name, state, split, started/ended, outcome; Create action (dialog or page:
  name + split 1–99). Use labelled stock Buttons (the package's ActionCell/Pagination
  aria-label bugs are known — follow the AD-6/CR-4b patterns already in the client).
- **Detail** page per experiment, content by state:
  - Draft: split editable (`SetSplitAsync`), links to the four variant-B editors (§2), Start
    button (confirmation dialog: "all visitors are bucketed immediately").
  - Running: live comparison report (§3), Conclude via **Promote B to live** / **Discard B** —
    both behind confirmation dialogs that say exactly what happens to the tuning rows.
  - Concluded: the final report snapshot query (same report, date-bounded to started..ended) +
    outcome badge. Read-only.
- Declare every `[PageCommand]` as a plain method on the FINAL page class (primer gotcha — the
  host has shown inherited/overridden commands failing discovery).

## 2. Variant-B draft editing — reuse the live pages, do not fork them

The amendment requires the draft to be "edited with the same pages the live tuning uses".
Approach: the existing Rules/Synonyms/FieldWeights/Stopwords page classes (and the rule-builder
edit pages) become variant-aware — an experiment-scoped route under the Experiments detail
(e.g. `…/experiments/{id}/rules/…`) that reuses the SAME page classes/templates parameterized
by the experiment id, with `VariantScope` doing the row filtering and creates/edits stamping the
experiment id. Constraints:

- Zero duplicated business logic: if you find yourself copying a page class body, stop and
  parameterize instead.
- Draft editors only exist while the experiment is Draft (Running/Concluded → read-only listing
  or a message; editing a running experiment's B rows would corrupt the test).
- Live pages stay byte-identical in behavior. The seeded create flow (zero-result → rule) stays
  live-only.
- A visible "Variant B draft — <experiment name>" banner/breadcrumb on every draft editor so
  nobody edits the wrong set (HW incident precedent: silent wrong-target edits).

## 3. Comparison report (honest samples, no fabricated significance)

- Per variant: searches, zero-result rate, CTR, average clicked position — the SAME metric
  definitions the Analytics page already computes, split by `LogVariant` where
  `LogExperimentID` = this experiment, date-bounded to the experiment's Started..(Ended|now).
- Show absolute sample sizes prominently ("A: 1 204 searches / B: 1 187") and a plain-language
  note that the report shows observed rates only — NO p-values, NO "winner" badge, NO
  significance claims (amendment's explicit "honest-sample… no fabricated significance").
- Server fills to MaxReportRows=200 conventions from AD-6 where a table is used; a small
  stat-tile layout is fine and probably better than a table here — follow the existing
  Analytics page's components.
- Query tester: add a "Variant" select (Live / B of <experiment>) when the index has a
  non-concluded experiment — the seam is `IRelevanceTuningSource`'s `TuningVariant` parameter;
  PZ-1's "Simulate contact group" select is the exact UI pattern.

## 4. Docs

- New wiki-ready guide page (experiments: concept, create→edit→start→report→conclude walk,
  cookie/consent reality — Essential level, works without tracking consent; one experiment per
  index; first-server-paint-leans-A limitation) + tuning guide cross-links.
- CHANGELOG `[Unreleased]`. KNOWN-LIMITATIONS only if you create a new honest ceiling.

## Deliverables

- Pages + client components (Admin React client: `src/XpSearch.Admin/Client`, webpack build
  must pass; strict TS), Admin C# tests for page data/commands where the existing suite has
  precedent, report-query tests.
- All C# suites + Admin client build green (JS widgets suite untouched). Conventional commits on
  `unit/xp-1b`; commit this spec file.

## Constraints

- No new dependencies. No contract changes. Use the Kentico docs MCP for admin UI API questions.
- UI text: the repo says "Action(s)" not "Consequence(s)"; DateTimeRangeInput is the date
  control in use; useMediaBreakpoints handles 1024 narrowing — match the existing pages.
- Do not touch Core's experiment/bucketing code — XP-1a is merged and reviewed; if something
  there blocks you, STOP and report rather than patching it.
