# Unit PS-1 — Platform personalization condition types

Owner-approved amendment `docs/spec/amendments/2026-08-31-platform-personalization.md` — read it
first; it is the authority. Two Page Builder **personalization condition types** so marketers can
personalize ANY widget by search behaviour and run sticky A/B splits via widget variants.
Requires the host to hold the XbyK Advanced license (a license fact — say so in the guide; the
code neither checks nor cares).

Read `docs/internal/agent-primer.md`. Work only in this worktree (branch `unit/ps-1`).

## 0. Placement (decided deviation from the amendment)

The amendment says "registered from XpSearch.Core"; the `ConditionType` base class lives in
`Kentico.PageBuilder.Web.Mvc.Personalization`, and Core must not grow a Page Builder dependency.
Put the condition types in the package that already references Page Builder (`XpSearch.Widgets`
— verify). Keep each ConditionType a THIN shell over a pure, testable evaluator class (the
project's standing pattern); record the placement in the report.
Consult the Kentico docs MCP for the current condition-type registration/dev pattern
(https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/content-personalization/develop-personalization-condition-types)
— do not guess the attribute or property-annotation shapes.

## 1. "Searched for" condition

*Current visitor searched for {term} within the last {N} days.*

- Properties: term (text, required), days (number, default 30, min 1). Term match: the visitor's
  logged search query CONTAINS the term, case-insensitive.
- Evidence source: the `xpsearch_search` activities of the current contact (AN-2:
  `ActivityValue` = the query). Resolve the contact the way PZ-1's `ContactGroupResolver` does —
  same consent gate, `GetExistingContact`, memoized per request on `HttpContext.Items`.
- No contact / no consent / no activities → **false** → the original widget variant renders.
  This is also the crawler-safe behaviour the Kentico docs recommend — say so in the guide.
- Evaluation runs per widget per page render: memoize the contact's recent queries once per
  request (one activity read), not per condition instance.

## 2. "Search A/B bucket" condition

*Visitor is in bucket {A|B} of a {split}% split named {group}.*

- Properties: bucket (A or B — dropdown/radio), split percent (1–99, the % in B), group name
  (text, default `"default"`; conditions sharing a group name bucket together, so a marketer
  can pair variants across widgets — explain this in the dialog's explanation text and guide).
- Bucketing: REUSE XP-1a's `xpsearch_bucket` cookie and `ExperimentBucketing` hashing — the
  visitor's bucket id hashed with the group name (add a string-seed overload beside the Guid
  one; same SHA-256 % 100). Same visitor + same group ⇒ same bucket, forever, on any server.
- Cookie assignment: the visitor may not have the cookie (XP-1a only assigns it while an
  experiment runs). Extract XP-1a's read-or-assign logic (`ExperimentAssignmentResolver.BucketId`
  + `CanAssignCookie` + below-Essential gate) into one small shared helper both call — behaviour
  identical, XP-1a's tests must stay green untouched (ordering/consent semantics must not
  change). No cookie obtainable (response started, below Essential) → condition evaluates
  **false** regardless of configured bucket → original variant. Record in KNOWN-LIMITATIONS:
  a visitor's very first paint can render the original when the cookie could not be written
  mid-response; buckets apply from the next request.
- No experiment entity, no report — the amendment explicitly scopes this to "deliberately dumb".

## 3. Docs

- New wiki-ready guide page (search-driven personalization: prerequisites — Advanced license,
  contact tracking, condition setup per Kentico docs; both conditions with screenshots-in-words;
  the A/B pairing recipe: same group name on two widgets = one page-level A/B test measured via
  existing analytics; crawler note; what "false" renders). Cross-link from the experiments guide
  ("page-side A/B" vs "search-tuning A/B" — one paragraph on which to reach for).
- CHANGELOG `[Unreleased]`; KNOWN-LIMITATIONS (first-paint miss, above; anything else honest).
  ADR only if the shared-bucket-helper extraction warrants one line somewhere — prefer noting it
  in ADR-0024's file as an addendum over a new ADR.
- Append host-pass items (§ next letter) to the HW-11 checklist: create a widget variant with
  each condition on the sample site, verify searched-for triggers after a consented search,
  verify sticky bucketing across reloads and that two widgets sharing a group name flip
  together.

## Deliverables

- Code + tests: evaluator logic for both conditions (term matching, day window, consent-absent
  false; bucket determinism, group independence, split edges 1/99, no-cookie false), the shared
  bucket helper with XP-1a's tests untouched and green, registration attributes present
  (reflection test like `AssemblyDiscoveryTests` precedent).
- All C# suites green; JS untouched. Conventional commits on `unit/ps-1`; commit this spec file.

## Constraints

- No new dependencies; no contract changes; no new cookies (reuse `xpsearch_bucket`); do not
  change XP-1a's bucketing semantics or the four tuning types. Kentico docs MCP mandatory for
  the condition-type API surface.
- Out of scope (amendment): reporting UI for page A/B, CDP segment conditions, full-page
  template experiments.
