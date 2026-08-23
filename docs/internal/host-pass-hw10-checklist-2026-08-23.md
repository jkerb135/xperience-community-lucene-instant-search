# HW-10 — owner browser checklist (2026-08-23)

Library `main` **09c92db**. Everything below needs a signed-in administrator and a real browser; none
of it is observable headlessly. Before starting: stop the host, run `npm ci && npm run build` in
`src/XpSearch.Admin/Client` (the admin bundle is gitignored and now carries three templates) and in
`src/XpSearch.Widgets/Client`, then `dotnet build CommProjects.sln` from the umbrella root and start
the host. On first start the library adds the `RuleContactGroup` column, refreshes the four activity
type descriptions and writes three `CMS_MacroRule` rows — watch the startup log for warnings.

Index **2** = `DancingGoatSample`. Tuning pages live at `/admin/lucene/indexes/tuning/2/<slug>`.

## A. Redesigned admin pages (AD-4a, AD-4b — design project d9cffec1…)

### Analytics (`…/tuning/2/analytics`)
1. **1a loaded.** Headline "Analytics", index line "Index **DancingGoatSample** · …". Controls card: range
   toggle 7/30/90, From/To date inputs, Rows select, **Load**. Four KPI tiles, the two-series chart, then
   **Zero-result queries** (with **Create rule** actions) above the two-column row of Top queries /
   Click-through (footer: average clicked position) / Slowest queries.
2. **Stock `Table` inside a `Card`** — the single most likely rendering problem: the table must not show
   a virtualisation scrollbar or collapse to zero height. Check all four tables.
3. **Create rule** on `yirgacheffe` opens the seeded rule form and the saved rule lands in Rules.
4. **DateTimeInput** (`timeZone="UTC"`): the day shown equals the day sent (compare the headline's range
   after Load); narrowing the range with the min/max clamps never locks you out.
5. **1b empty** — pick a range with no searches: KPIs read "—", one "No searches in this range" card with
   **Load last 30 days** replaces chart + tables.
6. **1c error** — stop the host's DB connection is not practical; accept the unit test evidence unless
   you can provoke it. Callout type Friendly warning + **Load again**.
7. **1d narrow** — resize to ≤1365 px: KPIs 2 per row, tables stack with zero-result first.

### Query tester (`…/tuning/2/query-tester`)
8. **2b before run** — Quick tip callout, two placeholder cards, **Run disabled** with helper text until a
   query is typed. Controls: Query, Language, Page size, **Contact group** (PZ-1), Run.
9. **2a loaded** — run `espresso`: two cards "With tuning"/"Without tuning", stats strip "N results · N ms ·
   N changed", one card per hit (position, title, url, score/base, change marker as icon + Tag, boosts),
   "Rewritten query per pipeline stage" collapsible below.
10. **2c error** — select a language the index has not been built for: Friendly warning with **Open status**
    (navigates to the Status page) and "Try <language>".
11. **2d narrow** — columns become the With/Without toggle row; explanations collapsed.

### Status (`…/tuning/2/status`)
12. **3a** — health Tag "Healthy", figures (Documents / Sources / Last external write — "never" if no
    external push), by-source stacked bar whose swatches match the table, Share sums to 100 %, Quick tip,
    Recent ingestion (last 10) with Operation/Result tags. **Rebuild index** is the destructive page action.
13. **3c/3e** — Rebuild → `Dialog` "Rebuild the index?" styled like the platform's own confirmations →
    **Rebuild** → success message "Rebuild of 'DancingGoatSample' triggered." and the header/health turn
    into "Rebuild in progress · Started <utc>" until reload. (No "n of m" numerator — Lucene 15.0.5 exposes
    none; KNOWN-LIMITATIONS.)
14. **3b degraded** — only if you can make an external push fail (push a document Lucene rejects via the
    ingestion API): failed rows first with the red invalid-row treatment **and** a "Failed" tag,
    Friendly warning with **Copy failure details** (clipboard — confirm it works on your origin) and
    **Rebuild index**, "<source> has never written successfully" line under the by-source table.
15. **3d narrow** — figures wrap, action under the headline, ingestion message on its own line.
16. **Permissions** — a role with *View* but not *Rebuild* on **Lucene Search** sees the page and the
    Rebuild command is refused.

## B. Activities and contact groups (AN-2, AN-3)

17. **Activity types** — Contact management → Activity types: "Search result click" and "Search
    conversion" show the new descriptions; a type you disabled before the restart stays disabled.
18. **Activity values** — in a browser session that accepted tracking, run a search with hits, one with
    none, click a result. Contact management → the contact → Activities: every row's value is the
    **query text** (no pipe string); `OM_Activity.ActivityComment` holds the result id on the click row and
    `ActivityItemDetailID` its 1-based position.
19. **Condition picker** — Contact groups → New → Edit conditions → Add condition, type `searched`: three
    rules under **Web activity** — *Contact has searched for text containing {text}*, *…without results
    for…*, *…clicked a search result after searching for…* — each with a text input for `{text}`
    (empty allowed).
20. **Recalculation** — group "searched without results for text containing `yirgacheffe`" → Save →
    Recalculate: the HW-9 contact appears on the Contacts tab. Then log a fresh no-result search from
    another consented session and confirm it joins on the next activity-driven recalculation (expected
    through the platform's all-activities fallback; per-contact macro evaluation, see KNOWN-LIMITATIONS).
21. System rule `CMSContactHasPerformedCustomActivityWithValue` still has `MacroRuleUsageLocation = 4`
    (unmodified).

## C. Personalised rules (PZ-1) — the loop

22. **Rules form** — Rules → New: **Contact group** object selector sits after *Enabled*; listing shows
    "Everyone" for existing rules (the new column was added without touching them).
23. Create group *Grinder shoppers* from item 20's pattern (or any condition you can satisfy), then a rule
    scoped to it: *Contains* `coffee`, boost a grinder ×2.
24. **Query tester simulation** — Contact group = *Grinder shoppers*, query `coffee`: the grinder is
    boosted and its boost line reads `rule:<name> (contact group grinder-shoppers)`. Switch to *Real
    visitor (your contact)*: the boost disappears.
25. **Real visitor** — in the consented session that is a member, search `coffee` on `/search`: boosted.
    In a session that declined tracking: not boosted, and no `CurrentContact` cookie is created by
    searching.
26. **Cache isolation** — search as a member and immediately as a non-member within the cache TTL: the
    two result orders differ.

## D. Page Builder (from HW-9, still open)

27. Widget list icons: Facet list now `icon-funnel`; Range filter `icon-arrows-h`; Category tree
    `icon-tree-structure` — none blank.
28. Range filter's **Attribute** drop-down lists only numeric/date fields of the selected index; the
    Category tree's lists facetable fields; both hide with no index selected.
