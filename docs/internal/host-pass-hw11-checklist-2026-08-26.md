# HW-11 — owner browser checklist (2026-08-26)

Everything merged since HW-10, one pass. Host must run library `main` **including AD-8** (the delete-command
fix). Rebuild order: `npm run build` in `src/XpSearch.Admin/Client` AND `src/XpSearch.Widgets/Client`,
then `dotnet build CommProjects.sln`, then start. First startup on a DB that ran the pre-rename build
also carries rule rows forward from the interim `RuleConsequences` column (automatic).

Index 2 = `DancingGoatSample`. Tuning now lives at `/admin/lucene/indexes/edit/2/<slug>` under the
sidebar label **Edit index** — old `/tuning/` bookmarks 500.

## A. Rule builder (CR-4b + Action rename + CR-5 + CR-5b)
1. Rules → the migrated **"HW10 grinder boost"** rule opens in the builder with the *converted from
   the previous format* note; `XpSearch_Rule` has `RuleConditions`/`RuleActions` JSON columns and none
   of the nine flat ones.
2. Condition side panel: Edit/Add condition slides the panel; Query/Filters/Context toggles;
   Apply local, Esc discards, focus trapped; Filters rows use the attribute+value dropdowns.
3. Action side panel: "+ Add action ▾" menu (10 types, "new" tags); each type opens its panel body.
4. **Item picker**: in Pin an item, type "grin" — real results from this index ~300 ms after typing
   stops; ↑/↓ + Enter picks; selected row shows title + URL; **Details** reveals the id. After saving,
   reopen: titles shown, not ids. Delete/unpublish the pinned item + rebuild the index → the row shows
   the id with a *no longer in the index* tag, action intact.
5. **Attribute + value picker** (Filter results / Boost matching / condition Filters): Attribute lists
   facetable fields; Value lists real values with counts; **Edit as text** shows
   `Category:coffee, Tags:brewing` and **Back to rows** round-trips identically.
6. **Drag reorder**: drag an action across 3+ rows both directions — the 3px insertion line follows the
   pointer's half of the row and the drop matches it; a drag ending outside any row changes nothing.
7. **Keyboard reorder**: focus a grip (name "Reorder Pin an item, 1 of 3"), Space lifts (spoken
   instruction), ArrowDown moves live with announcements, Space drops, Esc restores; focus stays on
   the moving grip; Tab afterwards lands on that row's Edit.
8. Validation: Save disabled with no condition; invalid custom-data JSON blocks save with the field error.
9. Seeded create: Analytics zero-result row → Create rule → `…/rules/from-query/{seed}` renders the
   builder pre-filled with the query.
10. **Picker privacy**: after a session of picker typing, Analytics shows no rows for those typed terms.

## B. Deletes (AD-8) — the reported bug
11. Delete a field weight, a synonym, a stopword, a rule and an API key from their listings — each
    deletes with a confirmation and the row is gone after refresh.
12. A deleted rule/synonym/stopword stops applying (verify in the Query tester) without an app
    restart — the tuning cache invalidates on the provider's delete events.
12b. Cross-index refusal: on index A's weights listing, hand-edit the delete request's row id to a
    row of index B — expect "This record belongs to a different search index and was not deleted."
12c. A role with View but not Delete on Lucene Search cannot invoke the tuning deletes; API keys
    delete under Search ingestion's Delete.

## C. Analytics + admin polish (AD-6/7, pager swap, range pickers)
13. Report tables: numbered `Pagination` control when rows exceed the page size; "Page X of Y · N rows";
    Rows-per-page applies instantly and resets to page 1; Create rule works from page 2.
14. **Date range** picker (one control) on Analytics — presets 7/30/90 still set it; UTC days honoured.
15. Rule settings **Runs** range picker: clear = always; a migrated rule with one bound shows the
    open-ended note and keeps its bound until a range is picked.
16. Chart "Show the numbers": a real table (Date / Searches / Zero-result searches) inside the collapse.
17. Field weight New/Edit: **Field** is a dropdown of searchable fields; an orphaned stored field name
    still shows as the selected option.

## D. Assets + themes (BR-2, TH-2, Sass)
18. The three asset URLs 200 under `/_content/XperienceCommunity.Search.Widgets/xpsearch/…`
    (shell.css, default.css, xpsearch.umd.js); `/search` renders and searches normally.
19. Visual glance at `/search` light + dark (`data-xps-theme="auto"`) — the Sass-built stylesheets
    should be indistinguishable from before.

## E. Carried over from HW-10 (still unseen)
20. Widget-list icons (`icon-funnel`, `icon-arrows-h`, `icon-tree-structure`) not blank; Range filter /
    Category tree attribute drop-downs filter correctly in the widget dialogs.
21. Narrow (≤1365) variants of Analytics / Query tester / Status; Status **Copy failure details**
    clipboard; role-restricted checks (View-only vs Rebuild/DELETE).
22. Degraded status (3b) — still needs the owner-approved failing push to stage it.

## F. PB-3 URL routing (added 2026-08-31)
23. On /search (existing editor-built page, host rebuilt): typing a query updates the address bar
    (?q=...), facet/page/sort changes follow, browser Back restores the previous state, and pasting
    the URL into a fresh tab reproduces the results. No re-save of the widget should be needed
    (default-on is retroactive).
24. Search box widget dialog shows "Sync search state to the URL" ticked by default; unticking it
    and saving stops the URL from changing.

## G. DX-2 server-rendered results (added 2026-09-01)
25. View-source of /search?q=coffee (before JS runs) shows result cards inside the results mount
    (`data-xps-server-rendered`); with JS disabled the cards remain and links work.
26. With JS on, hydration replaces the server block without visible duplication; brief skeleton
    flicker on a cold index is the recorded limitation, not a defect.
27. Register a test template on the host (RegisterSearchResultTemplate + partial per the guide
    sample), select it in the Results widget dialog, confirm the first paint uses it and
    content-type scoping falls back to the default card.
28. Results widget dialog: Title/Link/Snippet attribute fields present; restricted "Fields to
    show" plus a matching Title attribute no longer paints blank cards.
29. Awareness: first load journals TWO query-log rows (server + hydration; different queryIds) —
    known limitation with recorded upgrade path, verify it does not distort the Analytics page.

## H. PB-5 foreign-param routing fix (added 2026-09-01)
30. In admin PREVIEW mode (URL carries `uh=...`): searching on /search works — no 400, results
    paint, and `uh` stays in the address bar while q/filters/page update around it.
31. Deep link with page + facet (e.g. ?q=coffee&page=2 plus a facet param) still hydrates fully —
    page 2 preserved, facet applied.

## I. XP-1 experiments (added 2026-09-01)
32. Startup after rebuild: event log clean; XpSearch_Experiment table exists and the four tuning
    tables gained their nullable ...ExperimentID columns.
33. Experiments listing under index tuning (after Analytics): Create (name + split) works; second
    create for the same index is refused with a message.
34. Draft: the four variant-B editors open under the experiment route, show the draft banner, and
    edits there do NOT change live /search results; live tuning pages show no draft rows.
35. Start: confirmation dialog; after starting, an anonymous browser gets the xpsearch_bucket
    cookie (Essential level - present WITHOUT accepting tracking) and is sticky across reloads;
    two different browsers/profiles can land in different variants (split 50).
36. Variant B actually differs: give B an obvious rule (e.g. pin an item), confirm a B-bucketed
    browser sees it and an A-bucketed one does not; response cache does not leak across variants.
37. Report: per-variant searches/zero-result/CTR with visible sample sizes; no winner/significance
    language anywhere. Query log rows carry experiment + variant.
38. Query tester: Variant select appears while the experiment is unfinished; B simulation applies
    the draft rule without a cookie.
39. Conclude: Promote B - live pages now show B's rows, /search reflects them, draft editors gone
    read-only/concluded; OR Discard - B rows deleted, live unchanged. (The clone/promote/discard
    DB round trip is the XP-1a logic that unit tests could not cover - this is its verification.)
40. Started experiment: variant-B editors are read-only (listings show rows, no actions; direct
    save/delete attempts refused).

## J. RK-1 popularity boosts (added 2026-09-01)
41. Startup after rebuild: event log clean; the three XpSearch_Popularity* tables exist and
    XpSearch_QueryLog gained the nullable LogClickedResultID column (existing rows untouched).
42. Click tracking writes it: search on /search, click a result, then check the query log row for
    that queryId - LogClickedResultID holds the clicked result id, LogClickedPosition its position.
43. Scheduled task: create the `XpSearch.PopularitySignal` configuration per the guide and run it
    once. Last result reads "Popularity computed for N documents across M indexes ...";
    XpSearch_PopularityScore holds N rows and XpSearch_PopularityIndex one row per index with
    the computed-at stamp. (These DB round trips are the RK-1 logic unit tests could not cover.)
44. Idempotence: run the task a second time - the score row COUNT stays the same (rows replaced,
    not appended) and the computed-at stamp moves.
45. Off by default: with the task run but the index NOT opted in, /search results and the cached
    responses are unchanged, and `explain: true` shows no popularity entry.
46. Opt in on Field weights (header action): the callout flips to "on", `explain: true` now lists
    "Popularity boost from N document(s), up to 2.0x (signal ...)", and a clicked-heavily document
    ranks higher than before. Toggling back off restores the previous order without a restart.
47. Cache: after opting in, run the task again - the next identical search is served from a fresh
    response (the signal version changed the key), and an index that is opted OUT keeps its cache.
48. Suggestions: with enough clicks on one query for one document (5+, majority), the run fills the
    Suggestions page; the Rules page shows the "suggested rules are waiting" banner and links to it.
49. Approve one - an ordinary rule "Popular for '<query>'" appears in Rules, editable and deletable,
    and it applies to /search. Dismiss another. Run the task again: neither reappears.
50. Experiment boundary: with an experiment running, both A- and B-bucketed browsers see the same
    popularity boost, and the Suggestions page exists only for the live tuning.

## K. SY-1 mined synonyms (added 2026-09-01)
51. Startup after rebuild: event log clean; the XpSearch_SynonymSuggestion table exists with the
    two query columns, occurrences, last-seen and state.
52. Seed a reformulation on the host: on /search, search a word your index has nothing for (e.g.
    `settee`), click nothing, then within a minute search a word that works (e.g. `sofa`) and click
    a result. Repeat three times (the default threshold), leaving a gap between rounds.
53. Run the `XpSearch.PopularitySignal` task: Last result now ends "... N suggested synonyms", and
    XpSearch_SynonymSuggestion holds the settee -> sofa row with occurrences 3 and state 0.
    (This DB round trip is the SY-1 logic unit tests could not cover.)
54. Synonym suggestions page: Edit index -> Synonym suggestions lists the pair with "3
    reformulations" and a last-seen stamp; the Synonyms page shows the "suggested synonyms are
    waiting" banner and its Suggestions link opens the page.
55. Approve it: a two-way group `settee, sofa` appears on Synonyms, enabled and editable, and a
    /search for `settee` now returns the sofa results. Dismiss a second suggestion.
56. Replace-per-run: run the task again - neither the approved nor the dismissed pair reappears, and
    a still-pending pair keeps its row rather than duplicating.
57. Noise floor: a pair seen only once or twice never reaches the page; a retry more than a minute
    after the failed search produces no pair at all.
58. Experiment boundary: an experiment's variant B has no Synonym suggestions page, and approving
    writes into the live synonyms only.

## L. PS-1 personalization condition types (added 2026-09-01)
59. License gate first: on the host, open a page in Page Builder, hover a NON-search widget (any
    one) and confirm the burger menu offers **Personalize**. If it does not, the instance is not on
    the Advanced tier and items 60-64 cannot be checked - say so rather than guessing.
60. Condition selector: Personalize -> Add variant lists both **Search - searched for** and
    **Search - A/B bucket**, each with its icon, description tooltip and hint above the dialog.
    (Both dialogs are generated by the platform; unit tests only prove the registrations exist.)
61. Searched for, positive: with contact tracking on and cookie consent given, run a /search for
    `espresso`, then reload the personalized page - the variant renders. Set the term to something
    never searched and the original renders again.
62. Searched for, consent absent: in a private window that declines tracking (or with the cookie
    level below Visitor), the same page renders the ORIGINAL variant, and the event log stays clean.
    Same for the page fetched with a crawler user agent / no cookies at all.
63. Bucket stickiness: add a bucket variant (B, 50 %, split name `hw11`) and reload the page a
    dozen times in one browser - the same variant every time. A second browser profile eventually
    shows the other one. Check `xpsearch_bucket` is present exactly once and is not re-written per
    request.
64. Bucket pairing: put the same three values (B, 50 %, `hw11`) on a second widget on the page -
    both widgets flip together for a given browser, never one of each. Change one widget's split
    name and confirm they stop agreeing.

## M. RK-2 popularity task NULL-column fix (added 2026-09-01)
65. Re-run the failing task: **Scheduled tasks** -> `XpSearch.PopularitySignal` -> Execute. It now
    finishes green (no "Cannot insert the value NULL into column 'PopularityIndexEnabled'"), and
    XpSearch_PopularityIndex holds a row for the index with PopularityIndexEnabled = 0 and a
    computed-at stamp.
66. Idempotent: execute it a second time - still green, still one row per index, the stamp moves,
    and the opt-in toggle on Field weights still flips that row on and off.
67. Approve a popularity suggestion (Suggestions -> Approve): the rule is created without a
    RuleMigrated NULL error and appears on Rules.

## N. HW-13 host bundle — the sample now ships its own Vite build (added 2026-09-01)

The host no longer emits `<xps-search-assets />`; `src/Search/client/main.ts` imports seven widget
subpaths from the npm package and hydrates the Page Builder mounts. Rebuild order for this section:
`npm run build` in `libraries/.../XpSearch.Widgets/Client`, then `npm install && npm run build` in
`src/`, then `dotnet build CommProjects.sln`, then start. These are the HW-11 §F/§G behaviours
re-observed under the host bundle.

68. **No second runtime.** View source on `/search`: no `xpsearch.umd.js`, no
    `/_content/XperienceCommunity.Search.Widgets/xpsearch/*.css`; exactly one
    `~/dist/search/search.js` (`type="module"`) and one `~/dist/search/search.css`, both 200 in the
    network panel.
69. **SSR first paint, then hydration.** `/search?q=coffee` raw HTML still carries
    `data-xps-server-rendered` with product cards; after hydration the DOM has zero server blocks and
    one result list (no duplication), and the console is clean — in particular no
    `[xpsearch] unknown widget type` error, which would mean a `data-xps-widget` the bundle did not
    import.
70. **The product card survives the migration.** Hydrated result cards are the Dancing Goat product
    card (product name as the title, description snippet, `$18.50`-style price meta), identical to
    the server-rendered first paint — the `registerWidgetType('results', …)` override that used to
    live in `wwwroot/Scripts/xpsearchResults.js` now runs from the bundle.
71. **Facets, sort, stats, suggestions, pagination.** Tick a `ProductFieldCategory` facet, change the
    sort, page forward, type into the search box for the suggestions popup: each re-renders results,
    updates the "N results" line, and is styled (per-widget SCSS only — an unstyled control means a
    missing `scss/widgets/<name>` partial).
72. **URL routing and Back.** Typing writes `?q=…`, a facet click adds its param, Back restores URL +
    box + results; pasting the URL into a fresh tab reproduces the page server-side (HW-11 item 23).
73. **Only journaled once.** One search on page load in the query log for a shared result URL (the
    server `QueryId` is reused by the hydration query), as in HW-11 item 26.
74. **Adding a widget needs a rebuild.** Optional: place a widget the bundle does not import (e.g.
    Category tree, or switch Pagination's style to *load more*) → that one mount logs a
    `console.error` and is skipped while the rest of the page keeps working. Documented in
    `src/Search/README.md`; revert the page afterwards.

## O. XP-2 experiment section name (added 2026-09-01)

75. **The experiment section is named, not GUID'd.** Open an index's **Experiments** listing and click
    the experiment row (the seeded "Docs demo experiment" on DancingGoatSample, id 2, is the
    ready-made subject): the breadcrumb and the sidebar section header on the detail page show
    *Docs demo experiment*, not the experiment GUID (`3be7f5a5-...`). Same on a variant-scoped tuning page - open
    the experiment's **Rules** (`/admin/lucene/indexes/edit/2/experiments/2/rules`) and check both
    again. — **PASS** (owner, 2026-09-01, on the running host).

## P. HW-14 demo data parity — product thumbnails, path lines, clean autocomplete (added 2026-09-01)

Host-only changes, all in `src/`: the indexing strategy now contributes an `image` (product asset
URL) and a `path` (`Store › Grinders`) attribute and puts both in the index schema through an
`IContentTypeFieldSource` decorator; both product cards (`Search/Products.cshtml` and the
`registerWidgetType('results', …)` twin in `Search/client/main.ts`) render them; `SuggestField` is
`ProductFieldName`; the three leftover ingestion smoke-test documents (`pim` ×2, `kb` ×1) were
deleted and the index rebuilt (32 Xperience documents, no external ones).

76. **Thumbnails.** `/search?q=grinder` shows a 96×96 product photo on every product card, in the
    first paint (view source: `.xps-result__media > img.xps-result__image`) and after hydration.
    An image that 404s means the asset URL kept its `~/` prefix.
77. **Path lines.** Every product card shows a muted `Store › <category>` line between the title and
    the snippet (`.xps-result__path`), matching the mockup. Articles, if any are ever opted in,
    show their ancestor path instead.
78. **Autocomplete is real.** Type `aero` in the search box: the popup lists *AeroPress* and
    *AeroPress Filters* — product names, not web page item names (`AeroPress-vf15ekn4`) — and no
    `… (PIM)` / knowledge-base entries from the ingestion smoke tests. Clicking one navigates to the
    product page.
79. **No group headings yet (known gap).** The popup is one flat list with no "Suggestions" /
    "Pages" headings and no recent searches. That is a LIBRARY gap, not a host regression:
    `XpSearchIndexOptions.SuggestMode` is exclusive (`Documents` OR `QuerySuggestions`), so one
    response never carries both kinds, and `suggestionsPanel.ts` only groups when it does
    (`grouped = queries.length > 0 && documents.length > 0`). Verified on this host by temporarily
    flipping the index to `QuerySuggestions`: `"co"` → `coffee`, `coffee grinder` — text-only
    suggestions with no `result`, i.e. still one ungrouped list. Reverted to `Documents`.

## Q. Design-note review — mobile composition + edge states (added 2026-09-01; owner-requested check)

Source: mockup canvas `kentico-violet` — the `mobile-note` sticky and the three edge-state
artboards (LOADING / NO RESULTS–FILTERS ACTIVE / NO RESULTS–WITH RECOVERY). Items marked
**KNOWN FAIL** are confirmed gaps, tracked in `.paul/` (plans named per item) — tick them only
when their plan ships; the others verify what should already work.

80. **Sidebar collapses below 1024px.** Narrow the window under 1024px on `/search`: the facet
    sidebar disappears, leaving the Filter & Sort button (+ badge) and the scrolling chips row.
    **Walkable since MB-1** — the host's 25/75 side panel carries `dg-side-panel` and
    `Search/client/main.scss` hides it below 1024px (`:has(> .xps-mount)`, so only search
    sidebars collapse). Hidden, not unmounted, so the facet widgets keep owning the URL.
81. **Load-more replaces pagination below 1024px.** Under 1024px the results append via a
    Load more button; at desktop width numbered pagination returns. **Walkable since MB-1** —
    not a CSS swap: `loadMore` replaces `results`+`pagination` and owns `state.page`
    (widget-reference §loadMore), so `Search/client/main.ts` decides at mount time with
    `matchMedia('(max-width: 1023.98px)')`: the `results` mount gets a `loadMore` factory and the
    `pagination` mount an inert one. **The decision is per page load** — resizing the window does
    not swap; reload after resizing.
82. **Sheet apply button previews the pending count.** In the Filter & Sort sheet, tick a
    pending facet: the apply button reads "Show N results" with a live N (~250ms after the tick).
    Walkable since ES-1 (needs a bundle rebuild on the host). Check the analytics too: the
    previews must add **no** rows to the query log — that is what `probe: true` buys.
83. **First-search skeleton matches the card layout.** Cold load with a query: skeleton rows
    show a media square + text lines matching the thumbnail card (the mock's shape); skeletons
    appear only when no earlier results are on screen; refinements dim stale results instead of
    blanking. The media square is in the library markup (verified ES-1, `themes/fixtures/results.html`
    and `results.ts`; shell.css squares it off the media width), so this is a plain walk.
84. **Filtered no-results shows the unfiltered count.** `?q=<no-hits-with-filter>` + an active
    facet: "No results for ... with these filters", "There are N results without them", and the
    button reads "Clear filters and show N results". Walkable since ES-1 (needs a bundle rebuild
    on the host). The count appears ~250ms after the empty state; with nothing behind the filters
    either, the countless "Clear filters" is the correct answer, not a bug.
85. **No-results recovery: did-you-mean.** Misspelled query with no hits shows "Did you mean
    <correction>?" which runs the corrected query on click. **Walkable since SG-1** (needs a bundle
    rebuild on the host) — walked in detail as §S item 97.
86. **No-results recovery: popular searches.** The no-results state offers popular-search
    chips drawn from the analytics query log, rendered only when the host enables it.
    **Walkable since SG-1**, once the host sets `PopularSearchesOnNoResults` on the demo index —
    walked in detail as §S item 98.

## R. FZ-1 typo tolerance (added 2026-09-01)

87. Startup after rebuild: event log clean, and the `XpSearch_FuzzyIndex` table exists (no row yet —
    an index nobody opted in has none).
88. Off by default: before touching anything, search a misspelling on /search (e.g. `grinderr` —
    *not* `expresso`, which the seeded synonym group already covers) and confirm it returns nothing,
    exactly as before this unit.
89. Turn it on: **Edit index → Synonyms** shows the *Typo tolerance: off* callout and a **Turn typo
    tolerance on** header button. Click it — the success message appears, the callout flips to *on*,
    the button now reads *Turn typo tolerance off*, and `XpSearch_FuzzyIndex` holds one row for the
    index with the enabled flag set. (This DB round trip is what the unit tests could not cover.)
90. It works, without a restart: the same `grinderr` search now returns the grinder results, and each
    result's snippet still highlights the matched word (`<mark>grinder</mark>`) — the fuzzy-hit
    highlighting path. `explain: true` lists `fuzzy:on` beside the weight entries.
91. No stale page in between: the search in item 88 was cached before the toggle; running it again
    right after flipping (inside the cache TTL) returns the new results, not the empty page. Toggle
    back off and repeat — the empty result comes straight back.
92. Still ANDed, still exact-first: `espresso machne` returns only pages that have both words, and a
    correctly spelled `espresso` search keeps the exactly matching pages at the top.
93. Untouched by design: a rule whose condition is *Query contains espresso* does **not** fire for
    `expresso` (rule matching stays exact), and the suggestions dropdown still only completes what
    has been typed.
94. Experiment boundary: with an experiment running, A- and B-bucketed browsers both see typo
    tolerance, and the experiment's variant-B Synonyms page has no toggle.

## S. SG-1 mixed suggestions, recent searches, no-results recovery (added 2026-09-01)

Needs a bundle rebuild on the host (npm package + `Search/client`), and for items 96/98 two config
lines on the demo index: `o.Indexes["DancingGoatSample"].SuggestMode = SuggestMode.Mixed;` and
`o.Indexes["DancingGoatSample"].PopularSearchesOnNoResults = 5;`. Did-you-mean needs no
configuration — it is on by default, which is what item 97 checks.

95. **Recents are remembered and offered.** With the header/search-page autocomplete: search
    `espresso`, then `latte`, then clear the field and focus it — the panel opens on a **Recent
    searches** group alone, newest first, with no request to `/api/xpsearch/suggest` (check the
    network tab: the panel opens with no call). Type `esp` and the group narrows to the matching
    entries and now sits **above** the server's own groups. Picking one runs that search. Reload the
    page: the list survives. Check `localStorage` — one `xps-recent:DancingGoatSample` key, at most
    five entries — and confirm **no** request body ever carries them.
96. **Mixed suggestions.** With `SuggestMode.Mixed` on the demo index and a query log that has
    entries, typing `esp` shows two server groups — *Suggestions* (logged queries) then *Pages*
    (documents) — in one panel, never more than `limit` entries in total, queries first. Picking a
    query searches; picking a page navigates.
97. **Did-you-mean, unconfigured.** With typo tolerance **off** (turn it off if item 89 left it on),
    search a misspelling with no hits — e.g. `esspresso`. The empty state reads "Did you mean
    **espresso**?"; clicking the correction runs it and returns results. A misspelling the index
    cannot correct (`zzqwertyuiop`) shows the plain empty state, never a broken link or an empty
    "Did you mean ?".
98. **Popular searches.** Set `PopularSearchesOnNoResults = 5` on the demo index and restart. The
    same no-hit search now also shows a **Popular searches** chip row; clicking a chip runs that
    query. Remove the setting and the row disappears — it is opt-in. (Both recovery blocks are
    client-side: the server-rendered first paint still shows the plain empty state until the widgets
    hydrate.)
99. **Analytics honesty.** After walking 97 and 98, open **Edit index → Analytics** for the same
    range: the query log holds exactly **one** row per search you ran — the misspelling itself — and
    no row for the correction the server verified behind the scenes. Nothing in the report, the
    suggestion miner or the popularity signal knows about the verification search.
100. **The Page Builder switches.** Both the Search box (with *Suggest as the visitor types* on) and
    the Suggestions widget show an **Offer recent searches** checkbox, on by default. Clear it on one
    widget, save, reload: focusing its empty field opens nothing, and the panel shows only the
    server's groups. Tick it again and the previously stored list comes back (clearing the checkbox
    hides the group; it does not wipe the browser's list — the panel's own **Clear** control does).

## T. IX-1 contributed fields + suggest-field honesty (added 2026-09-01)

Prerequisite: the host replaces the `DancingGoatSearchFieldSource` decorator with `indexing.AddField`
calls (see the IX-1 report), then rebuilds the demo index.

101. **Contributed fields on the wire.** After the swap, `curl` `/api/xpsearch/query` for a product:
    the image and path fields the decorator used to smuggle in are present in `result.attributes`
    with the same names and values as before, and the results widget still renders thumbnails and
    path lines (HW-14 parity). The admin **Rules** and **Results** attribute dropdowns list them too,
    which the decorator never achieved.
102. **The undeclared-field warning.** Temporarily write a field from `ContributeAsync` without its
    `AddField` declaration and rebuild the index: the event log holds exactly **one** warning naming
    the field, the content type and the `AddField` call to add — not one per indexed item. Restore
    the declaration and rebuild: no warning.
103. **The suggest-field warning.** With an index whose `SuggestField` is left unset, type into the
    autocomplete: one warning per index appears in the event log naming `SuggestField`, and the
    suggestions show the slug-ish item names it describes. Set `SuggestField` to `ProductFieldName`,
    restart, and both the warning and the slugs are gone.

## U. CL-1 typed ingestion clients (added 2026-09-01)

Prerequisite: the dev API key's plaintext. `DevIngestionKeySeeder` logs it **once**, at Warning
level, the first time it creates `dev-sample`; it is unrecoverable afterwards. To get a fresh one,
delete the row from `XpSearch_ApiKey` and restart the host. Then `KEY=xps_…` and, with the host
running on `http://localhost:27340`:

104. **Node client round trip.** From `src/XpSearch.Widgets/Client`, after `npm run build`:

     ```bash
     node --input-type=module -e "
     import { createIngestionClient } from './dist/ingestion.mjs';
     const products = createIngestionClient({ endpoint: 'http://localhost:27340', apiKey: process.env.KEY }).index('DancingGoatSample');
     console.log(await products.upsert([
       { id: 'cl1-node-1', _source: 'pim', Title: 'CL-1 Node probe', ProductFieldName: 'CL-1 Node probe', ProductFieldPrice: 1.5, ProductFieldCategory: ['Coffees'] },
     ], { waitForIndex: true }));
     console.log((await products.status()).documents.bySource);
     "
     ```

     Expect `{ indexed: 1, failed: 0, batches: 1, errors: [], taskIds: [...] }` and a `pim` entry in
     the counts. Then search the site for **CL-1 Node probe** — the pushed document is in the
     results. Finally `console.log(await products.clear('pim'))` and search again: it is gone and the
     32 Xperience documents are still there.

105. **C# client round trip.** Same three steps from a throwaway console app that references
     `src/XpSearch.Client/XpSearch.Client.csproj` (`dotnet new console`, `dotnet add reference`):

     ```csharp
     using XpSearch.Client;
     using var client = new XpSearchIngestionClient("http://localhost:27340", Environment.GetEnvironmentVariable("KEY")!);
     var products = client.Index("DancingGoatSample");
     var result = await products.UpsertAsync([XpSearchIngestionClient.Document("cl1-dotnet-1",
         new { Title = "CL-1 dotnet probe", ProductFieldName = "CL-1 dotnet probe", ProductFieldPrice = 1.5, ProductFieldCategory = new[] { "Coffees" } },
         source: "pim")], waitForIndex: true);
     Console.WriteLine($"{result.Indexed}/{result.Failed} in {result.Batches} batch(es)");
     Console.WriteLine((await products.GetStatusAsync()).Documents.BySource["pim"]);
     Console.WriteLine((await products.ClearAsync("pim")).Deleted);
     ```

     Expect `1/0 in 1 batch(es)`, the `pim` count, then the deleted count. `dotnet build` on that app
     must pull in **no** Kentico or Lucene package — that is the point of the separate client.

106. **The failure paths are honest.** With the same key: push a document whose
     `ProductFieldPrice` is the string `"free"` — the call *succeeds* and `result.Errors` names
     `cl1-node-2` / `ProductFieldPrice`. Then run either client with a wrong key: it throws once,
     with status `401` and the server's Problem Details title, and does **not** retry (the call
     returns immediately, not four backoffs later).

107. **A `429` is waited out, not fought.** Push ~80 single-document batches in a loop
     (`maxDocumentsPerRequest: 1`) so the 60-per-minute per-key limit trips. The run must not fail on
     the first `429`: the client sleeps instead. Note what it does next — the host's fixed window is
     60 s and the client's `maxRetryMs` ceiling is 30 s, so with the defaults it can burn its four
     attempts inside one window and still throw. If it does, that is expected and the fix is a
     `maxRetryMs` / `MaxRetryDelay` at or above the window (raise it and re-run to confirm the loop
     then finishes clean). Either way the event log shows no ingestion *errors* — only rejections.

## V. EX-2 computed relevance field, worked example 2 (added 2026-09-01)

Prerequisite: the host's `src/Search/` carries the `clicks` field (`DancingGoatSearchIndexingStrategy`
+ the `AddField` / `SortKeys["popular"]` lines in `Program.cs`) and `ClicksBoostStage`. The built-in
popularity boost for `DancingGoatSample` was found **off** and left off — the two boosts stack, so the
demo below is only honest with it off. Turn it back on afterwards if item 45's flow needs it (**Lucene
Search → DancingGoatSample → Edit index → Field weights → Boost by popularity**).

108. **The computed field is on the wire.** Rebuild the demo index, then `curl`
     `/api/xpsearch/query` for a product a visitor clicked: `result.attributes.clicks` is a number,
     and it is `0` for a document nobody clicked (never missing). The value only changes on
     re-index — that is the guide's staleness ceiling, and it is the expected behaviour, not a bug.
109. **The `popular` sort works with no further code.** `{"index":"DancingGoatSample","query":"",
     "sort":"popular","fields":["title","clicks"]}` returns the documents in descending `clicks`
     order. The live search page's sort dropdown is *not* configured to offer it — adding
     `popular` to the widget's sort options is an owner decision.
110. **Ranking moves with the signal.** Search **filter** and note the order (a mid-list product with
     no clicks). Click that result four or five times through the results widget (or `POST
     /api/xpsearch/events` with each search's `queryId`), rebuild the index, and search **filter**
     again: that product is now first. Add `"explain": true` and `ranking.boosts` names the clicks
     boost. A document with clicks that does **not** match the text must still be absent from the
     results — the boost is a SHOULD clause, never a filter.

## W. CD-1 page commands answer (added 2026-09-01)

**Prerequisite, and the point of the whole section:** the host builds this library by
`ProjectReference` into `..\libraries\xperience-search\src`, i.e. the **main** worktree. Check out
`main` there (no unit branch), rebuild the solution and restart the host before clicking — the
2026-08-31 "command not found" reports came from an instance built minutes before the `Delete`
commands (23:01) and the rule builder's pickers (22:23) merged. `PageCommandDiscoveryTests` proves
every command below resolves on the current main; these items confirm the running host agrees.

111. **The three that failed.** Lucene Search → an index → Edit → **Field weights**: delete a weight
     (row menu → Delete → confirm) — the row goes, no "command not found". **Rules**: delete a rule
     the same way. Open a rule → in an action card use the **item picker** (type a word, results
     appear) — that is `SearchItems`.
112. **The never-clicked siblings of the same shape.** Delete a row on **Synonyms**, on **Stopwords**
     and on **API keys** (`xpsearch-tuning → API keys`). Each is the identical `Delete` command on a
     different listing.
113. **The variant-B twins.** Inside an experiment (Experiments → an experiment → Field weights /
     Rules / Synonyms / Stopwords), delete a variant row — that is the differently named `DeleteRow`
     command, and a wrong-named action would show up here and nowhere else.
114. **The rule builder's other commands.** In the rule editor: the attribute picker's value list
     (`GetAttributeValues`), **Save**, **Cancel**, and **Delete rule**. All four are inherited from
     the abstract `RuleBuilderPage` — the shape the old convention wrongly suspected.
115. **If any of these still says "command not found"**, capture the page name from the message and
     the assembly the host loaded (`XpSearch.Admin.dll` timestamp in the host's `bin`) *before*
     reporting — that message names the page it looked on, and the answer is usually the timestamp.

## §Y — AR-2 per-index search settings (2026-09-03)

Supersedes §X: the settings moved from one global page to **Lucene Search → an index → Search
settings** (named options per index; the host lambda's root values are the defaults for every
index; a row exists only after a save). Items 116–120 below are kept for history only.

121. **Per-index page, no global page.** Lucene Search → **DancingGoatSample** → **Search settings**
     lists the sixteen values with the code defaults (retention **365**, default page size **20**).
     **Search ingestion** no longer has a Settings entry.
122. **Live, and only that index.** Set **Default page size** to **3**, save, then reload
     `/search?query=coffee` → the first page shows three cards (the demo's Results widget has
     *Results per page* = 0 since 2026-09-03, i.e. "use the index setting"; a widget with its own
     number overrides the setting on purpose). The API probe from 116a shows the same `pageSize 3`.
     A second index (create one in Lucene Search if there is none) still answers `20`. Set
     DancingGoatSample back to 20 → twenty cards. No restart at any point.
123. **Retention per index.** Set DancingGoatSample's retention to **1**, save, run the
     `XpSearch query log retention` task → *Last result* names the index and its three deleted
     counts; a second index's rows (if any) are untouched. Restore the value.
124. **Survives a restart.** Set a value, restart the host, reopen → unchanged.
125. **Orphan rows use the defaults.** Delete (or rename) a test index that has a settings row and
     log rows, run the task → the event/console log names the orphan index and the rows are pruned
     with the default window.

## §X — AR-1 analytics retention setting (2026-09-02) — SUPERSEDED by §Y

Every global `XpSearchOptions` / `Analytics` value is now editable on **Search ingestion →
Settings** (the library's own admin application), seeded once from the host's `AddXpSearch`
lambda and loaded through `ConfigureOptions` with live updates; retention defaults to 365 days,
and the `XpSearch.QueryLogRetention` task now also prunes answered suggestions.

116. **Visible with the defaults.** Open **Search ingestion → Settings**. Every row of the AR-1
     spec's table is listed; **Remove search analytics older than X days** shows **365**, and
     **Maximum page size** shows the value the host lambda set (100 unless Program.cs changed it).
116a. **Live without a restart.** The demo's results widget sends its own page size (the
     *Results per page* property), so the demo page cannot show this — the API can. Set
     **Default page size** to **3**, save, then within a few seconds run in a terminal:
     `Invoke-RestMethod -Method Post -Uri http://localhost:27340/api/xpsearch/query -ContentType application/json -Body '{"index":"DancingGoatSample","query":"coffee"}' | Select-Object pageSize`
     → `pageSize 3`. Set it back to 20, re-run → `20`. No restart in between. (First walk
     2026-09-02: the value only showed up after ~30 minutes — the row read sat behind a Kentico
     cache entry whose dependency did not fire; fixed by reading the row directly on each
     options rebuild, which only happens on a save.)
117. **The task honours it.** Set the value to **1**, save. Open **Scheduled tasks** → the
     `XpSearch query log retention` configuration → **Run**. *Last result* reads
     `Deleted N query log rows, N popularity suggestions, N synonym suggestions older than <cutoff>` —
     the cutoff is yesterday, and the analytics dashboard's volume report drops everything older.
     Set the value back to something sane (e.g. 365) afterwards.
118. **Validation.** Set the value to **0** and save → the save is refused with a validation
     message; the stored value is unchanged.
119. **Survives a restart.** Set the value to **90**, restart the host, reopen the setting → still
     **90** (the installer seeds once and never overwrites).
120. **Suggestions.** On an index with an *accepted* or *dismissed* popularity or synonym suggestion
     older than the cutoff (set the value to 1 again to make yesterday's answered suggestion old),
     run the task → that answered row is gone from the listing's history; every **pending**
     suggestion is still listed.
