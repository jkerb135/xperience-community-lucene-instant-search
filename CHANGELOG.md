# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public behaviour API (spec §5.7) or the JSON contract
(spec §4.2, as amended by ADR-0010) are always major-version events.

## [Unreleased]

- **Changed (breaking):** the relevance-tuning admin pages moved inside the search index. They are now
  reached at **Lucene Search → indexes → click an index → the *Tuning* sidebar**: *Settings*, *Rules*,
  *Synonyms*, *Stopwords*, *Field weights*, *Query tester*, *Analytics* and *Status*. Clicking an index
  row in the Lucene index listing opens the sidebar instead of the integration's bare edit form; that
  form is the sidebar's *Settings* page. See ADR-0017.
- **Changed (breaking):** the old URLs are gone. `/admin/xpsearch-tuning/rules`, `/synonyms`,
  `/stopwords`, `/field-weights`, `/query-tester`, `/analytics` and `/index-status` are now
  `/admin/lucene/indexes/{id}/tuning/rules`, `/synonyms`, `/stopwords`, `/weights`, `/query-tester`,
  `/analytics` and `/status`. Re-point any bookmarks.
- **Changed (breaking):** permissions for those pages are now assigned on the **Lucene Search**
  application in *Role management*, not on *Search tuning*. A UI page is governed by the nearest
  ancestor application, and the moved pages sit under the Lucene integration. Grant *View*,
  *Create*, *Update*, *Delete* and *Rebuild* there. `XpSearch.SearchTuning` grants now cover only the
  two pages that stayed.
- **Changed (breaking):** the *Search tuning* application is renamed **Search ingestion** and holds
  only **API keys** and **Ingestion log**. Its identifier (`XpSearch.SearchTuning`) and slug
  (`xpsearch-tuning`) are unchanged, and it no longer declares the `UPDATE` permission, which none of
  its remaining pages evaluate.
- **Changed (breaking):** the index is no longer a field. Every tuning listing is filtered to the index
  in the URL and has lost its Index column; every tuning form shows the index read-only and takes its
  value from the URL. A row whose stored index differs from the URL's is refused on save rather than
  moved. The query tester's index selector and the analytics dashboard's "every index" option are gone
  — analytics now reports the index you are in.

- **Changed:** the search start time lives on `SearchContext` (`StartedTimestamp`, `Elapsed`) instead of
  in the internal `SearchTimingStage`, which is gone. The stage was never part of the documented
  stage-order table and nothing outside the library could reference it; the logged processing time and
  the response's `tookMs` now come from the same clock.
- **Fixed:** a redirect no longer disappears when the response comes from the cache. The default
  response cache re-issues `queryId` on every hit by copying the cached response, and that copy
  dropped `redirect`, so a host with caching enabled always saw `"redirect": null`. The copy is
  now a clone, so any future contract member is carried over too.
- **Fixed:** `_source` is facetable in practice, not only in the schema. Every document - Xperience
  content and externally pushed alike - now carries a `FacetField` for it alongside the term, so
  `facets: ["_source"]` returns counts and `filters.facets` can drill down to one provenance. The term
  is unchanged, so the ingestion status counts and `clear?source=` behave exactly as before.
- **Fixed:** `GET …/status` no longer reports `health: "degraded"` while a queued write is merely
  waiting to be indexed. `degraded` now means work failed to reach the index and nothing has succeeded
  since. `clear` and `delete` are asynchronous like `rebuild`, and the guide says so.

- **Added:** relevance tuning (spec §8). A new **Search tuning** administration application, under
  *Development*, with listings and editing pages for rules, synonyms, stopwords and field weights,
  built entirely on the built-in listing and edit UI page templates — no custom React. Four custom
  module classes (`XpSearch.Rule`, `XpSearch.Synonym`, `XpSearch.FieldWeight`,
  `XpSearch.StopwordList`) are installed on startup under their own module,
  `CMS.Integration.XpSearchTuning`. Rules pin, bury, boost or filter results, with a
  `Contains`/`Exact`/`StartsWith`/`Always` condition, an optional `Runs from`/`Runs until` schedule and
  a priority; conflicts resolve by priority then rule id, and for pin and bury the first rule to name
  a result wins. A pinned result the query did not match is injected only if it still matches the
  active filters. `explain=true` now fills `ranking.boosts` with `rule:<name>`, `weight:<field>×<w>`
  and `synonym:<term>` entries. See `docs/guides/relevance-tuning.md` and ADR-0014.
- **Added:** `XpSearch.Core.Tuning.IRelevanceTuningSource` and the four query-pipeline stages that
  read it — `SynonymExpansionStage` (200), `StopwordRemovalStage` (300), `BoostRulesStage` (700) and
  `PinnedAndBuriedStage` (900). Core registers an empty source, so search behaves exactly as before
  without `XpSearch.Admin`; `services.AddXpSearchAdmin()` swaps in the database-backed one, cached per
  index through `IProgressiveCache` and invalidated by the object types' own cache dependencies
  (spec §8.5).
- **Added:** the ingestion admin surface (spec §10.8) inside the same application — an **API keys**
  listing with a create page that shows the plaintext key exactly once, an **Index status** page with
  document counts by source, the last external write and a rebuild trigger, and an **Ingestion log**
  listing filtered by index and ordered newest first. `XpSearch.Admin` now references
  `XpSearch.Ingestion`; the reasoning is in ADR-0014.
- **Added:** redirect rules act (spec §8.2). `SearchResponse` gains a required, nullable
  `redirect: { url, rule }` — always present, `null` when no rule matched. `BoostRulesStage` sets it
  from the first matching redirect rule in the existing precedence order (priority, then id); a rule
  with an empty URL is skipped. The search is not short-circuited: the response carries its results
  next to the destination, and `explain=true` lists the rule as `rule:<name>` like any other. On the
  client, `withSearchBox` gains `submit(query)` beside `apply(query)` and the `searchBox` widget
  wires it to the form's submit event: a redirect is followed **only** for a query the visitor
  submitted, never on a keystroke and never on the search a restored URL runs at page load.
  `followRedirects: false` opts out and `withResults` exposes `redirect` so a template can render
  "Redirecting…" instead. This supersedes the "stored but not applied" note of ADR-0014.

- **Added:** the **Query tester** page (spec §8.4), under *Search tuning*. Pick an index, type a query
  and an optional language, and see the ranking twice side by side: **with rules** (what a visitor
  gets) and **without rules** (no rules, synonyms, stopwords or field weights at all). Each result
  shows its position, final score, base score and the boosts and rules that applied to it, and each
  side lists how the query itself was rewritten. Results that differ between the two sides are marked
  moved up, moved down, added or removed. Both sides run with `explain=true`; neither is served from
  the search cache, and neither is written to the analytics query log. See
  `docs/guides/relevance-tuning.md` and ADR-0016.
- **Added:** the **Analytics** dashboard (spec §9.3), under *Search tuning*: index selector, 7/30/90-day
  presets or a custom `yyyy-mm-dd` range, and all six reports from `ISearchAnalyticsService` — search
  volume over time (bar chart with a table fallback), zero-result queries, top queries, click-through
  rate and mean clicked position by query, and slowest queries. Every zero-result row has a **Create
  rule** button that opens the rule form with the index and the query pre-filled. See
  `docs/guides/analytics.md`.
- **Added:** an admin client module in `src/XpSearch.Admin/Client` (organization `yourco`, project
  `xperience-search-admin`), built with webpack and embedded into `XpSearch.Admin.dll`, so a consumer
  gets both pages from the NuGet package with no `appsettings.json` change and no dev server.
  **Building `XpSearch.Admin` now requires `npm ci && npm run build` in `src/XpSearch.Admin/Client`
  first** — the build fails with instructions when the bundle is missing. See
  `docs/guides/admin-client-development.md`.
- **Changed:** the *Search tuning* application now declares the `VIEW`, `CREATE`, `UPDATE` and `DELETE`
  permissions, so they can be assigned to roles in **Role management**; the two new pages and their
  page commands evaluate them (`VIEW` to read, `CREATE` for the *Create rule* deep link).

- **Added:** search analytics (spec §9). Four custom activity types (`xpsearch_query`,
  `xpsearch_noresults`, `xpsearch_click`, `xpsearch_conversion`) are created on startup and logged
  through `ICustomActivityLogger` for visitors whose cookie level is *Visitor* or higher — below that
  nothing is logged and nothing is thrown. Independently of consent, every search is written to the
  new `XpSearch.QueryLog` module class through a `ThreadQueueWorker`, a click event records the
  clicked position on its row, and `XpSearchQueryLogRetentionTask` (identifier
  `XpSearch.QueryLogRetention`, default 180 days) prunes it — create its configuration once in the
  *Scheduled tasks* application. `ISearchAnalyticsService` returns top queries, zero-result queries,
  click-through rate, average clicked position, daily volume and slowest queries (p95); the
  dashboard page itself is still pending (KNOWN-LIMITATIONS). See `docs/guides/analytics.md` and
  ADR-0015.
- **Added:** `SuggestMode.QuerySuggestions` now works: `/api/xpsearch/suggest` answers from the
  logged popular queries of the last `Analytics.QuerySuggestionDays` days (spec §4.3, §13.6). It
  previously returned an empty list with a warning.

- **Fixed:** the Page Builder widgets and the admin facet-attribute selector were invisible to
  Xperience. `XpSearch.Widgets`, `XpSearch.Admin` and `XpSearch.Core` did not carry
  `CMS.AssemblyDiscoverableAttribute`, so the system never scanned their `RegisterWidget`,
  `RegisterFormComponentConfigurator`, `RegisterModule`, `RegisterObjectType` and
  `RegisterScheduledTask` attributes and none of the seven widgets appeared in the Page Builder. A
  test now asserts the attribute on every shipped assembly that registers something by attribute.
- **Fixed:** the ingestion object types are registered. `XpSearchApiKeyInfo`,
  `XpSearchExternalDocumentInfo` and `XpSearchIngestionLogInfo` defined an `ObjectTypeInfo` but no
  `[assembly: RegisterObjectType]`, so no `IInfoProvider<T>` reached the container and a host failed
  DI validation on startup. Hosts that declared them as a workaround can delete that file.
- **Fixed:** a pushed document is searchable in the same process, without a restart. The Lucene
  integration caches one searcher per index and its client only invalidates it on rebuild and index
  deletion, never after an in-place upsert or delete, so `waitForIndex: true` returned before the
  document could be found and `GET …/status` reported a stale total. Every write through
  `ILuceneClient` now drops the integration's cached searcher as well as this library's response
  cache.
- **Fixed:** results render their title and link again. The default JavaScript template read
  `attributes.title`, `url` and `contentType` while the server projected the integration's `Title`,
  `Url` and `ContentTypeName`. The server owns the wire names of the base fields now — `title`,
  `url`, `contentType`, `language`, with the document id staying the result's own `id` — and maps
  them onto the Lucene fields; a field detected from a content type keeps its Xperience name on both
  sides. The `results` widget gained `titleAttribute`, `urlAttribute` and `snippetAttributes`
  params. A tuning rule's `field:value` filter expression resolves its field through the schema too,
  so `contentType:Article` reaches the `ContentTypeName` field the documents carry. **Breaking** for
  any client that read `attributes.Title`, `attributes.Url` or filtered on `ContentTypeName` /
  `LanguageName`.
- **Fixed:** the facet configuration is derived from the detected schema before anything is mapped.
  The strategy used to register a dimension the first time it mapped a document carrying one, so a
  fresh index handed the Lucene client an empty `FacetsConfig` and a document with two tags in one
  dimension failed the whole batch with *dimension "X" is not multiValued*. A host no longer needs a
  `FacetsConfigFactory` override. `XpSearchIndexingStrategy`'s constructor takes two more services
  (`ILuceneIndexAccessor`, `IIndexSchemaProvider`), which is **breaking** for derived strategies.
- **Fixed:** `GET …/status` counted replaced documents twice — `bySource.xperience` read 64 on an
  index of 32 documents — because a term's document frequency includes documents deleted from a
  segment that has not been merged away. Every figure counts live documents now, so the per-source
  counts add up to the total.
- **Fixed:** the worked custom-widget example in `docs/guides/custom-widgets.md`. The previous
  "dropdown facet in 40 lines" did not typecheck under `strict`, rendered `xps-dropdown__*` class
  names that exist in neither `MARKUP.md` nor either stylesheet and no `xps` class on its root,
  derived element ids from `container.id` — empty on a Page Builder mount, so every instance emitted
  `id="-select"` — interpolated editor text and taxonomy labels into HTML without `escapeHtml`, and
  broke single-select when two selections happened without an intervening render. The example is now
  `samples/CustomWidget.Dropdown/src/dropdownFacet.ts` reproduced verbatim, and
  `samples/pack-and-build.mjs` fails if the guide and the file diverge (ADR-0013).
- **Fixed:** the npm package now ships what the guides promise a JavaScript-only consumer.
  `themes/shell.css` and `themes/default.css` are package exports, the dependency-free mock server
  ships as `mock/server.mjs` with an `xpsearch-mock` bin entry, and `README.md` is in the tarball.
  `theming.md`'s stylesheet snippet and `js-client.md`'s mock-server instructions match reality;
  repository-only scripts are prefixed `repo:` so an installed package has no broken `npm run mock`.
  `"private": true` still blocks publishing (Phase 8) — see KNOWN-LIMITATIONS.
- **Fixed:** `docs/guides/custom-widgets.md` and `js-client.md` claimed a state change re-renders
  "the moment they are clicked". It renders on a **microtask**, so several mutations in one handler
  coalesce into one render and the DOM is current one microtask after `actions` returns, not
  synchronously. Behaviour unchanged; `src/behaviors/facet-apply.test.ts` pins it, along with what
  `apply()` does (`toggleFacet(...).search()` — toggle **and** search, two applies to one request).
- **Added:** `widgetId(container, widget, part)` and `readMountConfig(config, spec)`, exported from
  the package root. `widgetId` is the single implementation of `MARKUP.md` rule 4 — the instance
  segment falls back to `data-xps-instance`, then the container id, then `default` — and the four
  built-ins that hand-rolled an id base now use it. `readMountConfig` narrows the untyped
  `data-xps-config` values a widget factory receives, which is a trust boundary; a missing or
  wrong-typed required key throws naming the key and the bootstrap logs it once and skips the mount.
- **Added:** the shared `xps-select` utility block (`xps-select__label`, `xps-select__control`,
  `xps-select--disabled`), so a custom widget can render a themed `<select>` without inventing class
  names or borrowing another widget's. `sortSelect` renders the same block; its
  `xps-sort-select__label`/`__select` classes are **removed** — a markup-contract change, and
  semver-major per `MARKUP.md`.
- **Added:** `samples/CustomWidget.Dropdown` and `samples/pack-and-build.mjs` (`.ps1` entry point),
  which packs Core/Widgets/Admin and `npm pack`s the client into `samples/.feed/` and then restores,
  builds, typechecks and tests the sample from that feed alone — so a packaging regression fails a
  build instead of reaching a customer. `samples/README.md` explains why it is not a project
  reference.
- **Added:** `escapeHtml` is documented (`custom-widgets.md` → *Escaping*, `widget-reference.md` →
  *Templating helpers* and *The XSS model*), and `quick-start.md` gains "Installing from a private
  feed", including the `packageSourceMapping` entry that is mandatory, not optional, on a machine
  with source mapping enabled.
- **Fixed:** taxonomy fields no longer break indexing. The untyped content query result hands a Taxonomy
  column back as the JSON it is stored as, so `GetValue<IEnumerable<TagReference>>` threw
  `InvalidCastException` and took the whole rebuild batch with it — on a Dancing Goat host that meant an
  empty index and no facets at all. Values are now converted through the data type registered for the
  field data type (`DataTypeManager.ConvertToSystemType`), which works for any content type without a
  generated class, and every field is read as `object` and converted rather than cast. An item that
  still cannot be mapped is logged as an error naming the item and the field, and skipped, instead of
  escaping `MapToLuceneDocumentOrNull`.
- **Fixed:** `AddXpSearch` no longer requires `IInfoProvider<DataClassInfo>`, which Xperience 31.8.0 does
  not register (`DataClassInfoProvider` is `INotManagedByContainer`), so an application with the default
  DI validation failed to start. Class form definitions are read through the new
  `IDataClassDefinitionSource`, whose default implementation calls the documented static
  `DataClassInfoProvider.GetDataClassInfo(className)`.
- **Added:** linked reusable content items can be flattened into the parent document (spec §10.7) —
  `indexing.FlattenLinkedItems(contentTypeName, linkedFieldName, linkedContentTypeNames, depth)` indexes
  every field of each linked item onto the linking type's document under its own name, facets included,
  and reports those fields as part of the parent type's schema. Underneath it is a new
  `protected virtual XpSearchIndexingStrategy.ContributeAsync(IndexingContext, Document, CancellationToken)`
  hook whose context exposes the item, its data, its schema and `AddFieldAsync` / `AddTaxonomyAsync`, so a
  subclass adds fields with the base mapping's encoding instead of copying it. `XpSearchIndexingStrategy`
  takes `XpSearchIndexingOptions` as a new constructor parameter — update derived strategies.
- **Added:** the Page Builder widgets (spec §7) in `YourCo.Xperience.Search.Widgets` — seven
  view-component widgets (`XpSearch.SearchBox`, `.Results`, `.FacetList`, `.Pagination`, `.ResultStats`,
  `.SortSelect`, `.Suggestions`) that each render one configured `.xps-mount` element and self-assemble
  by `data-xps-instance`, the `XpSearchMountWidgetViewComponent<TProperties>` base class and
  `XpSearchMountWidgetProperties` (`Index`, `InstanceId` = `"default"`) third parties subclass, the
  `IXpSearchMountRenderer` seam, the editor-only unconfigured instruction block (invisible on a live
  page), `[RegisterSearchResultTemplate]` with `ISearchResultTemplateRegistry` behind the Results
  widget's template drop-down, sort-option validation against `XpSearchIndexOptions.SortKeys` and the
  `_asc`/`_desc` convention, `services.AddXpSearchWidgets()`, and `<xps-search-assets />` /
  `@Html.XpSearchAssets()` serving the bundle and both stylesheets as Razor Class Library static web
  assets under `_content/YourCo.Xperience.Search.Widgets/xpsearch/` (ADR-0012).
- **Added:** `FacetAttributeConfigurator` in `YourCo.Xperience.Search.Admin`, registered as
  `xpsearch.facetAttribute`, which fills a facet widget's attribute drop-down from the selected index's
  facetable schema fields and hides it until an index is chosen (spec §7.4). It is referenced by string
  identifier, so widget properties stay free of a dependency on `Kentico.Xperience.Admin`, and the pieces
  it shares with the widgets — `XpSearch.Core.XpSearchConstants` and
  `XpSearch.Core.Facets.FacetAttributeOptions` — live in Core, so `Admin` and `Widgets` stay independent
  of each other (spec §2.2).
- **Changed:** `mountAll()` now **merges** the `data-xps-instance-config` objects of every mount in a
  `data-xps-instance` group instead of using the first one that names an `index`. The first definition of
  a key wins and a mount that disagrees logs one `console.warn` naming the key and the instance, so the
  results widget's page size and retrieved fields apply wherever an editor placed it. Markup whose mounts
  already agreed is unaffected.
- **Added:** `resultStats` takes a string `textTemplate` with `{total}`, `{tookMs}`, `{query}`, `{page}`
  and `{totalPages}` placeholders (template and values escaped; `templates.text` still wins), which is
  what the Page Builder stats widget's **Text template** property emits.
- **Added:** `docs/guides/page-builder-widgets.md`, the C# half of `docs/guides/custom-widgets.md`, and
  ADR-0012.
- **Added:** `XpSearch.Ingestion` (spec §10) — push arbitrary documents into a Lucene index and search
  them alongside Xperience content. HTTP endpoints under `/api/xpsearch/admin/` (upsert, patch, delete,
  batch delete, scoped clear, rebuild, status, index list), the in-process `IXpSearchIndexer`, code-declared
  schemas (`[XpSearchSchema]` / `[XpSearchField]`) with narrow, explicit coercion and field-type-change
  detection, bearer API keys scoped per index and per operation (PBKDF2-hashed, shown once) with a
  per-key rate limit, and an ingestion audit log. Register with `AddXpSearchIngestion()` after
  `AddXpSearch()` and map with `MapXpSearchIngestion()`.
- **Added:** durable ingestion (ADR-0005, accepted). Pushed documents are persisted in the
  `XpSearch.ExternalDocument` custom module class before they are queued to Lucene through a
  `ThreadQueueWorker`; unprocessed rows are re-queued on startup, and a rebuild of Xperience content
  replays them instead of losing them. `waitForIndex: true` writes inline and is documented as a
  foot-gun for bulk imports.
- **Added:** provenance isolation. Every document carries the reserved `_source` attribute
  (`"xperience"` for content the Lucene integration indexes, the caller's own value for pushed
  documents); `clear` is scopeable to one source and can never reach Xperience content, and
  `GET .../status` reports document counts per source.
- **Added:** a second wire contract, `contract/xpsearch-ingestion.schema.json`, generated the same way
  as the query contract into `XpSearch.Ingestion.Contract` and
  `@yourco/xperience-search`'s `contract/ingestion-generated.ts`; `npm run contract:check` covers both.
- **Added:** `SearchFieldKind.Boolean` and the reserved `_source` schema field in `XpSearch.Core`, and
  `IServiceCollection.DecorateLuceneClient<TDecorator>(…)`, which the core package now uses for its own
  cache-evicting decorator and ingestion uses for the rebuild replay.

- **Changed (breaking):** the wire contract and the JavaScript API are owned by this product rather
  than modelled on Algolia and InstantSearch (ADR-0010). `SearchRequest` takes a one-based `page`,
  `pageSize`, `fields` and structured `filters` (`{ facets: [{ attribute, values, operator }],
  numeric: [{ attribute, operator, value }] }`); `SearchResponse` carries `results[]` of closed
  `Result { id, score, attributes, highlights, ranking }`, `facets` as ordered `FacetValue[]` arrays
  that include the taxonomy tag title as `label`, and `total` / `totalPages` / `tookMs`. `/suggest`
  takes `limit` and answers with `Suggestion.result`; `/events` takes `{ type, resultId, queryId,
  position }`. Validation errors are keyed by JSON path. In JavaScript, `createSearch()` replaces
  `xpsearch()`, `SearchActions` replaces the helper, widget hooks are `prepareState`/`prepareRequest`,
  render options are `{ params, results, state, actions, search }`, the second entry point is
  `./behaviors` with `with*` factories and the verbs `apply` / `urlFor` / `isActive` / `canApply` /
  `isStalled`, and the widgets and their classes are `results`, `facetList`, `resultStats`,
  `sortSelect`, `clearFilters`, `activeFilters` and `toggleFilter`. The routes, the
  `X-XpSearch-Api-Version: 1` header and the Problem Details error shape are unchanged.
- **Added:** `XpSearchIndexOptions.SortKeys` — a per-index map from a request's `sort` value to a field
  and a direction, alongside the existing `_asc` / `_desc` suffix convention.
- **Added:** `docs/guides/migrating-from-algolia.md`, generated from `contract/algolia-map.json` by
  `npm run docs:migration` and kept honest by `npm run docs:check`.

- Fixed: fields a content type inherits from a reusable field schema are detected, so a taxonomy that
  reaches a type only through a schema (`ProductFieldTags` and `ProductFieldCategory` on Dancing Goat's
  products) becomes a facet like any other. A name defined by both the content type and one of its
  schemas keeps the content type's field and logs a warning.

- Added: the nine default widgets (spec §5.3) — `searchBox`, `results`, `facetList`, `pagination`,
  `resultStats`, `sortSelect`, `clearFilters`, `activeFilters`, `toggleFilter` — each a behaviour
  plus a renderer over the public API, the escaping-by-default `html`/`highlight`/`formatNumber`
  template helpers, click tracking in `results`, an axe-core gate in CI, and a demo page on
  `npm run demo`; core plus all nine is 12.3 KB gzip against the 20 KB budget (ADR-0009).
- Added: the theme layer (spec §6) — `themes/src/shell.css` (structure only) and
  `themes/src/default.css` (opt-in, CSS-variable driven), the frozen `xps-` markup contract in
  `themes/MARKUP.md` with a fixture per widget, the three-way verification page in `themes/test/`,
  and `npm run check` to keep the stylesheets, fixtures and contract honest.
- Added: the JavaScript client core — `createSearch()` with the widget lifecycle and per-widget error
  isolation, `SearchState`, `SearchClient` (debounce, cancellation, retry, analytics), `SearchActions`,
  URL routing, the `render`/`error`/`stateChange` event bus, eight behaviours, the `.xps-mount`
  bootstrap with `registerWidgetType`, and ESM/UMD bundles under a gzip budget (ADR-0007).

- Added: the JSON search contract is owned and frozen — `contract/xpsearch-api.schema.json` generates the C#
  (`XpSearch.Core.Contract`) and TypeScript (`@yourco/xperience-search`) types for `/api/xpsearch/query`,
  `/suggest` and `/events`, versioned by the `X-XpSearch-Api-Version` response header (ADR-0006).
- Added: `XpSearch.Core` serves the contract — an ordered, injectable query pipeline behind
  `POST /api/xpsearch/query`, `/suggest` and `/events`, with taxonomy facets and drill-sideways counts,
  structured facet and numeric filters, sorting, XSS-safe highlighting, a short-TTL response cache invalidated on
  index writes, and `XpSearchIndexingStrategy`, which binds Xperience taxonomies as facets with no
  per-content-type code (spec §4, ADR-0008).
