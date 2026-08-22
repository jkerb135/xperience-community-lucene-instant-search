# Host pass HW-8 — 2026-08-22

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, Xperience by Kentico 31.8.0, DB `comm_projects`,
http://localhost:27340). Library `libraries/xperience-search`, **`main` 61bf778** (CA-6,
*fix(admin): re-parent the tuning section under the index listing*). No library source was changed and
nothing was committed (`git status --short` empty at the end of the pass; this note is untracked).
`src\Components\Widgets\CardWidget\` untouched.

Scope: one question only — does the admin UI tree that AD-3 broke
(`docs/internal/host-pass-hw7-2026-08-22.md` §6.1) build again after CA-6?

## Verdict: **PASS**

Every page this library registers resolves to a path. No `InvalidOperationException` about page location
anywhere; the HW-7 message
(*Node 'IndexTuningSection' must use either 'SidePanel' or 'Dialog' page location…*) does not appear.

---

## 1. Builds

`src/XpSearch.Admin/Client/dist` was already present
(`entry.kxh.8a5c62fecb94c3ea9193.js` + its `.LICENSE.txt`), so `npm ci && npm run build` was **not** run,
per the unit's condition.

```
$ dotnet build F:\Personal\CommunityProjects\CommProjects.sln     (probe present)
    115 Warning(s)
    0 Error(s)
Time Elapsed 00:00:10.61
```

115 = the 114 pre-existing host/sibling-project warnings HW-7 recorded, plus one from the probe itself
(`Hw8Probe.cs(43,13): warning CS0618: 'ApplicationEvents.PostStart' is obsolete`). No warning names an
`XpSearch` project.

No stale host was running: `Get-Process CommunityProjects` returned nothing and nothing was listening on
port 27340 before the pass.

## 2. The probe

`F:\Personal\CommunityProjects\src\Search\Hw8Probe.cs` — one file, `[assembly: RegisterModule]`, hooking
`ApplicationEvents.PostStart`, logging to stdout (`HW8PROBE …`) and to a file. Created and **deleted**
afterwards. As HW-7 found, `PostStart` fires on the **first HTTP request**, not at startup: `OnInit
reached` appeared during boot, `PostStart fired` only after `GET /`.

It resolves `IPageLinkGenerator` and `ILuceneConfigurationStorageService`, takes the lowest registered
index id (`GetIndexIds()` → **2** on this DB) and asks `GetPath` for every page.

**One deviation from the specified probe, and why.** The unit specified a single parameter set,
`new PageParameterValues { { typeof(IndexTuningSection), id } }`, for all seventeen types. Run that way
(first run, output below), the eleven pages inside the tuning section resolved and four rejected the call
because the parameter is wrong *for them*, not because they are unregistered:

```
IndexListingPage           ! InvalidOperationException: Too many parameters supplied for the link generation.
IndexEditPage              ! InvalidOperationException: Parameter for the page type 'Kentico.Xperience.Lucene.Admin.IndexEditPage' is missing.
IndexTuningRoot            ! InvalidOperationException: Too many parameters supplied for the link generation.
SearchTuningApplication    ! InvalidOperationException: Too many parameters supplied for the link generation.
ApiKeyListing              ! InvalidOperationException: Too many parameters supplied for the link generation.
ApiKeyCreate               ! InvalidOperationException: Too many parameters supplied for the link generation.
IngestionLogListing        ! InvalidOperationException: Too many parameters supplied for the link generation.
```

That is the *correct* behaviour after CA-6 — `IndexListingPage`, `IndexTuningRoot` and the four standalone
`Search ingestion` pages carry no parameterized slug, and `IndexEditPage` carries its own — and it is a
different exception from HW-7's, which named `IndexTuningSection`'s page location for **every** type
including these. To get the unit's stated expectation (*a path for EVERY type*) the probe was changed to
try three parameter sets per type in order — `tuning` (`{ IndexTuningSection: 2 }`), `none` (empty),
`edit` (`{ IndexEditPage: 2 }`) — and report which one worked. Nothing else changed; the host was rebuilt
(`0 Error(s)`) and restarted for the second run.

## 3. Probe output, verbatim (second run)

```
OnInit reached
PostStart fired
first registered Lucene index id = 2
IndexListingPage           = /lucene/indexes   [params: none]
IndexEditPage              = /lucene/indexes/2   [params: edit]
IndexTuningRoot            = /lucene/indexes/tuning   [params: none]
IndexTuningSection         = /lucene/indexes/tuning/2   [params: tuning]
IndexSettingsPage          = /lucene/indexes/tuning/2/settings   [params: tuning]
RuleListing                = /lucene/indexes/tuning/2/rules   [params: tuning]
RuleCreate                 = /lucene/indexes/tuning/2/rules/create   [params: tuning]
SynonymListing             = /lucene/indexes/tuning/2/synonyms   [params: tuning]
StopwordListing            = /lucene/indexes/tuning/2/stopwords   [params: tuning]
FieldWeightListing         = /lucene/indexes/tuning/2/weights   [params: tuning]
QueryTesterPage            = /lucene/indexes/tuning/2/query-tester   [params: tuning]
AnalyticsDashboardPage     = /lucene/indexes/tuning/2/analytics   [params: tuning]
IndexStatusPage            = /lucene/indexes/tuning/2/status   [params: tuning]
SearchTuningApplication    = /xpsearch-tuning   [params: none]
ApiKeyListing              = /xpsearch-tuning/api-keys   [params: none]
ApiKeyCreate               = /xpsearch-tuning/api-keys/create   [params: none]
IngestionLogListing        = /xpsearch-tuning/ingestion-log   [params: none]
ZeroResultRuleCreatePage   = /lucene/indexes/tuning/2/analytics/Cg
done
```

Against the unit's expectations:

| Expectation | Result |
|---|---|
| A path for every type | **Yes** — 18/18, including `ZeroResultRuleCreatePage` with the `EmptySeed` (`Cg`) parameter |
| Tuning pages read `/lucene/indexes/tuning/{id}/<slug>` | **Yes** — all eleven, with `id` = 2 |
| `IndexEditPage` still `/lucene/indexes/{id}` | **Yes** — `/lucene/indexes/2` |
| No `InvalidOperationException` about page location | **Yes** — none, for any type |

`IndexTuningRoot` resolving to `/lucene/indexes/tuning` (no id) confirms the CA-6 shape: the static
`tuning` segment is contributed by the pass-through root, and the index identifier by
`IndexTuningSection`'s own parameterized slug, one level below.

## 4. Host run

`dotnet run --project src --no-build`. Startup log: **61 lines**, of which 22 are `HW8PROBE` lines; it ends
with `Now listening on: http://localhost:27340` / `Application started.` and contains **no** line matching
`error|warn|fail|exception|SidePanel|IndexTuning` outside the probe's own output.

```
$ GET http://localhost:27340/       → 200   (this is what fires PostStart)
$ GET http://localhost:27340/admin  → 200
```

As HW-7 established, `GET /admin` 200 is *not* evidence about the UI tree — the SPA shell serves without
touching it. The probe in §3 is the evidence; the 200 is only a sanity check that nothing else regressed.

## 5. Host left unchanged

```
$ Stop-Process CommunityProjects; Remove-Item src\Search\Hw8Probe.cs
$ dotnet build CommProjects.sln
    6 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.72

$ Get-Process CommunityProjects → 0
$ ls src\Search\ → DancingGoatSearchIndexingStrategy.cs  DevIngestionKeySeeder.cs  README.md  SearchStartupExtensions.cs
```

The probe's temporary output files (`hw8-probe.txt`, `hw8-startup.log`, both at the umbrella root) were
deleted. No other host file was touched. Nothing was verified beyond registration — no tuning data was
created, read or modified, and no index was rebuilt.

## 6. Note for a follow-up unit (not a defect found by this pass)

`F:\Personal\CommunityProjects\src\Search\README.md`, rewritten during HW-7, tells the reader the tuning
pages live at `/admin/lucene/indexes/{id}/tuning/…`. CA-6 moved them to
`/admin/lucene/indexes/tuning/{id}/…`. Out of scope here (this pass makes no host change beyond the
deleted probe), but the host README is now stale by one path shape.

---

## 7. OWNER BROWSER CHECKLIST

Everything below needs a real browser and a signed-in administrator; none of it is observable headlessly.
The AD-3 half is now **unblocked** — every URL here was proven resolvable in §3. Index **2** is
`DancingGoatSample`.

### AD-3 — the index-scoped admin

1. **Row click.** Open `http://localhost:27340/admin/lucene/indexes`. Click the **DancingGoatSample** row.
   It must land on `http://localhost:27340/admin/lucene/indexes/tuning/2/settings` — this package's tuning
   section, **not** the integration's bare edit form at `/admin/lucene/indexes/2`.
2. **Sidebar order.** In that section the left navigation reads, top to bottom:
   **Settings, Rules, Synonyms, Stopwords, Field weights, Query tester, Analytics, Status**
   (registration orders 100/200/300/400/500/600/700/800). Each entry lands on, respectively:
   ```
   /admin/lucene/indexes/tuning/2/settings
   /admin/lucene/indexes/tuning/2/rules
   /admin/lucene/indexes/tuning/2/synonyms
   /admin/lucene/indexes/tuning/2/stopwords
   /admin/lucene/indexes/tuning/2/weights
   /admin/lucene/indexes/tuning/2/query-tester
   /admin/lucene/indexes/tuning/2/analytics
   /admin/lucene/indexes/tuning/2/status
   ```
3. **No Index column.** On **Rules**, **Synonyms**, **Stopwords** and **Field weights** there is no *Index*
   column, and each listing shows only rows belonging to index 2. (All four tuning tables were empty at
   HW-7; create a row to have anything to look at — `/admin/lucene/indexes/tuning/2/rules/create`.)
4. **Index as text.** On **Query tester** (`…/2/query-tester`) and the **Analytics** dashboard
   (`…/2/analytics`) the index is rendered as plain text, not a `Select`, and there is no *every index*
   option.
5. **Create rule from a zero-result row.** On the Analytics dashboard, find the zero-result query
   **`yirgacheffe`** (`XpSearch_QueryLog` LogID 5) and press its **Create rule** button. It must open
   `/admin/lucene/indexes/tuning/2/analytics/<seed>` (the seeded form; the empty-seed URL is
   `…/analytics/Cg`) with index 2 and the query `yirgacheffe` pre-filled, and saving must land the rule in
   `/admin/lucene/indexes/tuning/2/rules`.
6. **Cross-index edit rejected.** Create a rule under index 2, then open it through another index's URL —
   `/admin/lucene/indexes/tuning/{otherId}/rules/{ruleId}/edit`. The save must be **refused with a
   message**, not silently re-homed to the other index. (Needs a second Lucene index; create a throwaway
   one in the integration's index listing if none exists.)
7. **Role tests — Lucene Search.** With a role granted only **View** on the **Lucene Search** application:
   the eight tuning pages are readable, the row click in §1 still reaches the sidebar (note the
   integration's own edit URL `/admin/lucene/indexes/2` requires *Update*, so the row click is the path to
   test), and Create/Update/Delete actions are unavailable. Granting **Create**/**Update**/**Delete** makes
   the forms save.
8. **Role tests — Search ingestion.** With grants only on **Search ingestion**: the tuning pages are
   **not** reachable, but `/admin/xpsearch-tuning/api-keys` and `/admin/xpsearch-tuning/ingestion-log` are.
9. **Standalone application.** The **Search ingestion** application appears under *Development* and
   contains **only** *API keys* and *Ingestion log* — no tuning pages.

### W25 — the JavaScript widgets (carried over from HW-7 §7; still unverified in a browser)

10. **Suggestions popup, keyboard.** On `http://localhost:27340/search`, type into the search box: the
    popup opens after the debounce and the minimum query length; ↓/↑ move the active option and
    `aria-activedescendant` follows it; Enter commits the active option; Escape closes without committing;
    blur closes; typing fast never leaves a stale answer on screen (latest-response-wins). Expect the
    labels to be item-name slugs (`CoffeePlunger-p2e57tss`) — HW-7 §6.3.
11. **Load more — the button.** Switch the *Search - Pagination* widget's style to **load more** in the
    Page Builder (it emits a `loadMore` mount instead of a `pagination` one — never place both). The button
    must **append** the next page to the existing list (earlier results stay in the DOM, not rebuilt), the
    live-region counter must announce the new total shown, and the button must disappear on the last page.
12. **Load more — the scroll path.** Scrolling the sentinel into view loads the next page without a click.
13. **`rangeFilter`.** No Page Builder widget ships for it (by design). If mounted by hand, the two native
    sliders and the two number inputs move together, and with no bounds available the widget renders itself
    disabled.
