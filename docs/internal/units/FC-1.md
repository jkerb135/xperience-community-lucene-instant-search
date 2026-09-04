# Unit FC-1 — Facets always carry the request's selected values (with labels)

Owner rule 2026-09-03: no filter code reaches an end user. TH-12 resolves value labels from the
facet values the server returns, remembered across responses. That leaves one hole the owner will
hit first: a **filtered cold load** (deep link with filters, e.g.
`/search?q=coffee&ProductFieldTags=HotTips&CoffeeTastes=Sweet%2FAcidy&ProductFieldPrice_lte=200`)
whose result set is empty or no longer contains a selected value — the response has no facet
entry for `HotTips`, nothing has been remembered yet, and the chip and the zero-hit fallback row
print the stored code. Fix it at the source, the way every search API that supports refinements
does: **the facet output always contains the request's selected values, with their labels, count
0 when the filtered set has none.**

Read `docs/internal/agent-primer.md`, `docs/adr/0001-faceting-approach.md`, `Core/Facets/*`
(`TaxonomyFacetProvider`, `IFacetProvider`, `CollectFacetsStage`), `IndexSchema`/`SchemaField`
(where a facet value's label comes from), TH-7 §2 and TH-12 (the client side). Work only in your
worktree (branch `unit/fc-1`). **No contract change** — `FacetValue { value, label, count, path? }`
already has everything; only the *set* of values returned changes.

## 1. Behaviour

- For every facet the request asks for, after computing the counted values from the filtered
  result set, **append each value the request filters that attribute by** which is not already
  present, as `{ value, label, count: 0, path }` — label resolved the same way counted values get
  theirs (taxonomy tag title / path; for a plain facet field the value itself — say which fields
  have labels at all). Order: keep the provider's order for counted values; the appended
  selected-but-absent values follow in request order.
- Applies to both `or` and `and` operators and to the drill-sideways path (ADR-0001): with `or`,
  the sideways counts already include the selected values when the *unfiltered* set has them;
  the append is for values that have zero hits in every set (typo in a deep link, a tag removed
  from every product) — they must still come back with their label so the visitor can see and
  remove them.
- Numeric refinements are not facets: untouched.
- Hierarchical (`categoryTree`): a selected path `Sweet/Acidy` returns the leaf with its `path`
  segments labelled, so TH-12's `Taste: Sweet › Acidy` renders from the wire, not from memory.
- Response cache: the selected values are part of the request, hence already part of the cache
  key — no change; verify.

## 2. Server-rendered first paint

`ServerRenderedResults` today emits no facets. Emit the label map the client needs for a
filtered cold load: the smallest honest shape is a `data-xps-labels` attribute on the results
mount carrying `{ attribute: { value: label } }` for the request's selected values only (not all
facets), and `bootstrap.ts`/`labels.ts` seeds the label memory from it before the first render.
That removes TH-12's "raw value for one frame" limitation — delete that KNOWN-LIMITATIONS entry.
Keep the mount markup tests (`MountMarkupTests`) and `ServerRenderedResultsTests` pinned.

## 3. Tests

- Core: `TaxonomyFacetProvider`/`CollectFacetsStage` tests — selected value absent from the
  filtered set comes back with count 0 and its label; present values unchanged; `and`/`or`; a
  selected value that does not exist in the taxonomy at all returns `label = value` (never
  omitted); hierarchical path labels; cache key untouched.
- Widgets/JS: the TH-12 label test for the spec URL now passes against a *real-shaped* response
  (fixture updated), and the cold-load seed test reads `data-xps-labels`.
- Suites: Core, Widgets C# (after client build), JS.

## 4. Docs + commit

`docs/guides/search-api.md` facets section: one paragraph ("selected values are always returned,
count 0 if absent, so a UI can always name what is applied"). CHANGELOG `**Fixed (core, widgets):**`.
Delete the TH-12 SSR limitation entry. Commit on `unit/fc-1`:
`fix(core,widgets): facets always include the request's selected values with labels; first paint seeds them (FC-1)`.

## Report

Before/after JSON for the spec URL's `ProductFieldTags` and `CoffeeTastes` facets, the SSR
attribute sample, suite lines, commit hash.
