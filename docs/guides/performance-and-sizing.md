## Performance and sizing

How fast search is, how big the index gets, and — the part most search documentation skips — the
point at which a local Lucene index stops being the right answer for your site.

Every number on this page was measured, not estimated. Here is the short version, for one
representative search: a text query that also asks for facet counts on four attributes and
highlighted snippets, page size 20, **with the response cache bypassed**.

| Corpus | Typical search (p50) | Slow search (p95) | Suggest (p50) | Index on disk | Build (a floor) |
|---|---|---|---|---|---|
| 10,000 docs | 2.8 ms | 3.7 ms | 0.3 ms | 13 MB | ~4 s |
| 100,000 docs | 5.0 ms | 8.3 ms | 0.4 ms | 128 MB | ~19 s |
| 1,000,000 docs | 16.3 ms | 58.8 ms | 0.9 ms | 1.24 GB | ~3 min |

For scale: a typical Xperience website channel is a few thousand documents. A large content-heavy
site with several channels and languages is tens of thousands. A million documents is a product
catalogue or a document archive, not a website.

### Reproduce it yourself

The numbers come from `tests/XpSearch.Bench`, a console tool in this repository. It builds a
deterministic synthetic corpus and runs the **real query pipeline** — the same stage chain
`AddXpSearch` registers — against it:

```bash
dotnet run --project tests/XpSearch.Bench/XpSearch.Bench.csproj -c Release \
  -- --sizes 10k,100k,1m --runs 3 --iterations 100
```

It takes about fifteen minutes, needs ~1.5 GB of temporary disk (cleaned up afterwards), and writes
a dated results document with the full tables and the environment they were measured on. The tables
below are read from the run of **2026-09-01**, except the two typo-tolerance rows, which come from
the re-run in `docs/internal/perf-results-2026-09-01-hl-1.md` after the highlighting fix described
under [Typo tolerance](#typo-tolerance-costs-what-the-extra-terms-cost); that re-run left every
other row where it was:

| | |
|---|---|
| CPU | AMD Ryzen 9 7900X, 24 logical cores |
| RAM | 32 GB |
| OS / runtime | Windows 11 (10.0.26200), .NET 8.0.29, Lucene.Net 4.8.0-beta00017 |
| Storage | the machine's local temp disk. **The disk type is not discoverable from managed code**, so treat the build and reader-open figures as this machine's, not as a constant |

It is a developer workstation, not an idle server: repeated runs of the whole suite moved the
figures by up to ~1.5×. Read them as an order of magnitude with a shape, not as a specification.

Two honest framings that matter for how you read everything below:

- **The headline latencies are uncached.** Every iteration uses a different query text, so neither
  the response cache nor any Lucene-side reuse can answer twice. A benchmark of a 60-second cache
  tells you about the dictionary, not the engine.
- **Single-threaded, one query at a time.** These are per-query service times, not throughput. A
  real site serves searches concurrently on the same machine that renders its pages.

### What a search costs

Median of 3 runs of 100 queries each; percentiles are nearest-rank within a run, so every printed
number is a number that was actually measured.

| Workload | 10k p50 / p95 | 100k p50 / p95 | 1M p50 / p95 |
|---|---|---|---|
| Match-all + facet counts | 5.9 / 7.9 ms | 14.2 / 17.0 ms | 90.2 / 125.9 ms |
| Single term (facets + highlight) | 2.8 / 3.7 ms | 5.0 / 8.3 ms | 16.3 / 58.8 ms |
| Two terms, OR | 3.5 / 4.5 ms | 5.4 / 7.6 ms | 12.7 / 35.2 ms |
| Term + facet filter + numeric range | 5.7 / 8.8 ms | 9.7 / 15.5 ms | 36.3 / 80.7 ms |
| Term, sorted by a number | 3.4 / 4.6 ms | 5.8 / 10.8 ms | 19.4 / 77.5 ms |
| Match-all, deep page (rank 10,000) | 9.1 / 11.0 ms | 17.2 / 20.1 ms | 94.8 / 141.6 ms |
| Term with typo tolerance on | 8.2 / 11.2 ms | 11.4 / 19.3 ms | 35.4 / 93.6 ms |
| …the same, with highlighting off | 4.7 / 6.8 ms | 7.9 / 14.2 ms | 31.5 / 87.6 ms |
| Autocomplete (`/suggest`, prefix) | 0.34 / 0.54 ms | 0.40 / 0.64 ms | 0.94 / 1.74 ms |
| Cache hit on the same request | 0.00 ms | 0.00 ms | 0.00 ms |

#### Where latency is flat and where it grows

**Autocomplete is effectively free at any size.** It is a prefix scan of one field's term
dictionary, and a term dictionary does not grow with document count the way a corpus does — under
2 ms even at a million documents. Suggest-as-you-type is never the thing that makes your search box
feel slow.

**A term query grows sublinearly.** 100× the documents costs about 6× the time, because Lucene only
scores the documents that contain the term. The p95 grows faster than the p50 (16 ms → 59 ms at 1M),
and that gap is the frequency of the term: a word in 13% of a million documents is 130,000 documents
to score, and a rare one is a few hundred.

**Faceting is what grows with corpus size, not the text query.** Counting facet values is
proportional to the number of *matching* documents, so it is cheapest exactly when the search is
most specific. The worst case in the table — match-all with four facet dimensions counted, 90 ms at
1M — is what an unfiltered "browse everything" landing page costs. Every refinement a visitor
applies makes it faster, not slower.

**Sorting by a field costs a little, and mostly at the tail.** Relevance ordering is free (Lucene is
already scoring); a field sort loads doc-values for the matching set.

#### The deep-paging wall

`page * pageSize` may not exceed `XpSearchOptions.MaxResultWindow` (10,000 by default); beyond it the
API answers `400`. That ceiling exists because Lucene ranks *every* document down to the requested
rank, so page 500 does 500 pages of work and throws away 499 of them. At a million documents, page 1
of a match-all costs 90 ms and page 500 costs 95 ms — the cost is in reaching rank 10,000, and it is
paid on both.

Raising `MaxResultWindow` raises that cost linearly and buys nothing a visitor wants: nobody
paginates to result 20,000. If you have a use case that walks the whole result set (an export, a
sitemap, a migration), page it by a filter — a date range, a section — rather than by depth.

#### Typo tolerance costs what the extra terms cost

Turning on typo tolerance for an index (see [Relevance tuning → Typo
tolerance](relevance-tuning.md#typo-tolerance)) takes a search from 2.6 ms to 8.2 ms at 10,000
documents — roughly 3× an exact search, at every corpus size. That is the honest price of asking the
index for every near spelling of every query term instead of just the one the visitor typed.

Most of it is the search itself: with highlighting turned off, the same fuzzy search costs 4.7 ms at
10k against 1.9 ms for an exact one. Highlighting adds ~3.5 ms on top, against ~0.7 ms for an exact
query, because there are simply more terms to mark.

The original PF-1 run measured this row at **135 ms**, and that number is still quoted in the PF-1
changelog entry and in `docs/internal/perf-results-2026-09-01.md`. It was a defect, not a property of
fuzzy search: the highlighter re-expanded the near-spelling query for every result and every
highlighted field. The query is now expanded once per request, and the row is the 8.2 ms above.

What that means in practice:

- Typo tolerance plus highlighting is single-digit milliseconds at website sizes. It is a ranking and
  recall decision now, not a latency decision.
- If you do not render snippets, omit `highlight` from the request and typo tolerance is a little
  cheaper still.
- Autocomplete does not go through this path and is unaffected.
- The [response cache](search-api.md) hides the cost from repeat searches — and misspellings repeat,
  since a popular typo is still a popular query.

This is a known cost, not a defect being hidden: it is recorded in the results document with the
isolating measurement next to it.

#### The response cache

An identical request inside `XpSearchOptions.CacheTtl` (60 seconds by default) is answered in
essentially zero time — it never reaches Lucene. That is the row at the bottom of the table, and it
is why the rest of the page is deliberately measured *without* it.

Raise the TTL and you trade freshness for load: a longer TTL means an edited page takes longer to
appear in results. The cache is per index and is evicted when the index is written to, so the trade
is smaller than it sounds on a site that publishes rarely. It does not help the first visitor of
each distinct query, which is exactly the traffic the numbers above describe.

### Index build and disk

| Corpus | Build + commit | Throughput | Index size | Bytes per document | Cold reader open |
|---|---|---|---|---|---|
| 10,000 | 3.9 s | 2,600 docs/s | 12.9 MB | ~1.3 KB | 16 ms |
| 100,000 | 19.4 s | 5,200 docs/s | 128.5 MB | ~1.3 KB | 49 ms |
| 1,000,000 | 189 s | 5,300 docs/s | 1,244 MB | ~1.3 KB | 36 ms |

Index size is close to linear in content volume — about 1.3 KB per document for a synthetic document
with a title, a 50-500 word body, two taxonomy dimensions and a number. Your documents will differ;
scale by the size of your stored fields, since storing a field for highlighting is what costs the
space.

**Treat the build times as a floor.** The bench generates its documents in memory. A real rebuild
reads content items out of the Xperience database, resolves linked content and URLs, and pushes
through the Lucene integration's queue — all of which is slower than `StringBuilder`. What the
measurement does tell you is the shape: a full rebuild of a million documents is *minutes*, not
seconds and not hours, and it is dominated by content retrieval rather than by Lucene.

Plan the rebuild window accordingly. A rebuild is a full re-feed: while it runs, the index is being
rewritten, and one writer owns it. On a site of a few thousand documents this is seconds and nobody
notices. At a million it is a maintenance activity you schedule.

### When a local Lucene index stops being the right answer

Everything above is a local Lucene index: a set of files, written by one process, read by the same
process that renders your pages. That design is why this library is cheap to run — no service to
operate, no per-query bill, no data leaving your infrastructure. It also has edges that no amount of
tuning moves, and you should know where they are before you build on it.

**One writer.** Lucene takes a write lock on the index directory. One process indexes at a time.
Xperience's Lucene integration serializes indexing through a background queue, which is the right
answer for content changes — but it means indexing throughput is a single machine's throughput, and
a rebuild cannot be parallelized across servers.

**Search runs inside your web application.** There is no separate search tier. A slow, unfiltered,
heavily-faceted query at a million documents consumes the same CPU your pages are rendered on. You
cannot scale query capacity independently of the web tier — you scale the whole application or you
do not scale.

**Multi-instance deployments need shared storage, and shared storage is slower.** The Lucene
integration writes its index through Xperience's `CMS.IO` layer, so the directory can live on
[Azure Blob storage or Amazon
S3](https://docs.kentico.com/documentation/developers-and-admins/api/files-api-and-cms-io/file-system-providers)
and be shared across instances. That solves *"every instance has its own stale copy"*. It does not
make the numbers above true: segment reads and reader opens become network round-trips, and the
figures on this page are local-disk figures. Measure your own deployment before assuming otherwise.

**Freshness is per process.** A reader sees the index as of the last time it was opened. The
integration refreshes it, and the response cache sits on top with its own TTL, so "how long until an
edit shows up in search" is the sum of two intervals, not an instant.

**No sharding, no distributed faceting.** One index lives on one machine's disk and is counted by one
machine's memory. There is no query that fans out across nodes and merges. The index has to fit, and
the machine has to be able to open it.

**No replication or failover for the index itself.** Lose the disk and you rebuild from the database.
The content is safe; the index is derived data with a rebuild window (see above).

#### Roughly where the line is

Based on what is measured above and what is architectural:

- **Under ~100,000 documents:** you are nowhere near any of these edges. Single-digit-millisecond
  searches, a 128 MB index, a rebuild measured in seconds. This is where essentially every Xperience
  website channel sits, and there is no performance argument for anything else.
- **Around 1,000,000 documents:** still works, and works well — but you are now budgeting. A
  faceted browse page costs ~90 ms of your web server's CPU per uncached request, the index is over
  a gigabyte on the app server's disk, and a rebuild is a scheduled activity. Plan cache TTLs and
  rebuild windows deliberately.
- **Beyond that, or before that if any of the following are true:** the line is architectural, not
  numerical. Consider a hosted or clustered engine when you need **high availability of search
  itself** (search must survive losing the app server), **query capacity that scales separately from
  the web tier**, **an index too large for one machine**, **near-real-time indexing at high write
  volume**, or **search shared across several applications** that are not this Xperience instance.

#### What the step up looks like

A hosted or clustered search engine — Elasticsearch or OpenSearch (self-hosted or managed),
Azure AI Search, Algolia and similar services. What you buy is replication, sharding, a query tier
you scale on its own, and someone else's operational burden. What you pay is a service to run or a
bill per query, your content leaving your infrastructure, and a network hop on every search that
local Lucene does not have — several of the millisecond figures on this page are *faster* than a
round trip to a hosted service would be.

Neither direction deserves marketing. Local Lucene is not a toy: a million documents answered in
tens of milliseconds is a real search engine. And a hosted engine is not overkill for everyone: if
search going down is an incident, you need something with replicas. Pick on the architectural
requirements, not on the latency table — by the time latency is your problem, the other constraints
already were.

If you do move, the search contract this library exposes is deliberately its own (see
[Migrating from Algolia](migrating-from-algolia.md) for how a contract-level migration reads): the
widgets, the JSON API and the admin tuning are the parts your site is built on, and they are not
Lucene-specific.
