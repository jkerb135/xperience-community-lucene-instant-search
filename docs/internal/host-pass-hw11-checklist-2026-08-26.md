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
