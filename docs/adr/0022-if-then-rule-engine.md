# ADR-0022: The if/then rule engine

- **Status:** accepted — owner approval 2026-08-24 (design canvas signed off)
- **Date:** 2026-08-24
- **Spec reference:** §8.2, §8.3 — amends the rule model of [ADR-0014](0014-relevance-tuning.md)
- **Design canvas:** <https://claude.ai/code/artifact/e2b15580-1239-417c-afdb-118a100133df>
- **Implemented by:** unit CR-4a (Core) and unit CR-4b (storage, migration, rule builder — see the addendum).

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

**Compatibility.** `RuleStorageMigration.FromFlat` (`TuningRuleCompat.FromFlat` in CR-4a, before it
moved into the Admin package with the migration) maps the flat columns the Admin package used to store
onto the new model — one condition, one consequence — so this unit changes no stored data and no
observable behaviour of an existing rule. Two edges are preserved deliberately: *Is anything at all*
becomes `Contains ""` (which fires on every query, and keeps such a rule out of the
"no conditions" hole), and a blank pattern under any other operator comes back **disabled**, because
under the flat model it matched nothing.

## Evidence

- `tests/XpSearch.Core.Tests/RuleEngineTests.cs` — the condition matrix (operators × analyzed,
  filters, group, language, combinations, the empty-conditions guard), rewrite ordering with synonyms
  after, hide excluding a document from the total, a hidden document surviving a pin, a
  multi-consequence rule, custom-data merge order and the invalid-JSON skip; and the type
  discriminators every consequence must carry.
- `tests/XpSearch.Core.Tests/TuningTests.cs` — the pre-existing pin, bury, boost, filter, redirect and
  precedence tests, unchanged.
- `tests/XpSearch.Admin.Tests/RuleStorageTests.cs` — the stored shape, a round trip of every
  consequence type, the legacy mapping and its two edges, the flat-to-JSON round trip over every
  condition × consequence pair, the conversion marker, the column retirement, the validation matrix,
  the summary formatter and the seeded create.

## Consequences

**Easy.** One rule is one editorial intent. A campaign is "if the search is *espresso* and the
visitor is in *coffee-lovers*, then boost this, pin that, and hand the page a banner" — one row, one
priority, one thing to switch off afterwards.

**Expensive.** **Breaking for any custom `IRelevanceTuningSource`**: `TuningRule` is a different
record. Consumers rebuild it, or call `RuleStorageMigration.FromFlat` if their storage is flat too. The
condition evaluation now costs one analyzer pass over the query when a rule asks for
`matchAnalyzed`, and the rewrite stage costs one extra read of the synonym cache per search.

**Foreclosed / deferred.**

- **`Bury.FilterExpression`** is carried in the record but not applied — burying a group of documents
  is a post-execution filter over the page, and no one has asked for it yet.
- **Typo tolerance** stays out. If it ever arrives it belongs in the analyzer, not in rule matching.

## Addendum — storage and migration (unit CR-4b, 2026-08-24)

CR-4a left the storage flat and the shim in place. CR-4b replaces both.

### The stored shape

`XpSearch_Rule` gains two `LongText` columns and drops the nine flat ones
(`RuleConditionType`, `RulePattern`, `RuleConsequenceType`, `RuleTargetObjectID`,
`RuleTargetPosition`, `RuleBoostValue`, `RuleFilterExpression`, `RuleRedirectUrl`,
`RuleContactGroup`). Both are written with `System.Text.Json`, camelCase, enums as camelCase strings.
`RuleJson` owns the settings, and only nulls are omitted — "the rule matches any query" is the
*absence* of `query`, not a `null` spelled out, and everything else is written even when empty so
what a support engineer reads is the whole shape.

**`RuleConditions`** — one object:

```json
{
  "query": { "operator": "contains", "pattern": "grinder", "matchAnalyzed": true },
  "filters": [{ "attribute": "ProductFieldCategory", "value": "Grinders" }],
  "contactGroup": "CoffeeGrinders",
  "language": "en"
}
```

`operator` is `is`, `contains` or `startsWith`. A rule that is scoped to nobody in particular stores
`{"filters":[],"contactGroup":"","language":""}`. `IsEmpty` is derived and never stored.

**`RuleConsequences`** — an array in the order the rule applies them, each tagged with a `type`
discriminator declared on `RuleConsequence` itself:

| `type` | Members |
|---|---|
| `pin` | `targetId`, `position` |
| `hide` | `targetId` |
| `boost` | `targetId`, `filterExpression`, `multiplier` |
| `bury` | `targetId`, `filterExpression` |
| `filterResults` | `filterExpression` |
| `removeWord` | `word` |
| `replaceWord` | `word`, `replacement` |
| `replaceQuery` | `query` |
| `redirect` | `url` |
| `customData` | `json` (the author's text, verbatim, formatting and all) |

```json
[{ "type": "pin", "targetId": "doc-1:en", "position": 1 },
 { "type": "customData", "json": "{\"banner\":\"Grinder week\"}" }]
```

The discriminators are the storage contract: renaming one reinterprets every saved rule, so they are
spelled out on the model and asserted in `XpSearch.Core.Tests` rather than derived from type names.

### Migration policy

- **Automatic and lossless.** `RuleStorageMigration.Run` converts every flat row through the same
  mapper the shim used, so no rule changes meaning — including the two preserved edges above.
- **When.** From `XpSearchTuningModuleInstaller.Install`, on `ApplicationEvents.Initialized`, after
  `CombineWithForm` has added the JSON columns (an upgraded class briefly has both shapes) and before
  any page or query reads a rule.
- **The marker is the row.** A row whose `RuleConditions` column is empty has not been converted;
  a converted row is saved with it filled. No flag, no version table. That makes the pass idempotent
  and crash-safe: killing the process halfway through the table leaves the converted rows converted
  and the rest to be picked up next start. No rule the builder writes can look unconverted — even
  "matches anything" stores an object.
- **The flat columns are then dropped**, by removing them from the installed form definition.
  `CombineWithForm` only ever adds, so the removal is an explicit `FormInfo.RemoveFormField`. They
  cannot be left orphaned-but-unread: several are `NOT NULL` with no default, so every insert the new
  builder makes would fail. The drop only runs once nothing is left to convert.
- **Belt and braces.** `InfoRelevanceTuningSource.Read` still converts a flat-looking row on the fly,
  so a rule inserted by a script after startup does not silently stop firing.
- **Tolerance.** A column a hand edit left unparseable reads back as "no conditions" / "no
  consequences" — the rule goes inert instead of taking the whole index's tuning down.

### Validation

Save is refused, with field-level messages, when: the rule has no name; the conditions say nothing
(`RuleConditions.IsEmpty`); the Query toggle is on with a blank pattern; a filter row has only one
half; a pin has no target or a position below 1; a boost has neither a target nor an expression, or a
multiplier of 0 or less; a hide, bury, filter, rewrite or redirect has an empty required field; or
custom data does not parse to a JSON **object**. See `XpSearch.Admin.Tuning.RuleValidation`.

### Cost

**Breaking for anything reading `XpSearchRuleInfo`'s flat properties** — they are gone from the class
and from the table. A report or integration that selected `RulePattern` reads `RuleConditions` now.
The migration is one pass over one small table at startup; the JSON parse per rule happens inside the
30-minute tuning cache, not per search.
