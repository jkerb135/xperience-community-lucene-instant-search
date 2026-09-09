# ADR-0002: §13.2 — index per language vs language field

- **Status:** decided for now (LC-1, 2026-09-09)
- **Date:** 2026-09-09
- **Spec reference:** §13.2; unit LC-1

## Context

An index can be asked to hold one language or all of them. The spec left the choice open; everything
built since assumed the second (`BaseDocumentProperties.LANGUAGE_NAME` is on every document, and
`SearchRequest.Language` is a term filter), and LC-1 — which makes the page supply that filter —
forced the question, because "the page decides" is only sound if the index really covers the page's
language.

The deciding fact is that the language is not ours to choose: **a Kentico Lucene index definition
lists the languages it indexes**, in the Search application, and the integration only raises an
indexing event for an item whose language the index lists
([`IndexedItemModelExtensions`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/IndexedItemModelExtensions.cs)).
An index also has exactly one analyzer, chosen per index in the same application.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **One index, every language the definition lists, `language` as a term filter** | Matches how an index is configured in the administration; one schema, one set of facet counts, one rebuild; a cross-language search is a request that omits the member | One analyzer for every language, so stemming and stopwords follow whatever the index is configured with |
| One index per language, selected by `language` | Per-language analyzer, stemming and stopwords | The request member stops being a filter and becomes index selection, which the widgets, the cache key, the admin (settings, tuning, analytics are per index) and the facet counts all key on; an editor's index drop-down grows by a factor of the language count |
| A per-language analyzer inside one index (per-field analyzer wrapper) | One index, better stemming | Fields are shared across languages, so the wrapper would have to switch on a value it cannot see at write time; Lucene analyzes per field, not per document |

## Decision

**One index holds every language its definition lists. `language` is a term filter on
`LANGUAGE_NAME`, applied by `BuildQueryStage` and `DocumentSuggestService`, and part of the response
cache key.** The analyzer stays the index's own single setting; per-language analyzers, stemming and
stopword lists are **not supported** until a customer needs them.

Since LC-1 the filter is normally supplied by the page rather than by the caller: a mount carries the
language the page is being viewed in (`IPreferredLanguageRetriever`), so a Spanish page searches
Spanish content without configuration, and `language="*"` searches every language. A request that
omits the member searches all of them, which is what a headless caller gets by default.

## Consequences

- Facet counts, tuning rules, analytics and the per-index settings are per index, so they are
  **shared across languages**. A German query and an English one draw on the same synonym list.
- Stemming quality is whatever the index's analyzer gives. For a project whose languages need
  genuinely different analysis, the upgrade path is one index per language: `language` then selects
  the index instead of filtering it (`BuildQueryStage` loses its filter, the widgets map the page's
  language to an index name), and every per-index concern above becomes per language too — including
  the facet counts, which is the reason it is not the default.
- A page pointed at an index that does not list its language finds nothing; the widgets log one
  warning per index and language saying exactly that (LC-1).
