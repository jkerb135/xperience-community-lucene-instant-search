# Unit DX-2 — §5.8 server-rendered result templates + title-attribute overrides

Fixes HW-10 defect #4: selecting a "Result template" in the Results widget does nothing (the C#
emits `template` into `data-xps-config`, but `Client/src/widgets/results.ts` has no such param —
it is silently dropped — and no server-side rendering exists), and `Fields` configs that exclude
`title`/`url` produce blank cards with no way to point the default template at other attributes.

Read `docs/internal/agent-primer.md` first. Work only in this worktree (branch `unit/dx-2`).

## Part 1 — title/url/snippet attribute overrides (small, do first)

`results.ts` already supports `titleAttribute`, `urlAttribute`, `snippetAttributes` params and the
mount factory passes the whole config through (`widgets/index.ts` `fromMount`). Only the C# side
is missing:

1. Add to `ResultsWidgetProperties` (after `Fields`, orders +30/+40/+50):
   - `TitleAttribute` (TextInputComponent, "Title attribute") — attribute the default template
     reads the title from; empty keeps `title`.
   - `UrlAttribute` (TextInputComponent, "Link attribute") — empty keeps `url`.
   - `SnippetAttributes` (TextAreaComponent, "Snippet attributes", one per line) — tried in
     order; empty keeps `summary`, `content`, `excerpt`.
2. Emit them in `BuildConfig` only when non-empty (`titleAttribute`/`urlAttribute` strings,
   `snippetAttributes` string array via the existing `ParseLines`). They are display options →
   `data-xps-config`, NOT instance config.
3. MountMarkupTests: emitted when set, absent when empty.
4. Guide (`docs/guides/page-builder-widgets.md`): document the three options AND the blank-card
   foot-gun — if `Fields to show` is restricted, it must include whatever the title/link/snippet
   attributes name, or cards render empty.

No JS changes for Part 1.

## Part 2 — server-rendered templates (§5.8)

Design (decided — deviations go in your report's Assumptions):

**When.** Live and Preview modes only (Edit/ReadOnly keep the PB-4 editor previews). The Results
widget view component performs the initial search server-side and renders result markup INSIDE the
mount element, so the first paint needs no JS (progressive enhancement). JS hydration then owns
the DOM.

**Initial state from the URL.** Parse the request query string with the SAME mapping as
`Client/src/routing.ts` (`q`, `page`, `sort`, facet `<attr>` = comma-joined
encodeURIComponent-escaped values, `<attr>_op`, numeric `<attr>_<operator>` with operators
lt/lte/eq/ne/gte/gt). Implement it once in a small internal class with unit tests mirroring
`routing.test.ts` cases. Combine with the widget's own config (`ResultsPerPage`, `Fields`).

**How it queries.** Reuse the existing Core query path the public endpoint uses (find the service
the `/api/xpsearch/query` controller calls; inject it — do NOT go through HTTP). The full pipeline
must run (rules, personalization, analytics journaling happen there; do not bypass or duplicate).
If executing the query throws or the index is missing: log via the standard event log pattern and
render the empty mount — a broken search must never break the page. If the server-side render
fires, it IS the visit's initial query; check whether the query pipeline's journal/analytics would
double-count when JS hydrates and re-queries — if so, state what you found in your report; do NOT
build deduplication in this unit, just record it (KNOWN-LIMITATIONS if real).

**How it renders.**
- Wrap server output in `<div data-xps-server-rendered>` inside the mount.
- Per result: if the editor selected a template (`ResultTemplate`), resolve it via
  `ISearchResultTemplateRegistry.Find`; render its `ViewName` partial with the result as model
  when the template's `ContentTypes` is empty or contains the result's content type. Otherwise —
  and when no template is selected — render a built-in default partial shipped in the RCL that
  reproduces the default client markup (`themes/fixtures/results.html` block: `xps-result`,
  title/link/snippet/meta, honouring the Part 1 attribute overrides). An unresolvable identifier
  logs a warning and falls back to the default partial.
- Model for partials: define one small public view-model type (the result's attributes + url/title
  conveniences) — this is a public extension surface, keep it minimal and documented.

**Hydration handoff.** One deliberate JS change: on the results widget's first client render,
remove a `[data-xps-server-rendered]` child of its container before mounting
(`createRoot`/first-render path in `results.ts`). Vitest case: server block present → replaced on
first render, absent → unchanged behavior. Keep it surgical; no other JS changes.

**Client updates.** After hydration, rendering is the client's (default template + Part 1
overrides, or a JS `templates.item`). A Razor template does not apply to client re-renders —
document this plainly in the guide ("server template controls the first paint and no-JS
visitors; client rendering takes over afterwards").

## Deliverables

- Part 1 + Part 2 code, tests (MountMarkupTests additions; URL-state parser tests; a rendering
  test for the server block with a fake registry/template; the vitest handoff case).
- Guide: new "Server-rendered result templates" section in `docs/guides/page-builder-widgets.md`
  with a complete, compiling `RegisterSearchResultTemplate` sample + partial view sample.
- CHANGELOG `[Unreleased]` entry. KNOWN-LIMITATIONS entries for honest ceilings (e.g. server/JS
  double-query on first load if confirmed; Razor templates not applying to client re-renders is
  guide material, not a limitation entry).
- All four C# suites + JS suite green (you touch Widgets C# and Client). Conventional commit(s)
  on `unit/dx-2`; commit this spec file with the unit.

## Constraints

- No new dependencies. No contract (`SearchResponse`) changes. Do not touch Core's pipeline
  behavior — you only call it. Kentico docs MCP for any Xperience API questions (partial
  rendering inside a widget view component, event log pattern).
- If the Core query entry point turns out to be host-only (not resolvable from the Widgets
  package), STOP Part 2 after the URL parser, report the blocker with what you found, and finish
  Part 1 + docs — do not invent a second query path.
