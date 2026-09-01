# Unit MB-1 — Mobile swap: sidebar collapses, load-more replaces pagination (<1024px)

PAUL plan 1.1-01. Acceptance = host-pass checklist §Q items 80–81 flipping from KNOWN FAIL to
pass. Owner evidence 2026-09-01: on a phone-width viewport the full desktop facet sidebar
renders above the Filter & Sort button — the mockup's `mobile-note` says the sidebar BECOMES
the button below 1024px.

Two-surface unit: library worktree (branch `unit/mb-1`) for guide/recipe changes and any
minimal library change Part B needs, PLUS host edits at `F:\Personal\CommunityProjects\src`
(NOT a git repo — no commits there; report file-by-file). Read
`docs/internal/agent-primer.md` first.

## Part A — the sidebar half of the swap (host)

The guide's recipe (`widget-reference.md` § Composing the results page) already documents the
page's half: the sidebar container is desktop-only below 1024px. The host never applied it.

1. Find the search page's sidebar container in the host markup (the Page Builder
   `Section_25_75` side-panel zone on the search layouts — inspect the rendered wrapper the
   DancingGoat section emits; if it has no stable class, add one in the section's view or the
   search layout, scoped so ONLY the search page is affected — do not restyle every 25/75
   section on the site).
2. Host CSS (in `src/Search/client/main.scss`, after the package `@use`s): below 1024px hide
   the sidebar container; the Filter & Sort trigger + chips row already handle visibility
   themselves. Above 1024px nothing changes.
3. Sanity: the sheet must still cover the configured facets while the sidebar is hidden — it
   does (its own `Facets` property), but confirm the demo sheet lists Category + Products and
   note that Taste/Price are sidebar-only today (add them to the sheet's `Facets`/config if
   the sheet supports the widget kinds; if the sheet only does facet groups — it does, by
   design — say so in the README rather than pretending).

## Part B — the pagination ↔ load-more half (mount-time decision)

NOT a CSS swap: `loadMore` REPLACES `results` + `pagination` (it renders the result list and
owns `state.page`; the guide forbids both in one instance). Mechanism (decided — smallest
honest version, zero new library API):

1. The host bundle decides AT MOUNT TIME with `matchMedia('(max-width: 1023.98px)')`:
   - below 1024: register `results` as a `loadMore` factory (carrying the host's product-card
     `templates.item` — loadMore renders the same items, verify the templates param shape) and
     register `pagination` as a no-op widget (mount renders nothing; return an inert Widget).
   - at/above 1024: current behavior (results override + numbered pagination) unchanged.
2. Viewport CHANGES after boot do not live-swap — document plainly in the guide and README
   ("the choice is made per page load; rotating a tablet re-applies on the next navigation").
   No resize listeners, no remounting.
3. Guide (`widget-reference.md` mobile recipe section): replace the current one-line "that is
   page composition" note with this concrete `matchMedia` + `mountAll(root, { widgets })`
   recipe as the documented, supported pattern (plain-HTML-first per
   [[feedback-default-is-the-design]]; PB note: the Pagination widget's mount is simply
   resolved by whatever factory the bundle registered). This is the library-side deliverable —
   commit on `unit/mb-1`.
4. If the loadMore factory cannot honestly stand in for the results widget here (e.g. server-
   rendered first paint handoff: check how loadMore treats a `[data-xps-server-rendered]`
   child and the `initialQueryId` flow — DX-2/PB-6 behaviors), STOP on that sub-point, report
   precisely, and ship Part A + the guide recipe marked with the caveat.

## Verification

- Library: guide sample runs against the mock server (the recipe's matchMedia branch forced
  both ways in a jsdom check); themes/client/C# suites all green (expected unchanged counts —
  you add no library runtime code unless a STOP fires); commit spec + guide on `unit/mb-1`.
- Host: `npm run build` in `src/`; no dotnet rebuild needed unless you touched .cshtml (the
  section class in A.1 likely means one — then stop host, `dotnet build CommProjects.sln` from
  the umbrella root, restart, leave RUNNING).
- Evidence in the report: rendered-HTML/jsdom proof that <1024px produces load-more + no
  sidebar + no numbered pagination, and ≥1024px is unchanged; checklist §Q items 80–81
  updated from KNOWN FAIL to walkable (edit the two items' KNOWN-FAIL lines — permitted
  library docs write, same file exception as HW units).

## Constraints

- No new dependencies anywhere. No contract changes. No PB widget changes. Never touch
  `src\Components\Widgets\CardWidget\`. Host `src/Search/README.md` updated (the swap, the
  per-page-load caveat, sheet-facets note).
