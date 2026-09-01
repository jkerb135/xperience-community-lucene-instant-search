# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

## Integrated suggestions search twice per keystroke (`searchBox` in `Client/src/widgets/searchBox.ts`)

- **Simplified:** with `params.suggestions` set, the input handler calls both
  `withSuggestions.setQuery` (which searches in place itself) and `withSearchBox.apply` (which runs
  `queryHook` and remembers whether the query was submitted). Two behaviours, one field, and neither
  can be told "do not search".
- **Ceiling:** two `actions.search()` calls per keystroke. They collapse into one HTTP request —
  `SearchClient.search` supersedes anything still inside its debounce window — but each one builds a
  request and arms the stall timer, and the second `setQuery` is a no-op state write whenever
  `queryHook` is the identity.
- **Upgrade path:** give `SuggestionsBehaviorParams` an explicit `searchInPlace?: boolean` (today it
  is inferred from `resultsUrl === undefined`) so a caller that owns the query can take the popup
  without the search.

## The integrated suggestions panel has no keyboard-hints footer (`renderPanel` in `Client/src/widgets/suggestionsPanel.ts`)

- **Simplified:** the footer — TH-1's `xps-suggestions__hints` plus the "See all results" link — is
  rendered only when `seeAllUrl !== null`, i.e. only for a standalone widget configured with
  `resultsUrl`. A search box with integrated suggestions searches in place, so it never has one and
  never shows the hints.
- **Ceiling:** the two consumers show the same options with different affordances; a keyboard hint
  that only appears on landing pages is arbitrary. Splitting the condition was out of scope here
  because it would change the standalone widget's markup in its own no-`resultsUrl` mode, which TH-3
  was told to leave byte-identical.
- **Upgrade path:** render `xps-suggestions__footer` whenever there is at least one option, with the
  see-all link as its optional second child, and update `themes/fixtures/suggestions.html` and the
  standalone widget's tests in the same commit.

## `ServerRenderedResults.DefaultCard` in `XpSearch.Core/Rendering/ServerRenderedResults.cs`

- **Simplified:** the fallback card a host without a result partial gets is emitted as string literals
  in C#. `XpSearch.Core` is a plain library, not a Razor class library, and turning it into one to
  ship a single `_Result.cshtml` costs every consumer the Razor SDK; PK-2 was explicitly not allowed
  to add package references either.
- **Ceiling:** the default card's markup now exists three times — this method, the widgets'
  `_Result.cshtml`, and the client's `defaultResultItem`. TH-1 added
  `Client/src/widgets/card-parity.test.ts`, which reads the two server sources as text and compares
  their element/class pairs and their body order with what the client actually renders, so a
  one-sided edit now fails; the three still have to be *written* three times.
- **Upgrade path:** if the markup starts drifting, generate all three from `themes/MARKUP.md`, or make
  Core's Razor class library a fourth package (`XperienceCommunity.Search.Views`) that both Widgets
  and plain hosts reference.

## The empty state's "…and show 7 results" preview (`defaultEmpty` in `Client/src/widgets/results.ts`)

- **Simplified:** the refined empty state offers "Clear filters" without saying how many results
  clearing would bring back. The approved mockup shows the count.
- **Ceiling:** the number is the total of the *same query with no filters*, which no response in
  hand carries — it needs a second, unfiltered search. Nothing in the pipeline client issues one
  today (`withResults` renders the one response the instance holds), so the button is honest but
  vague, and a visitor may clear filters to find nothing behind them either.
- **Upgrade path:** ask the pipeline for it rather than the client — either a response field
  (`totalUnfiltered`, computed server-side from the same Lucene query without the filter clauses,
  which costs one extra count per empty search) or an opt-in `probeUnfiltered` request flag. Then
  `hasRefinements` grows a sibling `unfilteredTotal?: number` and the template appends the count.

## The sheet's countless Apply button (`filterSort` in `Client/src/widgets/filterSort.ts`)

- **Simplified:** the primary button reads "Show results", not "Show N results". The approved mockup
  previews the count the pending selection would return.
- **Ceiling:** the preview needs a count-probe query per pending tick, and **every** request the
  query endpoint answers is journaled: `CachedSearchPipeline.ExecuteAsync` calls
  `ISearchRequestJournal.Record` on both the cached and the uncached path, unconditionally, and the
  contract's `SearchRequest` has no flag to opt out (the journal's only skip is a `queryId` it has
  already recorded). Probing would therefore log a search activity and a query-log row per tick,
  inflating query volume and deflating click-through for every visitor who opens the sheet. The
  widget ships without the probe rather than pollute analytics.
- **Upgrade path:** a coordinated contract change — a `probe` (or `dontJournal`) boolean on
  `SearchRequest` that `CachedSearchPipeline` honours by skipping `Record`, plus a rate limit so it
  cannot be used to run unlogged queries. Then the widget debounces (~250ms) a smallest-page-size
  probe through the public `SearchClient`, discards a result that arrives after Apply, and falls
  back to the countless label on failure.

## `filterSort` registers no routable attribute (`Client/src/widgets/filterSort.ts`)

- **Simplified:** a widget declares at most one routable attribute (`Widget.$$routable` is a single
  `{ attribute, kind }`), and the sheet composes N of them, so it declares none. Its attributes are
  only read out of the URL when another widget on the page owns them.
- **Ceiling:** a page whose *only* filter UI is the sheet does not hydrate its facets from the URL,
  so a shared or bookmarked link loses those refinements. The documented recipe keeps the desktop
  `facetList` widgets mounted, which owns them, so this bites only a sheet-only page.
- **Upgrade path:** widen `$$routable` to accept an array and have `SearchInstance.addWidgets`
  register each entry — a small, source-compatible change to `types.ts` and `instance.ts`.

## "Did you mean" and "Popular searches" in the results empty state

- **Simplified:** neither is rendered. The approved mockup shows both as future slots.
- **Ceiling:** the empty state offers only "clear the filters" or "try fewer words"; a misspelled
  query gets no recovery path at all.
- **Upgrade path:** "Did you mean" needs a Lucene suggester in Core plus a `didYouMean` field on
  `SearchResponse` (contract addition, own unit). "Popular searches" needs a public read endpoint
  over the analytics store, which today is admin-only; until then a host can render its own list
  next to the widget.

## Per-widget stylesheets in `themes/src/scss/widgets/*` and `Client/styles/widgets/*.css`

- **Simplified:** a widget partial carries the *whole* rule a shared selector list belongs to, so a
  rule two widgets share is duplicated across the two compiled files rather than layered into a
  third one. `styles/widgets/toggle-filter.css` therefore also carries the facet-list checkable-row
  rules, `load-more.css` carries the result-list rules, and both carry the form-control block. The
  SCSS path does not duplicate anything — `@use` loads a module once — this is only about the
  precompiled `styles/widgets/*.css` files.
- **Ceiling:** a consumer loading several of the precompiled per-widget files ships those shared
  rules more than once; `styles/base.css` plus all twelve is 31.6 KB against 21.3 KB for
  `shell.css` + `default.css`
  (which is why the guide points at the two full stylesheets for a page that mounts most widgets).
  Identical duplicated rules cannot conflict, so only bytes are at stake.
- **Upgrade path:** split the shared selector lists into their own partials (`_checkable.scss`,
  `_form-controls.scss`, `_result-list.scss`), emit them as `styles/shared/*.css`, and have the
  widget CSS files carry only what is theirs — at the cost of a documented load order per widget.
  The rule-for-rule parity check in `Client/scripts/build-styles.mjs` keeps `shell.css`/`default.css`
  honest while that is done.

## Hierarchical facets in `XpSearchIndexingStrategy.AddTags` and `TaxonomyFacetProvider`

- **Simplified:** a tag's ancestors are resolved **when the document is indexed** (ADR-0018) and
  written onto it, and the ancestor titles are `TagInfo.TagTitle` - the default-language title -
  because the language-specific ones live serialized in `TagInfo.TagMetadata`, keyed by content
  language GUID, and deserializing every tag's metadata per language is not something a per-document
  write can afford. The top-N cut (`XpSearchOptions.MaxFacetValues`) is applied to the whole
  dimension, across every level, not per level.
- **Ceiling:** moving or renaming a tag in the *Taxonomies* application does not change the documents
  already indexed - counts and `path` stay as they were until a rebuild. A tag whose title is
  translated shows its default-language title as the *ancestor* of another tag, while the tag a
  document actually carries shows the language-specific title `ITaxonomyRetriever` returned: the same
  tag can therefore read differently at two places in one tree. And on a wide taxonomy the cut can
  drop a whole branch before its children are reached, so a deep node may be invisible even though
  its ancestors are listed. `TaxonomyFacetProvider.EnsureAncestors` guarantees only the converse -
  that a value's ancestors are never missing.
- **Upgrade path:** for freshness, react to `TagInfo` changes by reindexing the affected items (the
  seam is `ITagAncestrySource`, and Xperience already raises object events). For titles, give
  `ITagAncestrySource.AncestorsOf` a language and have the default implementation read
  `TagMetadata.Deserialize` behind the same cache entry. For the cut, count per level: ask for
  `GetTopChildren` once per depth once the provider tracks depth, and cap each level separately.

## `searchable` in `XpSearch.Widgets/Client/src/widgets/facetList.ts`

- **Simplified:** `searchable: true` renders a facet-search input that filters the values **already
  rendered**, in the browser, by case-insensitive substring. There is no facet-search endpoint in the
  JSON contract (there is no facet-value search route), so there is nothing server-side to call.
- **Ceiling:** a visitor cannot find a value that falls outside `limit`/`showMoreLimit`, or one the
  current query returned no documents for. On an attribute with hundreds of values the control looks
  like it is broken.
- **Upgrade path:** add a facet-search route to the contract (a coordinated contract change), give
  `withFacetList` a `searchFacetValues` render-state member, and have the widget call it instead of
  filtering locally; the markup and the option name do not change.

## The loading template in `XpSearch.Widgets/Client/src/widgets/results.ts`

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

## `withRange` in `XpSearch.Widgets/Client/src/behaviors/range.ts`

- **Simplified:** the control's bounds come from `params.min`/`max`, and `canApply` is false
  without them, because the JSON contract carries no numeric facet statistics — there is nowhere for a
  server-computed min/max to arrive.
- **Ceiling:** a range slider over an unknown corpus has to be hand-configured, and its ends do not
  follow the current result set. The `XpSearch.RangeFilter` Page Builder widget inherits this: its
  Minimum and Maximum are editor properties, and it renders the unconfigured block without them.
- **Upgrade path:** add facet statistics to `SearchResponse` (a contract change, so a coordinated event)
  and read them in the behaviour, keeping the params as an override. The `rangeFilter` renderer ships
  and needs no change: it already renders itself `disabled` when the bounds are missing, and would
  simply stop being disabled.

## Default route mapping in `XpSearch.Widgets/Client/src/routing.ts`

- **Simplified:** `defaultStateToRoute` owns the params `q`, `page` and `sort`, one param per facet
  attribute, `<attribute>_op` for a non-default facet operator, and
  `<attribute>_<lt|lte|eq|ne|gte|gt>` for numeric filters. A facet attribute called `q`,
  `page`, `sort` or `price_lte` collides with the mapping and will not round-trip, and two instances with
  `routing: true` on one page fight over the same params.
- **Ceiling:** those attribute names, and multi-instance routing, need the
  `routing: { stateToRoute, routeToState }` escape hatch — public and tested, but hand-written.
- **Upgrade path:** add a documented `routing.prefix` option that namespaces every param (`s1_q`,
  `s1_tags`) when a second instance needs the URL too.

## Routable attributes are collected once, in `createSearch().start` (`XpSearch.Widgets/Client/src/instance.ts`)

- **Simplified:** the routable registry is two `Set`s on the instance, filled by `addWidgets` from each
  widget's `$$routable`, and the URL is hydrated once at `start()`. A widget added later (a facet list
  mounted by a lazy tab, say) contributes to the registry but its param has already been skipped.
- **Ceiling:** a deep link into a filter whose widget mounts after `start()` is dropped, silently. The
  bootstrap never hits this: it adds every mount before starting.
- **Upgrade path:** re-read the URL in `addWidgets` when the added widgets register an attribute that
  the URL carries and nothing has moved the state since - or expose a `search.hydrateFromUrl()`.

## `mock/server.ts` in `XpSearch.Widgets/Client`

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

## `csharpEdits` in `XpSearch.Widgets/Client/scripts/contract.mjs`

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

## `Invalidate` in `XpSearch.Core/Search/LuceneIndexAccessor.cs`

- **Simplified:** the integration's searcher cache is dropped by resolving its `internal`
  `LuceneSearchCacheInvalidator` by type name and invoking `Invalidate` reflectively. There is no
  public route: `LuceneIndexSearcherProvider`, the invalidator and `InvalidateSearchIndexWebFarmTask`
  are all internal, and `DefaultLuceneClient` only invalidates on `Rebuild` and `DeleteIndex` — never
  after the in-place upsert or delete that `ILuceneClient` performs.
- **Ceiling:** an upgrade of `Kentico.Xperience.Lucene` that renames or moves that type turns every
  write into an `InvalidOperationException`. `CachingTests.TheIntegrationsSearchCacheInvalidator_IsStillReachable`
  fails first, at build time rather than in a host.
- **Upgrade path:** if the integration ever makes the invalidator public or invalidates after
  `UpsertRecords` itself, delete the reflection and call it (or drop the call entirely).

## `RegisterSchemaDimensions` in `XpSearch.Core/Indexing/XpSearchIndexingStrategy.cs`

- **Simplified:** the facet dimensions are read from `IIndexSchemaProvider` synchronously
  (`GetAwaiter().GetResult()`) the first time `FacetsConfigFactory` is called on an instance, because
  the integration's `FacetsConfigFactory` is a synchronous API and the schema comes from the database.
- **Ceiling:** the strategy is transient, so a query that resolves it (to read the facet
  configuration) pays one extra schema read — the same read the pipeline already makes per uncached
  request, so the cost doubles rather than appears. Nothing deadlocks: ASP.NET Core has no
  synchronization context.
- **Upgrade path:** cache the resolved dimension set per strategy type behind the index configuration's
  own cache dependency, or ask the integration for an async configuration hook.

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
  external document has no strategy to register them on. The reserved `_source` is *not* affected: it is
  a base schema field, so every strategy registers the dimension and both Xperience content and pushed
  documents carry it as a facet field - `_source` counts and drills down like `contentType`.
- **Ceiling:** pushed documents can be *filtered* by such an attribute and their values appear in
  results, but they do not contribute to the facet counts a `facetList` widget renders, because those
  come from the taxonomy sidecar. An index mixing Xperience content and pushed documents shows counts
  that are lower than the result totals.
- **Upgrade path:** have the ingestion writer register the dimensions on the index's
  `XpSearchIndexingStrategy` instance (it already owns a shared `FacetsConfig`) before upserting, or
  write the taxonomy sidecar directly through `ILuceneIndexService.UseIndexAndTaxonomyWriter`.

## `health` in `XpSearchIndexer.GetStatusAsync` (`XpSearch.Ingestion`)

- **Simplified:** `degraded` is derived from one number, `IIngestionQueue.FailedCount` - the count of
  work items that threw in `XpSearchIngestionQueueWorker.ProcessItem` without one succeeding since. It
  used to be derived from the queue length, which made every asynchronous write flip a healthy index to
  `degraded` until the queue drained (HW-3 §5.2).
- **Ceiling:** the counter is a static field of the worker, so it is per process and starts at zero
  after a restart, and work that is *stuck* rather than failing - a queue that never drains because the
  worker thread is wedged - still reads as `healthy`. A failure followed by an unrelated success also
  clears it.
- **Upgrade path:** the real signal is already persisted: `XpSearch_ExternalDocument` rows keep
  `DocumentStatus` and `UpdatedAt`, so an oldest-pending-row query on the store (one extra method on
  `IExternalDocumentStore`) would report both stuck and failed work across restarts.

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

## `"private": true` in `src/XpSearch.Widgets/Client/package.json`

- **Simplified:** the npm package is complete — `dist/`, `themes/shell.css`, `themes/default.css`, a
  runnable `mock/server.mjs` and a `README.md`, all verified by `npm pack --dry-run` — but the
  manifest still carries `"private": true`, so `npm publish` refuses it. Publishing is Phase 8's job
  (registry, scope, provenance, the release workflow); nothing about the tarball's contents is
  waiting on it.
- **Ceiling:** a JavaScript-only consumer cannot `npm install @xperience-community/xperience-search` from a
  registry. They install the tarball by path (`npm install ./xperience-community-xperience-search-0.1.0.tgz`),
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

## The analytics dashboard (spec §9.3)

- **Simplified:** the data behind every report on the dashboard ships (`ISearchAnalyticsService`,
  `SearchAnalyticsReport`), but the admin **page** that renders them does not — nor does the
  "Create rule" deep link from a zero-result row into the rule editor.
- **Ceiling:** a marketer cannot see the reports in the administration yet; a developer can read them
  from `ISearchAnalyticsService` (see `docs/guides/analytics.md`).
- **Upgrade path:** the React admin unit renders `SearchAnalyticsReport`; no data work is left.

## `ReportTable` in `XpSearch.Admin/Client/src/analytics/ReportTable.tsx`

- **Simplified:** the four report tables page **in the browser** over the rows one `Load` already
  returned, and the server fills each report to a fixed `AnalyticsReportDto.MaxReportRows` (200) cap
  rather than to the page size. The **Rows per page** control therefore only slices what is already
  there, and turning a page issues no request.
- **Ceiling:** nothing past the 200th row of a report can be reached at all — a range with more
  distinct queries is cut to the top 200 of each ranking, silently, with no "there is more" signal
  beyond the pager's row count. Every load also carries 200 rows per report whether or not the
  visitor pages that far.
- **Upgrade path:** if a report needs to go deeper, give `AnalyticsRequest` a page and page size
  again, page in `SearchAnalyticsService` (its `Take(limit)` becomes `Skip(...).Take(...)`, with the
  total returned alongside), and have `ReportTable` reload on page change instead of slicing. The KPI
  figures already come from full-range aggregates, so they are unaffected.

## `SearchAnalyticsService.GetReportAsync` in `XpSearch.Core/Analytics/SearchAnalyticsService.cs`

- **Simplified:** one `IQueryLogStore.ReadAsync` for the requested range, then every report is a LINQ
  pass over those rows in memory, instead of six SQL aggregates. Every figure is then guaranteed
  consistent with every other, and the store keeps four methods.
- **Ceiling:** memory and time grow with the number of rows in the range — fine for the day-to-month
  ranges a dashboard asks for, not for a multi-year range on a site doing millions of searches.
- **Upgrade path:** add aggregate methods to `IQueryLogStore` backed by ObjectQuery `GROUP BY` and
  have the service call them; `ISearchAnalyticsService` and its DTOs do not change.

## `QuerySuggestionService` in `XpSearch.Core/Analytics/QuerySuggestionService.cs`

- **Simplified:** prefix matching and volume counting happen in memory over the last
  `Analytics.QuerySuggestionDays` days of the log, cached per index/prefix/limit for
  `options.CacheTtl`. There is no suggestion index and no precomputed popularity table.
- **Ceiling:** the first uncached suggestion of a keystroke reads the window's rows; on a busy site
  with a long window that read is the cost of autocomplete, and the cache is per instance.
- **Upgrade path:** the same store-level `GROUP BY` as above, or a nightly materialized
  popular-queries table the service reads instead.

## `QueryContextMap` in `XpSearch.Core/Analytics/QueryContextMap.cs`

- **Simplified:** the `queryId` → query text map that gives click and conversion activities their
  query is in process memory: 10 000 entries, 30 minutes, oldest dropped when full. It gets one entry
  per request from `ISearchRequestJournal` (cache hits included, each under its own re-issued
  `queryId`), so a popular query held in the response cache still consumes an entry per caller.
- **Ceiling:** behind a load balancer, or after an application restart, a click whose search was
  answered elsewhere logs its activity with an empty query part. The clicked position still reaches
  the query log, because that lookup is by `LogQueryID` in the database, so click-through reports are
  unaffected.
- **Upgrade path:** back the map with Xperience's cache (or a distributed cache) behind the same
  `IQueryContextMap` interface.

## Queued query log rows are not durable

- **Simplified:** `XpSearchQueryLogQueueWorker` holds pending rows in memory; a process that dies
  before the worker drains loses them. Ingestion persists first and re-queues on start (ADR-0005);
  analytics deliberately does not.
- **Ceiling:** a crash or a redeploy loses up to one worker interval (10 s) of query log rows.
- **Upgrade path:** none planned — a missing aggregate row is not a missing document.

## `PinnedAndBuriedStage` in `XpSearch.Core/Pipeline/Stages/PinnedAndBuriedStage.cs`

- **Simplified:** pin and bury reorder the **current page** of results, which is all
  `ExecuteSearchStage` materializes. A pin to position 3 is applied on the page that contains
  position 3; other pages are untouched. Injecting a document the query did not match increments
  `total` by one and drops the last row of the page.
- **Ceiling:** with a pinned document, `total` and the page boundaries are approximate by at most the
  number of pins, and a document pinned to a position beyond `MaxResultWindow` can never appear.
- **Upgrade path:** move the reordering above pagination — have `ExecuteSearchStage` expose the full
  `TopDocs` window and apply pins to it before slicing the page.

## Model to row mapping in `XpSearch.Admin/UIPages/*.cs`

- **Simplified:** each edit page maps its form model onto its `*Info` row and back by hand, and those
  two methods have no unit test. An Info object cannot be constructed outside Kentico's IoC container
  (`Service.InitializeContainer`), so the round trip only runs on a live instance. The Ingestion unit
  hits the same wall.
- **Ceiling:** a mistyped assignment in `ApplyTo`/`From` is caught by a human clicking Save, not by
  CI. The column names themselves are pinned by `ModuleInstallerTests`.
- **Upgrade path:** stand up Kentico's IoC container in the test fixture, or move the mapping onto
  plain records that the pages then copy field-by-field.

## The index-scope check of `Delete` in `XpSearch.Admin/UIPages/{FieldWeights,Synonyms,Stopwords,Rules}.cs`

- **Simplified:** the same four-line guard — read the row, compare its index to the URL's, refuse or
  hand off to `base.Delete` — is repeated in each of the four index-scoped listings rather than
  factored into a shared `IndexScopedListingPage<TInfo>` base. Only the message string
  (`IndexScope.CrossIndexDeleteRefusal`) and the decision (`IndexScope.Matches`) are shared. Its
  accepting branch has no unit test either: `base.Delete` is platform code that needs the admin
  request pipeline, and a foreign-index row cannot be constructed offline (see *Model to row mapping*
  above), so only the refusal branch runs in CI.
- **Ceiling:** a fifth index-scoped listing can be added without the guard and nothing fails; and a
  guard reading the wrong row property would only be caught by a human, not by CI — though the
  compiler already limits the choice to the one index-name column each `*Info` has.
- **Upgrade path:** if a fifth listing appears, lift the four into an `IndexScopedListingPage<TInfo>`
  taking a `Func<TInfo, string>` for the index column, mirroring the existing
  `IndexScopedEditPage<TModel>`. For the accepting branch, the same IoC-container fixture that would
  unblock the model-to-row mapping test unblocks this one.

## `IndexSettingsPage` in `XpSearch.Admin/UIPages/IndexTuning.cs`

- **Simplified:** it duplicates the model getter and `ProcessFormData` of the integration's
  `IndexEditPage` (about thirty lines) instead of re-parenting it. A `UIPage` registration names one
  parent, and the integration's registration fixes `IndexEditPage` under `IndexListingPage`, so the
  only way to put the index configuration form in our sidebar is to derive a second page from the
  public `BaseIndexEditPage`.
- **Ceiling:** a change to the integration's edit page — a validation step, a different success
  message, a new form item — does not reach our copy. Nothing fails at compile time; the two forms
  simply drift.
- **Upgrade path:** if `Kentico.Xperience.Lucene.Admin` ever exposes the form as a base class or a
  reusable component (rather than only `BaseIndexEditPage` plus a sealed registration), derive from it
  and delete the copy. Failing that, ask the integration to accept a `parentType` override.

## Rebuild progress on `IndexStatusPage.Rebuild` in `XpSearch.Admin/UIPages/IndexStatus.cs`

- **Simplified:** the "Rebuild in progress" state carries the start time of the rebuild and no
  numerator. `Kentico.Xperience.Lucene` 15.0.5 exposes no rebuild progress: `ILuceneClient` is
  `Rebuild` / `UpsertRecords` / `DeleteRecords` / `DeleteIndex` / `GetStatistics`, and
  `LuceneIndexStatisticsModel` carries only `Name`, `Entries` and `UpdatedAt` — a live count, not a
  target — while `LuceneQueueWorker` is internal
  ([`ILuceneClient.cs`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/ILuceneClient.cs),
  [`LuceneIndexStatisticsModel.cs`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/LuceneIndexStatisticsModel.cs),
  [`LuceneQueueWorker.cs`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/LuceneQueueWorker.cs)).
  A "44 of 152" would have to be invented, so it is not shown. The state also lives only in the page
  session that triggered the rebuild: `Load` clears it, because nothing can be asked whether a
  rebuild is still running.
- **Ceiling:** an operator who reloads the page during a rebuild sees the ordinary health tag and
  counts that are still climbing, with no sign a rebuild is in flight.
- **Upgrade path:** count the documents the ingestion queue's `Replay` work item writes and record a
  started/finished pair in the ingestion log, then derive both the numerator and "still running" from
  the log rather than from the page session.

## Recent ingestion window on `IndexStatusPage` in `XpSearch.Admin/UIPages/IndexStatus.cs`

- **Simplified:** the page reads the ten newest ingestion log entries of the index and, while the
  index is degraded, sorts the failed ones to the top of those ten. It does not search further back
  for failures, and the table's "Source" column is the log's `KeyPrefix` (who wrote) rather than the
  document `_source`, which the log does not record.
- **Ceiling:** a failure older than the ten newest entries is invisible on the status page even
  though health is degraded; the full history is on the Ingestion log listing.
- **Upgrade path:** a second `ReadRecentAsync` call filtered to failures, merged ahead of the ten.

## Narrow-viewport ingestion rows in `XpSearch.Admin/Client/src/status/IndexStatusTemplate.module.css`

- **Simplified:** `Table` sizes its cells with inline `min-width`/`max-width`, so the 1024 board's
  "message on a second line" is achieved by overriding those inline widths through the cell's
  `data-testid="table-cell-message"` attribute inside the page's own module stylesheet.
- **Ceiling:** the override depends on a components-library DOM attribute. If a future release drops
  it, the message column simply scrolls horizontally again instead of wrapping.
- **Upgrade path:** a `Table` prop for a full-width overflow cell, or rows built from `Row`/`Column`
  once the components library offers an invalid-row treatment outside `Table`.

## Synonym expansion in `XpSearch.Core/Tuning/SynonymExpansion.cs`

- **Simplified:** synonyms are matched against whitespace-separated tokens of the normalized query,
  longest phrase first, before the analyzer runs. There is no stemming and no per-index analyzer
  awareness in the matching itself (the alternatives are analyzed when they are parsed).
- **Ceiling:** `sofas` does not match the synonym group `sofa` unless both are listed, and a
  punctuation-joined phrase is not recognised.
- **Upgrade path:** replace the token scan with a Lucene `SynonymFilter` over a `SynonymMap` built
  from the same rows and wired into the index's analyzer chain.

## Admin client templates in `XpSearch.Admin/Client/src/**`

- **Simplified:** the three React templates have no render tests. The Kentico admin client boilerplate
  ships no test runner, and adding jest or vitest plus a DOM and mocks for `usePageCommand` would be
  more machinery than the templates contain logic. Everything with a decision in it — the with/without
  marking, the explanation split, the report mapping, the deep-link token — is a pure function on the
  C# side and is unit-tested there; the client is checked by `tsc --noEmit` under `strict`.
- **Ceiling:** a broken JSX branch (an empty state, the aria-live region, the SVG chart) is caught by
  a human opening the page, not by CI.
- **Upgrade path:** add vitest with jsdom to `Client/package.json` and one render test per template
  over fixture data, stubbing `@kentico/xperience-admin-base`.

## Query tester result count in `XpSearch.Admin/UIPages/QueryTester/QueryTesterPage.cs`

- **Simplified:** the tester runs page 1 only, at most 50 results per side (`MaxPageSize`), and the
  diff therefore compares the first page of each side.
- **Ceiling:** a result the rules moved from position 3 to position 60 is marked `Removed` rather than
  `MovedDown`, because the with-rules page no longer holds it. Same class of approximation as the pin
  and bury reordering above.
- **Upgrade path:** run both sides over a larger window than the page shown and diff the window.

## No SQL translation for the search contact group rules in `XpSearch.Core/ContactGroups/XpSearchContactGroupRuleInstaller.cs`

- **Simplified:** the three rules the installer writes are evaluated by the macro engine only. The
  matching `IMacroRuleInstanceTranslator` — a contact query
  `ContactID IN (SELECT ActivityContactID FROM OM_Activity WHERE ...)` — was written and then dropped,
  because the only way to register one is `MacroRuleMetadataContainer.RegisterMetadata`, and in
  `Kentico.Xperience.Core` 31.8.0 both `MacroRuleMetadataContainer` and `MacroRuleMetadata` carry
  `[Obsolete("Class was not intended for public use and will be removed in the next version.")]`
  (verified by decompiling `CMS.ContactManagement.dll`; the interface itself is public and not
  obsolete). A library cannot ship a call the vendor has already announced it will delete, and
  `TreatWarningsAsErrors` would not compile it anyway.
- **Ceiling:** recalculating a contact group that uses one of the rules runs the macro once per
  contact, each evaluation a `SELECT TOP 1` against `OM_Activity` — fine for tens of thousands of
  contacts on a nightly rebuild, slow for millions. Because no metadata is registered,
  `MacroRuleTreeAnalyzer.GetAffectingItems` falls back to `ALL_ACTIVITIES`/`ALL_ATTRIBUTES` for these
  rules, so a group is scheduled for recalculation on *every* activity and contact change rather than
  only on the three search activities: correct, but more recalculation than necessary.
- **Upgrade path:** when a supported registration API appears (or the obsolete one survives and is
  un-deprecated), re-add the translator — it is three lines over
  `XpSearchActivityQuery.ContactIds` — and register it with `affectingActivities` set to the rule's
  activity types.

## No time window on the search contact group rules in `XpSearch.Core/ContactGroups/XpSearchContactGroupRules.cs`

- **Simplified:** each rule takes one text parameter and looks at the contact's whole activity
  history. There is no *in the last X days* parameter, although the system rules have one (a
  `Kentico.Administration.NumberInput` field plus `ToInt({days})` in the macro).
- **Ceiling:** a group on *searched for "standing desk"* keeps a contact who searched two years ago,
  so recency has to come from a second condition (**Contact has done any activity in the last X
  days**) in the same condition group, which is coarser — it means *any* activity, not *this* one.
- **Upgrade path:** add a second parameter to `XpSearchContactGroupRule.Parameters`, pass
  `ToInt({days})` to the macro methods and add
  `.WhereGreaterThan(nameof(ActivityInfo.ActivityCreated), DateTime.Now.AddDays(-days))` to
  `XpSearchActivityQuery.ContactIds`. Left out because three rules with two parameters each is more
  UI than the request needed, and marketers can combine conditions today.

## Design-spec substitutions in `QueryTesterTemplate.tsx` and `AnalyticsDashboardTemplate.tsx`

- **Simplified:** the owner's design spec names a `Tabs` component for the narrow query tester and a
  `Tag` carrying an icon for every change marker. `@kentico/xperience-admin-components` 31.8.0 has
  neither: its only tab export is `VerticalTab` (a single left-rail item) and `TagProps` has no
  `icon`. The narrow view uses `NameToggleButtons`, the package's horizontal segmented control, and a
  marker is an `Inline` of a stock `Icon` next to the `Tag`. The pipeline-stage panel collapses with a
  native `<details>` rather than a component, and the "results appear here" placeholders are plain
  `Card`s rather than the artboard's dashed outlines, because a dashed border would need a stylesheet
  and the module has no style loader. The pages also carry three inline style objects
  (`src/theme.ts`) for low-emphasis prose, monospace panels and the KPI figure.
- **Ceiling:** the narrow toggle does not carry `role="tablist"`/`aria-controls`, so a screen reader
  hears two buttons rather than a tab set; the collapsed panel is a native disclosure, styled by the
  browser rather than by the theme; and the three inline styles do not respond to a theme that
  changes type scale, only to one that changes the `Colors` tokens they read.
- **Upgrade path:** when the package ships a `Tabs` component and an icon slot on `Tag`, swap them in
  — the call sites are `QueryTesterTemplate`'s narrow branch, `ChangeTag` and `Explanations`. A style
  loader in `Client/webpack.config.js` plus one CSS module would retire `src/theme.ts`; it was not
  worth a build-chain dependency for three declarations. See ADR-0020.

## Contact group scoping in `RuleSelection.InContactGroup` and `RuleConditions.ContactGroup`

- **Simplified:** a rule carries one contact group code name, not a set and not an expression. There
  is no "any of these groups", no "not in this group", and no per-contact scoring. The rule builder's
  Context switch enforces it: only one condition card can own the contact group.
- **Ceiling:** an audience that is a combination of groups needs one rule per group, and the rules
  duplicate everything but the scope. A marketer who wants "grinder shoppers who are not wholesale"
  has to express it in the contact group's own dynamic condition instead.
- **Upgrade path:** make `RuleConditions.ContactGroup` a list (it is JSON now, so no column change)
  and turn `InContactGroup` into a set intersection; the panel's Select becomes multi-select. The
  pipeline side already works on an `IReadOnlySet<string>`, so only the parse and the predicate
  change.

## Contact group resolution in `ContactGroupResolver.ResolveAsync`

- **Simplified:** one query per HTTP request, memoized on `HttpContext.Items` and never beyond it.
  Every search of a consented visitor costs one indexed read of `OM_ContactGroupMember` joined to the
  group code names, even on an installation that has no group-scoped rule at all — the resolver runs
  before the index's rules are known, because the cache key needs the answer.
- **Ceiling:** on a high-traffic site with consented visitors this is one extra round trip per search.
  Group membership is deliberately not cached across requests, because a stale membership shows the
  wrong ranking silently.
- **Upgrade path:** cache per contact for a short sliding window with a dependency on
  `om.contactgroupmember`, accepting the staleness window; or skip the resolver entirely for indexes
  whose rule set contains no scoped rule, which needs the rule load moved ahead of the cache lookup.

## Rule listing contact group column in `ContactGroupCatalog.Label`

- **Simplified:** the listing resolves each row's group display name with one `IInfoProvider.Get(codeName)`
  call per row, served from Xperience's own info-object cache rather than joined in the query.
- **Ceiling:** O(rows) lookups per listing page. Harmless at a page of rules; wrong shape for a listing
  of thousands.
- **Upgrade path:** load the whole catalog once per page render into a dictionary, or add the join to
  the listing's `QueryModifiers`.

## Page Builder previews in `XpSearchMountWidgetViewComponent<T>.BuildEditorPreview`

- **Simplified:** the preview an editor sees in the Page Builder is markup assembled in C# from the
  widget's properties alone. It never asks the search API anything, so counts, facet values, result
  titles and page numbers are placeholder bars and fixed digits, and the shape is fixed (three facet
  rows, a two-level tree, at most four result cards, three page numbers) regardless of the corpus.
  The preview body is `aria-hidden="true"`: only the badge is announced, so a screen-reader user in
  the builder is told which widget sits there but not what it looks like.
- **Ceiling:** an editor cannot tell from the builder whether a facet attribute actually has values,
  whether a result template renders as intended, or how tall the widget will really be — the page
  still has to be previewed for that. A custom result template is not exercised at all: the preview
  shows generic skeleton cards and names the template in the note.
- **Upgrade path:** for real data, run one search server-side in edit mode (a `SearchClient` call in
  `BuildEditorPreview`, cached per index+config) and feed the values into the same markup; for
  templates, render the registered `SearchResultTemplate` server-side against that response.

## Editor preview styling in `themes/src/shell.css` (`.xps-editor-preview`)

- **Simplified:** the preview inherits the site's own widget styling, because it reuses the widget
  classes and the same two stylesheets `<xps-search-assets />` loads. Only the frame — spacing, the
  badge and the note — is new, and the per-widget modifier classes (`xps-editor-preview--facet-list`
  and friends) are emitted but unstyled.
- **Ceiling:** a host that omits `<xps-search-assets />` from the layout the Page Builder renders, or
  that ships its own design system and loads neither stylesheet, gets an unframed preview: the badge
  and note are plain text and the dashed border is missing. Nothing is broken, but the "this is not
  live" signal is weaker.
- **Upgrade path:** if that turns out to be common, emit a scoped `<style>` once per page from the
  first preview on it (a request-scoped flag on the editor context) instead of relying on the
  stylesheet.

## Query rewrites and the search reports, in `XpSearch.Core/Pipeline/Stages/QueryRewriteStage.cs`

- **Simplified:** a rewrite (remove a word, replace a word, replace the query) changes
  `SearchContext.QueryText` before the query is parsed, but the activity journal and the query log
  keep recording the **normalized original** query the visitor typed (`ISearchRequestJournal`, AN-4).
  Word matching in a rewrite is whole-word and space-separated: the query is split on spaces and each
  token compared case-insensitively, so a multi-word "word to remove" never matches and punctuation
  stuck to a word makes it a different word.
- **Ceiling:** the reports cannot answer "what did the search actually run" for a rewritten query, and
  a report reader who knows a rewrite exists has to hold both in their head. A marketer who writes
  *sofa bed* into **Remove a word** sees nothing happen, with no error to explain it.
- **Upgrade path:** for the reports, add a second column (`SearchedAs`) to the query log row and let
  the journal take both texts. For phrases, match the token list against the query's token list as a
  sliding window - the same comparison `RuleSelection.MatchesAnalyzed` already does.

## No typo tolerance in rule matching, in `XpSearch.Core/Tuning/RuleSelection.cs`

- **Simplified:** both comparisons a query condition can make are exact - a case-insensitive substring
  or prefix comparison on the raw text, or a term-by-term comparison of the analyzed text (ADR-0022).
  A misspelled query fires no rule.
- **Ceiling:** *esspresso* misses every rule about *espresso*, and a marketer's only remedy is to add
  the misspelling as a synonym or write a second rule for it. This is felt most on redirect rules,
  where the visitor gets ordinary (usually poor) results instead of the page that answers them.
- **Upgrade path:** the analyzed path already reduces the query to terms, so a fuzzy comparison could
  be dropped into `MatchesAnalyzed` alone - a bounded Damerau-Levenshtein distance per position, as
  Lucene's `FuzzyQuery` uses - behind a per-condition opt-in, so exact rules stay exact.

## `Bury.FilterExpression` in `XpSearch.Core/Tuning/IRelevanceTuningSource.cs`

- **Simplified:** the record carries a filter expression next to the target id so bury has the same
  dual targeting as boost, but `PinnedAndBuriedStage` only ever reads the id.
- **Ceiling:** "bury everything in the *Discontinued* category" cannot be expressed; a marketer needs
  one bury rule per document, or a filter rule that inverts the intent.
- **Upgrade path:** evaluate the expression against the projected page in `PinnedAndBuriedStage`
  (the schema is on the context, and `RuleFilterExpression.Parse` already exists), or - better for
  counts - turn it into `MUST_NOT` clauses in `BoostRulesStage`, the way `Hide` works.


## Condition cards are a presentation of one condition set, in `XpSearch.Admin/Client/src/rule-builder/model.ts`

- **Simplified:** the model stores a single `RuleConditions` per rule (ADR-0022), but the design shows
  a *list* of condition cards. `split` derives the cards from a stored rule (the query becomes one,
  the filters and context another - exactly how canvas 5a reads), `merge` folds them back, and
  `conflicts` refuses a second card that claims the query or the context rather than letting one
  silently overwrite the other.
- **Ceiling:** the cards are not independent conditions. A marketer who expects "condition 1 OR
  condition 2", or two query patterns on one rule, is refused with a message instead of being given
  what the screen's plural implies. Reordering cards changes nothing, and deleting the card that owns
  the query removes the query from the rule.
- **Upgrade path:** make the storage hold a list - `RuleConditions[]` with an all/any join - and the
  cards become real. That is a Core model change and a second storage migration, so it waits until
  someone asks for OR.

## A comma cannot appear in a filter value, in `XpSearch.Core/Tuning/RuleFilterExpression.cs`

- **Simplified:** the expression grammar is `field:value` pairs separated by commas, with no escape
  and no quoting. `Compose` writes such a value as typed, and `Parse` then reads it back as two
  malformed pairs and drops them.
- **Ceiling:** a facet value that genuinely contains a comma ("Coffee, whole bean") cannot be filtered
  on by a rule. Nothing warns: the row composes, saves, and the rule matches nothing. The value
  drop-down offers it, because the index really holds it.
- **Upgrade path:** allow `field:"value"` with a doubled quote as the escape, in `Parse` first (old
  expressions keep parsing), then in `Compose` for any value holding a comma or a quote. Both twins —
  `expression.ts` — have to move together. Not done because no Xperience taxonomy tag code name can
  contain a comma; only a pushed external document could.

## Three pieces of rule text logic are written twice, in C# and in `Client/src/rule-builder/`

- **Simplified:** the same rules exist on both sides of the wire, because each of them has to run
  while the marketer is still typing, with nothing saved:
  - the summary sentences — `XpSearch.Admin/Tuning/RuleSummary.cs` and `summary.ts` (conditions for
    the listing column and the builder's rows; CR-5 added the action sentences, worded identically);
  - what refuses a save — `XpSearch.Admin/Tuning/RuleValidation.cs` and `wrongWith` in `model.ts`.
    The C# one is the check that guards the save; the TS one only saves a round trip, and its
    messages are copied word for word;
  - the filter expression grammar — `XpSearch.Core/Tuning/RuleFilterExpression.cs` and
    `expression.ts`, which the attribute rows and the "Edit as text" box parse and compose with.
- **Ceiling:** they can drift — a wording or grammar change has to be made in both. The summaries
  already differ slightly on purpose: the listing leaves the contact group out because it has a column
  of its own, and the panel's "any language" only appears when Context is on. The builder shows an
  item's title where the listing shows its stored id, because only the builder resolves ids.
- **Checks:** each side has its own. C#: `RuleSummaryTests`, `RuleValidationTests` and
  `TuningTests.FilterExpression_*`. TypeScript: `expression.test.ts` (`npm test` in
  `src/XpSearch.Admin/Client`, node's own runner, no framework) — deliberately the same cases as the
  C# ones. `wrongWith` and `describeAction` have no TypeScript check; their C# twins do, and the
  server refuses anything the client wrongly lets through.
- **Upgrade path:** either have the builder ask a `Describe` page command for each summary (a round
  trip per keystroke, for a string), or generate the TypeScript from the C# the way
  `Client/scripts/contract.mjs` generates the contract types. Worth doing only if a fourth twin
  appears.

## Attribute rows only offer facetable fields, in `XpSearch.Admin/Client/src/rule-builder/AttributeRows.tsx`

- **Simplified:** the **Attribute** drop-down is `FacetAttributeOptions.BuildOptionsAsync`'s list —
  the fields the index can facet on — and **Value** is a facet query over the chosen one. A field that
  is filterable but not facetable is not offered, and a value no document currently carries is not in
  the list. Both fall back to a plain text box, and "Edit as text" writes anything.
- **Ceiling:** a marketer preparing a rule for a category that has no published content yet cannot
  pick its value from the list; nothing on screen explains why it is missing. A filter on a non-facet
  field is still typed, so a typo still produces a condition that never fires.
- **Upgrade path:** the value list would have to come from the taxonomy rather than from the index
  (the tag exists before anything is tagged with it), which means a second source behind
  `GetAttributeValues` keyed on the field kind.

## Action reorder has no touch drag, in `XpSearch.Admin/Client/src/rule-builder/ActionRow.tsx`

- **Simplified:** the pointer half of the reorder is the browser's own HTML5 drag events
  (`dragstart` on the grip, `dragover`/`drop` on the row wrapper), with no dependency and no pointer
  fallback. The keyboard half is hand-rolled in `reorder.ts` to the WAI-ARIA drag pattern and is the
  only path that does not need a drag: space or enter lifts, the arrows move, escape cancels.
- **Ceiling:** most touch browsers never raise the drag events for a finger, so on a tablet without a
  keyboard the actions cannot be reordered at all — the grip looks draggable and does nothing.
  Deleting and re-adding an action in the wanted order is the only way round it. A second, smaller
  gap: a lifted row stays lifted if focus leaves the grip (clicking elsewhere on the page), because
  the grip has no blur handler — the rows re-render under a keyboard move, so a blur handler would
  drop the row mid-move. Focus back on the grip and escape or space still resolves it.
- **Upgrade path:** add a pointer-events path beside the drag one — `pointerdown` on the grip with
  `setPointerCapture`, `pointermove` picking the gap from the row rectangles (the same arithmetic
  `landing` already does), `pointerup` dropping — and `touch-action: none` on the grip. That covers
  touch and mouse in one handler and lets the HTML5 drag events go. For the stuck lift, track whether
  focus went to *another* grip before treating a blur as a drop.

## The item picker reads the index one id at a time, in `XpSearch.Core/Search/IndexDocumentLookup.cs`

- **Simplified:** resolving the ids a rule's actions name is one `TermQuery` per id inside one
  searcher lease, and one round trip to the index per rule load.
- **Ceiling:** O(n) queries in the number of item-targeting actions on the rule being opened. A rule
  with fifty pins costs fifty term lookups; each is a single-term match on an indexed keyword field,
  so this is cheap, but it is not one query.
- **Upgrade path:** a single `BooleanQuery` of `SHOULD` term clauses over the id field, taking the
  top `ids.Count` hits. Not done because the loop is what makes "this id resolved, that one did not"
  obvious, which is the whole point of the orphan warning.

## Flat rule columns are dropped, not archived, in `XpSearch.Admin/Persistence/RuleStorageMigration.cs`

- **Simplified:** `RetireLegacyColumns` removes the nine ADR-0014 columns from the class once every
  row is converted, which drops them from the table. There is no backup copy and no way back.
- **Ceiling:** if the mapper ever got a row wrong, the original values are gone after the first
  successful start. They cannot simply be left in place - several are `NOT NULL` with no default, so
  every insert the new builder makes would fail - but "orphaned and nullable" was a third option that
  was not taken, because it leaves a permanently confusing table.
- **Upgrade path:** before dropping, write the row's flat values into a `RuleLegacy` JSON column, or
  export the table to the event log. Cheap to add if a customer's migration ever goes wrong; pointless
  weight if it does not.

## The migration and the retirement are not one transaction, in `XpSearch.Admin/Persistence/RuleStorageMigration.Run`

- **Simplified:** rows are converted one `Set` at a time and the columns are dropped afterwards, with
  no ambient transaction around either.
- **Ceiling:** a crash mid-pass leaves a half-converted table. That is safe - the marker is per row,
  so the next start finishes the job, and the drop only happens when nothing is left - but between the
  two starts the tuning source is reading a mixture, which it handles by converting flat-looking rows
  on the fly. A `Set` that fails for one row (a name that violates something) aborts the pass and the
  columns stay, which is the right way round but is not reported anywhere.
- **Upgrade path:** wrap the loop in `CMSTransactionScope` and log a warning per row that will not
  convert. Worth doing the first time a real upgrade fails.

## Two routed instances share one set of query params, in `XpSearch.Widgets/Client/src/routing.ts`

- **Simplified:** `defaultStateToRoute`/`defaultRouteToState` map state onto unprefixed params -
  `q`, `page`, `sort`, and the facet attribute names - and `createRouter` is created per instance with
  no knowledge of the instance id. Nothing detects a second routed instance on the page.
- **Ceiling:** two instances with `routing: true` on one page write the same params, so each
  `setState` overwrites the other's (`router.write` also deletes every param the mapping owns before
  re-appending), and on load both hydrate from the same `q`. It fails silently - no warning. The
  `XpSearch.SearchBox` widget's *Sync search state to the URL* property (default on) is the way out:
  untick it on the secondary search. The Page Builder guide states the one-per-page rule.
- **Upgrade path:** pass the instance id into `createRouter` and namespace the params of every
  instance but the first (`q` -> `products_q`), or have the bootstrap warn when a second group
  arrives with routing enabled. Either is a contract change to shareable URLs, so it waits for a
  project that actually places two searches on a page.

## The first-load journal handoff is per application instance, in `SearchRequestJournal.Record`

- **Simplified:** the results widget hands its server-rendered `queryId` to the client
  (`initialQueryId`), the client sends it on its first query, and the journal drops a `queryId` it has
  already recorded - the check is `IQueryContextMap.Get`, the same in-process map clicks resolve their
  query text through (10 000 entries, 30 minutes).
- **Ceiling:** behind a load balancer, or after an application restart between the two requests, the
  hydration query lands where the id was never recorded and the page load produces its second query
  log row again - the pre-PB-6 behaviour for that request. An id that has aged out of the map (a page
  left open for over 30 minutes before the bundle ran) does the same.
- **Upgrade path:** make the write idempotent in the database instead: have `InfoQueryLogStore.
  AppendAsync` update the row with that `LogQueryID` when one exists rather than insert. It costs a
  SELECT per logged search on the queue worker, which is why the in-memory check came first.

## The Results widget's field selectors list every index, in `IndexFieldSelectorDataProvider`

- **Simplified:** *Fields to show*, *Title attribute* and *Link attribute* are general selectors, and a
  general selector's data provider is resolved from the container without the dialog's other values -
  unlike the facet attribute drop-down, which is a `FormComponentConfigurator` and can read `Index`.
  The options are therefore the union of the retrievable fields of every registered index.
- **Ceiling:** with more than one index an editor can pick a field the selected index does not have;
  that field simply comes back empty, exactly as typing it did before. With one index - the usual case
  - the list is already exact.
- **Upgrade path:** an index-scoped `FormComponentConfigurator<GeneralSelectorComponent>` in
  `XpSearch.Admin` (where the configurator base class lives; `XpSearch.Widgets` deliberately does not
  reference `Kentico.Xperience.Admin`), registered under an identifier the way
  `XpSearchConstants.FacetAttributeConfiguratorIdentifier` is, setting the option list from the
  `Index` value.

## Snippet attributes stayed a text area, in `ResultsWidgetProperties.SnippetAttributes`

- **Simplified:** the other field properties became selectors; this one did not, because the order of
  its values decides which attribute wins and the general selector is not documented as preserving the
  order values were selected in.
- **Ceiling:** the editor types field names here and gets no schema help or validation.
- **Upgrade path:** confirm the ordering behaviour of the selector on a host (or ship a small custom
  form component with explicit reordering) and move the property over with the same
  new-property-plus-fallback scheme `FieldNames`/`Fields` uses.

## The server-rendered first paint is replaced by skeletons, in `Client/src/widgets/results.ts`

- **Simplified:** the widget's first client render empties its container (`createRoot`), so the
  server-rendered block goes at the moment the bundle hydrates - before the client's own response has
  arrived, while the widget is in its loading state.
- **Ceiling:** on a slow search, a visitor sees real results, then skeleton rows, then the same results
  again. The content is never wrong, but the flicker is visible on a cold index.
- **Upgrade path:** keep a `[data-xps-server-rendered]` child in place while `results === null` and
  remove it on the first render that has results. That is a second rendering state in a widget that
  currently has three, so it waits for someone to complain about the flicker.

## First paint buckets into A, in `ExperimentAssignmentResolver.BucketId`

- **Simplified:** a visitor with no `xpsearch_bucket` cookie can only be given one while a
  `Set-Cookie` header is still allowed. When the response has already started - DX-2's server-rendered
  results widget, whose render runs the pipeline mid-stream - when there is no `HttpContext` at all, or
  when the visitor's cookie level is below *Essential*, the request is bucketed into **variant A** and
  nothing is written. The next API query assigns the cookie and the visitor becomes sticky from then
  on. Bucketing on a throwaway id instead would flip the visitor's variant on every request.
- **Ceiling:** the very first server-rendered paint of a brand new visitor always shows variant A, and
  it is journaled as A, so A collects a few extra low-value impressions per new visitor. A visitor who
  refuses Essential cookies is permanently in A. Both lean the numbers towards A by a small,
  unmeasured amount; the owner accepted this in the amendment.
- **Upgrade path:** assign the bucket cookie in middleware, before anything can write to the response
  body - then the id exists by the time any render or query asks for it, and this whole branch is
  unreachable.

## Cloning and promoting tuning rows in `XpSearch.Admin.Tuning.ExperimentService`

- **Simplified:** create / promote / discard walk the rows one info-provider call at a time, with no
  transaction and no batching, and they are verified against the database only on a running instance -
  the unit tests cover the state machine (`ExperimentRules`) and the row-scoping condition
  (`VariantScope`), because an `Info` object cannot be constructed without Kentico's IoC container.
- **Ceiling:** an index with thousands of tuning rows makes creating an experiment slow, and a crash
  halfway through a promotion leaves some rows promoted while the experiment is still Running. The
  operation is repeatable (the remaining live rows are deleted and the remaining variant rows cleared
  on the next attempt), but a search in between sees a mixture of both tunings.
- **Upgrade path:** wrap each operation in a `CMS.DataEngine.CMSTransactionScope` and replace the
  row-by-row loops with bulk `UPDATE`/`DELETE` over the object query once the row counts justify it.

## The comparison report reads the range twice, in `ExperimentDetailPage.ReportAsync`

- **Simplified:** each variant's side is a separate `ISearchAnalyticsService.GetReportAsync` call with
  the experiment and variant on the query, and the service filters the range's rows in memory after
  `IQueryLogStore.ReadAsync` has read them. So one page load reads every query log row of the
  experiment's whole window twice, and builds every top-N list it does not show. The alternative -
  a variant-aware store read, or one read split in memory by the page - would either widen
  `IQueryLogStore` or restate the metric definitions the Analytics page already owns, and drifting
  definitions are the one thing an A/B report must not have.
- **Ceiling:** a long experiment on a busy index (months, millions of logged searches) makes the
  Overview page slow and memory-hungry, the same ceiling the analytics dashboard already has for wide
  ranges, doubled. There is no paging or aggregation in SQL.
- **Upgrade path:** push the experiment and variant filter into `IQueryLogStore.ReadAsync`, then into a
  `GROUP BY` that returns the four totals rather than the rows - the report only ever shows totals.

## The popularity signal learns from the ranking that produced it, in `PopularityAggregator.Aggregate`

- **Simplified:** the only evidence is clicks that actually happened, so a document that never
  appeared on page one can never earn a boost. `Damp` (`log2(position + 1)`) is a crude
  inverse-propensity correction: it values a click further down the list more, which is the cheapest
  honest defence against the feedback loop, but it does not model what a visitor actually saw.
- **Ceiling:** popularity entrenches what is already popular, and a new document starts from zero -
  a rich-get-richer bias that grows the longer the boost is on. There is no exploration, no
  impression data, and no significance test anywhere: the cap (2x) and the opt-in flag are what keep
  the damage bounded.
- **Upgrade path:** log impressions (which ids were returned for which query) alongside the clicks,
  then compute a click-through *rate* per (query, document) instead of a click count, and reserve a
  small share of traffic for exploration. Both need a contract change on the events endpoint.

## The task reads the whole lookback window into memory, in `XpSearchPopularityTask.Execute`

- **Simplified:** one `IQueryLogStore.ReadAsync` for every index and the whole window, grouped in
  memory - the same read-once shape `SearchAnalyticsService` uses, so the two can never disagree about
  what a click is. Storage is written per index with row-by-row info-provider calls and no
  transaction; a run that dies halfway leaves some indexes on the previous signal.
- **Ceiling:** a busy site with a long lookback holds a month of query log rows at once during the
  run. A crash between an index's score delete and its inserts leaves that index with an empty signal
  (no boost) until the next run - degraded, never wrong.
- **Upgrade path:** page `ReadAsync` by index and by day, or move the aggregation into a `GROUP BY`
  over `LogClickedResultID`; wrap each index's replace in a `CMSTransactionScope`.

## The boost is one SHOULD clause per scored document, in `PopularityBoostStage`

- **Simplified:** the signal is capped at `PopularityDocumentLimit` (100) documents per index and each
  one becomes a `TermQuery` on the document id with its factor, exactly like `BoostRulesStage` applies
  a rule's boost. Lucene adds that clause's score rather than multiplying the hit's, so "2x" is a
  bound on the boost clause, not a literal doubling of the final score.
- **Ceiling:** raising the limit grows every query of that index by one clause per document, and the
  effect of a factor depends on the query's own score scale - a boost is worth relatively more on a
  weak text match than on a strong one. Popularity can therefore reorder near-ties confidently and
  barely move a decisive text match, which is the intended bias but is not a promise.
- **Upgrade path:** a custom `Rescorer` (Lucene 4.8 has `QueryRescorer`) over the top-N hits would
  multiply the score instead of adding to it and would cost one clause instead of a hundred; it
  needs `ExecuteSearchStage` to expose the collector.

## A reformulation is two adjacent rows, not one visitor, in `SynonymMiner.Mine`

- **Simplified:** the query log carries no visitor or session identifier and may not gain one (no new
  cookie, no consent surface - ADR-0026), so "the same visitor searched again" is a row with no click
  followed by the nearest clicked row within 60 seconds, inside one index. Any two visitors active in
  the same minute can produce that shape.
- **Ceiling:** the noise floor is traffic-dependent and unmeasured - the busier the index, the more
  invented pairs the window contains, and the occurrence threshold (3) is the only defence besides a
  human reading the pair. Conversely a visitor who rephrases after two minutes, or on a second visit,
  contributes nothing. There is no significance test and no attempt at one.
- **Upgrade path:** if an owner-approved per-visitor correlator ever exists (a consented session id on
  the log row, or the AN-4 journal keyed by contact for consenting visitors), group by it instead of
  by time inside `Mine`; nothing outside that method assumes adjacency.

## Mining is O(rows x window) per index, in `SynonymMiner.Mine`

- **Simplified:** the window's rows are sorted once, then each clickless row scans forward with
  `Skip`/`TakeWhile` until the window closes - re-enumerating the tail per failed row rather than
  keeping a cursor. It runs on the rows `XpSearchPopularityTask` has already read, so it costs no
  extra query.
- **Ceiling:** the forward scan is bounded by the time window, not by row count, so it degrades on an
  index that logs hundreds of searches per minute - the scan for each failed row walks that minute
  again.
- **Upgrade path:** a single forward pass with an index cursor (both loops are already in timestamp
  order), or `GROUP BY` the pairs in SQL once the log is aggregated there.

## An approved pair becomes a two-way group, in `SynonymSuggestionGroup.For`

- **Simplified:** approval always writes a bidirectional `XpSearchSynonymInfo` with both phrases as
  terms; the amendment's "rewrite" alternative is left to the editor, who switches the created group
  to one-way. Commas in a mined query are replaced with spaces, because a comma is the term separator
  of the stored value.
- **Ceiling:** a genuinely asymmetric pair (a misspelling, a discontinued product name) widens both
  searches until someone edits the group, and a query whose commas mattered loses that structure.
- **Upgrade path:** offer direction on the approve action (two commands, or a small edit page before
  the write) once host use shows editors actually want it.

## A first-paint visitor is in neither bucket, in `SearchBucket.IsInBucket` (via `VisitorBucketProvider`)

- **Simplified:** the "search A/B bucket" personalization condition is false whenever no bucket id can
  be read or assigned - a visitor below the Essential cookie level, or a widget rendered after the
  response started streaming. It does not fall back to a throwaway id.
- **Ceiling:** a brand new visitor whose cookie could not be written mid-response sees the *original*
  variant on that first paint, so a page A/B split leans very slightly towards the original on first
  visits; from the next request on the visitor is bucketed and stays put. XP-1's search experiments
  make the same trade the other way (they bucket such a request into A, because a search still has to
  be answered from some tuning).
- **Upgrade path:** assign the bucket cookie in middleware before anything renders, which removes the
  case entirely for both callers.

## Only the newest 100 searches are considered, in `RecentSearchProvider.MaxSearches`

- **Simplified:** the "searched for" condition reads the contact's 100 most recent search activities
  once per request and applies each condition's day window in memory, rather than issuing one query
  per configured window.
- **Ceiling:** a contact who ran more than 100 searches inside the configured window is only matched
  against the newest 100 of them, so a very heavy searcher can stop matching an old term earlier than
  the day count promises. Every extra condition on the page is free, which is the trade.
- **Upgrade path:** query per distinct day window (memoized per window on `HttpContext.Items`), or push
  the term match into SQL with a `LIKE`, once a host reports a false negative.

## Required-column guard is a source scan, in `InfoCreationSiteTests` (`tests/XpSearch.Core.Tests/InfoCreationSiteTests.cs`)

- **Simplified:** the RK-2 rule (every non-`allowEmpty` form field must be set at every `new
  XpSearch...Info { ... }` site) is checked by reading the `XpSearch.Core` sources and matching
  `Name =` inside each object initializer, because constructing an Info object needs Kentico's IoC
  container and cannot happen in a unit test.
- **Ceiling:** only `XpSearch.Core` and only fields assigned in the initializer itself - a site in
  `XpSearch.Admin`/`XpSearch.Ingestion`, or one that assigns a required field after construction, is
  not covered; the check also relies on the sources sitting next to the test file (it is inconclusive
  otherwise).
- **Upgrade path:** move it to a Roslyn analyzer over the whole solution, or drop it once a host-level
  integration test can create the objects for real.
