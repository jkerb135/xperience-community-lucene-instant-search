# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

## `withSuggestions` and `withCategoryTree` in `XpSearch.Client/src/behaviors.ts`

- **Simplified:** eight of the ten behaviours spec 5.7 lists are published; these two are not exported at
  all. `withSuggestions` needs the `/suggest` semantics (query suggestions vs document suggestions, and
  the stale-response and keyboard policy) that are still open decision 6 in the spec, and
  `withCategoryTree` needs hierarchical facet semantics, which depend on the faceting approach of
  ADR-0001. The transport half of autocomplete exists already: `SearchClient.suggest()`.
- **Ceiling:** the `suggestions`, `categoryTree` and taxonomy-navigation widgets cannot be built
  yet, and a developer who needs either has to call `SearchClient.suggest()` and drive
  `actions.setQuery()` by hand.
- **Upgrade path:** add the two behaviour files once those decisions land; both are additive, so neither
  is a breaking change.

## `searchable` in `XpSearch.Client/src/widgets/facetList.ts`

- **Simplified:** `searchable: true` renders a facet-search input that filters the values **already
  rendered**, in the browser, by case-insensitive substring. There is no facet-search endpoint in the
  JSON contract (there is no facet-value search route), so there is nothing server-side to call.
- **Ceiling:** a visitor cannot find a value that falls outside `limit`/`showMoreLimit`, or one the
  current query returned no documents for. On an attribute with hundreds of values the control looks
  like it is broken.
- **Upgrade path:** add a facet-search route to the contract (a coordinated contract change), give
  `withFacetList` a `searchFacetValues` render-state member, and have the widget call it instead of
  filtering locally; the markup and the option name do not change.

## The loading template in `XpSearch.Client/src/widgets/results.ts`

- **Simplified:** `templates.loading` (and the default skeleton rows) render only while a **first**
  search is in flight *and* a render happens during it — in practice, once the instance's stall
  threshold (`stalledSearchDelayMs`, default 200 ms) has elapsed. Later searches keep the previous
  results on screen with `aria-busy="true"` and the `--loading` modifier instead of blanking them.
- **Ceiling:** a fast first response never shows the skeletons at all (deliberate — no flash), and a
  page configured with `searchOnInitialLoad: false` renders an empty results area rather than a
  placeholder until the visitor searches.
- **Upgrade path:** if a project wants a placeholder before the first search, pass a
  `templates.empty` that reads well as an idle state, or give the instance a `status` render pass on
  `start()` so the widget can distinguish "about to search" from "idle".

## Widget renderers not shipped, in `XpSearch.Client/src/widgets/index.ts`

- **Simplified:** `suggestions`, `rangeFilter`, `categoryTree` and `loadMore` have a markup
  contract in `themes/MARKUP.md` and a fixture, but no default renderer. Three of them have no
  behaviour either (see the entry above); `rangeFilter` has `withRange` but inherits its
  hand-configured bounds.
- **Ceiling:** a project needing any of the four writes the renderer itself against the behaviour, or
  waits. `FIRST_PARTY_WIDGET_TYPES` still reserves the four names, so a `.xps-mount` naming one is a
  console error and a skipped widget, not a silent no-op.
- **Upgrade path:** add the renderer next to the others and put it in `DEFAULT_WIDGETS`; the markup,
  the CSS and the fixtures are already in place, so each is additive.

## `withRange` in `XpSearch.Client/src/behaviors/range.ts`

- **Simplified:** the control's bounds come from `params.min`/`max`, and `canApply` is false
  without them, because the JSON contract carries no numeric facet statistics — there is nowhere for a
  server-computed min/max to arrive.
- **Ceiling:** a range slider over an unknown corpus has to be hand-configured, and its ends do not
  follow the current result set.
- **Upgrade path:** add facet statistics to `SearchResponse` (a contract change, so a coordinated event)
  and read them in the behaviour, keeping the params as an override.

## Default route mapping in `XpSearch.Client/src/routing.ts`

- **Simplified:** `defaultStateToRoute` owns the params `q`, `page` and `sort`, one param per facet
  attribute, `<attribute>_op` for a non-default facet operator, and
  `<attribute>_<lt|lte|eq|ne|gte|gt>` for numeric filters. A facet attribute called `q`,
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

## `SuggestMode.QuerySuggestions` in `XpSearch.Core/Search/DocumentSuggestService.cs`

- **Simplified:** spec §4.3 asks for two autocomplete modes. Only document suggestions (a prefix match on the
  index's suggest field, `Title` by default) is implemented. An index configured for query suggestions gets
  an empty `suggestions` array and a logged warning.
- **Ceiling:** query suggestions need a store of previously issued queries and their frequencies, which is
  Phase 6 (spec §13.6). Until then the mode exists as configuration and does nothing, so a project that
  sets it silently loses autocomplete apart from the warning.
- **Upgrade path:** when the Phase 6 analytics store lands, add a `QuerySuggestionsSuggestService` and pick
  the implementation per index in `AddXpSearch`; the `SuggestMode` option and the `ISuggestService`
  interface are already the seam.

## `RankingInfo.Boosts` in `XpSearch.Core/Pipeline/Stages/ProjectResponseStage.cs`

- **Simplified:** `explain=true` returns `ranking` with the raw Lucene score as `baseScore`, the
  one-based position, and an always-empty `boosts`.
- **Ceiling:** the admin query tester (spec §8.4) can show why a result scored what it scored, but not why it
  moved — because nothing moves it yet. `score` and `baseScore` are therefore always identical.
- **Upgrade path:** the Phase 5 boost and pin/bury stages occupy `SearchStageOrder.BoostRules` (700) and
  `SearchStageOrder.PinnedAndBuried` (900); each appends its own description to the result's `ranking.boosts`
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

- **Simplified:** fields are detected from `DataClassInfo.ClassFormDefinition` — read through
  `IDataClassDefinitionSource`, whose default implementation calls the static
  `DataClassInfoProvider.GetDataClassInfo` because `DataClassInfo` has no `IInfoProvider<T>` registration —
  via `FormInfo.GetFields`, and
  mapped by their field data type. Data types with no obvious search meaning (assets, references, booleans,
  GUIDs, XML) are dropped. Reusable field schema fields are **not** in a content type's own class form
  definition — verified against a Dancing Goat database, where `DancingGoat.ProductCoffee` holds only
  `<schema guid="fe13f703-…"/>` — so the source reads a second class, `CMS.ContentItemCommonData`, and
  merges in every field whose `kxp_schema_identifier` property matches one of the GUIDs the content type
  references. Kentico's own helper for this (`ReusableFieldSchemasHelper.CopySchemas`) is `internal`, and
  `IReusableFieldSchemaManager` lives in a `.Internal` namespace, so the merge is done here over the two
  public `FormInfo` objects instead.
- **Ceiling:** two class queries per content type instead of one, and the merge re-implements a rule that
  belongs to the platform: if Xperience ever stores schema fields somewhere other than
  `CMS.ContentItemCommonData`, or renames the `kxp_schema_identifier` property, detection silently loses
  every schema field again. A name defined by both a content type and one of its schemas is a configuration
  error: the content type's field is kept and the schema field dropped with an `ILogger` warning, rather
  than being merged or erroring out. The data type mapping is still fixed — a project that wants a boolean
  indexed must override it by hand. Reusable items indexed through `FindItemsToReindex` need nothing extra:
  `XpSearchIndexingStrategy` does not override it, and every item resolves its fields through the same
  `IContentTypeFieldSource.GetFields(item.ContentTypeName)` call, so it takes exactly this path.
- **Upgrade path:** drop the merge and call the platform helper if Kentico makes
  `ReusableFieldSchemasHelper` (or an equivalent) public. `IContentTypeFieldSource` remains the seam.

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

## `FlattenLinkedItems` in `XpSearch.Core/Indexing/XpSearchIndexingOptions.cs`

- **Simplified:** the registration names the content types the linked field can hold. The *document* is
  mapped from whatever each linked item's own `ContentTypeName` turns out to be, so the list is only used
  to report the flattened fields in the parent type's schema — but it is a list a developer has to keep in
  step with the content model, because the allowed types of a *Pages and reusable content* field live in
  the field's editor settings, which have no documented public API. Two further shortcuts: when the field
  holds several linked items, the first one to define a field name is the one that contributes it (a
  second `SortedDocValuesField` of the same name would make Lucene reject the document), and flattening
  reads only the first level of linked items, whatever `depth` the parent is loaded with.
- **Ceiling:** a content type added to the field later is flattened into documents but invisible to
  `facets`, `fields`, sort validation and the §7.4 dropdown until it is added to the registration. A page
  linking two products indexes the first one's values only. And `FindItemsToReindex` is untouched: nothing
  reindexes the page when the product it links changes, so each flattened relationship still needs an
  override of its own (the guide says so; Dancing Goat has one).
- **Upgrade path:** read the allowed content types off the field's settings if Kentico documents a public
  way to; make the accumulation per-field-kind (append taxonomies from every linked item, keep first-wins
  only for the sortable kinds) if a multi-item link ever needs it; and if `ContentRetriever`'s `Linking`
  becomes expressible from a class name and a field name alone, generate the `FindItemsToReindex` query
  from the same registration.

## Field renaming is not supported, in `XpSearch.Core/Indexing/XpSearchIndexingOptions.cs`

- **Simplified:** spec §4.5f asks for exclude, rename and boost. `Exclude` and `Configure` are implemented;
  `Configure` can change every flag and the boost, but `SchemaField.Name` is both the content type field the
  value is read from and the Lucene field it is written to, so changing it breaks the read. The guide says
  so instead of the code preventing it.
- **Ceiling:** a project that wants a shorter attribute name on the wire has to add a second field in an
  override of `ContributeAsync`, or rename in its own client code.
- **Upgrade path:** add a source-field name to `SchemaField` that defaults to `Name`, read values by the
  source name in the strategy, and index and project by `Name`.
## `check.mjs` in `themes/scripts/check.mjs`

- **Simplified:** the theme self-check tokenizes CSS with a regex (`([^{}]+)\{([^{}]*)\}`) and HTML
  with `class="…"` matching instead of parsing either. It sees flat declaration blocks, skips the
  at-rule wrappers it cannot nest into, and treats a colour as "a hex literal, a `rgb(`-family
  function, or one of ~20 named colours".
- **Ceiling:** a colour smuggled in through a nested at-rule's own prelude, a `@supports` block that
  re-opens braces inside a declaration value, a named colour outside the list (`rebeccapurple`), an
  `url()` data-URI containing a colour, or a class written into the DOM by JavaScript rather than a
  fixture, all pass unnoticed. The class-parity check compares literal strings, so it cannot know
  that `.xps-results__item` in a fixture and `.xps-results__item` in `MARKUP.md` mean the same element —
  only that both exist.
- **Upgrade path:** if the ruleset ever needs to reason about specificity or cascade layers, replace
  the tokenizer with `postcss` and keep the same five assertions; the checks are independent of how
  the file is parsed.

## `color-mix()` in `themes/src/default.css`

- **Simplified:** every tint and hover state (`.xps-highlight`, `.xps-chip`, `.xps-button:hover`,
  `.xps-autocomplete__option--active`, the panel shadow) is derived with
  `color-mix(in srgb, var(--xps-color-…) N%, …)` rather than being given its own custom property.
  That keeps the variable block exactly as spec §6 froze it — eight properties, no more.
- **Ceiling:** `color-mix()` shipped across browsers in 2023 (Chrome/Edge 111, Safari 16.2,
  Firefox 113). Because the mixes wrap a `var()`, an older browser makes the declaration invalid at
  computed-value time — it computes to `unset`, so no earlier declaration in the same rule can act
  as a fallback and the tint simply does not paint. Layout and text stay correct; the `<mark>`
  highlight and chip fills go plain. Documented, with the three-line workaround, in
  `docs/guides/theming.md`.
- **Upgrade path:** if a client needs a pre-2023 browser, add `--xps-color-highlight`,
  `--xps-color-hover` and `--xps-color-chip` to the variable block with flat defaults and use them
  directly — a spec §6 amendment, not a code change.

## `xps-autocomplete__panel` surface in `themes/src/shell.css`

- **Simplified:** shell positions the autocomplete popup but gives it no background, because the
  §6 rule "no colours beyond `currentColor`" leaves it nothing to paint with.
- **Ceiling:** a site that loads `shell.css` alone gets a see-through popup overlapping the page
  behind it until it sets a `background-color` on `.xps-autocomplete__panel`. Stated in the theming
  guide and in `themes/MARKUP.md`, but nothing enforces it.
- **Upgrade path:** none wanted — the alternative is shell shipping a colour, which is the rule
  that keeps it safe to load on any site.

## `ResultsWidgetProperties.ResultTemplate` in `XpSearch.Widgets`

- **Simplified:** `[RegisterSearchResultTemplate]` and the "Result template" drop-down register and select
  a template; the chosen identifier is written into `data-xps-config` as `template`. Nothing renders the
  registered view. Spec §5.8 also asks for server-rendered templates on the initial page load, which
  needs a server-side search on render and the progressive-enhancement handover to the client.
- **Ceiling:** an editor can choose a template and see no difference. The JavaScript `templates.item`
  option is the only way to change a result's markup today.
- **Upgrade path:** run the query in `ResultsWidgetViewComponent.InvokeAsync`, render
  `SearchResultTemplate.ViewName` per result into the mount element, and have the `results` widget adopt
  the existing children on its first render instead of replacing them.

## `SuggestionsWidgetViewComponent` and pagination style "Load more" in `XpSearch.Widgets`

- **Simplified:** both emit a mount for a JavaScript widget that is reserved but not shipped -
  `suggestions` and `loadMore`. The mount is rendered anyway, so the markup contract is already right when
  those widgets land.
- **Ceiling:** placing either today produces one `console.error` from the bootstrap ("unknown widget
  type") and an empty container. Every other widget of the instance keeps working - the bootstrap skips
  the mount rather than throwing.
- **Upgrade path:** none needed on the C# side; the widgets start working when `suggestions` and
  `loadMore` are added to `DEFAULT_WIDGETS` in `XpSearch.Client/src/widgets/index.ts`.

## `SortOptionsValidation.IsValidKey` in `XpSearch.Widgets`

- **Simplified:** a sort key is accepted when it is `relevance`, a key configured in
  `XpSearchIndexOptions.SortKeys`, or anything ending in `_asc` / `_desc`. The suffix branch does not check
  that the field before the suffix exists and is sortable, because that needs the index schema and the
  validation runs while the mount markup is built.
- **Ceiling:** an editor can type `nosuchfield_desc;Newest` and get a selector entry the API rejects at
  query time.
- **Upgrade path:** resolve `IIndexSchemaProvider` in the sort widget and check the field against
  `SchemaField.Sortable`, or move the check into an admin validation rule so the editor is told in the
  configuration dialog.
## `LuceneQuiescenceWaiter` in `XpSearch.Ingestion/Indexing/ExternalDocumentReplayLuceneClient.cs`

- **Simplified:** the post-rebuild replay decides that the integration's rebuild has finished by
  polling `ILuceneClient.GetStatistics` every two seconds until the index's last-write time stops
  changing, bounded by `XpSearchIngestionOptions.ReplayTimeout` (two minutes). There is no API that
  reports "the rebuild queue is drained": `LuceneQueueWorker` is `internal` and publishes the new index
  generation at the end of its batch.
- **Ceiling:** a naive quiescence heuristic. A rebuild that stalls for longer than the poll interval
  mid-way looks finished, and the replay can write into the outgoing index generation; a rebuild slower
  than the timeout is not waited for at all. Nothing is lost either way — the rows stay in
  `XpSearch.ExternalDocument` and the next push or rebuild rewrites them — but the pushed documents can
  be missing from search until then.
- **Upgrade path:** if the integration ever raises a rebuild-completed event, or exposes the published
  index generation, replace the body of `LuceneQuiescenceWaiter.WaitAsync` with a wait on that. The
  seam (`IRebuildCompletionWaiter`) already exists and is a single registration.

## Facet counts for pushed documents in `ExternalDocumentFactory` (`XpSearch.Ingestion`)

- **Simplified:** a `string[]` (taxonomy) attribute on an externally pushed document is written as plain
  indexed terms plus the `_text` and `_label` companions, not as a `FacetField`. Facet fields have to be
  registered in the strategy's `FacetsConfig` before `DefaultLuceneClient` builds the document, and an
  external document has no strategy to register them on.
- **Ceiling:** pushed documents can be *filtered* by such an attribute and their values appear in
  results, but they do not contribute to the facet counts a `facetList` widget renders, because those
  come from the taxonomy sidecar. An index mixing Xperience content and pushed documents shows counts
  that are lower than the result totals.
- **Upgrade path:** have the ingestion writer register the dimensions on the index's
  `XpSearchIndexingStrategy` instance (it already owns a shared `FacetsConfig`) before upserting, or
  write the taxonomy sidecar directly through `ILuceneIndexService.UseIndexAndTaxonomyWriter`.

## API key and schema administration in `XpSearch.Ingestion`

- **Simplified:** keys are created in code through `IApiKeyService.CreateAsync` and schemas are declared
  in code with `[XpSearchSchema]` / `[XpSearchField]` (or in options). Spec §10.3 and §10.8 also ask for
  both to be editable in the admin UI, on the Search tuning application.
- **Ceiling:** a non-developer cannot mint or revoke an ingestion key, change a field's flags, or read
  the ingestion log without SQL. The data is all there — `XpSearch.ApiKey` and `XpSearch.IngestionLog`
  are installed and written to — but nothing renders it.
- **Upgrade path:** the Admin unit adds three UI pages over the existing module classes and a schema
  editor that writes into `XpSearchIngestionOptions.Indexes[...]`; no change to this package is needed
  beyond exposing the key listing.

## Rate limiting in `XpSearchIngestionServiceCollectionExtensions.PartitionByKey`

- **Simplified:** the per-key rate limit is ASP.NET Core's in-memory fixed-window limiter, partitioned by
  the key prefix (or the remote address when there is no key).
- **Ceiling:** the window is per instance. Behind two web heads a key gets twice the configured budget,
  and a restart resets every window. Fine for "stop a runaway import"; not a billing-grade quota.
- **Upgrade path:** swap the partition's limiter for a distributed one backed by the cache the host
  already runs; the partition key is already the right shard key.

## `"private": true` in `src/XpSearch.Client/package.json`

- **Simplified:** the npm package is complete — `dist/`, `themes/shell.css`, `themes/default.css`, a
  runnable `mock/server.mjs` and a `README.md`, all verified by `npm pack --dry-run` — but the
  manifest still carries `"private": true`, so `npm publish` refuses it. Publishing is Phase 8's job
  (registry, scope, provenance, the release workflow); nothing about the tarball's contents is
  waiting on it.
- **Ceiling:** a JavaScript-only consumer cannot `npm install @yourco/xperience-search` from a
  registry. They install the tarball by path (`npm install ./yourco-xperience-search-0.1.0.tgz`),
  which is what `samples/pack-and-build.mjs` does, and every documented entry point then resolves.
- **Upgrade path:** delete the `private` field and set `publishConfig.access`, in the release unit.

## Repo-only scripts in the published `package.json`

- **Simplified:** npm has no mechanism to strip `scripts` from a published manifest, so the entries
  that only work inside this repository (`build`, `test`, `typecheck`, `size`, `contract:*`,
  `docs:*`, `repo:mock`, `repo:demo`) travel in the tarball. They are prefixed `repo:` where a
  consumer might plausibly run them, and `README.md` says so.
- **Ceiling:** `npm run repo:mock` inside an installed package fails with a missing `mock/server.ts`
  rather than being absent from `npm run`'s listing.
- **Upgrade path:** a `prepack` step that rewrites `package.json` (or `npm pkg delete scripts.*` in
  the release workflow) if the noise ever matters.
