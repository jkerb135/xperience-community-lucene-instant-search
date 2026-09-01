# ADR-0026 — Correlating a reformulation without a visitor identifier

- **Status:** accepted (unit SY-1)
- **Context:** owner amendment `docs/spec/amendments/2026-08-31-analytics-relevance.md`

## Context

The amendment asks for synonym candidates mined from reformulations: "when a query yields zero
results (or zero clicks) and **the same visitor** re-searches a different phrase within the session
and clicks, the pair is a candidate". That definition needs a way to tell that two searches came from
one person.

What the log actually offers, checked before building:

- `XpSearchQueryLogInfo` has no contact, session or visitor column, and its own summary says so —
  the rows are anonymous *by design*, which is why they are written for visitors who refused tracking
  (spec §9.2).
- `LogQueryID` is per **search**, not per visitor: `SearchRequestJournal` writes a fresh id for every
  request, and a click event carries it back only to stamp that one row.
- `SearchRequestJournal` writes the consent-gated activity and the anonymous row independently; the
  activity is per contact but is not queryable as an aggregate, and using it would make mining work
  only for consenting visitors.
- The JS client sends no session or visitor identifier with a search or a click event (searched
  `src/XpSearch.Widgets/Client/src`), and the contact cookie is Xperience's, consent-gated.

So no same-visitor correlator exists, and the unit spec's deciding constraint is that none may be
added: no new cookie, no visitor identifier on the query log, no new consent surface.

## Decision

**Time adjacency inside one index stands in for "the same visitor".** In timestamp order, a row with
no clicked result followed by the *nearest* row that does have one, within
`XpSearchOptions.Analytics.SynonymWindowSeconds` (default 60), is one reformulation. `SynonymMiner`
does this over the same rows `PopularityAggregator` already receives, in the same task loop
(`XpSearchPopularityTask`), so the window is read once.

**Noise is answered by repetition, not by certainty.** Two visitors searching in the same minute
produce a pair nobody made. A pair therefore has to occur `SynonymMinimumOccurrences` times (default
3) in the whole 30-day window before it is stored, and it is stored as a *suggestion* a human reads
with its occurrence count before approving. Nothing mined reaches a search on its own.

**Containment is excluded.** `coff` → `coffee` is autocomplete typing and `sofa` → `red sofa` is
narrowing; neither is a synonym, and both are far more common in a log than a genuine reformulation.
Texts are compared trimmed, lowercased and with runs of whitespace collapsed, so case and spacing are
never a new pair.

**Approval writes a two-way group.** The amendment calls the pairs "synonym/rewrite candidates". The
evidence — these two phrases mean the same thing here — is symmetric, so the MVP writes an ordinary
two-way `XpSearchSynonymInfo` group through the existing storage; an editor who wants only the failed
phrase rewritten switches the created group to one-way, which is a normal edit. Building a separate
rewrite path for a guess the editor can make better would be code with no owner.

## Consequences

- Mining works for every visitor, consenting or not, because it uses only anonymous rows — the same
  property that makes the popularity signal work.
- The heuristic is honestly lossy in both directions: interleaved visitors invent pairs, and a
  visitor who reformulates after two minutes contributes none. Both are documented in the guide and
  in `docs/internal/KNOWN-LIMITATIONS.md`; the window and the threshold are the two knobs.
- A quiet site produces few suggestions, which is the correct behaviour: fewer than three
  occurrences is not evidence.
- If a same-visitor correlator ever arrives for another reason (an owner-approved session id, say),
  `SynonymMiner.Mine` is where it replaces adjacency; nothing else changes.
