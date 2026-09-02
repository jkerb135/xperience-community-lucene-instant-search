# Unit PF-1 — §12 performance pass: benchmark the real pipeline, publish honest sizing docs

PAUL plan 04-03, the last Phase 4 plan. Spec §12's performance bullet: "benchmark 10k / 100k /
1M document corpora; document the point at which Lucene local indexes stop being the right
answer and be honest about it in the docs." Library unit, worktree branch `unit/pf-1` (already
created for you). Read `docs/internal/agent-primer.md` first.

Ground truth:

- The roadmap's rider "remove `SearchTimingStage` (slot 99)" is ALREADY DONE — TM-1 (5183f3e,
  2026-08-22) moved timing onto `SearchContext.Elapsed`. Nothing to remove; do not go looking.
- SP-1's spike harness survives at `tests/XpSearch.FacetSpike` and is the measurement pattern
  to follow: environment table, Release-only, warmup, median-of-runs with `[min-max]`,
  nearest-rank percentiles (`Measure.cs:17` — a reported value is always a measured value),
  results as a dated markdown doc (`docs/internal/spike-faceting-results.md`). It benchmarked
  RAW Lucene backends at 10k/100k; PF-1 benchmarks the REAL product pipeline and adds 1M.
- `tests/XpSearch.Core.Tests/Fixtures/TestSearchIndex.cs` builds real Lucene indexes with no
  Kentico runtime — the seam the bench should stand on. `tests/performance/corpora/` exists,
  empty, from early scaffolding — use it or delete it, don't leave it dangling.

## 1. The bench tool

New console `tests/XpSearch.Bench` (FacetSpike's structure, fresh code — do NOT entangle the
frozen SP-1 record; copying the small `Measure` pattern is fine). References Core + the Lucene
packages only. STOP clause: if driving the real `ISearchPipeline` Kentico-free needs more than
the fakes/seams Core.Tests already demonstrates, stop and report the design rather than
building a fake farm.

- **Corpora:** synthetic 10k / 100k / 1,000,000 documents, deterministic (fixed seed),
  realistic shape: a short title, a body of varying length (say 50–500 words, skewed short),
  two facet dimensions with skewed cardinality (one ~10 values, one ~1,000), a numeric field,
  a language field, ~2% of documents sharing high-frequency terms so match counts vary.
  Corpus build is part of the run (`--sizes 10k,100k,1m` style switches; 1M must be buildable
  in minutes, and the tool prints build throughput as a result, not an obstacle).
- **The system under test is the pipeline, not Lucene:** the full stage chain as `AddXpSearch`
  composes it (normalize → rules/synonyms/weights present but realistically light: a handful
  of synonyms, one boost rule, default weights → build → execute → facets → highlight →
  project). Cache: measure UNCACHED latencies as the headline (unique query texts per
  iteration — a 60s-TTL cache hit tells you nothing about the engine) plus ONE cache-hit row
  for contrast.
- **Workloads** (per corpus size, p50/p95 + median[min-max]): match-all with facets;
  single-term; two-term OR; term + facet filter + numeric range; sorted-by-field; page 1 vs a
  deep page near the 10k result-window ceiling; suggest prefix (Documents mode); the same
  single-term query with FZ-1 fuzzy ON (its per-term expansion is the interesting cost);
  index build + commit time, on-disk size, first reader open (the spike's table shape).
- Output: writes `docs/internal/perf-results-<date>.md` itself (env table incl. CPU/RAM/OS/
  .NET/Lucene version, the caveat that disk type is not discoverable — copy the spike's
  honesty), and prints it. Runs land in the repo; the doc is the committed artifact.

## 2. The public docs — the honest part

New guide `docs/guides/performance-and-sizing.md` ([[feedback-docs-wiki-ready]]: numbers come
from a run you actually made, environment stated):

- The measured tables, summarized readably (per corpus size: typical and p95 latency for a
  faceted text query, suggest, fuzzy overhead, build time, index size).
- What the numbers mean operationally: where latency is flat vs where it grows; the
  MaxResultWindow deep-paging wall; fuzzy's cost; the rebuild window at 1M (a rebuild is a
  full re-feed — say how long the synthetic 1M took and that real content loads from the
  database, so treat it as a floor); cache TTL's role.
- **The honesty section the spec demands** — "when a local Lucene index stops being the right
  answer": single-process file index (no HA/replication; one writer), rebuild-window
  downtime characteristics, index size on one disk, no distributed faceting, near-real-time
  visibility is per-process. Name the shape of the step up (a hosted/clustered engine —
  Elasticsearch/OpenSearch/Algolia-class services) without FUD in either direction: most
  Xperience sites are nowhere near the line; state roughly where the line is based on what
  you measured and what is architectural.
- Cross-link from `docs/guides/search-api.md` (MaxResultWindow/paging mentions) and the
  README project table if it lists guides.

## 3. Bookkeeping

- CHANGELOG (Added: bench tool + guide). agent-primer: how to run the bench (and that it is
  minutes-long and Release-only — not part of the test suites). `docs:check` clean.
- NO host-pass checklist section expected (nothing here is a browser/host item) — add one
  only if you genuinely produce a host-verifiable claim.
- If the bench surfaces a real performance defect (something pathological, not "1M is slower
  than 10k"): measure it, report it precisely, do NOT fix it in this unit.
- All six C# suites green (five test suites — the bench is a tool, not a test — plus prove
  the bench itself builds and a 10k smoke run completes); JS untouched.
- Commit this spec with the unit (copy from `docs/internal/units/PF-1.md` on main if your
  worktree predates it). The full 10k/100k/1M result doc must come from a real full run on
  this machine — budget the minutes for it.

## Constraints

- No new dependencies (the harness is Stopwatch + math, per the spike precedent — no
  BenchmarkDotNet). No library code changes (STOP clause above; the unit is a tool + docs).
  Kentico docs MCP for any Xperience question. Never touch
  `src/Components/Widgets/CardWidget/`. Host is out of scope.
