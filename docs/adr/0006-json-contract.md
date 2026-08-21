# ADR-0006: the JSON search contract

- **Status:** accepted
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
   script, not a shell `diff`, so it runs on Windows agents. The generated C# opens with
   `#pragma warning disable CS1591`: quicktype's serializer plumbing carries no XML docs and the build
   treats warnings as errors.
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
7. **`Hit.url` is root-relative or absolute, never `~/…`.** Xperience's URL retriever returns the
   app-relative form, which a browser cannot resolve and a JSON consumer has no way to expand. Resolving it
   is the server's job, once, before it reaches the wire.
8. **No validation keywords that generate throwing converters.** `minLength` on `index` made quicktype
   emit a converter that throws a bare `Exception` mid-deserialization, which would surface as a 500
   instead of a 400. Required-ness and non-emptiness are enforced by the endpoint and answered as Problem
   Details; the schema documents them in prose.

## Evidence

quicktype's TypeScript backend honours `"additionalProperties": true` faithfully and its C# backend does
not. Verified by generating both from this schema:

```ts
export interface Hit {
    _highlights?: { [key: string]: string };
    _rankingInfo?: RankingInfo;
    _score?: number;
    objectID: string;
    url?: string;
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
    [JsonPropertyName("url")] public string? Url { get; set; }
    // no [JsonExtensionData] - the open half is missing
}
```

So the fallback the brief describes was needed on the C# side only, and in its cheapest form: quicktype
emits `partial` classes, so the open half is hand-written next to the generated file in
`Contract/Hit.cs` and nothing is post-processed or excluded from generation:

```csharp
public partial class Hit
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Attributes { get; set; } = [];
}
```

`contract:check` fails if the generated `Hit` stops being `partial`, if `Contract/Hit.cs` loses
`[JsonExtensionData]`, if the TypeScript `Hit` loses its index signature, or if either generated `Hit`
stops carrying every property name in the schema's `Hit.properties`. The round-trip tests then prove the
result on the spec's own payload: `title` and `summary` survive C# deserialize/serialize through the
extension data, and the TypeScript fixture only type-checks because the interface is open.

## Consequences

- Adding a field is a schema edit plus `npm run contract:gen`; the C# and TS sides cannot disagree, and a
  reviewer sees the generated diff in the same commit.
- Changing or removing a field breaks both packages at once, which is the point: it is a semver-major,
  coordinated event, and the CHANGELOG already says so.
- The generated files must not be hand-edited. CI must run `npm run contract:check`.
- The generator needs Node in the toolchain of anyone changing the contract. Consumers of either package
  need nothing.
- quicktype emits one placeholder top-level type (`XpSearchContract`) whose only job is to pull the five
  wire types into the output. It is never sent on the wire; its properties say so in their docs.
- C# hits pay a `Dictionary<string, JsonElement>` per hit for the open attributes, and callers read them
  as `JsonElement` rather than as typed members. That is the price of a contract whose attribute set is
  chosen per index at query time.
- `SearchResults`/`SearchState` in the widget SDK (§5.7) build on these generated types rather than
  re-declaring the wire shape.
