# ADR-0031: Website channels are a document field, and the page supplies it

- **Status:** accepted
- **Date:** 2026-09-09
- **Spec reference:** §4.2, §5.7, §10.2; unit LC-1

## Context

Until this unit the library had no channel concept at all. Kentico's `BaseDocumentProperties` carries
`ID`, `ITEM_GUID`, `CONTENT_TYPE_NAME`, `LANGUAGE_NAME` and `URL` and nothing about a channel, so a
multichannel project either accepted that a search on one site returned the other site's pages, or
built one index per channel. The language was in the same state from the other end: the filter existed
on the wire and in the JavaScript instance, but nothing supplied it, so a German page rendered English
results.

Two facts shape the design:

- A Kentico Lucene index definition **already lists its website channels and languages** (the Search
  application writes them; `ILuceneIndexAccessor.GetDefinitionAsync` reads them since IX-2/ADR-0030).
  The library does not need, and must not add, configuration for something the index already knows.
- An indexing event for a web page item carries `IndexEventWebPageItemModel.WebsiteChannelName` — the
  same value `XpSearchIndexingStrategy` already passes to `IWebPageUrlRetriever.Retrieve` — so writing
  the channel onto the document costs one field and no lookup. A reusable content item has no channel
  at all.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Channel as a document field + request filter** | One index can serve several channels, exactly as the index definition allows; facet counts and the cache key follow the same rule the language already follows; a cross-channel search is a request that omits the member | A document written before the upgrade has no `channel` and is invisible to a channel-scoped search until the index is rebuilt |
| One index per website channel (status quo by convention) | No new field; total isolation | Forces an index — with its own settings, tuning, analytics and rebuild — per site, and contradicts the index definition, which accepts several channels |
| Filter by URL prefix or by tree path | No new field | The path is not the channel; two channels can share a path, and a channel's domain is not in the document |
| Let the caller always pass the channel | No defaults to get wrong | Every widget on every page would have to be configured with what the request already knows |

## Decision

**The channel is a keyword field on the document (`channel`, stored and facetable, written for web
page items only), an additive `channel` member on `SearchRequest` and `SuggestRequest`, and a term
filter beside the language.** It joins the response cache key, and the query log's channel column is
taken from the request when it names one, falling back to `IWebsiteChannelContext` as before.

**The page decides.** `XpSearchMountTagHelper` resolves the language and the channel once, in this
order: the tag's own attribute, the options record, the enclosing `<xps-search>`, then the page itself
through the new `IXpSearchPageContext` seam over `IPreferredLanguageRetriever.Get()` and
`IWebsiteChannelContext.WebsiteChannelName`
([Retrieve page content](https://docs.kentico.com/documentation/developers-and-admins/development/content-retrieval/retrieve-page-content)).
Both resolve only inside a website channel request, so outside one — a plain Razor page, a test host —
the keys are simply absent and the search covers everything the index does. The resolved pair is
written into `data-xps-instance-config` beside `index`, and into the server-rendered first paint, so
the cold paint and the hydrated search agree. `*` is the opt-out: it searches every language or every
channel from a page that belongs to one.

**No new editor properties.** A Page Builder widget takes the page's language and channel
automatically; the Index property's explanation says so. An editor who needs a cross-channel search
uses the tag helpers or a custom widget.

**The seam is set as a property, not injected into every constructor.**
`AddXpSearchWidget<TTagHelper, TOptions>()` creates the tag helper with `ActivatorUtilities` and
assigns `PageContext`. Adding a constructor parameter to `XpSearchMountTagHelper<TOptions>` would have
changed the constructor of all fifteen shipped widgets and of every third-party widget built on the
published base class (`samples/CustomWidget.Dropdown`), for a dependency none of them names.

**Mismatches are reported, never repaired.** When the resolved index's definition does not cover the
resolved language or channel, one warning per index and value is logged at render — the IX-1
once-per-process pattern. Filtering the request to something the index covers would silently change
what the page shows.

**The index picker orders, it does not hide.** `DropDownOptionItem` is a value and a text and nothing
else — no group, no separator — so `XpSearchIndexOptionsProvider` lists the indexes whose definition
covers the current channel first and suffixes the rest with `(other channel)`. Hiding them would make
a deliberate cross-channel index unpickable; outside a channel request the list is exactly what it was.

## Evidence

- `tests/XpSearch.Core.Tests/ChannelScopingTests.cs` — the filter narrows, the facet counts respect it,
  the cache key differs, suggest filters, the journal prefers the request's channel, and the first
  paint's query state round-trips both members. `IndexingStrategyTests` covers the field being written
  for a web page item and absent for a reusable one.
- `tests/XpSearch.Widgets.Tests/LanguageAndChannelTests.cs` — the four-step precedence, the absent keys
  outside a channel request, `*`, Page Builder ≡ tag helper byte parity with a page context on both
  sides, the picker's ordering, and the warning firing once per index and value.
- Suites: Core 424 → 435, Widgets 119 → 130, JS 314 → 316.

## Consequences

- **Behaviour change for multilingual and multichannel sites**: widgets that used to search every
  language and channel now search the page's own. `language="*"` / `channel="*"` restores the old
  behaviour per widget or per scope (CHANGELOG `**Changed (widgets):**`).
- **Documents indexed before the upgrade carry no `channel`**, so a channel-scoped search skips them
  until the index is rebuilt. Rebuilding is the documented upgrade step.
- `ISearchRequestJournal.Record` gained an optional `channel` parameter: source-breaking for a custom
  implementation of the interface, not for a caller.
- Pushed (ingestion) documents may still carry `channel` as an ordinary attribute — it is not a
  reserved name — and are matched by a channel-scoped search when they do.
- There is still no channel condition in the tuning rules and no per-language analyzer (ADR-0002);
  both are in `docs/internal/KNOWN-LIMITATIONS.md`.
