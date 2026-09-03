# Unit AR-3 — Widgets own their sizes; the index owns the caps

Owner decision 2026-09-03 (closing 06-02): the per-index **Default page size** and **Default
suggestion count** settings are redundant — every widget that pages or suggests has its own size
property, and API callers that omit a size already get the code default. Remove both settings;
make the widget size properties required with minimum 1; keep the caps.

Read `docs/internal/agent-primer.md`, `docs/internal/units/AR-2.md`, `docs/internal/units/UX-1.md`.
Land this ON TOP of `unit/ux-1` (rebase that branch onto current `main` first — main now has the
cache-eviction fix 583fb6b), in the same worktree, as a second commit on `unit/ux-1`.

## 1. Settings removed (Core + Admin)

- `XpSearchIndexSettings`: drop `DefaultPageSize` and `DefaultSuggestLimit`.
- `XpSearchOptions.DefaultPageSize` / `DefaultSuggestLimit` **stay** as the code defaults for API
  callers that omit `pageSize` / `limit`; `NormalizeRequestStage` and `DocumentSuggestService` read
  those two from `IOptionsMonitor<XpSearchOptions>` (root) again, everything else from the
  per-index settings as today. XML docs: "API callers that send no size; widgets always send one".
- `SearchSettingsValues`, `StoredSearchSettings.Read/NewRow`, `XpSearchSettingsInfo`, the installer
  form: drop the two columns. The two database columns already created on existing installs stay
  behind as unused — record in KNOWN-LIMITATIONS (symbol: installer `SettingsForm`; ceiling: two
  dead int columns; upgrade path: a one-off `DataClassInfo` field removal). Do not write DDL.
- `SearchSettingsModel` / `SearchSettingsPage`: drop the two fields.
- Tests: adapt `StoredSettingsTests`, `InfoCreationSiteTests`, any pipeline test that set the
  per-index default page size / suggest limit.

## 2. Widget size properties required, minimum 1 (Widgets)

- **Results — Results per page:** `[MinimumIntegerValueValidationRule(1)]` + `[RequiredValidationRule]`
  (or the equivalent Kentico attribute the file already uses elsewhere), default **20**; label back
  to **Results per page**; tooltip/explanation: "Results on each page of this widget. Capped by the
  index's *Maximum page size*." Remove the `0 = index setting` branches in `ResultsWidget`
  (`pageSize > 0` guards, the editor preview's `<= 0 ? 3` fallback) and in `ServerRenderedResults`
  (`if (options.ResultsPerPage > 0)`) — the value is always set now. Keep `firstPaint?.PageSize`
  (the clamped value the server applied) as the hydration page size.
- **Search box — Maximum suggestions** and **Suggestions — Maximum items:** required, minimum 1,
  defaults unchanged (5); explanation: capped by the index's *Maximum suggestion count*.
- Editor preview notes that printed "unset" for these values: simplify.
- Tests: `MountMarkupTests`, `EditorPreviewTests`, `ServerRenderedResultsTests` (Core + Widgets)
  — update pinned strings/behaviour; no test may keep asserting a 0 path.

## 3. Wording (UX-1 follow-through)

The interaction rule becomes one sentence, stated once in the guide and echoed on the relevant
fields: **widgets own their sizes; the index owns the caps; API callers that send no size get the
code default.** Update every UX-1 tooltip/explanation that mentioned "0 = index setting" or
"Default page size" / "Default suggestion count"; fix UX-1's cache sentence — a save DOES drop the
index's cached responses now (583fb6b): "A save applies to the next request; the index's cached
responses are dropped with it."

## 4. Docs + changelog

- `docs/guides/search-api.md` per-index table: remove the two rows; note the two code defaults in
  the "code defaults" paragraph. `docs/guides/page-builder-widgets.md`: property table + the
  interaction subsection updated. `docs/guides/server-rendering.md` sample already sets
  `ResultsPerPage: 10` — fine; state it is required.
- CHANGELOG `[Unreleased]`: fold into the existing AR-2 **Breaking (core)** entry (two settings
  removed; widget sizes required) and the UX-1 **Changed** entry; no new entries.
- `docs/internal/KNOWN-LIMITATIONS.md`: the dead-columns entry (§1).

## 5. Verification + commit

All five suites green (build both clients first); Bench compiles. BOM/mojibake scan clean. Second
commit on `unit/ux-1`: `feat(core,widgets,admin): widgets own their sizes, the index owns the caps (AR-3)`.
Report: suite lines, what the rebase touched, commit hashes (both), files changed, concerns.
