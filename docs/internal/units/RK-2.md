# Unit RK-2 — NULL-column fix in popularity task + unset-required-field audit (live defect)

Owner-reported on the running host, 2026-09-01: scheduled task `XpSearch.PopularitySignal`
fails — `Cannot insert the value NULL into column 'PopularityIndexEnabled', table
XpSearch_PopularityIndex; column does not allow nulls.`

Root cause (diagnosed): `src/XpSearch.Core/Popularity/InfoPopularitySignalStore.cs` line ~92
creates the first settings row as
`new XpSearchPopularityIndexInfo { PopularityIndexGuid = …, PopularityIndexName = … }` without
setting `PopularityIndexEnabled`. Kentico Info objects serialize only fields that were SET — a
C# property getter's default never reaches the INSERT — and the installer's form
(`XpSearchAnalyticsModuleInstaller.PopularityIndexForm`) defines the boolean without
`allowEmpty`, so the DB column is NOT NULL. The FieldWeights toggle path is safe only because it
always assigns the property before `Set`.

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/rk-2`).

## 1. The fix

Set `PopularityIndexEnabled = false` in that initializer. That's the fix — resist anything
bigger on this line.

## 2. The audit (same defect class, everywhere)

For EVERY module class this library installs (analytics installer: query log + 3 popularity +
synonym suggestion; tuning installer: experiment + the four tuning types — find them all from
the two installers), cross-check each form field that is NOT `allowEmpty` against every code
path that constructs a new Info of that type (`new XpSearch…Info` sites, object initializers).
Any required field a creation site can leave unset is the same latent crash — fix by setting it
explicitly at the creation site. Report a table of what you audited and what you found, even
where the answer is "already safe".

## 3. Regression guard

One focused test per finding is impractical; instead add ONE test that encodes the rule where
it can be encoded cheaply: for the Info types whose creation sites you fixed or that a store
builds (popularity settings row at minimum), construct the object exactly the way the
production code does (extract a tiny internal factory method if needed — smallest change that
makes the construction testable) and assert `GetValue(field)` is non-null for every
non-allowEmpty field of the corresponding installer form. If a type's construction cannot be
reached without the container, say so in the report and leave it to the host pass.

## 4. Docs

CHANGELOG `[Unreleased]` fix entry. Append the re-run check to the HW-11 checklist (§ next
free item numbers): task `XpSearch.PopularitySignal` runs green in the Scheduled tasks app,
XpSearch_PopularityIndex row exists with Enabled = 0, and re-running stays green (idempotent).
KNOWN-LIMITATIONS/primer only if the audit teaches a reusable lesson — a primer line under
"Patterns to copy" about setting every required field when constructing Info objects is
probably warranted.

## Deliverables

All C# suites green. Conventional commit(s) on `unit/rk-2`; commit this spec file.

## Constraints

No new dependencies; no schema changes (fix the creation sites, not the columns — installed
DBs already carry the NOT NULL columns); no behaviour changes beyond making the writes legal.
