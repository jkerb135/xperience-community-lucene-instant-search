# ADR-0015: Search analytics — two stores, one consent gate

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §4.3, §4.4, §9.1, §9.2, §9.3, §13.6

## Context

Spec §9 asks for two things that look like one: Xperience *activities*, which are per contact and
legally gated on tracking consent, and an *aggregate query log*, which powers the reports a content
team acts on. Conflating them would make every report go blank on a site where visitors decline
tracking — exactly the sites that most need to know what visitors cannot find.

Three platform constraints shaped the design.

1. `ICustomActivityLogger.Log` writes for the **current contact** and therefore needs the HTTP request
   context; the documentation says explicitly that it cannot be called from a worker thread.
2. Custom activities on website channels are only logged for visitors with cookie level *Visitor* or
   higher, and the documented way to ask is `ICurrentCookieLevelProvider.GetCurrentCookieLevel()`.
3. Writing a row per search synchronously would put a database round trip on the search path.

## Decision

**Two independent paths.** `SearchActivityLogger` writes activities and is consent-gated;
`XpSearchQueryLogQueueWorker` writes anonymous rows and is not. Both are driven from the same two
places — `LogActivityStage` (pipeline slot 1200) and `ActivitySearchEventSink` (the `/events` sink) —
and neither can fail a search: every exception in either is caught and logged at `Debug`.

**The consent check is a cookie-level comparison.** `GetCurrentCookieLevel() >=
CookieLevel.Visitor.Level`, the same check the platform's own `CurrentContactCanBeTracked` sample
makes. Reading the level outside a request throws; that throw is caught and treated as "no consent",
which is the safe direction and also handles the request-context constraint above.

**Activity types are created in code.** The documentation only describes the administration route, so
`XpSearchActivityTypeInstaller` creates the same `ActivityTypeInfo` objects through
`IInfoProvider<ActivityTypeInfo>` with the fields the admin form fills in
(`ActivityTypeName`/`DisplayName`/`Description`/`Enabled`/`IsCustom`). An existing type is never
touched, so a marketer's decision to disable one survives a restart. Nobody should have to hand-create
four types before the product works.

**The query log lives in Core, not Admin.** It is written by the query pipeline and read by query
suggestions, both of which are Core features; the dashboard (a later unit) only renders what
`ISearchAnalyticsService` returns. Putting the store in Admin would make Core depend on Admin or
duplicate the schema.

**`queryId` → query is an in-memory map.** A click event carries only the `queryId`, but the activity
value needs the query *text*. Storing it would mean a database read on every click; instead
`QueryContextMap` keeps the last 10 000 ids for 30 minutes in process. The clicked *position* still
reaches the database, because the click updates the row whose `LogQueryID` matches — the id is a column
for exactly this reason. The cost is that behind a load balancer a click may land on an instance that
never saw the query, and then the activity value's query part is empty. Accepted: the aggregate CTR
report, which is what the numbers are for, is unaffected.

**Processing time comes from the context, not from the response.** The pipeline sets `tookMs` after
the last stage runs, so the logging stage cannot read it. `SearchContext` takes a `Stopwatch` timestamp
in its constructor and exposes `Elapsed`; the logging stage and the pipeline both read it, so the
logged time and the response's `tookMs` measure the same thing. (Superseded the original
`SearchTimingStage`, an extra stage at slot 99 that stamped the timestamp into a
`ConditionalWeakTable`.)

**Retention is a scheduled task with a manual configuration.** `[assembly: RegisterScheduledTask]`
makes `XpSearch.QueryLogRetention` selectable; the platform has no documented API for creating the task
*configuration*, so the guide tells the developer to create it once in the *Scheduled tasks*
application. Defaults: 180 days, 1000 rows per batch.

## Consequences

- Reports work with zero consent, which is the point of §9.2.
- A visitor who declines tracking produces query log rows and no activities, silently.
- The dashboard unit has no data work left: it renders `SearchAnalyticsReport`.
- Aggregation happens in memory over the rows of the requested range (see KNOWN-LIMITATIONS); pushing
  it into SQL `GROUP BY` is a store-level change that no caller would notice.
- Queued log rows are lost if the process dies before the worker drains — deliberately, unlike
  ingestion (ADR-0005), because a missing analytics row is not a missing document.
