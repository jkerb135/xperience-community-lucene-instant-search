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
