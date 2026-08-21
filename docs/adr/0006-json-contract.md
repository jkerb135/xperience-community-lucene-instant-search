# ADR-0006: the JSON search contract

- **Status:** superseded by ADR-0010 (owned contract, 2026-08-21) — the Algolia-shaped wire contract described here is replaced; versioning header, Problem Details, schema-as-source and quicktype generation carry over
- **Date:** 2026-08-21
- **Spec reference:** §4.2, §4.3, §5.7, §8.4, §9.1

## Context

The JSON payloads of `/api/xpsearch/query`, `/suggest` and `/events` are the seam between every C# and
every TypeScript workstream in this product, and between us and third-party widget authors (§5.7). Two
languages have to agree on the same field names, casing and optionality, and stay agreed while eight
phases of work land in parallel. The spec fixes the shape (§4.2) but not how it is expressed, versioned
or kept in sync.

Constraints: names are Algolia's, casing is mixed (`objectID`, `nbHits`, `_score`) and cannot be produced
by a naming policy; a hit is an open object whose non-reserved attributes are decided per index; the
library targets `net8.0` with `TreatWarningsAsErrors` and `GenerateDocumentationFile`, so every public
member needs an XML doc.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Hand-write DTOs twice (C# + TS) | No tooling, no build step | Two sources of truth; drift is silent and only shows up as a runtime 400 |
| C# DTOs as the source, generate TS from them | One language owns it; refactors flow through | Needs a reflection/Roslyn generator we would own; the JS package depends on a .NET build |
| OpenAPI document as the source | Standard; Swagger UI for free | Describes endpoints we have not written yet; the type generators still go through JSON Schema |
| **JSON Schema as the source, quicktype for both languages** | Language-neutral single file; descriptions become XML docs and TSDoc; a checked-in generator with a CI diff guard | quicktype's C# backend ignores `additionalProperties`; a wrapper top-level type is emitted |
| Version in the route (`/v1/query`) | Visible; two versions can run side by side | The spec's routes have no version segment and the owner froze them |
| **Version in a response header** | Routes stay as specified; the client can assert compatibility | Not discoverable from the URL |

## Decision

1. **`contract/xpsearch-api.schema.json` (JSON Schema draft 2020-12) is the single source of truth.**
   quicktype 26.0.0 accepts it as `--src-lang schema`. Every property carries a `description`, which
   becomes the XML doc on the C# property and the TSDoc on the TypeScript member — the schema is the
   contract documentation, not a parallel copy of it.
2. **Both outputs are generated and checked in.** `npm run contract:gen` writes
   `XpSearch.Core/Contract/Generated/XpSearchContract.g.cs` and `XpSearch.Client/src/contract/generated.ts`;
   `npm run contract:check` regenerates into a temp directory and fails on any drift. The check is a Node
   script, not a shell `diff`, so it runs on Windows agents.
3. **The API version travels as a response header, `X-XpSearch-Api-Version: 1`.** Routes stay exactly as
   §4.2 and §4.3 write them. The contract version is the semver major of `YourCo.Xperience.Search.Core`
   and of `@yourco/xperience-search`, which are released together. The value and the three routes are
   hand-written constants (`ContractConstants`, `constants.ts`), not generated, and a test asserts them.
4. **Errors are RFC 9457 Problem Details** (`application/problem+json`), ASP.NET Core's native
   `ProblemDetails` — see <https://learn.microsoft.com/aspnet/core/web-api/handle-errors>. The schema does
   not redefine the shape; the guide shows a 400 example.
5. **`explain` is part of `SearchRequest`** as a boolean defaulting to `false`. The spec's response
   documents `_rankingInfo` as present "only when explain=true" (§4.2) and §8.4 backs the query tester
   with "the `explain=true` flag on the search endpoint", but the request sample never lists it. This is
   spec-implied, not an invention: without it the documented response member is unreachable.
6. **`Hit` stays an open object** in both languages (see below).
7. **`url` is an attribute of a hit, not a member of it.** §4.2 lists `url` inside
   `attributesToRetrieve` next to `title`, `summary` and `image`, so it is retrieved exactly like them and
   is absent from a hit whose index projects no link. Naming it in `Hit.properties` would have made
   `hit.url` typed while `hit.title` stayed `unknown`, an exception with no rule behind it — and the freeze
   would have made it permanent. Whatever attribute carries a link is root-relative or absolute, never the
   app-relative `~/…` form Xperience's URL retrievers return: that form is one a browser cannot resolve and
   a JSON consumer has no way to expand, so resolving it is the server's job, once, before it reaches the
   wire. The rule lives in the `Hit` description, and on `Suggestion.url`, which *is* a contract member.
   Typed access to individual attributes belongs to the client's `Hit<TItem>` wrapper, not to the contract.
8. **No validation keywords that generate throwing converters.** `minLength` on `index` made quicktype
   emit a converter that throws a bare `Exception` mid-deserialization, which would surface as a 500
   instead of a 400. Required-ness and non-emptiness are enforced by the endpoint and answered as Problem
   Details; the schema documents them in prose.
9. **The package's public API is the contract types and nothing else.** `XpSearch.Core` is published to
   agencies, so `--features attributes-only` drops quicktype's `FromJson`/`ToJson`/`Converter.Settings`
   plumbing, and four asserted edits in `scripts/contract.mjs` handle what it still emits as public: the
   placeholder top-level type and the unused `DateOnlyConverter`/`TimeOnlyConverter` become `internal`,
   and `[JsonConverter(typeof(EventTypeConverter))]` is attached to the `EventType` enum. Each edit throws
   if quicktype stops emitting its anchor, and `Contract_Namespace_Exports_Only_The_Contract_Types`
   asserts the resulting exported-type set mechanically. With the plumbing gone there is no file-level
   `#pragma warning disable CS1591`: every emitted member carries its schema description, except the two
   `EventType` members, which JSON Schema cannot describe individually and which are covered by a pragma
   scoped to the enum alone.

## Evidence

quicktype's TypeScript backend honours `"additionalProperties": true` faithfully and its C# backend does
not. Verified by generating both from this schema:

```ts
export interface Hit {
    _highlights?: { [key: string]: string };
    _rankingInfo?: RankingInfo;
    _score?: number;
    objectID: string;
    [property: string]: unknown;      // <- the open half
}
```

```csharp
public partial class Hit
{
    [JsonPropertyName("_highlights")] public Dictionary<string, string>? Highlights { get; set; }
    [JsonPropertyName("_rankingInfo")] public RankingInfo? RankingInfo { get; set; }
    [JsonPropertyName("_score")] public double? Score { get; set; }
    [JsonPropertyName("objectID")] public string ObjectId { get; set; }
    // no [JsonExtensionData] - the open half is missing
}
```

So the fallback the brief describes was needed on the C# side only, and in its cheapest form: quicktype
emits `partial` classes, so the open half is hand-written next to the generated file in
`Contract/Hit.cs`, and no type has to be excluded from generation:

```csharp
public partial class Hit
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Attributes { get; set; } = [];
}
```

`contract:check` fails if the generated `Hit` stops being `partial`, if `Contract/Hit.cs` loses
`[JsonExtensionData]`, if the TypeScript `Hit` loses its index signature, or if either generated `Hit`
declares a property set different from the schema's `Hit.properties`. The round-trip tests then prove the
result on the spec's own payload: `title`, `url` and `summary` survive C# deserialize/serialize through
the extension data, and the TypeScript fixture only type-checks because the interface is open.

On the public surface, `--features attributes-only` removes `FromJson`, the `Serialize` extension class
and `Converter.Settings`, but still emits `public partial class XpSearchContract` (the placeholder),
`public class DateOnlyConverter` and `public class TimeOnlyConverter`, and it emits `EventTypeConverter` —
which maps `Click`/`Conversion` to `"click"`/`"conversion"` — without attaching it to anything, so a
default `JsonSerializer` would have written the enum as `0`/`1`. Hence decision 9's four edits. The
resulting exported types are exactly:

```
ContractConstants, EventRequest, EventType, HighlightOptions, Hit,
RankingInfo, SearchRequest, SearchResponse, SuggestRequest, SuggestResponse, Suggestion
```

asserted by `Contract_Namespace_Exports_Only_The_Contract_Types`, with
`EventRequest_Round_Trips_With_Lower_Case_Event_Type` proving `{"eventType":"click"}` survives a round
trip through `EventType.Click` with no serializer options at the call site.

## Consequences

- Adding a field is a schema edit plus `npm run contract:gen`; the C# and TS sides cannot disagree, and a
  reviewer sees the generated diff in the same commit.
- Changing or removing a field breaks both packages at once, which is the point: it is a semver-major,
  coordinated event, and the CHANGELOG already says so.
- The generated files must not be hand-edited. CI must run `npm run contract:check`.
- The generator needs Node in the toolchain of anyone changing the contract. Consumers of either package
  need nothing.
- quicktype emits one placeholder top-level type (`XpSearchContract`) whose only job is to pull the five
  wire types into the output. It is never sent on the wire, and it is rewritten to `internal` in C# so it
  does not appear in the package's API.
- The generated C# is not byte-for-byte quicktype output: four small, asserted edits run over it. Anyone
  reading the generated file sees the result, and `contract:check` reproduces it exactly.
- C# hits pay a `Dictionary<string, JsonElement>` per hit for the open attributes, and callers read them
  as `JsonElement` rather than as typed members. That is the price of a contract whose attribute set is
  chosen per index at query time.
- `SearchResults`/`SearchState` in the widget SDK (§5.7) build on these generated types rather than
  re-declaring the wire shape.
