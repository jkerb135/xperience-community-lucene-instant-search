# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

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

## `SuggestMode.QuerySuggestions` in `XpSearch.Core/Search/FederatedHitsSuggestService.cs`

- **Simplified:** spec §4.3 asks for two autocomplete modes. Only federated hits (a prefix match on the
  index's suggest field, `Title` by default) is implemented. An index configured for query suggestions gets
  an empty `suggestions` array and a logged warning.
- **Ceiling:** query suggestions need a store of previously issued queries and their frequencies, which is
  Phase 6 (spec §13.6). Until then the mode exists as configuration and does nothing, so a project that
  sets it silently loses autocomplete apart from the warning.
- **Upgrade path:** when the Phase 6 analytics store lands, add a `QuerySuggestionsSuggestService` and pick
  the implementation per index in `AddXpSearch`; the `SuggestMode` option and the `ISuggestService`
  interface are already the seam.

## `RankingInfo.AppliedBoosts` in `XpSearch.Core/Pipeline/Stages/ProjectResponseStage.cs`

- **Simplified:** `explain=true` returns `_rankingInfo` with the raw Lucene score as `baseScore`, the
  one-based position, and an always-empty `appliedBoosts`.
- **Ceiling:** the admin query tester (spec §8.4) can show why a hit scored what it scored, but not why it
  moved — because nothing moves it yet. `_score` and `baseScore` are therefore always identical.
- **Upgrade path:** the Phase 5 boost and pin/bury stages occupy `SearchStageOrder.BoostRules` (700) and
  `SearchStageOrder.PinnedAndBuried` (900); each appends its own description to the hit's `appliedBoosts`
  as it changes a score or a position.

## Language as a document field, in `XpSearch.Core/Pipeline/Stages/BuildQueryStage.cs`

- **Simplified:** a request's `language` becomes a term filter on `BaseDocumentProperties.LANGUAGE_NAME`.
  One index holds every language variant, which is how the Lucene integration indexes by default.
- **Ceiling:** every language shares one analyzer and one set of term statistics, so relevance scoring is
  blended across languages and language-specific stemming is impossible. This is **not** a decision on spec
  §13.2 (multilingual strategy); it is the current behaviour, chosen because it is what the integration
  gives us for free.
- **Upgrade path:** whatever §13.2 resolves to. If it becomes one index per language, `language` selects an
  index instead of filtering inside one, and the filter clause in `BuildQueryStage` is removed.

## Schema detection in `XpSearch.Core/Indexing/FormInfoContentTypeFieldSource.cs`

- **Simplified:** fields are detected from `DataClassInfo.ClassFormDefinition` via `FormInfo.GetFields`, and
  mapped by their field data type. Data types with no obvious search meaning (assets, references, booleans,
  GUIDs, XML) are dropped. Whether fields contributed by a reusable field schema appear in a content type's
  own class form definition was not confirmed against a live database, only assumed.
- **Ceiling:** if reusable field schema fields are *not* materialized into the class form definition, a
  content type that gets its taxonomy field from a schema — `IProductFields` in Dancing Goat — would have
  that field missing from the schema and therefore unfacetable, which is exactly the failure spec §4.5
  forbids. The mapping is also fixed: a project that wants a boolean indexed must override it by hand.
- **Upgrade path:** verify on a live instance; if schema fields are absent, additionally read each
  `FormInfo.GetFormSchema(...)` entry's definition and merge its fields. The
  `IContentTypeFieldSource` interface is the seam, so the fix is one implementation.

## Schema resolution per uncached request, in `XpSearch.Core/Pipeline/SearchPipeline.cs`

- **Simplified:** `IIndexSchemaProvider.GetSchemaAsync` is called on every request that misses the response
  cache, and its default implementation queries `CMS_Class` for each of the index's content types.
- **Ceiling:** a cold cache costs one class query per content type per search. With the default 60 second
  response TTL that is bounded, but a high-cardinality query mix keeps hitting it.
- **Upgrade path:** decorate `IIndexSchemaProvider` with an `IProgressiveCache` entry keyed by index name,
  dependent on `cms.class|all`, so a content type change still invalidates it.

## `FacetsConfig` accumulation in `XpSearch.Core/Indexing/XpSearchIndexingStrategy.cs`

- **Simplified:** the strategy registers a taxonomy dimension as multi-valued the first time it maps a
  document that has one, and `FacetsConfigFactory` returns that same accumulating instance. This works
  because the task processor maps documents before the client asks for the configuration and builds them.
- **Ceiling:** in a process that has never indexed anything, the configuration is empty. Querying still
  works — an unregistered dimension falls back to `FacetsConfig`'s defaults, which is what drill-down and
  counting need — but it means the configuration is not a static description of the index.
- **Upgrade path:** give the strategy the index's content types (through `IIndexContentTypeSource`) and
  register every taxonomy dimension up front in the constructor.

## Field renaming is not supported, in `XpSearch.Core/Indexing/XpSearchIndexingOptions.cs`

- **Simplified:** spec §4.5f asks for exclude, rename and boost. `Exclude` and `Configure` are implemented;
  `Configure` can change every flag and the boost, but `SchemaField.Name` is both the content type field the
  value is read from and the Lucene field it is written to, so changing it breaks the read. The guide says
  so instead of the code preventing it.
- **Ceiling:** a project that wants a shorter attribute name on the wire has to add a second field in an
  override of `MapToLuceneDocumentOrNull`, or rename in its own client code.
- **Upgrade path:** add a source-field name to `SchemaField` that defaults to `Name`, read values by the
  source name in the strategy, and index and project by `Name`.
