# IX-2 — Linked-item edits reindex the pages that flatten them

**Status:** DISPATCHED 2026-09-07 (owner: "let's work through these" — GTM review product gap).
**Origin:** GTM review 2026-09-06 and the long-standing open decision: `XpSearchIndexingOptions.FlattenLinkedItems`
(`FlattenedLink(ContentTypeName, LinkedFieldName, LinkedContentTypeNames)`) copies a linked item's fields onto the
page document, but `XpSearchIndexingStrategy` never overrides Kentico's `FindItemsToReindex`, so editing the linked
product leaves every page that embeds it stale until a full rebuild. `indexing-strategy.md` says "Reindexing is
still yours"; the demo host hand-writes the override (`src/Search/DancingGoatSearchIndexingStrategy.cs` — read it,
it is the reference behaviour and must keep working unchanged or become redundant).

Scope: `XpSearch.Core/Indexing` + tests + docs. No contract, no JS, no admin UI.

## 1. Behaviour
- `XpSearchIndexingStrategy.FindItemsToReindex(IndexEventReusableItemModel changedItem)` (verify the exact
  override signature in `Kentico.Xperience.Lucene` 15.0.5 `DefaultLuceneIndexingStrategy` — GitHub source, cite
  the URL in the ADR) returns, for a changed reusable item whose content type appears in ANY registration's
  `LinkedContentTypeNames`, every web page item of that registration's `ContentTypeName` that links it through
  `LinkedFieldName`. Implementation: the content query API's linking-items lookup (`ForContentType(...)`
  `.Linking(field, [guid])` or the documented equivalent — Kentico docs MCP is mandatory here; cite the page).
  Union across registrations; de-duplicate; every language variant of the page (the index is per-language docs).
- Web page edits keep the default behaviour (base call). Non-flattened reusable types → base call.
- **Registration coverage at startup:** Kentico only raises reusable-item events for content types the index
  is configured to watch (the index's `IncludedPaths`/content types — verify what 15.0.5 actually requires for a
  reusable item to reach `FindItemsToReindex`). If a registered linked type is not on the stored index config,
  log ONE warning per index at startup naming the type and the `FlattenLinkedItems` call (IX-1's
  once-per-field warning pattern) — do NOT mutate the stored index config silently. State in the guide what the
  developer adds to the index definition.
- Host: the demo's hand-written override becomes redundant. Keep the host untouched in this unit; report whether
  its override now duplicates yours (lead removes it after merge).

## 2. Verification
- Core.Tests: fake `IContentQueryExecutor` (the suite has one — find `FormInfoContentTypeFieldSource` /
  strategy tests) returning linking pages for a changed item; assert the returned models (ids, content type,
  language); two registrations sharing a linked type union correctly; unregistered type falls through to base;
  the startup warning fires once per missing type and not for present ones.
- Docs: `indexing-strategy.md` "Reindexing is still yours" → rewritten: flattened links reindex automatically;
  what the index definition must include; when you still override. CHANGELOG `**Added (core):**`. KNOWN-LIMITATIONS
  if a ceiling remains (e.g. two-level links A→B→C not followed — state it).
- Checklist row `§AD`: edit a product's name in Content hub → the Store page that embeds it shows the new name in
  search within the queue interval, with no rebuild.
