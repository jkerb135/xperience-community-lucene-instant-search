# ADR-0022: The if/then rule engine

- **Status:** accepted — owner approval 2026-08-24 (design canvas signed off)
- **Date:** 2026-08-24
- **Spec reference:** §8.2, §8.3 — amends the rule model of [ADR-0014](0014-relevance-tuning.md)
- **Design canvas:** <https://claude.ai/code/artifact/e2b15580-1239-417c-afdb-118a100133df>
- **Implemented by:** unit CR-4a (Core). The storage, migration and admin UI are unit CR-4b.

## Context

A relevance rule shipped as one condition (query pattern plus operator, or "always") and one
consequence (pin, bury, boost, filter or redirect), because that is the shape the flat `XpSearchRule`
columns of ADR-0014 could hold. Two years of Algolia habits do not fit in it:

- "Boost the espresso machine **and** attach a campaign banner" is two rules that have to be kept in
  step by hand, with two priorities to reason about.
- "Only when the visitor has *category: coffee* selected" and "only for German" cannot be expressed
  at all, so the same query cannot be tuned per refinement or per language.
- A query rewrite (drop *cheap*, search *sofa* for *couch*) had no home: synonyms are symmetric and
  index-wide, while a rewrite is a targeted, conditional edit of one query.
- A marketer's payload for the page — a banner, a layout switch — had nowhere to travel. Algolia
  answers this with `userData`; we had nothing.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep one condition and one consequence, add more consequence kinds | No model change; storage untouched | The two-rule-per-intent problem stays; conditions still cannot combine |
| N conditions, N consequences on one rule (chosen) | One rule is one editorial intent; conditions compose; consequences are ordered and inspectable | Storage has to become JSON, and every custom `IRelevanceTuningSource` breaks |
| A small expression language (`query contains "x" and lang = "de"`) | Maximum power | A marketer's tool with a parser, an error surface and no form; the UI could not be a form at all |
| Consequences as an open plug-in interface | Extensible by developers | Nothing to render a form from, and no way to reason about precedence between third-party effects |

Query rewrites specifically: a separate "rewrite" entity was considered and rejected — the conditions
that decide whether to rewrite are the same conditions a rule already has, so it would have been a
second rule table with a different name.

## Decision

**Model.** `TuningRule(Id, Name, Enabled, Priority, ValidFrom, ValidTo, Conditions, Consequences)`
with `RuleConditions(Query, Filters, ContactGroup, Language)` and a closed set of nested
`RuleConsequence` records: `Pin`, `Hide`, `Boost`, `Bury`, `FilterResults`, `RemoveWord`,
`ReplaceWord`, `ReplaceQuery`, `Redirect`, `CustomData`.

**Semantics.**

| Rule | Behaviour |
|---|---|
| Conditions | **All** must hold. `Filters` holds when every `attribute is value` pair is selected in the request's `filters.facets`; empty `ContactGroup` / `Language` mean "any". |
| No conditions at all | The rule **never fires**. A source must not emit one; the guard is defensive, not a feature. |
| Query operators | `Is`, `Contains`, `StartsWith`, compared case-insensitively on the trimmed pattern. An empty pattern is a wildcard for `Contains` and `StartsWith` (this is how "any query" is expressed) and matches only an empty query for `Is`. |
| `MatchAnalyzed` | Compares the analyzed pattern against the analyzed query, position by position, with the index's synonyms folded into each position. Falls back to the raw comparison when nothing analyzed the query. |
| Typo tolerance | **None**, either way. Out of scope for Lucene 4.8 term matching, and a marketer who wants a misspelling can write a synonym. |
| Consequences | Applied in the order the rule lists them. Several rules may fire; the precedence order (priority, then id) of ADR-0014 is unchanged. |
| Pin / Bury | Unchanged, including "the first consequence to name a document wins". |
| Hide | A `MUST_NOT` on the document id, so it is on no page and the total excludes it — and a pin cannot bring it back, because the clause is in `ActiveFilters` too. |
| Rewrites | Applied to the query text before synonym expansion, in rule order then listed order. |
| `CustomData` | Shallow-merged onto `SearchResponse.ruleData` in application order; a later rule wins a key. A payload that is not a JSON object is skipped with a Debug log. |

**Pipeline.** A new `QueryRewriteStage` at `SearchStageOrder.QueryRewrite = 175` — after
`ResolveContactGroups` (150), before `SynonymExpansion` (200). It is the one place a rule's schedule
and conditions are evaluated, because the rewrites it applies change the text the later stages see.
`SynonymExpansionStage` no longer loads or selects rules; it keeps loading synonyms, stopwords and
weights. `BoostRulesStage` (700) gained `Hide`; `PinnedAndBuriedStage` (900) is unchanged in
behaviour. `ProjectResponseStage` (1100) builds `ruleData`.

**Conditions are judged on the original query, not the rewritten one.** A rule that rewrites and a
rule that reacts to the rewritten wording would otherwise depend on each other's priorities. The
activity journal and the query log keep recording the visitor's own words (ADR-0015, AN-4).

**Compatibility.** `TuningRuleCompat.FromFlat` maps the flat columns the Admin package still stores
onto the new model — one condition, one consequence — so this unit changes no stored data and no
observable behaviour of an existing rule. Two edges are preserved deliberately: *Is anything at all*
becomes `Contains ""` (which fires on every query, and keeps such a rule out of the
"no conditions" hole), and a blank pattern under any other operator comes back **disabled**, because
under the flat model it matched nothing.

## Evidence

- `tests/XpSearch.Core.Tests/RuleEngineTests.cs` — the condition matrix (operators × analyzed,
  filters, group, language, combinations, the empty-conditions guard), rewrite ordering with synonyms
  after, hide excluding a document from the total, a hidden document surviving a pin, a
  multi-consequence rule, custom-data merge order and the invalid-JSON skip, and the compat mapping
  of every legacy condition and consequence.
- `tests/XpSearch.Core.Tests/TuningTests.cs` — the pre-existing pin, bury, boost, filter, redirect and
  precedence tests, now running through the compat shim, unchanged.

## Consequences

**Easy.** One rule is one editorial intent. A campaign is "if the search is *espresso* and the
visitor is in *coffee-lovers*, then boost this, pin that, and hand the page a banner" — one row, one
priority, one thing to switch off afterwards.

**Expensive.** **Breaking for any custom `IRelevanceTuningSource`**: `TuningRule` is a different
record. Consumers rebuild it, or call `TuningRuleCompat.FromFlat` if their storage is flat too. The
condition evaluation now costs one analyzer pass over the query when a rule asks for
`matchAnalyzed`, and the rewrite stage costs one extra read of the synonym cache per search.

**Foreclosed / deferred.**

- **Storage is still flat** until CR-4b: the shim means at most one condition and one consequence
  survive a round trip through the database, so the new kinds are unreachable from the admin UI in
  this unit. CR-4b adds the JSON columns, the migration and the form.
- **`Bury.FilterExpression`** is carried in the record but not applied — burying a group of documents
  is a post-execution filter over the page, and no one has asked for it yet.
- **Typo tolerance** stays out. If it ever arrives it belongs in the analyzer, not in rule matching.
