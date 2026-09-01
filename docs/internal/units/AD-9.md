# Unit AD-9 — Analytics "Show the numbers" paging + alphabetical field dropdown (HW-11 #13, #17)

Owner-reported on the host, 2026-09-01, during the HW-11 walk. Two small admin polish fixes.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/ad-9`).

## 1. "Show the numbers" table has no paging (#13)

The Analytics chart's "Show the numbers" collapse renders the full date series as one long
table (90-day range = 90 rows). Give it the SAME pager the report tables got in AD-6
(`TablePager` built from labelled stock Buttons — find it in
`src/XpSearch.Admin/Client/src/` and reuse it verbatim; do NOT use the package's own
Pagination, its prev/next chevrons are unlabelled — recorded defect). Default rows-per-page:
whatever the report tables default to. If the series fits one page, no pager renders — match
the report tables' behaviour exactly.

## 2. Field dropdown alphabetical order (#17)

The Field weight New/Edit "Field" dropdown lists searchable fields in discovery order. Sort the
options alphabetically (case-insensitive, invariant culture) **at the options-provider level**,
so every consumer of that provider benefits. Audit the other dropdowns fed by index-schema
providers (facet attribute configurator, rule builder attribute picker if it shares the
provider) — sort those providers too IF they are not already sorted; leave alone anything with
a deliberate non-alphabetical order (state what you found either way). The orphaned-stored-name
behaviour (still shown as selected) must survive.

## Deliverables

- Client change for #1 (typecheck + webpack green), C# provider change for #2 with a unit test
  asserting the ordering (and orphan preservation if not already covered).
- CHANGELOG `[Unreleased]` entries. All four C# suites green.
- Conventional commit(s) on `unit/ad-9`; commit this spec file.

## Constraints

- Files: `src/XpSearch.Admin/**` (pages + client) EXCEPT `Client/src/rule-builder/**` — a
  sibling agent owns that directory this round. Widgets/Core untouched.
