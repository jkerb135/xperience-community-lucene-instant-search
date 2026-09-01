# Unit XP-2 — experiment section shows the GUID instead of the name (live defect)

Lead-observed on the running host, 2026-09-01: every variant-scoped experiment page (e.g.
`/admin/lucene/indexes/edit/2/experiments/2/rules`) shows the experiment's GUID
(`3be7f5a5-…`) in the admin breadcrumb and the sidebar section header, where the display name
("Docs demo experiment") belongs.

Root cause (diagnosed): `src/XpSearch.Admin/Persistence/XpSearchExperimentInfo.cs` passes
`null` as the `displayNameColumn` argument (8th positional) of the `ObjectTypeInfo`
constructor. `ExperimentSection : EditSectionPage<XpSearchExperimentInfo>` names the
breadcrumb/section from the routed object's generalized `ObjectDisplayName`, which with no
display-name column falls back to (code name, then) the GUID. The table has the column —
`ExperimentDisplayName` is already a `[DatabaseField]` and the listing sorts/searches on it.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/xp-2`).

## 1. The fix

In `XpSearchExperimentInfo.TYPEINFO`, pass `nameof(ExperimentDisplayName)` as the
`displayNameColumn` constructor argument (currently the second `null` after
`nameof(ExperimentGuid)`). That's the fix — resist anything bigger. No schema or installer
change: `DisplayNameColumn` is code-side type metadata over an existing column.

Verify the mechanism before trusting it: inspect `EditSectionPage<T>` in the referenced
`Kentico.Xperience.Admin` assembly (decompile/metadata view from the worktree's NuGet cache)
and confirm the section/breadcrumb name comes from the object's display name resolution. If —
and only if — it does NOT, fall back to overriding the appropriate member on
`ExperimentSection` (`src/XpSearch.Admin/UIPages/Experiments/ExperimentPages.cs`) to supply
`ExperimentDisplayName`, and say so in your report.

Do NOT "fix" the other Info classes the same way: none of the other types is routed through an
`EditSectionPage`, and the log-style types have no display-name column at all. Leave them.

## 2. Regression guard

One test in `tests/XpSearch.Admin.Tests` asserting
`XpSearchExperimentInfo.TYPEINFO.DisplayNameColumn == nameof(XpSearchExperimentInfo.ExperimentDisplayName)`
(plain static access, no container needed — follow whatever test-file conventions the suite
already uses; put it beside existing persistence/experiment tests if a fitting file exists,
otherwise one small new file).

## 3. Docs

- CHANGELOG `[Unreleased]`: one `**Fixed (admin):**` entry — experiment breadcrumb/sidebar
  showed the GUID instead of the experiment name.
- Append to `docs/internal/host-pass-hw11-checklist-2026-08-26.md` a new section
  `## O. XP-2 experiment section name (added 2026-09-01)` with item **75**: open an index's
  **Experiments** listing, click the experiment row, and check that the breadcrumb and the
  sidebar section header on the detail page AND on a variant-scoped tuning page (e.g. the
  experiment's Rules) show the experiment's display name, not a GUID. The seeded
  "Docs demo experiment" on DancingGoatSample (id 2) is the ready-made subject.
- No KNOWN-LIMITATIONS entry (nothing is being simplified).

## Deliverables

`tests/XpSearch.Admin.Tests` green (run the full Admin suite). Conventional commit(s) on
`unit/xp-2`; commit this spec file with the unit.

## Constraints

No new dependencies; no schema changes; no behaviour changes beyond the name resolution.
