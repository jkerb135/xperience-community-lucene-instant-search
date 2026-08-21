# SP-1 - faceting spike results (spec 4.5 / 13.1)

Generated 2026-08-21 16:07 UTC by `XpSearch.FacetSpike`, Release configuration.
Every cell is the **median of 3 runs** in a single process, with `[min-max]` alongside.

## Environment

| Item | Value |
|---|---|
| CPU | AMD64 Family 25 Model 97 Stepping 2, AuthenticAMD (24 logical cores) |
| RAM (available to runtime) | 31.6 GB |
| OS | Microsoft Windows 10.0.26200 (X64) |
| .NET | .NET 8.0.26 |
| Lucene.Net | 4.8.0-beta00017 commit:[5784b18a4c] |
| Index storage | `FSDirectory` under `C:\Users\jkerb\AppData\Local\Temp\` (disk type not discoverable from managed code; see Caveats) |

## Correctness proof

PASS - for 30 fixed queries (5 match-all, 13 single-term, 12 two-term OR) A and B produced identical counts for `contentType`, `language` and `tags`, and B's flat `a/b/c` counts equalled A's `category` leaf-path counts. Run with `--skip-verify` to skip it on repeat runs.

## Index build and on-disk size

| Docs | Backend | Build + commit (ms) | Main index (MB) | Taxonomy (MB) | Total (MB) | First reader open (ms) |
|---|---|---|---|---|---|---|
| 10,000 | A (taxonomy) | 532.89 [495.32-563.19] | 2.40 [2.40-2.40] | 0.01 [0.01-0.01] | 2.40 [2.40-2.40] | 2.06 [1.89-2.31] |
| 10,000 | B (docvalues) | 455.26 [451.75-513.03] | 2.38 [2.38-2.38] | 0.00 [0.00-0.00] | 2.38 [2.38-2.38] | 1.14 [0.89-1.19] |
| 100,000 | A (taxonomy) | 4729.04 [4709.49-4949.84] | 23.46 [23.46-23.46] | 0.01 [0.01-0.01] | 23.46 [23.46-23.46] | 2.50 [2.47-3.00] |
| 100,000 | B (docvalues) | 4511.35 [4365.04-4558.34] | 23.27 [23.27-23.27] | 0.00 [0.00-0.00] | 23.27 [23.27-23.27] | 2.55 [2.27-2.65] |

## Query latency (ms)

300 faceted queries (100 match-all, 100 single-term, 100 two-term OR), top-10 counts for `contentType`, `language`, `tags` and `category`; then 100 drill-sideways queries (term query + one `contentType`/`tags` filter, sideways counts for all four dimensions). 20 warm-up queries discarded.

| Docs | Backend | Class | p50 | p95 | p99 | Total |
|---|---|---|---|---|---|---|
| 10,000 | A (taxonomy) | match-all | 0.59 [0.57-2.33] | 0.80 [0.77-2.93] | 0.85 [0.79-2.97] | 61.27 [60.44-242.45] |
| 10,000 | A (taxonomy) | single-term | 0.13 [0.12-0.57] | 0.47 [0.42-1.89] | 0.83 [0.62-2.80] | 18.65 [16.30-79.29] |
| 10,000 | A (taxonomy) | two-term OR | 0.29 [0.28-0.89] | 0.70 [0.68-2.76] | 0.83 [0.80-3.32] | 34.98 [32.82-110.68] |
| 10,000 | A (taxonomy) | drill-sideways | 0.20 [0.13-0.25] | 0.81 [0.53-0.91] | 6.62 [0.72-7.35] | 50.93 [18.97-52.97] |
| 10,000 | B (docvalues) | match-all | 1.06 [1.02-2.28] | 1.40 [1.25-3.04] | 1.54 [1.37-3.12] | 110.56 [103.53-209.57] |
| 10,000 | B (docvalues) | single-term | 0.19 [0.19-0.23] | 0.84 [0.77-0.86] | 1.01 [1.00-1.13] | 28.19 [28.18-32.96] |
| 10,000 | B (docvalues) | two-term OR | 0.43 [0.42-0.49] | 1.10 [1.07-1.33] | 1.19 [1.14-1.83] | 53.80 [51.74-61.20] |
| 10,000 | B (docvalues) | drill-sideways | 0.27 [0.24-0.29] | 1.04 [0.99-1.16] | 1.33 [1.18-1.51] | 39.03 [34.78-42.93] |
| 100,000 | A (taxonomy) | match-all | 6.40 [6.11-6.54] | 7.68 [6.74-8.28] | 8.53 [7.32-8.90] | 648.10 [618.25-673.32] |
| 100,000 | A (taxonomy) | single-term | 1.23 [1.17-1.30] | 4.64 [4.52-5.25] | 6.58 [6.47-8.55] | 169.38 [163.91-186.72] |
| 100,000 | A (taxonomy) | two-term OR | 2.43 [2.29-2.61] | 7.08 [7.05-7.35] | 7.55 [7.54-8.67] | 323.53 [310.46-327.42] |
| 100,000 | A (taxonomy) | drill-sideways | 1.33 [1.26-1.36] | 5.67 [5.60-5.74] | 6.65 [6.56-6.68] | 195.77 [188.62-200.19] |
| 100,000 | B (docvalues) | match-all | 10.52 [10.50-11.22] | 12.01 [11.69-13.01] | 13.77 [13.60-14.20] | 1072.81 [1065.08-1138.48] |
| 100,000 | B (docvalues) | single-term | 1.85 [1.85-1.86] | 7.58 [7.20-7.79] | 11.03 [10.80-11.23] | 269.22 [267.57-273.18] |
| 100,000 | B (docvalues) | two-term OR | 3.93 [3.83-4.02] | 11.14 [11.07-11.26] | 12.85 [11.73-12.98] | 510.88 [500.06-517.78] |
| 100,000 | B (docvalues) | drill-sideways | 2.20 [2.19-2.28] | 10.42 [10.33-11.20] | 12.45 [12.25-12.79] | 355.99 [354.34-358.27] |

## Incremental update (1% of documents re-upserted)

Delete-by-id-term then add, the way `DefaultLuceneClient` does it (A commits the taxonomy writer too), then reopen the reader and replay all 300 faceted queries to expose any cold-cache cliff.

| Docs | Backend | Update + commit (ms) | Reader reopen (ms) | of which reader state (ms) | Post-reopen p50 | Post-reopen p95 |
|---|---|---|---|---|---|---|
| 10,000 | A (taxonomy) | 38.48 [35.75-45.10] | 2.33 [1.74-4.84] | 0.00 [0.00-0.00] | 0.43 [0.33-0.48] | 0.89 [0.79-1.06] |
| 10,000 | B (docvalues) | 36.93 [31.45-49.59] | 1.36 [1.19-2.96] | 0.17 [0.13-1.62] | 0.63 [0.56-0.65] | 1.36 [1.32-1.58] |
| 100,000 | A (taxonomy) | 88.29 [83.98-94.44] | 1.93 [1.74-2.10] | 0.00 [0.00-0.00] | 3.52 [3.23-3.74] | 7.34 [6.82-7.77] |
| 100,000 | B (docvalues) | 82.80 [81.40-86.64] | 1.96 [1.93-5.67] | 0.24 [0.21-4.01] | 5.19 [5.16-5.45] | 12.53 [11.82-12.82] |

## Caveats

- Synthetic corpus: a 2000-token pronounceable vocabulary with Zipf-weighted picks. Term frequencies are realistic in shape; real content has phrases, stopwords and a much longer tail.
- `FSDirectory` on the local temp disk. The integration uses `CmsIODirectory`, which is the local filesystem in a dev/on-prem deployment. **Azure Blob-backed index storage is spec 13.4 and out of scope here** - it changes reader-open and directory-enumeration costs, not the relative facet-counting cost.
- **The `category` top-10 is not the same work for both backends.** A returns the 5 rolled-up top-level children of the hierarchy; B returns 10 of the 125 flat `a/b/c` labels, because SortedSet faceting has no drill-down tree. This is the functional gap, and it slightly favours A in the query numbers.
- B's `DefaultSortedSetDocValuesReaderState` is documented as expensive, but the measured cost scales with the number of *distinct facet labels* (183 in this corpus) and segment count, not with document count - hence the sub-millisecond figures. High-cardinality facets (thousands of taxonomy values) would move this number; more documents alone would not.
- 1M documents is deferred to the spec 12 performance pass (owner decision). Both backends' counting work is O(matching docs), so the ranking is not expected to invert, but only a measurement settles it.
- The fresh build is pure adds (`OpenMode.CREATE`); only the incremental pass does delete-then-add. Both backends are treated identically.
- Single-threaded, no concurrent query load.
