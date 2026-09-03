# Unit UX-1 — Tooltips and explanation text for every setting and widget property

Owner request 2026-09-03: every field on the per-index **Search settings** page and every Page
Builder widget property carries a **tooltip** (one sentence: what it is) and **explanation text**
(one or two sentences: what it changes on the site and how it interacts with the other side —
the index setting a widget property overrides, or the widgets a setting drives). Text only; no
behaviour change, no new properties, no contract change.

Read `docs/internal/agent-primer.md`, then `docs/internal/units/AR-2.md` §1 and §6 (the settings and
their precedence). Work only in your worktree (branch `unit/ux-1`).

## 1. Precedence to state consistently

Three layers, one direction, and the text must say so wherever it applies:

1. **Widget property** (when set — for numbers, above 0) wins for that placement only.
2. **Index setting** (Lucene Search → index → Search settings) applies to every request that does not
   name the value, including a widget left at 0.
3. **Code default** (`AddXpSearch(o => …)`) applies to an index nobody saved settings for.

## 2. Search settings page (`src/XpSearch.Admin/UIPages/SearchSettings.cs`)

For each of the sixteen fields, set on the `NumberInputComponent` attribute:
- `Tooltip` — what the value is, ≤ 1 sentence.
- `ExplanationText` — what it affects and which widgets/requests read it. Name the widgets by their
  Page Builder display names. Mention the override where one exists. Examples of the register:
  - Default page size → "Results per page when a request does not ask for a size. The Results and
    Load more widgets use it unless their own *Results per page* is set above 0; the JS client uses
    it whenever `pageSize` is omitted."
  - Maximum page size → "Larger requested sizes are clamped to this and the clamped value is
    reported back. Caps every widget's *Results per page* and every API caller."
  - Response cache lifetime → "How long an identical query is answered from cache. Saving these
    settings drops this index's cached responses, so a change is visible on the next request."
  - Remove search analytics older than X days → the retention sentence from AR-1 (task name, three
    tables, pending suggestions never deleted) + "Feeds the Analytics dashboard's history depth."
  - Query suggestion window / Popularity* / Synonym* → which admin listing or widget consumes the
    result (Suggestions widget in query/mixed mode; Popularity suggestions listing; Synonym
    suggestions listing; popularity boost stage) and which scheduled task computes it.
- Keep labels as they are; validation attributes unchanged.

## 3. Widget properties (`src/XpSearch.Widgets/Components/Widgets/XpSearch/*Widget.cs`)

Inventory every `[…Component(...)]` property on every widget properties class (14 widgets; include
the shared `Index`/`InstanceId` base properties once, on the base class). For each: `Tooltip` +
`ExplanationText` per §1's register. Required specifics:
- **Results / Load more — Results per page:** "0 = the index's *Default page size*; any other value
  overrides it for this widget only, capped by *Maximum page size*." Rename the label to
  **Results per page (0 = index setting)** — the only label change in this unit.
- **Search box — Suggestion limit:** relation to the index's *Default/Maximum suggestion count*
  (same 0/override/cap rule if the property behaves that way; check the code, do not assume).
- **Facet list / Category tree — Limit:** relation to *Maximum values per facet* (cap).
- **Range filter, Filter & sort, Sort select, Result stats, Active filters, Clear filters,
  Pagination, Suggestions:** state what the property changes on the page and which other widget or
  index setting it depends on (e.g. Sort select options must be sort keys the index publishes in
  code; Filter & sort facets must match a Facet list attribute; Instance id groups widgets that
  share one search state).
- Where a property has no interaction with anything else, the explanation says what it changes
  and nothing more — do not invent interactions.

## 4. Docs

- `docs/guides/page-builder-widgets.md`: the property table gains the same wording (short form)
  and a new subsection **"How widget properties and index settings interact"** stating §1 once.
- `docs/guides/search-api.md` per-index settings table: one "Used by" column naming the widgets/
  callers, matching the explanation texts.
- `docs/internal/screenshot-manifest.md`: mark the widget property dialogs and the Search
  settings page rows STALE (text changed); list them in your report.
- No CHANGELOG entry beyond one `**Changed (admin, widgets):**` line ("every setting and widget
  property now explains what it does and what it interacts with; *Results per page* label
  clarified").

## 5. Constraints and verification

- Text only. No behaviour, defaults, validation or property changes; no new resources unless the
  widgets already use `WidgetResources` for property text — follow whatever the file does today.
- Run Admin + Widgets suites (build both clients first) — `PageCommandDiscoveryTests`, mount markup
  tests and editor preview tests must stay green (labels appear in previews: check the preview
  tests that pin label strings and update the pinned strings, not the behaviour).
- One commit on `unit/ux-1`: `docs(admin,widgets): tooltips and explanation text for every setting and widget property (UX-1)`.
- Report: the full inventory (widget → properties → done), the suite lines, files changed, commit.
