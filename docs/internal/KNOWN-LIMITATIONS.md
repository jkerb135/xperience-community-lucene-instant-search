# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

## `connectAutocomplete` and `connectHierarchicalMenu` in `XpSearch.Client/src/connectors.ts`

- **Simplified:** eight of the ten connectors spec 5.7 lists are published; these two are not exported at
  all. `connectAutocomplete` needs the `/suggest` semantics (query suggestions vs federated hits, and the
  stale-response and keyboard policy) that are still open decision 6 in the spec, and
  `connectHierarchicalMenu` needs hierarchical facet semantics, which depend on the faceting approach of
  ADR-0001. The transport half of autocomplete exists already: `SearchClient.suggest()`.
- **Ceiling:** the `autocomplete`, `hierarchicalMenu` and taxonomy-navigation widgets cannot be built
  yet, and a developer who needs either has to call `SearchClient.suggest()` and drive
  `helper.setQuery()` by hand.
- **Upgrade path:** add the two connector files once those decisions land; both are additive, so neither
  is a breaking change.

## `connectRange` in `XpSearch.Client/src/connectors/range.ts`

- **Simplified:** the control's bounds come from `widgetParams.min`/`max`, and `canRefine` is false
  without them, because the JSON contract carries no numeric facet statistics — there is nowhere for a
  server-computed min/max to arrive.
- **Ceiling:** a range slider over an unknown corpus has to be hand-configured, and its ends do not
  follow the current result set.
- **Upgrade path:** add facet statistics to `SearchResponse` (a contract change, so a coordinated event)
  and read them in the connector, keeping the params as an override.

## Default route mapping in `XpSearch.Client/src/routing.ts`

- **Simplified:** `defaultStateToRoute` owns the params `q`, `page` and `sort`, one param per facet
  attribute, and `<attribute>_<lt|lte|eq|gte|gt>` for numeric refinements. A facet attribute called `q`,
  `page`, `sort` or `price_lte` collides with the mapping and will not round-trip, and two instances with
  `routing: true` on one page fight over the same params.
- **Ceiling:** those attribute names, and multi-instance routing, need the
  `routing: { stateToRoute, routeToState }` escape hatch — public and tested, but hand-written.
- **Upgrade path:** add a documented `routing.prefix` option that namespaces every param (`s1_q`,
  `s1_tags`) when a second instance needs the URL too.

## `mock/server.ts` in `XpSearch.Client`

- **Simplified:** the mock matches query terms as lowercase substrings over title, body and tags, scores
  by term hits with a title bonus, and highlights by regex over the HTML-encoded snippet. It models the
  wire contract, not the Lucene pipeline: no analyzers, stemming, stopwords, synonyms or boost rules.
- **Ceiling:** relevance-ordering assertions written against it prove nothing about the real engine, and
  a UI tuned to its scores may look different against Lucene.
- **Upgrade path:** point the docs' examples and the widget tests at the real endpoint once
  `XpSearch.Core` serves it; keep the mock for offline development.

## `NoWarn NU5104` in `Directory.Build.props`

- **Simplified:** the packages are marked stable while depending, transitively, on the
  `Lucene.Net 4.8.0-beta00017` prerelease. `Kentico.Xperience.Lucene 15.0.5` pins that version, so there
  is no stable Lucene.Net to depend on; NU5104 ("a stable release of a package should not have a
  prerelease dependency") is suppressed for the whole library instead of being resolved.
- **Ceiling:** consumers see a prerelease package in their transitive dependency graph, and tooling that
  refuses prerelease dependencies (some corporate feed policies, `--no-prerelease` restore gates) will
  reject the package. The suppression is repo-wide for this library, so a *new*, genuinely wrong
  prerelease dependency would also go unnoticed.
- **Upgrade path:** drop the `NoWarn` when Kentico.Xperience.Lucene moves to a stable Lucene.Net release,
  and let the warning fail the build again.

## `Hit.Attributes` in `XpSearch.Core/Contract/Hit.cs`

- **Simplified:** `Hit` is generated from `contract/xpsearch-api.schema.json` like every other contract
  type, but quicktype's C# backend ignores the schema's `additionalProperties`, so the open half of the
  object — every non-reserved attribute a query retrieves — is hand-written as a `[JsonExtensionData]`
  property on a partial class next to the generated file. The TypeScript side needs no such help.
- **Ceiling:** one member of the contract exists in two places. If quicktype ever stops emitting `Hit` as
  a `partial class`, the hand-written half silently stops applying and hits lose their attributes; the
  `contract:check` script asserts against exactly that, plus the property names and the extension data
  attribute, so the failure is loud rather than silent. Reading an attribute in C# costs a dictionary
  lookup and a `JsonElement` unwrap.
- **Upgrade path:** delete `Contract/Hit.cs` and its assertions in `scripts/contract.mjs` if quicktype
  learns to emit `[JsonExtensionData]` for `additionalProperties`; otherwise leave it.

## `csharpEdits` in `XpSearch.Client/scripts/contract.mjs`

- **Simplified:** even with `--features attributes-only`, quicktype's C# output publishes types that are
  not part of the contract (the placeholder `XpSearchContract`, `DateOnlyConverter`, `TimeOnlyConverter`)
  and leaves the generated `EventTypeConverter` attached to nothing, so `EventType` would serialize as
  `0`/`1` instead of `"click"`/`"conversion"`. Rather than owning a C# emitter, the generator rewrites four
  anchor strings in the output: three `public` → `internal`, and one `[JsonConverter]` attribute plus a
  scoped `#pragma warning disable CS1591` on the enum.
- **Ceiling:** string surgery on generated code. A quicktype release that renames or reformats any of the
  four anchors breaks the edit — loudly, because each edit throws when its anchor is absent, and
  `Contract_Namespace_Exports_Only_The_Contract_Types` fails if a non-contract type reaches the public API.
  It also means the checked-in C# is not byte-identical to raw quicktype output.
- **Upgrade path:** drop an edit as soon as quicktype offers the behaviour directly (an option to suppress
  the helper converters, or `--features types-only`); or, if the list ever grows past a handful, generate
  the C# from the schema with a small emitter of our own instead.
