# Unit PB-5 — URL routing must ignore foreign query parameters (live defect)

Owner-reported on the running host, 2026-09-01: with a `uh` parameter in the page URL (Kentico's
own preview parameter), every search returns HTTP 400 —
`filters.facets[0].attribute: ["'uh' is not an attribute of index 'DancingGoatSample'."]`.

Root cause: `defaultRouteToState` (`Client/src/routing.ts`) adopts EVERY query parameter that is
not `q`/`page`/`sort` as a facet or numeric filter. Any foreign param — Kentico's `uh`, `utm_*`,
`gclid`, anything — becomes a filter the API correctly rejects. Latent since JS-1; exposed by
PB-3 turning routing on by default. `SearchQueryState` (server, DX-2) has the same greedy
mapping — it degrades gracefully (logged warning, empty mount) but must be fixed consistently.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/pb-5`).

## Fix design (decided)

**Client — route only what the page declares.**
- The instance keeps a registry of routable filter attributes. Facet-ish behaviors
  (`facetList`, `categoryTree`, `toggleFilter`, `activeFilters`? — audit `behaviors/`, every one
  that puts an `attribute`/numeric filter into state) register their attribute with the
  instance when connected; `withRange` registers as numeric.
- Default routing hydration (`defaultRouteToState`, or its call site if cleaner) then adopts,
  besides `q`/`page`/`sort`: facet params only for registered facet attributes (including their
  `_op`), numeric params only for registered numeric attributes with a valid operator suffix.
  Everything else is IGNORED — and must be PRESERVED in the URL by `router.write` (verify: write
  already only deletes params the mapping owns; add a test proving `uh=x` survives a state
  write).
- Bootstrap path ordering is safe (widgets are added before `start()`); document in the code
  that attributes registered after `start()` are not routable, don't engineer for it.
- A custom `routing.routeToState`/`stateToRoute` bypasses all of this — untouched escape hatch
  for anyone who wants adopt-everything back.

**Server — validate against the schema.**
- `SearchQueryState.Apply` (or its caller `ServerRenderedResults`) restricts facet/numeric
  adoption to attribute names that exist in the index schema (`IIndexSchemaProvider` — the §7.4
  dropdown already reads it; resolve schema by index code name, tolerate lookup failure by
  falling back to current behavior's graceful degradation). `q`/`page`/`sort` unchanged.

**Behavior change to document:** a shared URL carrying a filter for an attribute with NO
corresponding widget on the page is now ignored client-side (previously it applied invisibly and
was only discoverable via activeFilters chips). State this in CHANGELOG and the guide's
shareable-URLs section; it is the intended trade.

## Deliverables

- JS: registry + hydration filtering + URL-preservation, vitest cases: `uh`-style param ignored
  and preserved; registered facet still round-trips; numeric operator param for an unregistered
  attribute ignored; custom routeToState bypass untouched. Update `routing.test.ts` expectations
  where the old adopt-everything behavior was asserted.
- C#: schema-restricted `SearchQueryState`, tests updated/added (unknown attribute dropped, known
  kept, schema-lookup failure falls back gracefully).
- CHANGELOG `[Unreleased]` (defect + behavior change); guide touch-up; KNOWN-LIMITATIONS only if
  a new honest ceiling appears. Update the two-routed-instances KNOWN-LIMITATIONS entry ONLY if
  your change affects it (it should not).
- Suites: JS + all C# green. Conventional commit(s) on `unit/pb-5`; commit this spec file.

## Constraints

- No new dependencies; no contract changes; don't touch XP-1a/XP-1b code (an XP-1b agent is
  working in a sibling worktree — your files must not overlap Admin UIPages/Client).
- Smallest correct change wins; the registry is a Set on the instance, not an abstraction.
