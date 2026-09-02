# Unit CD-1 — the 'command not found' admin defect (PageCommand discovery)

PAUL plan 03-01. The library's admin pages intermittently answer "command not found" for
commands that are wired correctly in source. This unit is research-first: find the REAL
discovery rule in Kentico 31.8, reproduce the failure deterministically in a test, then fix to
a convention the whole assembly is guarded against. Library unit (Admin only), worktree branch
`unit/cd-1` (already created for you). Read `docs/internal/agent-primer.md` first.

## The evidence (host observations, 2026-08-31, FRESH build + fresh start — not staleness)

- Failures seen live: **Field weights → Delete**, **Rule → SearchItems** (×3), **Rules →
  Delete** (~23:00, freshly built and started instance).
- Common thread: every failing command is a `[PageCommand]` that is NOT a plain method
  declared on the final page class. Two shapes:
  1. **Re-annotated overrides**: `Rules.cs:219` — `[PageCommand] public override … Delete(int
     id)` where `ListingPage.Delete` on the Kentico base ALREADY carries `[PageCommand]`
     (possible duplicate-attribute collision in discovery). Same shape: `Rules.cs:289`,
     `Stopwords.cs:215,256`, `Synonyms.cs:300,404`, `FieldWeights.cs:238,330`,
     `ApiKeys.cs:42` — inventory precisely, there may be more.
  2. **Inherited from an abstract base**: `RuleBuilderPage` (abstract, `RuleBuilderPage.cs:29`)
     declares `[PageCommand]` methods (`Load`, `SearchItems` at 183ish, etc.) inherited by the
     final `RuleEdit` (`Rules.cs:320`) and — check — the XP-1b variant pages (XP-1b's
     convention was "all NEW [PageCommand]s are plain methods on final classes,
     reflection-asserted" in `VariantPagesTests`; the RuleBuilderPage base predates it).
- Page/template/client-module wiring was verified correct in source at the time; Kentico's
  docs claim inherited commands are supported. The failures are INTERMITTENT across fresh
  builds — a correct root cause must also explain the intermittency (ordering? caching keyed
  somewhere unstable? scan order?).

## 1. Research: the actual discovery rule

- Decompile the command discovery path in `Kentico.Xperience.Admin.Base` **31.8.0** — the
  DLL is already on disk under `tests/XpSearch.Admin.Tests/bin/Debug/net8.0/` (XP-2 set the
  precedent: decompile-verified findings, cited by type/method). Find: how a client command
  name resolves to a method (reflection flags — `DeclaredOnly`?), how attributes on
  overridden/inherited methods are treated (`GetCustomAttribute` inherit flag; duplicate
  name handling — dictionary add vs indexer?), and where results are cached (what key, what
  population order). The intermittency lives somewhere in there; name it.
- Write the finding up in the unit report AND as a short `docs/adr/` entry if it changes our
  conventions (it will) — future pages need the rule stated once, with the decompiled
  evidence cited.

## 2. Reproduce deterministically

- Preferred: instantiate/invoke Kentico's OWN discovery component in a test (the assembly is
  referenced by Admin.Tests) against the affected page types, asserting every command name
  the React client actually sends (grep the admin client source for command invocations —
  `usePageCommand`/command name strings) resolves. If the real component cannot be stood up
  in-test, replicate the decompiled algorithm faithfully in the test and say so.
- The test must be RED on current main for at least one observed failure (SearchItems on
  RuleEdit, or a listing Delete) — if you cannot make it red, your root cause is wrong;
  STOP and report rather than shipping a green test that proves nothing. (If the root cause
  turns out genuinely nondeterministic in-process — e.g. scan-order dependent — the test may
  instead pin the FIXED convention and the report must demonstrate the failure by the
  decompiled logic on the OLD shapes; explain honestly which you achieved.)

## 3. Fix

- Shape the fix to the discovery rule you found — do not guess. The likely landing zone,
  consistent with XP-1b's existing convention: every command our client invokes is a plain,
  uniquely-named `[PageCommand]` method declared on the final page class (listing Deletes
  stop re-annotating the base override — either drop the attribute if the base's registration
  is what discovery uses, or shadow with a distinct final-class method; the decompile
  decides). Behavior of every command is UNCHANGED — routing/permissions/payloads identical;
  `ListingDeleteCommandTests` and friends keep passing (update call sites only if signatures
  legitimately move).
- Assembly-wide guard: extend the `VariantPagesTests`-style reflection assertion to the WHOLE
  Admin assembly — no `[PageCommand]` shape that the discovered rule can miss (whatever that
  turns out to be: none on abstract classes, none on overrides, unique names — encode the
  actual rule). This is the test that keeps the defect from coming back with the next page.

## 4. Verification

- Admin suite green including the new red-turned-green discovery test + the assembly guard;
  all other suites untouched but run (Core/Ingestion/Widgets/Client).
- Admin client `npm run build` green if any command names moved (they should NOT — moving
  names would break the React side; if the fix forces a client change, STOP and report
  first).
- Host click-through cannot be done headlessly by you: append a checklist section (numbered
  after the current last item — §V ends at 110 on main; verify in YOUR worktree) listing
  every previously-failing command for the owner to click on the rebuilt host (Field weights
  Delete, Rules Delete, rule SearchItems, plus one representative from each never-clicked
  sibling: Synonyms/Stopwords/ApiKeys Delete), and note the sln-rebuild prerequisite.
- CHANGELOG (Fixed, admin). ADR per §1. Commit this spec with the unit (copy from
  `docs/internal/units/CD-1.md` on main if your worktree predates it).

## Constraints

- Kentico docs MCP for any Xperience question; decompilation is authorized for
  `Kentico.Xperience.Admin.Base` discovery internals (read-only, findings cited by
  type/method — XP-2 precedent). Admin project + tests only; no contract/JS/Core changes; no
  new dependencies. Never touch `src/Components/Widgets/CardWidget/`. Host is out of scope
  (checklist items are the handoff).
