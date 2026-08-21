# ADR-0001: §4.5 — Lucene taxonomy sidecar vs DocValues

- **Status:** accepted — owner decision 2026-08-21 (lead recommendation A; benchmark in docs/internal/spike-faceting-results.md)
- **Date:** 2026-08-21
- **Spec reference:** §4.5, §13.1

## Context

Spec §4.5 offered two ways to count facets in Lucene.NET 4.8 and asked for a Phase 1 benchmark rather than a coin flip:

- **A — `Lucene.Net.Facet` taxonomy writer.** Correct, hierarchical, but requires a parallel taxonomy directory next to each index.
- **B — DocValues-based counting.** No sidecar, flat facets only.

The spec's recommendation was to start with B, on the premise that A "requires a meaningful change to how indexes are built and stored."

**That premise is false, and it was the whole reason B was preferred.** `Kentico.Xperience.Lucene` 15.0.5 — the integration this product is required to build on (§4.1) — already implements A natively, on both sides:

- Write: `DefaultLuceneClient` calls `ILuceneIndexService.UseIndexAndTaxonomyWriter`, which opens an `IndexWriter` plus a `DirectoryTaxonomyWriter` over a sibling `*_taxonomy` directory, then per document does delete-by-term followed by `writer.AddDocument(facetsConfig.Build(taxonomyWriter, document))`, committing the taxonomy writer every 1000 documents and at the end.
- Read: `DefaultLuceneSearchService.UseSearcherWithFacets` runs `FacetsCollector.Search(...)` and builds `TaxonomyFacetCounts(new DocValuesOrdinalsReader(FacetsConfig.DEFAULT_INDEX_FIELD_NAME), taxoReader, config, fc)`; `UseSearcherWithDrillSideways` builds `new DrillSideways(searcher, config, taxoReader)`. **Both throw if the index has no taxonomy reader.**

So the taxonomy sidecar is not a change we would have to make — it is the platform's existing storage layout, created for us by an indexing strategy that returns a `FacetsConfig` from `FacetsConfigFactory()`. Option B is the one that costs a change: it means bypassing `UseSearcherWithFacets` entirely, falling back to plain `UseSearcher`, and hand-rolling `DefaultSortedSetDocValuesReaderState` + `SortedSetDocValuesFacetCounts` outside the integration's cached searcher lease.

With that corrected, the question is no longer "which is cheaper to build" but "does B buy enough performance to justify leaving the platform's paved road and giving up hierarchical facets?" This spike measures it.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **A — taxonomy sidecar** (`FacetField`, `DirectoryTaxonomyWriter`, `TaxonomyFacetCounts`) | Native to `Kentico.Xperience.Lucene` 15.0.5 on both write and read paths; hierarchical facets with parent roll-up, which §5.3 `hierarchicalMenu` needs; drill-sideways via the integration's own `UseSearcherWithDrillSideways`; taxonomy reader is cached in the integration's searcher lease; measurably faster on every query class | A second directory per index generation to create, commit, publish and retain; a second writer commit in the write path; taxonomy ordinals are append-only, so a deleted facet value leaves a dead ordinal until a full rebuild |
| **B — SortedSet DocValues** (`SortedSetDocValuesFacetField`, `DefaultSortedSetDocValuesReaderState`, `SortedSetDocValuesFacetCounts`) | No sidecar directory; one writer, one commit; slightly smaller and faster to build; reader state turned out to be cheap to construct at these cardinalities | Flat facets only — a hierarchy has to be encoded as an `"a/b/c"` string with no roll-up and no drill-down tree; bypasses the integration's facet API, so we own the reader-state cache with no hook to hang it on; 1.5×–1.9× slower on every measured query class; the integration would still build the taxonomy sidecar anyway if any strategy returns a `FacetsConfig` |

## Evidence

Measured by `tests/XpSearch.FacetSpike` (Release, single process, deterministic `Random(42)` corpus, median of 3 runs). Full tables, environment and caveats: [`docs/internal/spike-faceting-results.md`](../internal/spike-faceting-results.md). Hardware: AMD 24-core, 31.6 GB, .NET 8.0.26, Lucene.Net 4.8.0-beta00017, local `FSDirectory`.

**Correctness first.** For 30 fixed queries the two backends agreed on every facet count — 5,489 counts at 10k documents and 5,490 at 100k — across `contentType`, `language`, `tags`, and B's flat `a/b/c` labels against A's `category` leaf-path counts. The timings below are therefore comparing two implementations that produce the same answers.

**Query latency (ms, median of 3 runs):**

| Docs | Class | A p50 | B p50 | A p95 | B p95 | A p99 | B p99 |
|---|---|---|---|---|---|---|---|
| 10,000 | match-all | 0.59 | 1.06 | 0.80 | 1.40 | 0.85 | 1.54 |
| 10,000 | single-term | 0.13 | 0.19 | 0.47 | 0.84 | 0.83 | 1.01 |
| 10,000 | two-term OR | 0.29 | 0.43 | 0.70 | 1.10 | 0.83 | 1.19 |
| 10,000 | drill-sideways | 0.20 | 0.27 | 0.81 | 1.04 | 6.62 | 1.33 |
| 100,000 | match-all | 6.40 | 10.52 | 7.68 | 12.01 | 8.53 | 13.77 |
| 100,000 | single-term | 1.23 | 1.85 | 4.64 | 7.58 | 6.58 | 11.03 |
| 100,000 | two-term OR | 2.43 | 3.93 | 7.08 | 11.14 | 7.55 | 12.85 |
| 100,000 | drill-sideways | 1.33 | 2.20 | 5.67 | 10.42 | 6.65 | 12.45 |

A is faster on every class at both sizes — roughly 1.4×–1.9× at p50 and p95. The one place B wins is A's drill-sideways p99 at 10k (6.62 ms vs 1.33 ms); that tail is a single outlier in the run (the same cell's min across runs was 0.72 ms) and it does not reappear at 100k, where A's drill-sideways p99 is 6.65 ms against B's 12.45 ms. Both backends scale roughly linearly in matching documents, as expected — the counting loop is per-hit in both.

**Build and storage:**

| Docs | Backend | Build + commit (ms) | Main (MB) | Taxonomy (MB) |
|---|---|---|---|---|
| 10,000 | A | 532.89 | 2.40 | 0.01 |
| 10,000 | B | 455.26 | 2.38 | — |
| 100,000 | A | 4,729.04 | 23.46 | 0.01 |
| 100,000 | B | 4,511.35 | 23.27 | — |

The taxonomy sidecar costs **10 KB and about 5% of build wall time** at 100k documents. The sidecar is sized by the number of distinct facet labels (183 here), not by document count, so it stays negligible as the corpus grows. This is the number that falsifies the spec's "meaningful change to how indexes are built and stored".

**Incremental update (1% of documents re-upserted, delete-by-id then add):**

| Docs | Backend | Update + commit (ms) | Reader reopen (ms) | of which reader state (ms) | Post-reopen p50 | Post-reopen p95 |
|---|---|---|---|---|---|---|
| 10,000 | A | 38.48 | 2.33 | — | 0.43 | 0.89 |
| 10,000 | B | 36.93 | 1.36 | 0.17 | 0.63 | 1.36 |
| 100,000 | A | 88.29 | 1.93 | — | 3.52 | 7.34 |
| 100,000 | B | 82.80 | 1.96 | 0.24 | 5.19 | 12.53 |

Neither backend has a cold-cache cliff — post-reopen latencies are within noise of the steady-state numbers, and are in fact slightly better because the merged post-update index has fewer segments. The taxonomy commit adds about 6% to A's update time.

**The `DefaultSortedSetDocValuesReaderState` worry did not materialise at this scale.** It is documented as expensive and needing a per-`IndexReader` cache, and the integration's searcher provider gives us nowhere to put that cache — but measured, it costs 0.17–0.24 ms, because its cost tracks the number of distinct facet labels and segments, not documents. It is a real architectural wart for B (we would be caching outside the platform's lease, with our own invalidation), but it is not a performance argument against B.

## Recommendation

**Adopt option A, the taxonomy sidecar, and do not build option B.** Option B was recommended in the spec only because A was believed to require reworking how indexes are built and stored; the integration already does exactly that rework for us, so B's single advantage has evaporated. What is left is a straight comparison in which A wins on every axis we measured — 1.4×–1.9× lower faceted-query latency at both 10k and 100k documents, including drill-sideways — while its supposed cost is 10 KB of sidecar and 5% of build time. A also keeps hierarchical facets, which §5.3's `hierarchicalMenu` widget requires and which B structurally cannot provide: B can only store a category path as a flat `"a/b/c"` string, with no parent roll-up and no drill-down tree, so choosing B would mean either dropping a first-party widget or writing our own roll-up layer on top of flat counts. The platform-convention argument points the same way: A is `ILuceneSearchService.UseSearcherWithFacets` and `UseSearcherWithDrillSideways` used as intended, with the taxonomy reader held in the integration's cached searcher lease, whereas B means bypassing that API for plain `UseSearcher` and owning a `DefaultSortedSetDocValuesReaderState` cache with no lease hook to hang it on and no lifecycle event to invalidate it — and the integration would still create and commit the taxonomy sidecar anyway the moment any indexing strategy returns a `FacetsConfig`, so B does not even reliably avoid the sidecar. Neither option satisfies §4.5's "facets must bind to Xperience Taxonomies without custom code" on its own: that requirement is met by our own `DefaultLuceneIndexingStrategy` subclass, which must auto-detect taxonomy fields on indexed content types and emit facet fields plus a matching `FacetsConfig` — the same amount of work either way, except that A's `FacetsConfig` is the object the integration already asks strategies for. The remaining unknown is 1M documents, deferred to the §12 performance pass; both backends count per matching hit and scaled linearly from 10k to 100k here, so a rank inversion at 1M would be surprising, but if one appears it would appear in B's favour on nothing we have measured — A's lead was widest at the larger size. If the §12 pass overturns this, the exit is cheap in one direction only: facet counting sits behind `IFacetProvider` (§4.5), so B remains implementable later as a flat-facet fast path, whereas having shipped B first and needing hierarchy would mean reindexing.

## Consequences

**If A is accepted:**
- Every index that carries facets gets a sibling `*_taxonomy` directory. Index publish, generation retention, backup and the SaaS storage story (§13.4 / ADR-0004) must all treat main + taxonomy as one atomic unit. This is already how the integration behaves; we must not break it.
- Hierarchical facets are available from day one, so §5.3's `hierarchicalMenu` and `refinementList` share one backend and one counting path.
- Faceted search goes through `UseSearcherWithFacets` / `UseSearcherWithDrillSideways`, so we inherit the integration's searcher-lease caching and its invalidation on publish, and write no reader-lifecycle code of our own.
- The write path commits two writers. Ingestion throughput carries roughly a 5% tax and a second failure mode (taxonomy commit succeeding while the index commit fails, or vice versa) — relevant to ADR-0005 (ingestion durability).
- Taxonomy ordinals are append-only. Deleting a taxonomy value in Xperience leaves a dead ordinal in the sidecar with a zero count; it disappears only on a full rebuild. Cosmetically invisible (zero-count values are not returned), but worth knowing before someone reports "the index keeps growing".
- Forecloses nothing: `IFacetProvider` still isolates the counting strategy if B is ever wanted for a flat-facet fast path.

**If B were accepted instead:**
- We would bypass the integration's facet API and own a `DefaultSortedSetDocValuesReaderState` cache, its keying by `IndexReader`, and its invalidation on publish — code the platform already writes for A.
- §5.3 `hierarchicalMenu` would need a roll-up layer written over flat `"a/b/c"` counts, or would have to be cut.
- We would accept 1.4×–1.9× higher faceted-query latency for no measured benefit.
- The taxonomy sidecar would probably exist anyway, since any indexing strategy returning a `FacetsConfig` triggers it — so the storage saving is not guaranteed.
- Upside retained: one writer, one commit, no sidecar lifecycle to reason about. If a future deployment makes the sidecar genuinely expensive (per-generation blob copies on SaaS, say), this is the fallback, and the spike code stays in the repo to re-measure it.
