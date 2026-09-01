# Amendment — Platform personalization condition types (PS-1)

Lead proposal, 2026-08-31, per the owner's zero-external-cost constraint. Queued after PB-3,
DX-2, XP-1 and RK-1/SY-1 (the random-bucket condition reuses XP-1's cookie infrastructure).
Requires the host to hold the XbyK **Advanced** license tier (widget personalization gate) —
a license fact, not a usage charge.

## What ships

One small unit adding two Page Builder **personalization condition types** (standard
`ConditionType` subclasses, registered from `XpSearch.Core` so live-site hosts get them without
the Admin package):

1. **"Searched for" condition** — *contact searched for {term} in the last {N} days*.
   Evaluates against the AN-2 search activities for the current contact
   *(correction 2026-09-01, PS-1: this originally named `xpsearch_search`, which does not
   exist — the real AN-2 types are `xpsearch_query` and `xpsearch_noresults`, and the shipped
   condition reads both)*
   (`ActivityValue` = query since AN-2). Term match: contains, case-insensitive; same consent
   gate as activity logging (no consent → condition false → original variant, which is also the
   crawler-safe behaviour the docs recommend).
2. **"Random bucket" condition** — *visitor is in bucket {A|B} of {split}%*.
   Sticky assignment via the XP-1 functional cookie hash (anonymous visitors included). This
   turns ordinary widget personalization variants into page-level A/B variants for ANY widget
   on the site, measured through existing analytics/custom activities. It is deliberately dumb:
   no experiment entity, no report — marketers read outcomes in Analytics or their own tooling.

Both get configuration dialogs via stock editing components, guide pages per the wiki-ready
docs rule, and Core tests for the evaluation logic.

## Explicitly out of scope

- Full-page/template experiments and automated winner promotion — VWO via the Tag Manager
  integration remains the recommendation there.
- A reporting UI for random-bucket page tests (XP-1's report is search-tuning-specific and
  stays that way).
- Condition types based on CDP segments (feature is preview-mode, opt-in, one-way).

## Why this shape

PZ-1/AN-3 already personalize the *results* side (group-scoped rules, search-driven contact
groups). This closes the loop on the *page* side using the platform's own personalization
surface: marketers personalize any widget by search behaviour, and get sticky A/B splits, with
zero external services and almost zero new code — the activities, cookie and consent gates all
exist.
