# PF-1 - pipeline performance results (spec §12)

Generated 2026-09-02 01:25 UTC by `XpSearch.Bench`, Release configuration.
Every latency cell is the **median of 3 runs** of 100 queries, with `[min-max]` across those runs alongside.
Percentiles are nearest-rank within a run: a reported value is always a measured value.

## Environment

| Item | Value |
|---|---|
| CPU | AMD64 Family 25 Model 97 Stepping 2, AuthenticAMD (24 logical cores) |
| RAM (available to runtime) | 31.6 GB |
| OS | Microsoft Windows 10.0.26200 (X64) |
| .NET | .NET 8.0.29 |
| Lucene.Net | 4.8.0-beta00017 commit:[5784b18a4c] |
| Index storage | `FSDirectory` under `C:\Users\jkerb\AppData\Local\Temp\` (disk type is not discoverable from managed code - see Caveats) |

## What is being measured

The **product pipeline**, not raw Lucene: `SearchPipeline` with the stage chain `AddXpSearch` composes -
normalize → query rewrite → synonym expansion → stopwords → build query → facet filters → numeric filters →
boost rules → execute (`DrillSideways`) → pinned/buried → collect facets → highlight → project. Tuning is
present but light, the way a modest site configures it: 5 two-way synonym groups, one always-on boost rule
(`contentType:Article`, ×1.5) and non-default field weights (`title` ×3). Every request asks for facet counts on
four dimensions (`section`, `topic`, `contentType`, `language`) and highlighted snippets for `title` and `body`, page size 20.

**The headline numbers are uncached.** Every iteration of every row but the last uses a different query text, so
neither the response cache nor any Lucene-side reuse can answer twice. The single cache-hit row is there for
contrast only.

Corpus: deterministic (`Random(42)`), a Zipf-distributed 5,000-token vocabulary, titles of 3-6 words,
bodies of 50-500 words skewed short, a ~10-value facet dimension (`section`), a ~1,000-value one (`topic`, 1-3 per
document), a `price` number, a language and a content type. About 2% of documents carry one of five high-frequency
marker terms so match counts vary across the workload.

## Index build, size and reader open

Corpus generation and indexing in one pass, stock `IndexWriterConfig`, single-threaded, one commit at the end.
Measured once per size (rebuilding a 1M index three times measures the disk, not the library).

| Docs | Build + commit | Throughput (docs/s) | Main index (MB) | Taxonomy (MB) | Total (MB) | Bytes/doc | Cold reader open (ms) |
|---|---|---|---|---|---|---|---|
| 10,000 | 3,892 ms | 2,569 | 12.9 | 0.02 | 12.9 | 1,353 | 15.59 |
| 100,000 | 19.4 s | 5,168 | 128.4 | 0.02 | 128.5 | 1,347 | 49.12 |
| 1,000,000 | 189.1 s | 5,287 | 1,244.0 | 0.02 | 1,244.0 | 1,304 | 35.73 |

## Query latency (ms)

| Docs | Workload | Matched docs | p50 | p95 | max |
|---|---|---|---|---|---|
| 10,000 | match-all + facets | 10,000 | 5.90 [5.85-5.97] | 7.88 [7.30-7.96] | 28.70 [13.00-38.12] |
| 10,000 | single-term | 1,338 | 2.76 [2.59-2.78] | 3.67 [3.57-3.85] | 4.61 [4.14-5.07] |
| 10,000 | single-term, no highlight | 1,338 | 1.99 [1.92-2.16] | 2.63 [2.53-3.09] | 4.14 [4.01-6.19] |
| 10,000 | two-term OR | 101 | 3.45 [3.44-3.76] | 4.52 [4.46-4.66] | 5.18 [4.93-5.42] |
| 10,000 | term + facet filter + numeric range | 312 | 5.70 [5.61-5.71] | 8.77 [8.54-13.14] | 24.15 [12.38-29.25] |
| 10,000 | single-term, sorted by price | 1,338 | 3.44 [3.36-3.53] | 4.55 [4.21-4.65] | 6.30 [5.86-6.71] |
| 10,000 | match-all, deep page (rank 10,000) | 10,000 | 9.13 [9.13-9.43] | 11.02 [10.66-12.19] | 59.18 [21.04-71.84] |
| 10,000 | single-term, fuzzy on | 1,429 | 135.18 [128.14-137.52] | 204.34 [200.60-223.42] | 329.33 [326.57-397.56] |
| 10,000 | single-term, fuzzy on, no highlight | 1,429 | 4.85 [4.72-5.01] | 6.03 [6.01-6.27] | 11.46 [11.15-31.20] |
| 10,000 | suggest prefix (Documents mode) | n/a | 0.34 [0.33-0.37] | 0.54 [0.48-0.56] | 0.63 [0.61-0.64] |
| 10,000 | cache hit (same request) | 1,338 | 0.00 [0.00-0.00] | 0.01 [0.01-0.01] | 0.18 [0.12-0.18] |
| 100,000 | match-all + facets | 100,000 | 14.17 [13.97-14.59] | 16.96 [15.98-18.31] | 63.08 [55.59-93.44] |
| 100,000 | single-term | 13,029 | 4.97 [4.96-5.25] | 8.32 [7.64-8.65] | 19.86 [19.11-20.33] |
| 100,000 | single-term, no highlight | 13,029 | 3.98 [3.92-4.44] | 6.91 [6.57-8.98] | 20.91 [19.55-21.59] |
| 100,000 | two-term OR | 928 | 5.42 [5.30-5.53] | 7.56 [7.46-8.33] | 16.58 [15.58-17.87] |
| 100,000 | term + facet filter + numeric range | 3,051 | 9.66 [9.54-10.03] | 15.47 [14.69-16.44] | 72.07 [54.90-102.87] |
| 100,000 | single-term, sorted by price | 13,029 | 5.81 [5.69-6.28] | 10.82 [10.04-11.61] | 28.54 [27.74-30.09] |
| 100,000 | match-all, deep page (rank 10,000) | 100,000 | 17.22 [16.73-19.13] | 20.11 [19.08-68.63] | 80.46 [42.92-160.43] |
| 100,000 | single-term, fuzzy on | 14,021 | 136.83 [128.67-151.69] | 202.12 [189.10-355.42] | 354.30 [315.48-516.30] |
| 100,000 | single-term, fuzzy on, no highlight | 14,021 | 8.54 [8.37-8.63] | 14.66 [14.16-15.52] | 44.78 [33.55-61.57] |
| 100,000 | suggest prefix (Documents mode) | n/a | 0.40 [0.40-0.43] | 0.64 [0.62-0.64] | 1.02 [0.85-1.07] |
| 100,000 | cache hit (same request) | 13,029 | 0.00 [0.00-0.00] | 0.00 [0.00-0.00] | 0.07 [0.04-0.09] |
| 1,000,000 | match-all + facets | 1,000,000 | 90.20 [89.97-91.96] | 125.89 [123.99-134.01] | 156.34 [149.72-167.54] |
| 1,000,000 | single-term | 131,163 | 16.33 [15.43-20.21] | 58.76 [54.76-59.05] | 177.75 [174.76-185.24] |
| 1,000,000 | single-term, no highlight | 131,163 | 14.78 [14.53-15.56] | 58.40 [50.87-60.64] | 172.60 [171.07-173.38] |
| 1,000,000 | two-term OR | 9,217 | 12.70 [12.47-17.80] | 35.17 [33.47-41.89] | 121.26 [121.01-126.16] |
| 1,000,000 | term + facet filter + numeric range | 30,292 | 36.29 [35.91-38.69] | 80.65 [75.36-81.86] | 237.01 [233.55-277.00] |
| 1,000,000 | single-term, sorted by price | 131,163 | 19.43 [19.27-20.05] | 77.45 [77.31-86.58] | 239.42 [236.35-256.02] |
| 1,000,000 | match-all, deep page (rank 10,000) | 1,000,000 | 94.81 [94.08-96.74] | 141.62 [137.48-143.66] | 175.58 [173.25-183.22] |
| 1,000,000 | single-term, fuzzy on | 141,409 | 166.88 [166.51-178.92] | 239.71 [231.02-245.43] | 463.32 [461.92-633.36] |
| 1,000,000 | single-term, fuzzy on, no highlight | 141,409 | 31.35 [30.01-31.85] | 92.32 [90.75-97.29] | 220.19 [215.55-247.00] |
| 1,000,000 | suggest prefix (Documents mode) | n/a | 0.94 [0.87-0.99] | 1.74 [1.66-2.16] | 7.89 [7.08-7.91] |
| 1,000,000 | cache hit (same request) | 131,163 | 0.00 [0.00-0.00] | 0.00 [0.00-0.00] | 0.05 [0.04-0.07] |

## Caveats

- Synthetic corpus. Term frequencies are realistic in shape (Zipf), but real content has phrases, stopwords, a much longer tail and far more varied document lengths.
- `FSDirectory` on the local temp disk. **The disk type is not discoverable from managed code**, so the build and reader-open numbers carry whatever this machine's storage is; treat them as a machine-specific floor, not a portable constant.
- Single process, single threaded, no concurrent query load. A production site serves queries concurrently; these numbers are per-query service time, not throughput.
- The searcher is opened once and reused, which is what the integration's cached searcher lease does. Nothing here measures index writes competing with reads.
- Journaling, contact-group resolution, experiments and the popularity signal are stubbed out: they are database round-trips on a real site and would measure SQL, not this library.
- The cache-hit row measures `CachedSearchPipeline` over an in-memory dictionary. On a real site the cache is Xperience's `IProgressiveCache`, which is slower than a dictionary but still nowhere near a search.
- The two `no highlight` rows are the same requests with `highlight` omitted. Subtracting them from the rows above isolates what `HighlightStage` costs on an ordinary query and on a fuzzy (multi-term) one; they are here because those two costs are not remotely the same.
- The build row includes corpus generation, which is part of the same loop and cannot be subtracted. Real content is read from the database instead, which is slower - treat the build number as a floor.
