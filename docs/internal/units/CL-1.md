# Unit CL-1 — §10.5 typed ingestion clients (C# + Node)

PAUL plan 04-01: spec §10.5 "Client convenience layers" — two thin clients over the ingestion
API so integrators stop hand-rolling HTTP. Both handle batching, retry with exponential
backoff, and partial-failure reporting. Library unit, worktree branch `unit/cl-1` (already
created for you). Read `docs/internal/agent-primer.md` first.

Ground truth (verify, then build against it):

- The ingestion API is LIVE and frozen: routes in
  `src/XpSearch.Ingestion/Contract/IngestionContractConstants.cs` (upsert, patch, delete,
  batch delete, scoped clear, rebuild, status, index list; bearer API key; per-key rate
  limit), wire types in `contract/xpsearch-ingestion.schema.json`, generated C# in
  `XpSearch.Ingestion/Contract/Generated/` and TS in
  `Client/src/contract/ingestion-generated.ts`. The clients speak THIS contract — no new
  endpoints, no contract changes.
- `src/XpSearch.Ingestion/Clients/` is an empty placeholder directory — but note the
  constraint below before putting anything there.
- **The C# client cannot live in XpSearch.Ingestion**: that project references
  `Kentico.Xperience.Core` + `Lucene.Net`, and §10.5's C# client is for OTHER .NET apps (a
  PIM sync job, a console importer) that must not drag Kentico in.
- Naming is OURS, not Algolia's ([[ADR-0010]] / the owned-contract rule): no `saveObjects`,
  no `initIndex`. Verbs mirror the endpoints: upsert / patch / delete / deleteMany / clear /
  rebuild / status, plus a root-level index list.
- Read the actual server batch caps (documents-per-request and body-size limits in the
  Ingestion options/validation) and the real auth header format before hardcoding client
  defaults — the spec says 1,000 docs / 10MB and `Authorization: Bearer`, the code wins.

## 1. C# client — new project `XpSearch.Client`

- New `src/XpSearch.Client/XpSearch.Client.csproj` (NuGet id
  `XperienceCommunity.Search.Client` per the branding scheme — follow the shared props in
  `libraries/`; actual publishing is Phase 8). Dependencies: the BCL only (`HttpClient`,
  `System.Net.Http.Json`, `System.Text.Json`). NO Kentico, NO Lucene, NO Polly — backoff is
  a few lines, not a dependency.
- Contract DTOs: extend the contract generator (`Client/scripts/contract.mjs`) to emit a
  SECOND C# copy of the ingestion types into `XpSearch.Client` under its own namespace —
  same emitter, one more output target, covered by `contract:check`. Wire DTOs never cross
  assemblies, so the duplication is safe and keeps the client standalone. STOP clause: if
  this needs more than adding an output target (generator restructuring), stop and report.
- Shape (spec §10.5 sketch, our verbs):
  `new XpSearchIngestionClient(baseUrl, apiKey)` (plus a ctor taking `HttpClient` for
  IHttpClientFactory users) → `.Index("products")` returns an index-scoped view with
  `UpsertAsync(docs)`, `PatchAsync(id, fields)`, `DeleteAsync(id)`, `DeleteManyAsync(...)`,
  `ClearAsync(source?)`, `RebuildAsync()`, `GetStatusAsync()`; root has `ListIndexesAsync()`.
- **Batching**: `UpsertAsync` splits any enumerable into request-sized chunks under BOTH
  server caps (count and serialized size) and reports one aggregated result: totals for
  indexed/failed, all per-document errors concatenated, per-batch task ids. A mid-run
  transport failure surfaces which documents were already accepted (aggregate-so-far in the
  exception or result — pick one, document it, pin it).
- **Retry**: exponential backoff with jitter on 408/429/5xx and transport exceptions;
  honour `Retry-After` when present; never retry other 4xx (a validation 400 retried is the
  same 400 slower); bounded attempts (default ~4, configurable); retried POSTs are safe
  because upsert is idempotent by contract (`objectID` upsert semantics — say so in the
  docs). Errors surface as a typed exception carrying the Problem Details body.
- Tests: fake `HttpMessageHandler` — batch splitting at both caps, aggregation, backoff
  schedule (inject the delay), Retry-After, no-retry-on-400, partial failure, auth header.
  Put them in a new `tests/XpSearch.Client.Tests` (the project is Kentico-free; its tests
  should prove that by referencing nothing else) — wire it into the solution.

## 2. Node client — subpath export of the existing npm package

- New module in `Client/src/` exported as `@xperience-community/xperience-search/ingestion`
  (PK-1's subpath pattern; add it to the exports map + package checks). It must import ONLY
  the generated ingestion contract types and its own code — no widget/browser modules; the
  existing tree-shake/package-check guards should prove the isolation (extend them if they
  only cover widget subpaths).
- `createIngestionClient({ endpoint, apiKey, fetchFn? })` → same verb set as the C# client,
  same batching/retry/aggregation semantics (share the numbers: caps, attempts, backoff
  base — document them in one place per language). Runs on Node 18+'s global fetch; the
  `fetchFn` seam is for tests and exotic runtimes.
- **The API key is a server-side secret**: the guide and the module's jsdoc both say this
  client is for build pipelines / sync jobs / server code, never for browser bundles. Do
  not export it from the package's root entry — subpath only, so a widget bundle can't
  pick it up by accident.
- Tests (vitest, stubbed fetch): mirror the C# matrix. Plus ONE e2e happy path against the
  mock server — extend `Client/mock/server.ts` with the ingestion routes it needs (upsert +
  one failure case is enough; the mock is a test double, not a full server).

## 3. Docs + bookkeeping

- Extend the ingestion guide (`docs/guides/` — find the one DOC-1 shipped) with a "Typed
  clients" section: both languages, install/import, the batching+retry behavior, the
  partial-failure story, the browser-secret warning, and when to use `IXpSearchIndexer`
  (§10.6, already shipped) instead — in-process code should skip HTTP entirely.
- `javascript-bundler-setup.md`: one note that the ingestion subpath exists and is
  Node-only.
- CHANGELOG (Added: client package + npm subpath). KNOWN-LIMITATIONS only if you cut a real
  corner. Host-pass checklist: new section numbered after the current last item (§T ends at
  103 on main — verify in YOUR worktree): a real upsert→search→clear round trip against the
  running host with each client (the lead runs these; keep the items concrete: what to run,
  what to see).
- Commit this spec file with the unit (copy from `docs/internal/units/CL-1.md` on main if
  your worktree predates it).

## 4. Verification

- All five C# suites green (the four existing + the new XpSearch.Client.Tests); solution
  builds (`dotnet build` from the library root).
- JS: `npm run build`, full test suite, `contract:check` (now covering the second C#
  emission), package checks (exports walk + tree-shake guards pass with the new subpath),
  `docs:check`.
- Report the exact commands the lead should run for the host round trip (including the API
  key gotcha: the dev key's plaintext is only readable at creation — see
  `src/Search/README.md`, host is read-only reference).

## Constraints

- No new dependencies in ANY project (BCL/fetch only). No contract or endpoint changes.
  The C# client project must compile with zero Kentico/Lucene references — that is the
  point of it. Kentico docs MCP for any Xperience question. Never touch
  `src/Components/Widgets/CardWidget/`. Host is out of scope entirely.
