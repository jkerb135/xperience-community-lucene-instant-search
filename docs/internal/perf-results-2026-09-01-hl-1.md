# PF-1 - pipeline performance results (spec §12)

Generated 2026-09-02 01:57 UTC by `XpSearch.Bench`, Release configuration.
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
| 10,000 | 3,654 ms | 2,737 | 12.9 | 0.02 | 12.9 | 1,353 | 14.77 |
| 100,000 | 18.1 s | 5,520 | 128.4 | 0.02 | 128.5 | 1,347 | 8.70 |
| 1,000,000 | 169.8 s | 5,889 | 1,244.0 | 0.02 | 1,244.0 | 1,304 | 23.64 |

## Query latency (ms)

| Docs | Workload | Matched docs | p50 | p95 | max |
|---|---|---|---|---|---|
| 10,000 | match-all + facets | 10,000 | 5.98 [5.32-13.24] | 7.94 [6.33-18.68] | 20.95 [16.04-41.33] |
| 10,000 | single-term | 1,338 | 2.60 [2.49-3.57] | 3.45 [3.31-5.06] | 4.50 [4.04-10.33] |
| 10,000 | single-term, no highlight | 1,338 | 1.94 [1.77-2.07] | 2.65 [2.47-3.09] | 3.54 [3.20-8.83] |
| 10,000 | two-term OR | 101 | 3.28 [3.20-3.31] | 4.30 [4.00-4.39] | 4.92 [4.66-5.04] |
| 10,000 | term + facet filter + numeric range | 312 | 2.89 [2.65-2.91] | 3.95 [3.79-6.04] | 4.77 [4.74-22.48] |
| 10,000 | single-term, sorted by price | 1,338 | 3.31 [3.30-3.35] | 4.29 [4.21-4.68] | 5.36 [5.28-5.80] |
| 10,000 | match-all, deep page (rank 10,000) | 10,000 | 8.91 [8.80-9.11] | 10.67 [10.11-11.11] | 36.57 [34.90-59.76] |
| 10,000 | single-term, fuzzy on | 1,429 | 8.19 [8.11-8.39] | 11.17 [10.06-26.65] | 43.29 [41.39-63.33] |
| 10,000 | single-term, fuzzy on, no highlight | 1,429 | 4.65 [4.56-4.85] | 6.75 [6.51-6.82] | 27.22 [15.71-31.93] |
| 10,000 | suggest prefix (Documents mode) | n/a | 0.32 [0.32-0.38] | 0.52 [0.48-0.54] | 0.59 [0.58-0.59] |
| 10,000 | cache hit (same request) | 1,338 | 0.00 [0.00-0.00] | 0.01 [0.00-0.01] | 0.20 [0.12-0.24] |
| 100,000 | match-all + facets | 100,000 | 13.35 [12.89-13.64] | 15.97 [15.09-16.02] | 56.00 [37.31-60.54] |
| 100,000 | single-term | 13,029 | 4.85 [4.53-5.40] | 7.91 [7.47-8.73] | 22.11 [21.20-22.96] |
| 100,000 | single-term, no highlight | 13,029 | 4.02 [3.73-4.25] | 7.32 [7.05-8.11] | 18.57 [17.29-19.51] |
| 100,000 | two-term OR | 928 | 4.64 [4.34-5.60] | 6.68 [6.55-7.14] | 13.48 [12.89-15.87] |
| 100,000 | term + facet filter + numeric range | 3,051 | 6.78 [6.55-7.40] | 11.45 [10.91-11.84] | 23.24 [21.77-23.95] |
| 100,000 | single-term, sorted by price | 13,029 | 5.05 [4.90-6.03] | 10.07 [9.84-10.90] | 25.47 [24.18-27.96] |
| 100,000 | match-all, deep page (rank 10,000) | 100,000 | 16.23 [15.99-17.09] | 18.89 [18.55-19.34] | 52.68 [23.49-60.85] |
| 100,000 | single-term, fuzzy on | 14,021 | 11.43 [10.89-12.13] | 19.32 [18.98-19.54] | 61.31 [38.19-67.82] |
| 100,000 | single-term, fuzzy on, no highlight | 14,021 | 7.93 [7.82-8.50] | 14.24 [12.08-14.34] | 40.81 [37.80-58.59] |
| 100,000 | suggest prefix (Documents mode) | n/a | 0.34 [0.33-0.44] | 0.56 [0.52-0.63] | 0.72 [0.68-0.93] |
| 100,000 | cache hit (same request) | 13,029 | 0.00 [0.00-0.00] | 0.00 [0.00-0.00] | 0.05 [0.05-0.06] |
| 1,000,000 | match-all + facets | 1,000,000 | 85.35 [84.94-88.33] | 118.13 [109.24-127.48] | 155.99 [142.25-166.49] |
| 1,000,000 | single-term | 131,163 | 15.87 [15.66-16.71] | 56.13 [53.34-61.03] | 176.14 [174.12-178.89] |
| 1,000,000 | single-term, no highlight | 131,163 | 15.02 [14.78-15.25] | 52.16 [48.16-57.35] | 172.94 [163.97-173.61] |
| 1,000,000 | two-term OR | 9,217 | 12.83 [11.81-13.24] | 30.24 [28.61-30.55] | 124.61 [121.34-127.60] |
| 1,000,000 | term + facet filter + numeric range | 30,292 | 31.14 [30.43-31.20] | 69.81 [68.62-70.58] | 184.17 [174.67-204.72] |
| 1,000,000 | single-term, sorted by price | 131,163 | 20.36 [19.96-20.85] | 83.11 [75.24-84.18] | 235.94 [235.00-249.59] |
| 1,000,000 | match-all, deep page (rank 10,000) | 1,000,000 | 92.08 [89.77-92.21] | 118.15 [109.34-124.68] | 157.91 [147.33-194.19] |
| 1,000,000 | single-term, fuzzy on | 141,409 | 35.41 [35.03-36.92] | 93.61 [89.00-94.86] | 259.05 [250.06-268.10] |
| 1,000,000 | single-term, fuzzy on, no highlight | 141,409 | 31.45 [29.95-32.77] | 87.62 [86.52-92.53] | 222.37 [218.34-231.45] |
| 1,000,000 | suggest prefix (Documents mode) | n/a | 0.88 [0.86-0.90] | 1.47 [1.41-1.56] | 6.28 [5.82-7.00] |
| 1,000,000 | cache hit (same request) | 131,163 | 0.00 [0.00-0.00] | 0.00 [0.00-0.00] | 0.05 [0.05-0.05] |

## Caveats

- Synthetic corpus. Term frequencies are realistic in shape (Zipf), but real content has phrases, stopwords, a much longer tail and far more varied document lengths.
- `FSDirectory` on the local temp disk. **The disk type is not discoverable from managed code**, so the build and reader-open numbers carry whatever this machine's storage is; treat them as a machine-specific floor, not a portable constant.
- Single process, single threaded, no concurrent query load. A production site serves queries concurrently; these numbers are per-query service time, not throughput.
- The searcher is opened once and reused, which is what the integration's cached searcher lease does. Nothing here measures index writes competing with reads.
- Journaling, contact-group resolution, experiments and the popularity signal are stubbed out: they are database round-trips on a real site and would measure SQL, not this library.
- The cache-hit row measures `CachedSearchPipeline` over an in-memory dictionary. On a real site the cache is Xperience's `IProgressiveCache`, which is slower than a dictionary but still nowhere near a search.
- The two `no highlight` rows are the same requests with `highlight` omitted. Subtracting them from the rows above isolates what `HighlightStage` costs on an ordinary query and on a fuzzy (multi-term) one; they are here because those two costs are not remotely the same.
- The build row includes corpus generation, which is part of the same loop and cannot be subtracted. Real content is read from the database instead, which is slower - treat the build number as a floor.
