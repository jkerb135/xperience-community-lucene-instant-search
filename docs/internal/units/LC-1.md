# LC-1 — Language and channel: the page decides, the index already knows

**Status:** DISPATCHED 2026-09-08 (owner: "yes to both" on the wave-2 decisions; "channels are already configured
on the index as well as language").
**Origin:** GTM review 2026-09-06, platform audit: (1) the language filter exists on the wire (`SearchRequest.Language`,
`SuggestRequest.Language`, term filter on `BaseDocumentProperties.LANGUAGE_NAME` in `BuildQueryStage` ~108 and
`DocumentSuggestService` ~269, `IndexSchemaProvider` base field ~76) and the JS instance passes `options.language`
(`types.ts:247`, `instance.ts:112/332/345`) — but NOTHING supplies it: the mounts carry only index + instance,
`SearchQueryState` reads q/page/sort, no `IPreferredLanguageRetriever` anywhere in Widgets. A German page renders
English results. (2) There is no channel concept at all: no channel field on documents (Kentico's
`BaseDocumentProperties` has ID / ITEM_GUID / CONTENT_TYPE_NAME / LANGUAGE_NAME / URL and nothing for channel), no
request member, no JS option; the index picker lists every index. The query journal already reads
`IWebsiteChannelContext` (`ISearchRequestJournal.cs` ~66-153) for the log's channel column.

**Owner's framing (binding):** the Kentico index definition already carries its channels and languages
(`IndexDefinition` from IX-2's `ILuceneIndexAccessor.GetDefinitionAsync`: channels, languages, content types). The
library must USE that, not add configuration: a page's own language and website channel are the defaults for every
mount on it; the developer overrides only when the page deliberately searches something else.

Scope: Core (document field, contract, filters, cache key, suggest, journal), Widgets (tag helper base resolves the
defaults; `<xps-search>` attributes; SSR query state; index picker), JS client (a `channel` instance option mirroring
`language` — JS change ALLOWED in this unit, kept to that mirror), docs, ADR-0002 decided-for-now + ADR-0031. Do not
touch Admin. Do not build per-language analyzers (out of scope, ADR-0002 states it).

## 1. Core
- **Document:** `XpSearchIndexingStrategy` writes a `channel` keyword field (constant `XpSearchDocumentProperties.CHANNEL`
  or the closest existing naming convention — check how `_source`/`path` are named) = the web page item's website
  channel name (`IndexEventWebPageItemModel.WebsiteChannelName`); reusable items (no channel) write nothing; pushed
  (ingestion) documents may carry `channel` as an ordinary attribute, untouched. `IndexSchemaProvider` lists it as a
  base field like `language` (Keyword, facetable, retrievable, not searchable). Ingestion schema guard: not a reserved
  name collision — verify.
- **Contract (coordinated regen, additive):** `SearchRequest.Channel` and `SuggestRequest.Channel` (`string?`,
  "Website channel name to search. Omit to search every channel the index covers."). Regenerate C# ×2 + TS via the
  Widgets client codegen; run `contract:check`; update the JSON schema description.
- **Filters:** `BuildQueryStage` and `DocumentSuggestService` add a `channel` term filter exactly as `language`.
  `NormalizeRequestStage` validates the name (non-empty, trimmed; no allow-list — a channel the index does not cover
  simply matches nothing; say so in the docs). `SearchCacheKey` includes channel (it already includes language).
- **Journal:** the query log's channel column is filled from `request.Channel` when present, else the current
  `IWebsiteChannelContext` as today (read the journal; keep the fallback).
- **Facets:** the channel filter is a filter clause, so facet counts respect it like language — assert in a test with two
  channels in the fixture.
- **Server first paint:** `SearchQueryState` gains `Language` and `Channel`, filled by the Widgets layer (§2), passed
  through `ServerResultsOptions` (positional record — add at the END with defaults so existing callers compile) into
  the request `ServerRenderedResults` builds.

## 2. Widgets — the page decides
- **Resolution, once, in `XpSearchMountTagHelper<TOptions>`:** the instance config written to every mount gains
  `language` and `channel`. Precedence: tag attribute (`language` / `channel` on any widget tag — add them to the base
  the way `index`/`instance` are) → options record (`XpSearchMountOptions` gains `Language`, `Channel`) →
  `<xps-search language channel>` scope → **the current page**: `IPreferredLanguageRetriever.Get()` and
  `IWebsiteChannelContext.WebsiteChannelName` (Kentico docs MCP for both; both resolve only inside a website-channel
  request — outside one (e.g. a Razor page under a non-channel route, a test host) they yield null/empty → omit the
  key and let the server search everything; never throw). Inject the two through a small `IXpSearchPageContext`
  seam (Widgets) with a `KenticoPageContext` implementation and a test fake, so the tag helper tests need no Kentico
  container (pattern: `IXpSearchEditorContext` / `KenticoEditorContext`).
- **Page Builder:** no new editor properties. The widgets take the page's language and channel automatically; the
  explanation text of the Index property gains one sentence saying so. (An editor who wants a cross-channel search
  on one page uses the Razor/tag-helper surface or a custom widget — state it in the guide.)
- **Index picker (`XpSearchIndexOptionsProvider`):** list indexes whose `IndexDefinition` covers the current
  website channel (via `ILuceneIndexAccessor.GetDefinitionAsync`) FIRST, then the rest under a separator or with a
  "(other channel)" suffix — whichever the Kentico dropdown provider supports; if it supports neither, filter to
  covering indexes and fall back to all when none covers. Sole-index fallback (`ResolveIndex`) unchanged.
- **Consistency guard:** when the resolved index's definition does NOT cover the resolved language or channel
  (developer pointed a page at the wrong index), the tag helper logs one warning per (index, language/channel) at
  render (IX-1 once-per pattern) — never throws, never silently searches nothing without a trace.
- **SSR:** the Widgets layer passes the resolved language/channel into `SearchQueryState` so the first paint matches
  what the client will hydrate with (byte parity of PB ≡ tag helper still holds — both go through the base).
- **Editor preview / query tester:** untouched.

## 3. JS client (mirror `language`)
- `createSearch` option `channel?: string` beside `language` (`types.ts` ~247), forwarded on every query, suggest and
  probe request exactly where `language` is (`instance.ts` 112/332/345). `bootstrap.ts` instance-config merge needs
  nothing new (generic). Contract TS regenerated. Vitest: one test that `channel` reaches the request body in all
  three places; `language` tests untouched. `mountAll` docs table (generated from bootstrap) updated if it lists
  instance keys. Widget reference: the instance option row.

## 4. Docs + ADRs
- `search-api.md`: `channel` request member; "what the page decides" paragraph (language/channel defaults, cross-channel
  = omit). `page-builder-widgets.md`, `razor-tag-helpers.md` (attribute tables regenerate via `TagHelperDocsTests` —
  `language`/`channel` join `index`/`instance`/`options` as the attributes every tag takes; `<xps-search>` table),
  `building-a-search-page.md` (one line), `indexing-strategy.md` (the `channel` base field; multi-channel indexes;
  "one index per channel is no longer required"), `js-client.md` (`channel` option).
- `docs/adr/0002-multilingual-strategy.md`: fill in as DECIDED-FOR-NOW — one index, every language the index lists,
  `language` term filter, one analyzer per index (the index's own setting); per-language analyzers/stemming are not
  supported until a customer needs them; the upgrade path (index per language selected by `language`) and what it
  would change (`BuildQueryStage` filter → index selection; facet counts per language index).
- NEW `docs/adr/0031-channel-scoping.md`: channel as a document field + request filter (not index-per-channel),
  defaults from the page, the index picker ordering, the consistency warning; alternatives rejected.
- CHANGELOG: `**Added (core, widgets):**` channel; `**Changed (widgets):**` mounts now carry the page's language and
  channel (behaviour change for multilingual/multichannel sites: results narrow to the page's own — say it plainly,
  with the attribute to widen). `**Breaking (core):**` if `ServerResultsOptions`'s shape forces it (avoid by appending
  with defaults). KNOWN-LIMITATIONS: no channel condition in rules; no per-language analyzer; consistency warning is
  once per process.

## 5. Verification
- Core.Tests: channel field written for web pages / absent for reusable; filter narrows; facet counts respect it;
  cache key differs by channel; suggest filters; journal column from request vs context; `SearchQueryState` round trip.
- Widgets.Tests: precedence (attribute > record > scope > page context) for both; page context null → keys absent;
  PB ≡ tag parity still byte-identical (the parity test's fake page context must be the same on both sides);
  index picker ordering with a fake accessor; consistency warning once; `TagHelperDocsTests` tables regenerated
  (paste the expected block); `BuildingASearchPageTests` still green.
- JS: vitest for the `channel` option; `npm run build` incl. size checks; `contract:check`.
- Host pass rows `§AG`: on the demo (one channel, en+es) the Spanish `/es/search` and `/es/search-razor` show only
  Spanish documents on first paint AND after hydration; the English pages only English; the raw mount carries
  `"language":"es","channel":"DancingGoat"`; `?language=` no longer needed by hand.

## 6. Slices (one agent, sequential, one worktree)
A — §1 Core + contract regen + §3 JS (both ends of the wire in one slice so `contract:check` is green at the commit).
B — §2 Widgets (page context seam, base resolution, scope attrs, SSR state, index picker, warning) + tests.
C — §4 docs + ADRs + CHANGELOG. Commit each slice separately; report after C.
