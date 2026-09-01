# HW-11 partial pass — headless items (lead, 2026-09-01)

Driven in the in-app browser + SQL against the running host (build of ~12:30, which includes
PB-5 C# but a PRE-PB-5 widgets bundle — see the one FAIL). Item numbers per
`host-pass-hw11-checklist-2026-08-26.md`. Owner-only items (admin sign-in / visual judgement)
untouched.

| Item | Verdict | Evidence |
|---|---|---|
| 18 (assets) | **PASS** | shell.css / default.css / xpsearch.umd.js all 200 under `/_content/XperienceCommunity.Search.Widgets/xpsearch/` |
| 19 (themes) | PASS (headless half) | /search renders correctly; dark-scheme emulation identical (host page pins light). Owner eyeball optional |
| 23 (routing) | **PASS** | typing → `?q=grinder`; facet click → `ProductFieldCategory=Grinders` param; Back restored URL+box+results |
| 25 (server cards) | **PASS** | raw HTML of `/search?q=coffee` carries `data-xps-server-rendered` with 6 cards |
| 26 (hydration) | **PASS** | post-hydration DOM: 0 server blocks, 1 result list, no duplication |
| 30 (uh param) | **FAIL → stale bundle, fix built, host rebuild pending** | served bundle lacks PB-5 (`$$routable` absent) → client adopts `uh`, API 400, 0 cards. Server side IS fixed (raw HTML with `uh` renders 6 cards). Widgets `dist/` rebuilt locally at 12:5x; needs sln rebuild + restart to serve. RETEST after restart |
| 32 (schema) | **PASS** (DB half) | `XpSearch_Experiment` + 4 tuning `…ExperimentID` columns exist; event log clean for 6h except the known pre-RK-2 task failure 08:38 and ENDAPP restarts |
| 37 (log columns) | PASS (DB half) | `LogExperimentID`/`LogVariant` columns exist |
| 41 (RK-1 schema) | **PASS** | 3 `XpSearch_Popularity*` tables + `LogClickedResultID` column |
| 42 (click writes) | **PASS** | 3 grinder clicks logged with `LogClickedResultID = 09df9a96-…:en` |
| 43/53 (task round trip) | **PASS** (owner's 12:41 run) | `XpSearch_PopularityIndex` row (Enabled=1, computed 12:41), 1 score row (2.0), 1 mined pair `test → aer` occ 3 state 0 — both aggregations produced real rows |
| 51 (SY-1 schema) | **PASS** | `XpSearch_SynonymSuggestion` with the expected 8 columns |
| 52 (seed reformulations) | **DONE** | 3× settee(0 results, no click) → grinder(clicked) at 12:49–12:51, each pair ≤ 30 s apart. Next task run should mine `settee → grinder` occ 3 (verifies 53 cleanly + 44/56 on re-run) |

## Notes / observations

- **Task history explained:** the 08:38 `PopularitySignal` failure was the RK-2 NULL defect; the
  owner's "Boost by popularity" toggle then created the settings row safely (Enabled=1), which is
  why the 12:41 run succeeded. RK-2 (merged, 6dff104) fixes the first-run path for any other
  index and two sibling defects (incl. `RuleMigrated` on suggestion-approve — item 49 would have
  crashed without it).
- **Observation (minor):** server first paint shows 6 cards vs the client's 12 — the server
  render and the hydrated request disagree on page size when the widget leaves ResultsPerPage
  unset. Not a checklist failure; worth a look (likely index default vs client default).
- The popularity boost is currently **opted in** on DancingGoatSample (owner's toggle). Item 45
  ("off by default" behaviour) can no longer be observed on this index without toggling off.

## Required before the owner's admin pass

1. `main` is at 6dff104 (RK-2 included). Widgets `dist/` already rebuilt in the working tree.
2. Stop host → `dotnet build` the solution → start. (Admin client stays on the :3010 dev server.)
3. Retest item 30 (should flip to PASS) — then the owner walks §A–C, E, 24, 27–29, 33–40,
   45–50 (45 needs the toggle off first), 54–58, 59–64, 65–67.
