# Unit PK-2 — SSR primitives lift Widgets→Core + stable mount contract

Companion to PK-1 (npm-first distribution). Today server-rendered first paint (§5.8, built in
DX-2) is locked inside XpSearch.Widgets: `ServerRenderedResults`, `SearchQueryState`, and the
result-template surface (`ISearchResultTemplateRegistry`, `RegisterSearchResultTemplateAttribute`,
`SearchResultTemplate`, `SearchResultViewModel`) all live there. A host that builds its search
UI in plain JavaScript against the npm package + XpSearch.Core gets no SSR. This unit lifts the
primitives into Core so SSR is a Core capability, and documents the mount contract so a host's
own bundle can hydrate Page Builder widget mounts without the tag helper.

Read `docs/internal/agent-primer.md` first. Work only in this worktree (branch `unit/pk-2`).
Kentico docs MCP for any Xperience API questions.

## Part 1 — the lift

Move to Core (namespace `XpSearch.Core.Rendering`; keep type and member names, adjust usings):

- `SearchQueryState` (Rendering/SearchQueryState.cs)
- `ServerRenderedResults`, `ServerResultsOptions`, `ServerResultsRender`
- The Templates quartet: `ISearchResultTemplateRegistry`, `RegisterSearchResultTemplateAttribute`,
  `SearchResultTemplate`, `SearchResultViewModel` (namespace `XpSearch.Core.Rendering` too —
  one public rendering namespace, not two).

Rules:

1. Core compiles against MVC via the existing `Kentico.Xperience.WebApp` reference — add NO new
   package references. If a needed MVC type turns out not to resolve in Core, STOP and report;
   do not add a reference on your own authority.
2. The registry implementation and its DI registration move to Core's service-collection
   extension (find the existing `AddXpSearch*` seam in Core and register there). Widgets' DI
   extension keeps working but no longer registers rendering services itself — it must remain
   safe to call both (idempotent registration, `TryAdd*`).
3. The default result partial (`XpSearchWidgetConstants.DefaultResultViewPath`, `_Result.cshtml`
   in the Widgets RCL) STAYS in Widgets — Core must not become an RCL for one view. Break the
   dependency by making the default view path a parameter: add `DefaultViewPath` (or equivalent,
   follow the existing options shape) to `ServerResultsOptions`; Widgets passes its RCL path.
   When the caller supplies none or the view is unresolvable, Core falls back to a built-in
   C#-emitted default card whose markup matches the client default card and `themes/MARKUP.md`
   (the class names in the existing `ListOpen`/`Empty` constants show the idiom). That fallback
   is what plain-JS hosts without a partial get — test its markup against the client fixture.
4. Source-breaking namespace moves are fine pre-publish: CHANGELOG them explicitly (old → new
   namespace per type). No type-forwarders. Update all Widgets/host-facing usings; the Widgets
   view component and `ResultsWidget` become thin consumers of the Core types.
5. Move the corresponding tests from the Widgets suite to the Core suite (SearchQueryState
   parser tests, ServerRenderedResults rendering tests). Tests that exercise the Widgets view
   component stay put and keep passing.

## Part 2 — plain-JS SSR path (small; the lift makes it possible, this makes it usable)

A Razor page in a host WITHOUT XpSearch.Widgets must be able to do first-paint SSR:

1. Verify `ServerRenderedResults` is resolvable and functional from a host that only references
   Core (the existing DX-2 error handling — broken search renders empty, never throws through —
   must survive the move; add/keep a test).
2. Hydration handoff: PB-6 gave the widget path a server `QueryId` pass-through into instance
   config so the first client query journals as the same search. Find the client-side option it
   flows through and confirm an ESM consumer of `createSearch` can supply it directly (it
   should already exist post-PB-6). If it is reachable, document it in Part 3; if it is buried
   in mount-only plumbing, expose it as a public `InitOptions` member (additive, JS suite
   case) — smallest possible change, no other JS work in this unit.

## Part 3 — docs: the mount contract + server rendering guide

New guide `docs/guides/server-rendering.md` (do NOT restructure existing guides — PK-1 owns
that; only add cross-links):

- Widgets path: what the Results widget does automatically (recap, link).
- Plain-JS path: inject `ServerRenderedResults`, call `RenderAsync` inside your own mount
  element, pass the returned `QueryId` to `createSearch`, client replaces
  `[data-xps-server-rendered]` on first render. Complete compiling Razor + JS sample, verified
  per [[feedback-docs-wiki-ready]].
- The mount contract, now stated as STABLE: the mount element shape (`data-xps-widget`,
  `data-xps-config`, instance grouping attributes — document exactly what
  `Client/src/bootstrap.ts` `readMountConfig` reads, generated-from-source accuracy) and the
  guarantee that Page Builder widget mounts hydrate from EITHER the tag-helper bundle or a
  host bundle importing the npm package. From this unit on, a change to mount markup or config
  keys is a breaking change and must be CHANGELOG'd as such — say so in the guide and add the
  sentence to the contract section of the internal docs if one exists.

## Deliverables

- Parts 1–3; all four C# suites + JS suite green. MountMarkupTests unchanged (markup is frozen).
- CHANGELOG `[Unreleased]`: namespace moves (breaking, itemized), Core SSR for plain-JS hosts,
  mount contract declared stable. KNOWN-LIMITATIONS only for honest ceilings found en route.
- Conventional commits on `unit/pk-2`; commit this spec file with the unit.

## Constraints

- No new package references anywhere. No mount markup / `data-xps-config` / wire contract
  changes. JS changes limited to Part 2's possible additive `InitOptions` member.
- Expect a trivial CHANGELOG merge conflict with PK-1 (runs in parallel) — keep your entry
  self-contained; the lead resolves the merge.
