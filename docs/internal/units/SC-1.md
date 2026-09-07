# SC-1 — Public endpoint rate limiting + events endpoint hardening

**Status:** DISPATCHED 2026-09-07 (owner: "let's work through these" — GTM review security findings).
**Origin:** GTM review 2026-09-06, security audit: (Med) no rate limiting on `POST /api/xpsearch/query`,
`/suggest`, `/events` — only ingestion has a policy (`XpSearchIngestionServiceCollectionExtensions.cs`
`PartitionByKey`, fixed window 60/min per key, `RequireRateLimiting` on every ingestion route); (Med) `/events`
is anonymous and unthrottled, and its click rows drive `PopularityAggregator` / `PopularityBoostStage`, so a
visitor who replays a captured `queryId` with fabricated positions biases ranking and the query log.

Scope: `XpSearch.Core` only (endpoints, DI, analytics). No contract change (STOP if one seems needed —
`EventRequest` already carries `queryId`, `resultId`, `position`). No JS change.

## 1. Rate limiting for the three public routes
- Copy the ingestion pattern: `AddXpSearch()` registers a named policy (`XpSearchConstants.PublicRateLimitPolicy`
  = `"xpsearch-public"`) with `services.AddRateLimiter(...)` — additive to a limiter the host may already configure
  (ingestion proves `AddRateLimiter` composes; verify two `AddPolicy` calls coexist). Partition by remote IP
  (`HttpContext.Connection.RemoteIpAddress`, honouring forwarded headers ONLY if the host configured
  `UseForwardedHeaders` — do not parse `X-Forwarded-For` yourself), sliding window. Defaults on
  `XpSearchOptions`: `PublicRateLimitPermitsPerWindow` (default 120), `PublicRateLimitWindow` (default 1 min),
  `PublicRateLimitEnabled` (default TRUE). A visitor typing triggers a debounced request per keystroke pause plus
  suggests, so 120/min is generous for a human and tight for a script; say so in the XML docs.
- `MapXpSearch()` applies `.RequireRateLimiting(policy)` to all three routes when enabled. 429 carries
  `Retry-After` (the limiter does this; assert it). Rejected requests are NOT journaled and NOT cached.
- `UseXpSearch()` must call `app.UseRateLimiter()` exactly like `UseXpSearchIngestion()` documents/does — read
  that code; if ingestion leaves it to the host, do the same and make the docs say it in BOTH guides, once.
  Idempotence: the middleware may be registered twice by a host that also uses ingestion — verify ASP.NET tolerates
  it or guard with a marker in `app.Properties`.
- Per-process ceiling (same as ingestion's KNOWN-LIMITATIONS entry): one entry covering both, referencing the
  existing one; do not duplicate prose.

## 2. `/events` hardening
- **Only events for a queryId this server issued are accepted.** `ISearchEventSink`/`ActivitySearchEventSink`
  resolves `IQueryContextMap.Get(queryId)`; today a miss is tolerated (find where — audit cites
  `ActivitySearchEventSink.cs:57-66`). Change: a miss → the event is dropped (202 still returned — the caller
  cannot distinguish, by design; log at Debug). Because `QueryContextMap` is per process (30 min, 10k entries),
  a load-balanced site drops cross-node clicks — that is TODAY's behaviour for attribution anyway and unit
  WF-1 (in flight in parallel) is moving the map to a shared store; coordinate by keeping the check behind
  `IQueryContextMap` so WF-1's replacement inherits it. Say this in the spec-level comment, not in code.
- **Per-queryId budget:** at most `MaxEventsPerQuery` (default 20) accepted events per queryId, counted in the
  same map entry (extend `QueryContext` or a sibling counter — smallest change). Beyond it: dropped.
- **Position bound:** `position` must be within `[1, pageSize × page]` of the recorded query when the context
  knows the page size (extend `QueryContext` if it doesn't carry it; if the page size is unknown, cap at
  `MaxPageSize` of the index). Out-of-range → dropped.
- **`resultId` must belong to the query's index:** cheap check only — the id shape (`{guid}:{lang}` or an
  external id) is not verifiable without a lookup; skip this if it needs an index read (STOP-report instead).
- Everything above applies to click AND conversion events; the popularity aggregator reads only accepted rows,
  so no change there.

## 3. Verification
- Core.Tests: policy registered by `AddXpSearch` (resolve `RateLimiterOptions` and assert the named policy);
  a test host (WebApplicationFactory is NOT in the suite — use `TestServer` if already referenced, else the
  endpoint filter/limiter can be asserted via the `RequireRateLimiting` metadata on the endpoint data source);
  events: unknown queryId dropped, budget exhausted dropped, position out of range dropped, valid accepted —
  through `ISearchEventSink` with a fake map.
- Docs: `search-api.md` gains a "Rate limiting" section (defaults, how to change, disable, the `UseRateLimiter`
  line, per-process ceiling) and an "Events are validated" paragraph; `ingestion.md` cross-links instead of
  repeating. CHANGELOG `**Added (core):**` ×2 (`**Changed (core):**` for the event drops — behaviour change).
- Host pass row for the checklist: `§AC` — 130 quick requests → 429 with Retry-After; a replayed queryId beyond
  20 events stops moving the popularity score.
