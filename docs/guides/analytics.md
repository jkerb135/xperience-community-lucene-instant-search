## Search analytics

Every search this library answers is recorded twice, for two different audiences.

- **Xperience activities** — per contact, consent-gated. They land in the standard contact activity
  log, so a marketer can build a contact group out of "ran a search that found nothing" and
  personalize content with it (see *Search activities in contact groups*).
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

| Activity code name | Logged when | Activity value | Other fields |
|---|---|---|---|
| `xpsearch_query` | a search returned at least one result | the normalized query | — |
| `xpsearch_noresults` | a search returned nothing | the normalized query | — |
| `xpsearch_click` | `POST /api/xpsearch/events` with `type: "click"` | the normalized query | comment = result id, item detail ID = one-based position |
| `xpsearch_conversion` | `POST /api/xpsearch/events` with `type: "conversion"` | the normalized query | comment = result id |

**All four activities carry the searched text, and nothing else, as their value.** That is the field
a marketer segments on, so one condition reads the same way whichever activity it is built on. What
the visitor clicked travels in the other columns Xperience gives a custom activity
(`CustomActivityData.ActivityComment` and `ActivityItemDetailID`), where it stays out of the way of
filtering.

The four activity types are created on application start by `XpSearchActivityTypeInstaller`, so they
appear in **Contact management → Activity types** without anybody adding them by hand. Of a type that
already exists only the *Description* is refreshed on start — the enabled flag and the name are left
alone, so if a marketer disables or renames one, it stays that way.

The query and no-results activities are written by `ISearchRequestJournal`, called once per request by
the caching decorator — outside the pipeline, so a search answered from the response cache is recorded
too and its clicks can still be attributed. Click and conversion are written by
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
in-memory map the journal fills. It holds at most 10 000 entries for 30 minutes, per application
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

### Search activities in contact groups

A contact group turns "did this" into an audience. The three search activities a visitor produces on
their own — `xpsearch_query`, `xpsearch_noresults`, `xpsearch_click` — are all usable as conditions.

**Build a group of visitors who searched and found nothing:**

1. Open the **Contact groups** application and select **New contact group**.
2. **Contact group name**: *Searched and found nothing*. The **Code name** fills itself in.
3. Write a **Description** — the next marketer needs to know what the group is for.
4. In the **Conditions** area, select **Add**, then **Add condition group**.
5. In the condition picker (a searchable list, grouped by category), type `searched` and pick
   **Contact has searched without results for text containing {text}**, under **Web activity**.
6. Select the **{text}** placeholder and leave it empty — an empty text matches any search. (Fill it
   in to narrow the group down; see below.)
7. **Apply**, then **Save**.
8. The group's **Contacts** tab now fills as visitors search. If you change the condition later, the
   **General** tab grows a **Recalculate contact group** button — use it, otherwise the group keeps
   the contacts the old condition put there.

#### Segment on *what* a visitor searched for

The library installs three conditions on startup, all in the **Web activity** category of the
condition picker, all with the same single text parameter:

| Condition | Matches a contact who |
|---|---|
| Contact has searched for text containing **{text}** | ran any search, with or without results (`xpsearch_query`, `xpsearch_noresults`) |
| Contact has searched without results for text containing **{text}** | ran a search that returned nothing (`xpsearch_noresults`) |
| Contact has clicked a search result after searching for text containing **{text}** | opened a result after searching (`xpsearch_click`) |

The **{text}** parameter is a *contains* match against the searched text, case insensitive on a
default database, and it is matched against the activity value of *any* of the contact's search
activities — not only the most recent one. Leave it empty to match any search of that kind.

**Build a group of visitors interested in one thing:**

1. **Contact groups → New contact group**, name it *Interested in standing desks*.
2. **Conditions → Add → Add condition group**, type `searched` in the picker and pick **Contact has
   searched for text containing {text}**.
3. Select **{text}** and enter `standing desk`. A contact who searched for *standing desks*,
   *Standing Desk mat* or *best standing desk* all match; a contact who searched for *desk* does not.
4. **Apply**, **Save**, then **Recalculate contact group** on the **General** tab to fill the group
   from the activities that are already logged.

Two notes before you build a campaign on it:

- **Matching is literal.** There is no stemming, no synonyms and no spelling correction on this side —
  a group on `laptop` does not hold the visitor who searched for `laptops`. Prefer the shortest stem
  that is still unambiguous (`standing desk`, not `standing desks`), and check the **Analytics**
  dashboard's top queries for the words your visitors actually type.
- **No time window.** The conditions look at the contact's whole activity history, however old. If you
  need "in the last 30 days", combine them with the built-in **Contact has done any activity in the
  last X days** condition in the same condition group.

The searched text is also readable per contact, without any group: **Contact management → a contact →
Activities** shows it as the activity value.

#### Two things that surprise people

- **Consent.** A visitor below cookie level *Visitor* produces no activities at all, so they can never
  enter one of these groups. Groups built on search activities describe your consenting visitors only.
  The Analytics dashboard, which reads the anonymous query log, describes everybody.
- **Only browser traffic counts.** Activities are logged for the *current contact*, which the platform
  resolves from the contact cookie on the request. A search issued by a server-to-server call, a
  scheduled job, or the admin **Query tester** carries no contact and logs no activity — it still
  lands in the query log. If a contact group looks emptier than the dashboard suggests, that is why.

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

**Lucene Search → indexes → click the index → Edit index → Analytics** shows the whole log for that
index and one date range (`/admin/lucene/indexes/edit/{id}/analytics`). The index is the one you
clicked; it is shown above the date range and cannot be changed, so there is no "every index" view —
open each index's dashboard in turn.

The headline reads *Analytics*, and under it a line naming the index, the provider, the range and the
total number of searches — *Index **ProductIndex** · Lucene · 1 Jul – 30 Jul 2026 · 4,812 searches*.

The controls sit in one card under the headline:

1. **Range** — a **7 days / 30 days / 90 days** toggle. Picking one reloads immediately.
2. **From** and **To** — date pickers, both in UTC, both included. Editing either one leaves the
   range toggle unselected until the span matches a preset again.
3. **Rows per page** — how many rows one page of each table holds: 10, 25 (the default), 50 or 100.
   It takes effect straight away, without a reload, and sends every table back to page one.
4. **Load** — reruns the report. It shows a spinner and disables itself while the load is in flight.

Each report is paged. Every table that holds more rows than one page shows a pager under it —
*Page 2 of 4 · 87 rows*, with **Previous page** and **Next page**; a table that fits on one page shows
no pager at all. The paging is done in the browser over the rows the report already returned, so
turning a page is instant and does not touch the query log. Each report carries at most **200 rows**
(`AnalyticsReportDto.MaxReportRows`): a range with more distinct queries than that is cut to the top
200 of each report, which is why the reports are ranked in the first place. Narrow the range if you
need to see past it.

What you get, in the order the page shows it:

- Four **KPI tiles**: *Total searches*, *Zero-result rate*, *Click-through rate* and *Avg clicked
  position*. A tile with nothing to divide by reads `—` rather than `0%`.
- **Searches over time** — two lines, total searches and zero-result searches, one point per day.
  *Show the numbers* opens the same data as a table.
- **Zero-result queries** — what visitors asked for and did not find, most searched first, with the
  date it was last asked, and a **Create rule** action on every row. This is the report to read
  first, and the only table on the page that changes anything.
- **Top queries** — what visitors search for most, with the 95th percentile of their processing time.
- **Click-through** — how often a search led to a click, and the mean position of the thing clicked.
  A high volume with a low rate means the results are wrong; a good rate with a high average position
  means the right result is too far down (a case for a pin). The card's footer repeats the average
  clicked position across all queries.
- **Slowest queries** — the 95th percentile of server-side processing time per query.

**When the range holds no searches** the four tiles read `—` and one card replaces the chart and all
four tables: *No searches in this range*, with a **Load last 30 days** button.

**When the load fails** a friendly-warning callout takes the place of every number — *Analytics could
not be loaded*, the reason, and a **Load again** button. No partial figures are shown, because a
half-read log would be misleading. The controls stay usable.

Below 1366 px the tiles go two per row and the two table columns stack, zero-result queries first.

#### From a zero-result query to a fix

Every row of **Zero-result queries** has a **Create rule for …** button. It opens the rule builder
for the index you were looking at, already carrying:

- one condition, *Query contains* that query,
- **Rule name** pre-filled as *Rule for '<query>'*.

Choose what the rule should do — **Add consequence**, usually **Pin an item**, pointing at the page
that *should* have come back — fill in the item, and **Save rule**. You land back on the **Rules**
listing.
Then check it in **Query tester**, next to Analytics in the same sidebar (see
`docs/guides/relevance-tuning.md`).

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
(one point per day across the whole range, including days nobody searched, each carrying `Volume` and
`ZeroResultVolume`), `SlowestQueries` (95th percentile of `LogProcessingTimeMs`), `TotalSearches`,
`ZeroResultSearches` and `Clicks`. `TopQueries` carries `P95ProcessingTimeMs` too, so the top-queries
table can show it without a second read.

### Replacing a piece

Every part is an interface registered with `TryAdd`, so registering your own first wins:
`ISearchActivityLogger` (what an activity looks like, or none at all), `IQueryLogStore` (where the log
lives), `IQueryLogQueue` (how rows are written), `IQuerySuggestionSource` (where suggestions come from),
`ISearchAnalyticsService` (how the reports are computed) and `ISearchEventSink` (what `/events` does).
