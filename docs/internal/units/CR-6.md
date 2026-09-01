# Unit CR-6 — Rule builder: attribute+value picker "Add row" does nothing (HW-11 #5)

Owner-reported on the host, 2026-09-01, during the HW-11 walk: in the rule builder's
attribute+value rows editor (used by the Filter results action, Boost matching action, and the
condition side panel's Filters), the **Add row button does nothing** — no new row appears.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/cr-6`).

## Scope

1. **Reproduce in source first.** The rows editor lives in the Admin React client
   (`src/XpSearch.Admin/Client/src/rule-builder/` — find the component; CR-4b built it per the
   approved canvas, board 5a/5c). Establish WHY the click is inert: dead handler, state not
   propagating, disabled gate, event swallowed by the side panel, a regression from the
   DateTimeRangeInput/ConditionPanel edits the owner made on these files. State the root cause
   in your report — no fix without a named cause.
2. Fix minimally. Both call sites (action editors AND condition Filters panel) must add rows.
3. **Test:** the client has vitest? Check `src/XpSearch.Admin/Client/package.json` — if the
   admin client has no test runner (likely; the C# suite covers pages), add the smallest
   regression check the existing tooling supports: at minimum `npm run typecheck` + webpack
   build green, and if a JS test rig exists, one case for "add row appends an editable row".
   Do NOT introduce a new test framework.
4. CHANGELOG `[Unreleased]` fix entry. No guide changes unless behaviour changed visibly.

## Constraints

- Files: `src/XpSearch.Admin/Client/**` only (plus CHANGELOG + this spec). Two sibling agents
  are working on Admin PAGES (C#) and the Widgets package — do not touch those areas.
- The owner may have uncommitted local edits in the main checkout — irrelevant to you; your
  worktree is clean main.
- `npm ci && npm run build` + `npm run typecheck` green in `src/XpSearch.Admin/Client`; all
  four C# suites still green (Admin csproj embeds the client build). Conventional commit on
  `unit/cr-6`; commit this spec file.
