# Unit PB-6 — Results widget: schema-driven field selectors + first-load journal handoff (HW-11 #28, #29)

Owner-requested on the host, 2026-09-01. Two changes to the Results widget, one unit because
they share the same files.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/pb-6`).

## 1. Fields / attribute selectors from the index schema (#28)

Replace the free-text editor properties on `ResultsWidgetProperties` with schema-driven
selectors, using the EXACT §7.4 precedent (the facet-attribute dropdown: a
`FormComponentConfigurator` registered via `[assembly: RegisterFormComponentConfigurator]` with
the identifier constructor — so the live-site package never depends on Kentico.Xperience.Admin —
reading the widget's `Index` value through `IFormFieldValueProvider` and filling options from
`IIndexSchemaProvider`; the Index field's lower `Order` makes the dependency legal; find it in
the Widgets/Admin source and follow it):

- **Fields to show** → multi-select `GeneralSelectorComponent` over the index's retrievable
  fields. Verify the component's stored value shape and its data-provider contract in the
  Kentico docs MCP first — do not guess.
- **Title attribute** / **Link attribute** → single-select dropdowns over the same schema
  (keep "empty = default" semantics via a placeholder).
- **Snippet attributes** → multi-select, ordered (verify GeneralSelector preserves selection
  order; if it does not, keep the textarea for snippets and say so — order is semantic there).

**Backward compatibility is non-negotiable:** existing saved widgets store these as
newline/plain strings. Changing a property's serialized shape must not break deserialization of
existing pages. Verify what each component actually persists; where the shape changes, keep the
stored property readable (e.g. new property + fallback read of the old one in the view
component, old property hidden from the dialog) — pick the smallest scheme that keeps every
existing page rendering identically, and record it in the report. MountMarkupTests must cover
old-shape and new-shape properties producing correct config.

## 2. First-load double-journal fix + request parity (#29)

Today the server-rendered first paint (DX-2) journals one query-log row and the JS hydration
query journals a second (different queryIds) — recorded limitation with this exact upgrade path:

- The server render's `SearchResponse` carries its query id (verify the member; the journal
  keys rows by it). `ResultsWidget`/mount emits it into `data-xps-instance-config` (e.g.
  `initialQueryId`) ONLY when server rendering actually produced it.
- JS (`Client/src`): the instance's FIRST query after hydration sends that id with the request
  (`SearchRequest` gains an optional member — this is an **additive owned-contract change**:
  regen via `npm run contract:gen`, `contract:check` must pass, note the minor-version bump in
  CHANGELOG per contract policy). Subsequent queries never send it. Verify the Core pipeline
  reuses a supplied queryId for journal/log rows (the KNOWN-LIMITATIONS entry says it does —
  confirm in `SearchRequestJournal`/`CachedSearchPipeline`; if reuse needs a small Core tweak to
  update-instead-of-insert the log row, make it surgically and test it).
- **Request parity:** the handoff is only honest if hydration re-runs the SAME query. HW-11
  found the server paint shows 6 cards vs the client's 12 — the server used the index default
  page size while the client used its own default. Align them: the server render must build the
  request with the same effective defaults the JS client sends (find where each default lives;
  prefer emitting the effective page size into instance config over duplicating constants).
  State root cause + fix in the report.
- Remove/amend the double-journal KNOWN-LIMITATIONS entry; the skeleton-flicker entry stays.

## Deliverables

- C# + JS changes, tests: MountMarkupTests (selector configs, old/new shapes, initialQueryId
  presence only when server-rendered), vitest (first query carries the id, second does not),
  Core test if the reuse path changed, parity test (server request page size == client default
  when widget leaves it unset).
- Guide updates (`page-builder-widgets.md` — the dialog fields changed; wiki-ready, verified
  samples). CHANGELOG entries. ADR only if the compat scheme warrants recording.
- All four C# suites + JS suite + `contract:check` green. Conventional commits on `unit/pb-6`;
  commit this spec file.

## Constraints

- Files: `src/XpSearch.Widgets/**`, `src/XpSearch.Core/**` (only if the queryId reuse needs
  it), their test projects, docs. Do NOT touch `src/XpSearch.Admin/**` — sibling agents own it
  this round. Kentico docs MCP mandatory for GeneralSelector/configurator APIs.
- No new dependencies. Contract change is ADDITIVE only.
