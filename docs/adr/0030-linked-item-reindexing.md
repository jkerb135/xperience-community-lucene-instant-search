# ADR-0030: Linked-item edits reindex the pages that flatten them

- **Status:** accepted
- **Date:** 2026-09-08
- **Spec reference:** §10.7; unit IX-2

## Context

`XpSearchIndexingOptions.FlattenLinkedItems(contentTypeName, linkedFieldName, linkedContentTypeNames)`
copies a linked reusable item's fields onto the page's document, so a `ProductPage` document carries
`ProductFieldName`, `ProductFieldPrice` and the product's tags. Until this unit the option said nothing
about *when* the page is rebuilt: `XpSearchIndexingStrategy` did not override
`FindItemsToReindex`, so editing the product left every page embedding it stale until a full rebuild.
The guide admitted it ("Reindexing is still yours") and the demo host hand-wrote the override
(`src/Search/DancingGoatSearchIndexingStrategy.cs`) — a typed `IContentRetriever.RetrievePages<ProductPage>`
query with `Linking`, one page type and one field, hard-coded.

Two facts from `Kentico.Xperience.Lucene` 15.0.5 shape the design:

- `DefaultLuceneIndexingStrategy.FindItemsToReindex(IndexEventReusableItemModel)` returns the changed
  item itself; the caller queues an UPDATE task per returned item, per index
  ([`DefaultLuceneTaskLogger.HandleReusableItemEvent`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/DefaultLuceneTaskLogger.cs),
  [`DefaultLuceneIndexingStrategy`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/DefaultLuceneIndexingStrategy.cs)).
- The strategy is only asked when `reusableItem.IsIndexedByIndex(...)` says yes, which requires the
  changed item's content type to be in the index's `IncludedReusableContentTypes` **and** its language
  to be among the index's languages
  ([`IndexedItemModelExtensions`](https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Core/Indexing/IndexedItemModelExtensions.cs)).
  A flattened type absent from the index definition therefore produces no event at all — nothing a
  strategy can compensate for.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Leave it to each project (status quo) | Nothing to build | Every flattened relationship needs a hand-written typed query; the option ships a documented staleness bug |
| Derive the query from the registrations | The registration already names the page type, the field and the linked types — exactly the query's three inputs; zero extra configuration | Needs an untyped `Linking` query and hand-built event models; needs the index's channels and languages, which the integration keeps internal |
| Add a callback to the registration (`FlattenLinkedItems(..., findPages: ...)`) | Fully general | Asks the developer for what the registration already says; an abstraction nobody requested |
| Mutate the stored index definition at startup so the linked types are always watched | The event always arrives | Silently changes what an index contains (a rebuild would then index those items as their own documents); the administration owns that decision |

## Decision

**`FindItemsToReindex(IndexEventReusableItemModel)` is generated from the flatten registrations.**

When the changed item's content type appears in any registration's `LinkedContentTypeNames`, the
strategy runs one content query per (index the strategy serves, registration, channel, language):

```csharp
builder.ForContentType(link.ContentTypeName, config => config
    .ForWebsite(channelName)
    .Linking(link.LinkedFieldName, [changedItem.ItemID]));
builder.InLanguage(languageName);
```

`Linking(referenceFieldName, items)` is the documented lookup from a linked item back to the items that
reference it, and takes content item identifiers — what a reusable-item event carries
([Reference - Content item query](https://docs.kentico.com/documentation/developers-and-admins/api/content-item-api/reference-content-item-query)).
`ForWebsite` is what makes the web page columns available, which the returned
`IndexEventWebPageItemModel`s are built from. Results are de-duplicated on (page GUID, language),
because that pair is one document. Every language the index covers is queried, not only the changed
item's: a page variant can show the item through language fallback. Any other content type keeps the
base behaviour — the changed item itself.

**The changed item is not returned for a flattened type.** It is the page's document that went stale;
returning the product as well would create a second, URL-less document for it on every edit.

**A missing registration is reported, never repaired.** `FlattenedLinkRegistrationCheck` runs once at
application start (from `XpSearchAnalyticsModule`, beside the installers) and logs one warning per index
and linked content type that the index does not list as a reusable content type, naming the page type,
the field and the fix. Editing the stored index definition would change what the index contains.

**The index's channels and languages come from a new seam member,
`ILuceneIndexAccessor.GetDefinitionAsync`.** `LuceneIndex.ChannelConfigurations` is `internal` in
15.0.5, so the definition is read from `ILuceneConfigurationStorageService` — the same rows the admin
Search application writes, and the same source `LuceneIndexContentTypeSource` already uses. The
alternative, a new constructor parameter on `XpSearchIndexingStrategy`, would break every project that
derives from it, including the demo host.

## Evidence

- `tests/XpSearch.Core.Tests/LinkedItemReindexTests.cs` — the returned models (id, GUID, content type,
  language, channel, tree path), one item per indexed language, two registrations sharing a linked type
  returning each page once, an unregistered type falling through to the base, and the startup warning
  firing once per missing type and not for a type the index already watches.
- Suite: Core 385 → 391.

## Consequences

- **Dancing Goat's `FindItemsToReindex` override is now redundant** — its behaviour is the library's
  default, plus every language rather than only the changed item's. The host is untouched by this unit;
  removing the override (and its `productContentTypeNames` list) is a follow-up.
- **The index definition is now part of the contract of `FlattenLinkedItems`**: every flattened linked
  type must be listed under the index's *reusable content types*, or no event is raised. The guide says
  so, and the startup warning says so at runtime.
- **A rebuild indexes the listed reusable items as documents of their own**, so a product can appear
  twice — once as a page with a URL, once as a reusable item without. Incremental updates do not, since
  the strategy answers a product event with pages only. The guide shows the two-line
  `MapToLuceneDocumentOrNull` override that drops them.
- Only the first link level is followed (A links B, not A links B links C), and there is no caching of
  "which pages link this item" — an answer that goes stale exactly when an editor changes a link. Both
  are in `docs/internal/KNOWN-LIMITATIONS.md`.
- `ILuceneIndexAccessor` gained a member: source-breaking for a custom implementation of the seam
  (CHANGELOG `**Breaking (core):**`).
