# ADR-0008: Query pipeline, caching and the Lucene seams

- **Status:** accepted; amended by ADR-0010 (the DTOs the pipeline maps onto are renamed and reshaped; the stage order, the caching and the Lucene seams are unchanged)
- **Date:** 2026-08-21
- **Spec reference:** §4.2–§4.7, §13.2, §13.6

## Context

Spec §4.4 requires the query path to be "an ordered, injectable pipeline so consumers can insert their own
stages", with slots reserved for the Phase 5 relevance features and the Phase 6 analytics. Around it, §4.7
requires a short-TTL response cache invalidated on index rebuild, and §4.3 requires two autocomplete modes,
one of which depends on a store that does not exist yet.

Three constraints from `Kentico.Xperience.Lucene` 15.0.5 shaped the result and were verified against its
source rather than assumed:

1. `LuceneIndex` has no public constructor, and `ILuceneSearchService` takes one. Nothing that touches
   those types can be exercised outside a running Xperience application.
2. There is no rebuild event. `ILuceneClient` is the only public write-side surface, and the integration's
   own `LuceneSearchCacheInvalidator` is `internal`.
3. `ServiceProviderExtensions.GetRequiredStrategy`, which resolves an index's strategy (and therefore its
   `FacetsConfig`), is `internal` too.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Pipeline as a middleware-style `next` chain | familiar, allows short-circuiting | a stage cannot be reordered without knowing its neighbours; ordering is implicit in registration order, which is nondeterministic within an assembly |
| Pipeline as ordered stages over a mutable context | explicit integer slots, reorderable, trivially testable | no short-circuit; a stage that misbehaves corrupts shared state |
| Caching as a pipeline stage | uniform with everything else | a hit still walks every earlier stage, and a later stage can be skipped by accident |
| Caching as a decorator on `ISearchPipeline` | a hit costs one lookup; all-or-nothing | the cached response has to be rewritten with a fresh `queryId` |
| `IProgressiveCache` | platform convention, collapses parallel misses, works on SaaS | needs a running application, so it is not unit-testable |
| ASP.NET Core output caching | testable standalone | not the platform convention, second cache to reason about, awkward on SaaS |
| Depending on a Lucene rebuild event | direct | does not exist |
| Decorating `ILuceneClient` | documented Kentico pattern, covers rebuild, upsert, delete and index deletion | misses writes made by another process against shared storage |

## Decision

**Pipeline.** `ISearchPipeline` runs `IEnumerable<ISearchStage>` ordered by `ISearchStage.Order`, over a
mutable `SearchContext`. Slots are constants on `SearchStageOrder`, in the order spec §4.4 lists them, with
the bracketed phases reserved but unimplemented:

| Slot | Value | Status |
|---|---|---|
| `Normalize` | 100 | shipped |
| `SynonymExpansion` | 200 | reserved, Phase 5 |
| `StopwordRemoval` | 300 | reserved, Phase 5 |
| `BuildQuery` | 400 | shipped |
| `FacetFilters` | 500 | shipped |
| `NumericFilters` | 600 | shipped |
| `BoostRules` | 700 | reserved, Phase 5 |
| `Execute` | 800 | shipped |
| `PinnedAndBuried` | 900 | reserved, Phase 5 |
| `CollectFacets` | 950 | shipped |
| `Highlight` | 1000 | shipped |
| `Project` | 1100 | shipped |
| `LogActivity` | 1200 | reserved, Phase 6 |

Consumers add stages with `services.AddXpSearchStage<T>()`, or `AddXpSearchStage<T>(order)` to place a
stage at a slot other than its own. Resolving the index and its schema happens in `SearchPipeline` before
the first stage, because a stage cannot run without them and an unknown index is a `404`, not a validation
error.

**Lucene seam.** All reader-side access goes through `ILuceneIndexAccessor`, keyed by index code name;
`LuceneIndexAccessor` is the only type that touches `ILuceneIndexManager` and `ILuceneSearchService`, and
it resolves the strategy itself (`GetRequiredService(index.LuceneIndexingStrategyType)`) because the
integration's helper is internal. Everything downstream is tested against a real Lucene directory built in
the test process, not against a mock.

**Caching.** `CachedSearchPipeline` decorates `ISearchPipeline`; `ISearchCache` is the storage interface
and `ProgressiveSearchCache` its `IProgressiveCache` implementation. The key is a SHA-256 of the normalized
request with `queryId` excluded, so identical searches share an entry; a cache hit is returned with the
caller's own `queryId` re-issued. TTL is `XpSearchOptions.CacheTtl`, 60 seconds by default; a zero TTL
disables caching. Entries depend on a per-index dummy key, `xpsearch|index|<name>`.

**Invalidation.** `CacheEvictingLuceneClient` decorates `ILuceneClient` (registered by `AddXpSearch`, which
therefore must be called after `AddKenticoLucene`), forwards every member, and touches the index's
dependency key after `Rebuild`, `UpsertRecords`, `DeleteRecords` and `DeleteIndex`. The previous descriptor
is taken from the `IServiceCollection` rather than resolved from the container, so the decoration works the
same whichever container the host uses.

**Faceting.** Per ADR-0001 the native taxonomy sidecar is used. A single group refining a single dimension
becomes a `DrillDownQuery` executed through `DrillSideways`, keeping that dimension's counts at "what if I
picked another value". Anything a drill-down cannot express — a group spanning dimensions, a second group
on the same dimension, or an index whose strategy returns no `FacetsConfig` — falls back to boolean MUST
clauses on the base query.

**Sort keys.** The convention is a suffix on the attribute name: `Price_desc`, `Title_asc`, or the literal
`relevance`. No separate sort-key configuration exists; the schema's `Sortable` flag is the whitelist.

**Suggest modes.** `SuggestMode.FederatedHits` (prefix match on the index's suggest field, `Title` by
default) is implemented and is the default. `SuggestMode.QuerySuggestions` needs the Phase 6 analytics
store (spec §13.6); it returns an empty list and logs a warning until then.

**Language.** `language` filters on `BaseDocumentProperties.LANGUAGE_NAME`; one index holds every language.
This is the current behaviour, **not** a resolution of spec §13.2 — whether an index should be per language
stays open, and this ADR does not decide it.

## Evidence

- `Kentico.Xperience.Lucene.Core` 15.0.5, read at
  https://github.com/Kentico/xperience-by-kentico-lucene/tree/master/src/Kentico.Xperience.Lucene.Core:
  `Indexing/DefaultLuceneClient.cs` (no rebuild event; the write path this decorator wraps),
  `ServiceProviderExtensions.cs` (`internal static GetRequiredStrategy`),
  `Search/DefaultLuceneSearchService.cs` and `Indexing/LuceneIndex.cs` (no public constructor,
  `ChannelConfigurations` not public).
- Xperience documentation retrieved through the docs MCP server:
  [data caching](https://docs.kentico.com/documentation/developers-and-admins/development/caching/data-caching),
  [decorate system services](https://docs.kentico.com/documentation/developers-and-admins/customization/decorate-system-services),
  [field data types](https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types),
  [secure custom endpoints](https://docs.kentico.com/documentation/developers-and-admins/customization/secure-custom-endpoints).
- `XpSearch.Core.Tests` runs the whole pipeline against a real Lucene index with a taxonomy sidecar, built
  the way `DefaultLuceneClient.UpsertRecordsInternal` builds one, and covers AND/OR facet semantics with
  drill-sideways counts, numeric filters including `!=`, sorting, paging, the highlight XSS guard, the
  cache hit/miss path and eviction through the `ILuceneClient` decorator.

## Consequences

- Phase 5 and Phase 6 add stages at reserved slots without renumbering or touching what already ships.
- The pipeline cannot short-circuit; a stage that wants to stop early has to leave the later stages with
  nothing to do rather than skipping them.
- Because the cache is per index and evicted wholesale, one document changing drops every cached response
  for that index. With a 60 second TTL that is cheaper than tracking per-document dependencies.
- Index writes performed by a different process against shared storage do not evict this cache; the TTL is
  the only bound on that staleness.
- `AddXpSearch` must come after `AddKenticoLucene`, and a host that resolves `ILuceneClient` during
  registration would capture the undecorated instance.
- Everything below `ILuceneIndexAccessor` is testable; `LuceneIndexAccessor` and `ProgressiveSearchCache`
  themselves are only verifiable on a real Xperience host.
