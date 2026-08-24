# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public behaviour API (spec §5.7) or the JSON contract
(spec §4.2, as amended by ADR-0010) are always major-version events.

## [Unreleased]

- **Changed (Page Builder):** a configured search widget now renders a **static server-side preview**
  of itself in the Page Builder (edit and read-only mode) instead of the bare `.xps-mount` div, which
  looked like an empty shell to editors. The preview mirrors the widget's live markup with disabled
  controls, no links and `xps-skeleton` bars for result data, under a badge naming the widget and
  saying the content is not live, and it reflects the widget's own properties (placeholder, label and
  attribute, page size and template, pagination style, sort options, range bounds). Extends the
  unconfigured-instruction-block precedent of spec §7.5 to the configured state. **Preview and the
  live site are unchanged** — both still render the mount element and the working widget. Custom
  widgets get a labelled default preview from `XpSearchMountWidgetViewComponent<T>` and can own it by
  overriding the new `BuildEditorPreview(properties)`; `XpSearchMountViewModel` gains `Preview`, and
  `shell.css` / `default.css` gain the `xps-editor-preview` block (`themes/MARKUP.md`). See
  [What editors see in the Page Builder](docs/guides/page-builder-widgets.md#what-editors-see-in-the-page-builder).

- **Added (contact groups):** three search conditions are installed on start and appear in the
  contact group condition picker under **Web activity** — *Contact has searched for text containing
  {text}*, *Contact has searched without results for text containing {text}* and *Contact has clicked
  a search result after searching for text containing {text}*. The `{text}` parameter is an optional
  case-insensitive *contains* match against the searched text of the contact's `xpsearch_query`,
  `xpsearch_noresults` and `xpsearch_click` activities, so a marketer can finally segment on **what**
  a visitor searched for, not only that they searched. They are ordinary `cms.macrorule` rows
  (`XpSearch.Core/ContactGroups/`); the installer never overwrites the *Enabled* flag of a rule that
  already exists, so hiding one survives a restart. This supersedes the AN-2 note that the platform
  had no seam for this — see the ADR-0015 addendum (AN-3) for the mechanism and its risks. Groups are
  recalculated by evaluating the macro per contact (no SQL translation, see KNOWN-LIMITATIONS), and
  the conditions carry no *in the last X days* parameter.

- **Added (personalisation):** a relevance rule can be scoped to a **contact group**, so the same
  query ranks differently for a segment (boost, pin, bury, filter and redirect all honour it). The
  rule form gains an object selector over `om.contactgroup` right after **Enabled**, the Rules
  listing a **Contact group** column showing *Everyone* for an unscoped rule, and the **Query
  tester** a **Contact group** drop-down that simulates a group so an admin can see the effect
  without being a member. The visitor's groups are resolved once per request by a new pipeline stage
  (`SearchStageOrder.ResolveContactGroups`, 150) behind the same consent gate the activity logger
  uses — cookie level *Visitor* or higher, `ICurrentContactProvider.GetExistingContact`, never
  creating a contact — and no consent means no group-scoped rule applies. A group-scoped rule shows
  as `rule:<name> (contact group <code name>)` in `ranking.boosts`. See ADR-0021 and
  [Personalise rules by contact group](docs/guides/relevance-tuning.md#personalise-rules-by-contact-group).
- **Changed (schema):** `XpSearch.Rule` gains a nullable `RuleContactGroup` column (text, 100). The
  tuning module installer adds it to an existing installation on the next application start, merging
  it into the class without touching existing rows; an empty value means "everyone", so every rule
  that exists today keeps behaving exactly as it does.
- **Changed (breaking, extenders):** the `TuningRule` record gains a trailing `ContactGroup` member.
  Custom `IRelevanceTuningSource` implementations and any code constructing `TuningRule`
  positionally must pass it; `string.Empty` reproduces today's behaviour. `CachedSearchPipeline` also
  takes an `IContactGroupResolver`, and the response cache key now includes the visitor's contact
  groups so a personalised response is never served to a visitor in different groups.
- **Changed (breaking, activities):** `xpsearch_click` and `xpsearch_conversion` now log the
  **searched text** as their `ActivityValue`, the same as `xpsearch_query` and `xpsearch_noresults`,
  instead of the pipe-joined `query | resultId | position` / `query | resultId`. The result id moved
  to `CustomActivityData.ActivityComment` and the one-based click position to `ActivityItemDetailID`
  (`0` when there is none). The value is the only field a contact group condition can reach, so it now
  holds the one thing worth segmenting on. **Anything built on the old format needs updating** — a
  contact group, customer journey or automation condition matching the old value, and any report or
  export that split the value on `|`. The `ISearchActivityLogger` method signatures are unchanged.
  `XpSearchActivityTypeInstaller` refreshes the *Description* of the four activity types on start so
  an upgraded project does not keep the old wording; the *Enabled* flag and the name are still never
  overwritten. See
  [Search activities in contact groups](docs/guides/analytics.md#search-activities-in-contact-groups)
  and the ADR-0015 addendum (AN-2).

- **Changed (admin UI):** the **Analytics** and **Query tester** pages were rebuilt to the owner's
  design spec and are now assembled only from `@kentico/xperience-admin-components` — `Card`, `Table`
  with `ActionCell`, `Tag`, `Callout`, `NameToggleButtons`, `DateTimeInput`, `Select` and the layout
  primitives — with no hand-rolled tables, no inline-styled hit rows and no stylesheet. Analytics
  gains four KPI tiles (total searches, zero-result rate, click-through rate, average clicked
  position), a two-series chart, an empty-range state and a friendly-warning error state; the query
  tester gains a quick tip before the first run, a required-query helper text that disables **Run**,
  a language drop-down filled from the index's own languages, per-side *N results · N ms · N changed*
  strips, and an error callout that links to the index's Status page. Both pages go to a single
  column below 1366 px. The index is no longer a selector on either page, so the
  `indexNames`/`indexLocked` client properties are gone. See ADR-0020.
- **Added:** `SearchAnalyticsReport.ZeroResultSearches` and `.Clicks`,
  `SearchVolumePoint.ZeroResultVolume` and `QueryVolume.P95ProcessingTimeMs` — the four figures the
  new dashboard shows, all computed in the single log read the service already did. Code
  constructing these records positionally must add the arguments.
- **Changed (admin UI):** the per-index **Status** page is now a custom React template
  (`@yourco/xperience-search-admin/IndexStatus`) instead of an edit form whose only field was a
  read-only text area. It shows the health tag with its explanation, the document/source/last-write
  figures, a stacked *Documents by source* bar with a share table, and the last ten ingestion log
  entries — failed ones first, with the invalid-row treatment **and** a *Failed* tag while the index
  is degraded. **Rebuild index** is a destructive page action behind a confirmation dialog and the
  Lucene integration's *Rebuild* permission, and reports back with a success notification and a
  *Rebuild in progress* tag. Built only from `@kentico/xperience-admin-components`; follows ADR-0020
  (AD-4a). See [Reading the Status page](docs/guides/relevance-tuning.md#reading-the-status-page).
- **Added:** `IIngestionLog.ReadRecentAsync(indexName, count, cancellationToken)` — the read side of
  the ingestion log, so a page can show the recent entries of one index without going through the
  listing. Breaking for anyone who implemented `IIngestionLog` themselves; the HTTP contract
  (`contract/xpsearch-ingestion.schema.json`) is untouched, and the status page's `failedWrites`
  comes from the ingestion queue rather than from the wire `IndexStatus`.
- **Changed (repository layout):** the JavaScript client moved from `src/XpSearch.Client` to
  `src/XpSearch.Widgets/Client`, so it sits inside the package that ships it, the same way
  `src/XpSearch.Admin/Client` holds the admin UI module. Nothing about the product changed: the npm
  package is still `@yourco/xperience-search` with the same entry points, the bundle is still
  `dist/xpsearch.umd.js`, and the widgets are still served from
  `/_content/YourCo.Xperience.Search.Widgets/xpsearch/`. Contributors run `npm ci` in the new path.
  See ADR-0019.
- **Added:** a **Search - Range filter** Page Builder widget (`XpSearch.RangeFilter`), which emits a
  `rangeFilter` mount. Its **Attribute** drop-down is filled from the selected index's numeric and date
  fields by a new form component configurator (`XpSearchConstants.NumericAttributeConfiguratorIdentifier`);
  **Minimum**, **Maximum**, **Step** and the two input labels are editor properties, because the search
  response carries no statistics the bounds could be derived from. A widget without usable bounds shows
  the unconfigured instruction block. See
  [Page Builder widgets](docs/guides/page-builder-widgets.md#the-range-filters-bounds-are-hand-configured).
- **Added (contract, minor):** `FacetValue.path` — the code names of a taxonomy value's ancestors,
  root first, excluding the value itself, absent for a root-level value and for every non-taxonomy
  attribute. Every ancestor a `path` names is itself present among the same facet's values with its
  own count, so a client can build the whole tree from one facet. Additive and optional, so a client
  that ignores it is unaffected and `X-XpSearch-Api-Version` (the semver **major**) stays at `1`.
  See ADR-0018 and the [spec amendment](docs/spec/amendments/2026-08-23-facet-value-path.md).
- **Added:** hierarchical taxonomy facets. `XpSearchIndexingStrategy` writes every **ancestor** of a
  tag as a value of the same, still-flat dimension, so facet counts roll up (*Coffee* counts every
  *Espresso* document) and `filters.facets` on a parent matches the documents tagged with its
  descendants — with no change to the request shape and none to the query pipeline.
- **Added:** the `categoryTree` widget and the `withCategoryTree` behaviour, and the
  **Search - Category tree** Page Builder widget (`XpSearch.CategoryTree`). The widget renders the
  `themes/MARKUP.md` category-tree contract: nested lists with a depth modifier per level, real
  crawlable links, `aria-current="true"` on every node of the open path, and a disabled `<span>` at
  count 0. Selection is one value at a time, because a parent's count already includes its
  descendants. `categoryTree` is no longer a reserved name with nothing behind it.
- **Changed (breaking, indexing):** **rebuild your indexes.** A tag's ancestors and its ancestry are
  written into the document, and the `<dimension>_label` term went from `code name ␟ title` to
  `code name ␟ path ␟ title`. An index written by an earlier version still searches, filters and
  labels correctly — the two-part term is still read — but its counts do not roll up and its facet
  values carry no `path` until it is rebuilt.
- **Changed (breaking, API):** `XpSearchIndexingStrategy` takes a new constructor parameter,
  `ITagAncestrySource`, after `ITaxonomyRetriever`. A derived strategy must add it to its own
  constructor and pass it through; the guide's sample shows the new signature. The default
  implementation, `TagAncestrySource`, reads the tag table once through `IInfoProvider<TagInfo>` and
  caches it on `cms.tag|all` — `ITaxonomyRetriever` exposes a tag's `ParentID` but nothing that
  resolves it to a tag.
- **Changed:** `LuceneFieldNames.ComposeLabel` takes an optional `path`, and `SplitLabel` returns a
  third element. Both still read the two-part form.

- **Changed (breaking):** the relevance-tuning admin pages moved inside the search index. They are now
  reached at **Lucene Search → indexes → click an index → the *Tuning* sidebar**: *Settings*, *Rules*,
  *Synonyms*, *Stopwords*, *Field weights*, *Query tester*, *Analytics* and *Status*. Clicking an index
  row in the Lucene index listing opens the sidebar instead of the integration's bare edit form; that
  form is the sidebar's *Settings* page. See ADR-0017.
- **Changed (breaking):** the old URLs are gone. `/admin/xpsearch-tuning/rules`, `/synonyms`,
  `/stopwords`, `/field-weights`, `/query-tester`, `/analytics` and `/index-status` are now
  `/admin/lucene/indexes/tuning/{id}/rules`, `/synonyms`, `/stopwords`, `/weights`, `/query-tester`,
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

- **Added:** the Phase 2.5 JavaScript widgets (spec §5.3): `rangeFilter`, `loadMore` and
  `suggestions`, each a behaviour plus a default renderer emitting the markup contract in
  `themes/MARKUP.md`, each in `DEFAULT_WIDGETS` so a `.xps-mount` resolves it by name. `rangeFilter`
  renders `withRange` as two native `<input type="range">` sliders and two number inputs, and renders
  itself disabled when it has no bounds to offer. `loadMore` accumulates the pages of one search into
  one `<ol>` that is appended to and never rebuilt, with a live-region counter, a real button and an
  `IntersectionObserver` sentinel for the scroll path; place it *or* `pagination`, never both.
  `suggestions` implements the WAI-ARIA APG combobox-with-listbox pattern over `POST /suggest`, with
  a debounce, a minimum query length, latest-response-wins, and full keyboard support. Two new
  behaviours, `withLoadMore` and `withSuggestions`, are exported from
  `@yourco/xperience-search/behaviors`. See `docs/guides/widget-reference.md`.
- **Added:** `SearchInstance.suggest({ query, limit?, language? })` — autocomplete over the
  instance's own index and transport (endpoint, headers, `fetchFn`, contract-version check).
  Neither debounced nor cancelled: `withSuggestions` owns that policy.
- **Changed:** the Page Builder "Search - Suggestions" widget and the pagination "Load more" style no
  longer say their JavaScript widget ships later — both work now. No new Page Builder widget for
  `rangeFilter`: its bounds are a property of the corpus that an editor cannot know, and the contract
  does not report them (`docs/internal/KNOWN-LIMITATIONS.md`).
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
