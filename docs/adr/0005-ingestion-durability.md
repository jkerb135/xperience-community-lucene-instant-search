# ADR-0005: §13.5 — ingestion durability and source isolation

- **Status:** accepted — owner decision 2026-08-21, implemented by unit IN-1 (`XpSearch.Ingestion`)
- **Date:** 2026-08-21
- **Spec reference:** §13.5, §10.2

## Context

Spec §13.5 asks one question — "if the app restarts mid-queue, are queued writes lost?" — but the
investigation turned up a second, larger one. `Kentico.Xperience.Lucene` 15.0.5 rebuilds an index like
this (`DefaultLuceneClient.RebuildInternal`):

```csharp
luceneIndexService.ResetIndex(luceneIndex);            // new index generation, OpenMode.CREATE
…                                                      // query Xperience content only
indexedItems.ForEach(item => LuceneQueueWorker.EnqueueLuceneQueueItem(
    new LuceneQueueItem(item, LuceneTaskType.PUBLISH_INDEX, luceneIndex.IndexName)));
```

`ResetIndex` starts a new generation and the re-queued items are content items from
`LuceneIndexChannelConfiguration` — Xperience content, nothing else. Anything written straight to
Lucene by another party is gone the moment an editor presses **Rebuild** in the Search application.
Spec §10.2 states the requirement in the opposite direction: "A rebuild of Xperience content must never
delete externally pushed documents… Getting this wrong means a routine content rebuild silently wipes a
client's product catalogue."

So the two questions are the same question: if Lucene is the only copy of a pushed document, both a
restart and a rebuild lose it.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Accept loss; callers retry | No storage, no schema, no installer | A rebuild loses data no caller knows to retry — the failure is silent and days later |
| Require `waitForIndex` for anything important | Cheap | Does not survive a rebuild at all, and serializes bulk imports against the index writer |
| Persist the queue only | Survives a restart | Does not survive a rebuild: after the queue drains there is no record of what was pushed |
| **Persist the documents in a custom module class; Lucene is derived** | Survives restart *and* rebuild; makes `PATCH`, per-source counts and scoped `clear` possible; matches how Xperience integrations store data | One more table, an installer, and a replay step after every rebuild |

## Decision

The database is the source of truth and Lucene is derived.

1. Every pushed document is written to the `XpSearch.ExternalDocument` custom module class first —
   index name, source, id, JSON body, content hash, created/updated timestamps, status — and only then
   queued to Lucene through a `ThreadQueueWorker` (`XpSearchIngestionQueueWorker`), the pattern
   Xperience prescribes for integration batches and the one the Lucene integration itself uses.
2. Rows are committed with status `Pending` and flipped to `Indexed` once the write lands. On startup
   the module re-queues every `Pending` row (`XpSearchIngestionModule.RequeuePendingAsync`), so a
   restart mid-queue costs latency, not data.
3. `ILuceneClient` is decorated (`ExternalDocumentReplayLuceneClient`). After the integration's
   `Rebuild` returns, a replay of that index's external documents is queued behind it. The replay waits
   for the index to stop changing before writing, because `Rebuild` only *enqueues* the content items:
   the integration's own worker indexes them and publishes the new generation afterwards, and writing
   into the outgoing generation would lose the documents again.
4. Every document carries the reserved `_source` field, written by `XpSearchIndexingStrategy` for
   Xperience content (`"xperience"`) and by the ingestion writer for everything else. `clear` and
   filtered delete only ever name stored external rows, so no ingestion path can reach content the
   integration owns.
5. `waitForIndex: true` runs the Lucene half inline instead of queueing it. It changes *when* the
   document is searchable, never whether it is stored.

External documents are addressable by the integration's own client because they are written with
`ItemGuid` set to the caller's `id`, which is the term `DefaultLuceneClient.DeleteRecordsInternal`
deletes by. The writer deletes before it upserts, because `UpsertRecordsInternal` only replaces a
document when it can read both `ItemGuid` and `LanguageName` off it, and an external document has no
language.

## Evidence

- `DefaultLuceneClient.RebuildInternal` → `ILuceneIndexService.ResetIndex`, and
  `DefaultLuceneIndexService.UseIndexAndTaxonomyWriter(..., OpenMode)` — decompiled from
  `Kentico.Xperience.Lucene.Core` 15.0.5; the rebuild re-queues only content from the index's channel
  configuration and reusable content types.
- `DefaultLuceneClient.DeleteRecordsInternal` deletes by `new Term("ItemGuid", itemGuid)`;
  `UpsertRecordsInternal` deletes by `ItemGuid` AND `LanguageName` and skips the delete when either is
  missing from the document.
- `tests/XpSearch.Ingestion.Tests/SourceIsolationTests.cs` reproduces the whole sequence against a real
  Lucene index whose client mirrors those three behaviours: two Xperience documents and two pushed
  documents, a rebuild that leaves two documents behind, and a replay that brings the pushed pair back
  and makes them searchable again.
- `IngestionTests.PendingRowsAreRequeuedOnStartup` drops the queue between the commit and the write and
  shows the document arriving anyway.

## Consequences

- **Easy:** partial updates (`PATCH` is a read-modify-rewrite of the stored row), per-source document
  counts, scoped `clear`, replaying an index into a new schema, and an audit trail that outlives the
  index.
- **Expensive:** a rebuild now costs a replay proportional to the number of external documents, and
  every push is a database write before it is an index write. Two tables' worth of storage duplicated
  between SQL and Lucene.
- **Foreclosed:** nothing — but it does mean the ingestion API can never be a thin passthrough to
  Lucene, and a host that deletes the module class data has effectively deleted its external documents.
- **Bounded risk:** the replay's "wait until the index stops changing" is a heuristic with a timeout
  (`XpSearchIngestionOptions.ReplayTimeout`). If it fires early, the documents stay in the database and
  the next push or rebuild writes them again; nothing is lost, they are just missing from search until
  then. See KNOWN-LIMITATIONS.
