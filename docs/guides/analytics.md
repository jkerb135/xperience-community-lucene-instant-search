## Search analytics

Every search this library answers is recorded twice, for two different audiences.

- **Xperience activities** — per contact, consent-gated. They land in the standard contact activity
  log, so a marketer can build a contact group out of "searched for *pricing*" and personalize content
  with it.
- **The `XpSearch.QueryLog` module class** — anonymous aggregates. No contact, no visitor identifier,
  nothing personal, so it is written for every visitor whether or not they consented to tracking. It
  is what the top-queries, zero-result and click-through reports read, and what query suggestions are
  built from.

Both are on by default with `services.AddXpSearch()`. Nothing here can fail a search: every failure
inside logging is swallowed and written to the log at `Debug`.

### Turn it on

```csharp
// Program.cs
builder.Services.AddXpSearch(options =>
{
    options.Analytics.RetentionDays = 180;         // default; older query log rows are deleted
    options.Analytics.RetentionBatchSize = 1000;   // rows deleted per batch by the retention task
    options.Analytics.QuerySuggestionDays = 30;    // how far back query suggestions count volume

    options.Indexes["ProductIndex"].SuggestMode = SuggestMode.QuerySuggestions;
});
```

Then create the retention task configuration once (see *Retention*, below). That is the whole setup.

### What gets logged, and where

| Activity code name | Logged when | Activity value |
|---|---|---|
| `xpsearch_query` | a search returned at least one result | the normalized query |
| `xpsearch_noresults` | a search returned nothing | the normalized query |
| `xpsearch_click` | `POST /api/xpsearch/events` with `type: "click"` | `query \| resultId \| position` |
| `xpsearch_conversion` | `POST /api/xpsearch/events` with `type: "conversion"` | `query \| resultId` |

The four activity types are created on application start by `XpSearchActivityTypeInstaller`, so they
appear in **Contact management → Activity types** without anybody adding them by hand. A type that
already exists is left alone — if a marketer disables one, it stays disabled.

The query and no-results activities are written by `LogActivityStage`, the last stage of the query
pipeline (slot `SearchStageOrder.LogActivity`, 1200). Click and conversion are written by
`ActivitySearchEventSink`, the `ISearchEventSink` behind `/api/xpsearch/events`. Both run **inside the
HTTP request**, because Xperience logs a custom activity for the *current contact* and a worker thread
has no current contact.

The `queryId` from the search response is what ties a click back to its query. The `hits` widget sends
it automatically; if you post events yourself, send the `queryId` you got back:

```jsonc
POST /api/xpsearch/events
{ "type": "click", "queryId": "1b2c…", "resultId": "abc-en", "position": 3 }
```

`ActivitySearchEventSink` resolves the query text behind that `queryId` from `IQueryContextMap`, an
in-memory map the query stage fills. It holds at most 10 000 entries for 30 minutes, per application
instance — an event whose id is unknown (expired, or answered by another instance behind a load
balancer) is still recorded, only with an empty query.

### Consent

Activities are only logged for a visitor whose cookie level is **Visitor** or higher, which is what
Xperience itself requires for custom activities on website channels. `SearchActivityLogger` reads it
with `ICurrentCookieLevelProvider.GetCurrentCookieLevel()` and compares it to
`Kentico.Web.Mvc.CookieLevel.Visitor.Level`. Below that level — or outside a request context, where the
level cannot be read at all — nothing is logged and nothing is thrown.

Set the **Default cookie level** of your website channel to *Essential* in **Channel management →
Channel settings → Cookies** and raise it from your own consent component, and search activities follow
the visitor's decision with no further work.

The query log is unaffected by any of this. It stores no personal data, so the reports below are
complete even on a site where nobody consents to tracking.

### The query log

`XpSearchQueryLogInfo` (class `XpSearch.QueryLog`, module `CMS.Integration.XpSearchAnalytics`) is
installed on first start:

| Column | Meaning |
|---|---|
| `LogQueryID` | the response's `queryId`; a click event finds its row by this |
| `LogIndexName` | code name of the index searched |
| `LogQueryText` | the query, normalized and lowercased |
| `LogResultCount` | how many documents matched |
| `LogTimestamp` | when the search ran, UTC |
| `LogChannelName` | website channel, when the request came from one |
| `LogLanguage` | requested language |
| `LogClickedPosition` | one-based position of the clicked result, `0` when nothing was clicked |
| `LogProcessingTimeMs` | server-side processing time |

Rows are written by `XpSearchQueryLogQueueWorker`, a `ThreadQueueWorker`, so a search response never
waits for the database.

### Retention

`XpSearchQueryLogRetentionTask` deletes rows older than `Analytics.RetentionDays` in batches. It is
registered under the identifier `XpSearch.QueryLogRetention`; Xperience needs a *task configuration* to
actually run it, and that can only be created in the administration:

1. Open the **Scheduled tasks** application.
2. Select **New scheduled task configuration**.
3. **Scheduled task configuration name**: `XpSearch query log retention`.
4. **Task implementation**: `XpSearch.QueryLogRetention`.
5. **Enabled**: yes. **Task schedule**: daily is plenty.
6. **Save**.

The *Last result* column shows how many rows the run deleted. Until the configuration exists the query
log grows without bound — this is the one manual step of the feature.

### Query suggestions

An index configured with `SuggestMode.QuerySuggestions` answers `/api/xpsearch/suggest` from the query
log instead of from documents: queries that start with the typed prefix, that found at least one
result, within the last `Analytics.QuerySuggestionDays` days, most searched first, deduplicated and cut
to `limit`. Results are cached for `options.CacheTtl` (60 s by default), because autocomplete fires on
every keystroke.

```jsonc
POST /api/xpsearch/suggest
{ "index": "ProductIndex", "query": "cof", "limit": 5 }

{ "suggestions": [{ "text": "coffee" }, { "text": "coffee grinder" }] }
```

A query suggestion carries `text` only — there is no document behind it. Leave the mode at
`SuggestMode.Documents` (the default) for a dropdown that shows actual results.

### The dashboard

**Search tuning → Analytics** shows the whole log for one index and one date range.

1. Pick the **Index**, or leave it on **Every index**.
2. Pick the range: **Last 7 days**, **Last 30 days**, **Last 90 days**, or type **From** and **To**
   yourself as `yyyy-mm-dd` and press **Apply**. Both dates are in UTC and both are included.

What you get:

- **Search volume over time** — one bar per day. *Show the numbers* opens the same data as a table.
- **Zero-result queries** — what visitors asked for and did not find, most searched first, with the
  date it was last asked. This is the report to read first.
- **Top queries** — what visitors search for most.
- **Click-through rate by query** — how often a search led to a click, and the mean position of the
  thing clicked. A high volume with a low rate means the results are wrong; a good rate with a high
  average position means the right result is too far down (a case for a pin).
- **Slowest queries** — the 95th percentile of server-side processing time per query.

The header line gives the total number of searches in the range and the average clicked position
across all of them.

#### From a zero-result query to a fix

Every row of **Zero-result queries** has a **Create rule** button. It opens the rule form with:

- **Index** set to the index you were looking at,
- **Words to look for** set to that query,
- **Rule name** pre-filled as *Rule for '<query>'*.

Choose what the rule should do — usually **Pin a result to a position**, pointing at the page that
*should* have come back — fill in the result id, and save. You land back on the **Rules** listing.
Then check it in **Query tester** (see `docs/guides/relevance-tuning.md`).

If the query found nothing because the content genuinely does not exist, the report is telling you to
write the page, not the rule.

Nothing on this page changes any data except that button, and the button only opens a form.

### Reading the reports from code

`ISearchAnalyticsService` returns everything the analytics dashboard shows, for one index and date
range, from a single read of the log:

```csharp
public class SearchInsights(ISearchAnalyticsService analytics)
{
    public async Task<IReadOnlyList<ZeroResultQuery>> WhatVisitorsCannotFind(CancellationToken token)
    {
        var report = await analytics.GetReportAsync(
            new SearchAnalyticsQuery("ProductIndex", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, Limit: 20),
            token);

        return report.ZeroResultQueries;
    }
}
```

`SearchAnalyticsReport` carries `TopQueries`, `ZeroResultQueries` (with volume and last sighting),
`ClickThrough` (rate and mean clicked position per query), `AverageClickedPosition`, `VolumeOverTime`
(one point per day across the whole range, including days nobody searched), `SlowestQueries` (95th
percentile of `LogProcessingTimeMs`) and `TotalSearches`.

### Replacing a piece

Every part is an interface registered with `TryAdd`, so registering your own first wins:
`ISearchActivityLogger` (what an activity looks like, or none at all), `IQueryLogStore` (where the log
lives), `IQueryLogQueue` (how rows are written), `IQuerySuggestionSource` (where suggestions come from),
`ISearchAnalyticsService` (how the reports are computed) and `ISearchEventSink` (what `/events` does).
