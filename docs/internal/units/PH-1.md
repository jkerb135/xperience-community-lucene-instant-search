# PH-1 — Quoted phrases are phrases

**Status:** DISPATCHED 2026-09-07 (owner: "let's work through these" — GTM review product gap).
**Origin:** GTM review 2026-09-06: all visitor text goes through `QueryParserBase.Escape` before
`MultiFieldQueryParser.Parse` (`BuildQueryStage.cs` ~119-171, `Prepare`), which is right for operator safety but
also escapes `"` — so `"french press"` becomes AND-of-terms. Every mainstream search UI honours quotes.

Scope: `XpSearch.Core` `BuildQueryStage` (+ a small pre-parser type), tests, docs. No contract change, no JS,
no admin. Typo tolerance (FZ-1), synonyms (`QuerySlots` path), highlighting (HL-1 `PrepareHighlightQuery`),
stopwords, field weights and the cache key must all keep working — each gets a test.

## 1. Behaviour
- A **balanced pair of double quotes** around one or more words is a phrase: the terms must appear adjacent and in
  order (Lucene `PhraseQuery`, slop 0) in at least one searchable field (per-field phrase clauses OR-ed across the
  same field/boost set the free text uses, so field weights apply). Text outside quotes keeps today's behaviour
  (escaped, AND-ed). Multiple phrases AND together with the loose terms.
- **Unbalanced quote** = literal, escaped as today (no syntax exposure, no error). Empty phrase `""` = ignored.
  Smart quotes `“ ”` are normalised to `"` first (visitors paste them).
- **Phrases are never fuzzed** (FZ-1's `~N` suffix applies to loose terms only) and are **not synonym-expanded**
  (a phrase is exact by intent). Stopword removal: a phrase keeps its stopwords (`"the press"` must match "the
  press" — the analyzer decides; do not strip inside quotes).
- The phrase terms go through the field's analyzer (use the parser's phrase support per field — the classic
  parser handles `field:"a b"` when the quotes are NOT escaped; the cleanest implementation is: tokenise the
  visitor text into phrase segments and loose segments, escape each loose segment and each phrase's inner text,
  re-wrap phrases in raw quotes, and hand the whole thing to the same `MultiFieldQueryParser`). Verify the
  parser emits `PhraseQuery` (or `MultiPhraseQuery` with a stemming analyzer) and that `AND` default operator
  still applies between segments.
- Highlighting: HL-1's rewrite already handles positional queries — assert `<mark>` on each phrase word.
- Cache key: query text is already in the key; nothing to add — assert unchanged for a non-quoted query.
- Analytics: the journal records the visitor's text verbatim (quotes included) — assert.
- Query tester / explain: the explanation line for the phrase clause should read as a phrase — check
  `ScoreBreakdownStage`/explain does not choke on `PhraseQuery` (test).

## 2. Verification (Core.Tests, real Lucene fixture as the suite already does)
`"french press"` matches the doc with the adjacent words and not the doc with "press … french"; `french press`
(no quotes) matches both; `"french press" grinder` = phrase AND term; unbalanced `"french press` = today's
result; `""` ignored; smart quotes; fuzzy ON does not fuzz the phrase but fuzzes the loose term; synonym rule on
a phrase word does not expand inside the phrase; highlight marks both words; cache key unchanged for a plain
query; `MaxQueryLength` still applies to the raw text.

## 3. Docs
`search-api.md` "What the visitor can type" (or the closest existing section): quotes are the one piece of syntax;
everything else is literal. `relevance-tuning.md`: one line that rules see the raw text (quotes included) for
`contains` conditions — verify `RuleSelection` behaviour and state what is true. CHANGELOG `**Added (core):**`.
KNOWN-LIMITATIONS: no proximity/slop, no single-quote phrases (state ceilings + upgrade path).
