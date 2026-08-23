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

## Addendum, 2026-08-23 (AN-2): the activity value is the query, and only the query

`xpsearch_click` used to log `"query | resultId | position"` and `xpsearch_conversion`
`"query | resultId"`. A marketer cannot segment on a pipe-joined string, and the value is the only
field a contact group condition can ever reach, so the composite made click and conversion useless for
the thing activities exist for. All four types now log the **searched text** as `ActivityValue`; the
result id goes to `CustomActivityData.ActivityComment` and the one-based position to
`ActivityItemDetailID` (zero when there is none — positions are one-based). Breaking for anyone who
parsed the old value; the `ISearchActivityLogger` signatures are unchanged.

`XpSearchActivityTypeInstaller` now rewrites `ActivityTypeDescription` on a type that already exists,
because the description states what this library puts in the activity's fields and would otherwise
describe the old layout forever. `ActivityTypeEnabled` and `ActivityTypeDisplayName` are still never
touched, so the promise above — a marketer's decision survives a restart — still holds.

**No contact group rule was added, because the platform has no seam for one.** The built-in
*Contact has performed custom activity* condition matches on the activity type only. The condition
rules behind the builder are `cms.macrorule` objects, which the platform documents as
["used internally for the condition builder in contact groups, customer journeys and automation
processes"](https://docs.kentico.com/documentation/developers-and-admins/ci-cd/reference-ci-cd-object-types)
and for which 31.8 publishes no registration API — unlike automation, which has a documented
[`RegisterAutomationCondition<T>`](https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/automation-customization/automation-custom-steps).
Kentico's own Kbank demo, whose journey stage filters an activity that *"contains value"*, notes that
["targeting the value of the logged activity was custom
built"](https://docs.kentico.com/guides/customer-journeys/build-customer-journey) for that demo.
Writing `MacroRuleInfo` rows by hand would be building on an internal object type behind the
platform's back; the alternative — a custom contact field maintained from the search pipeline — buys
value targeting at the cost of writing to every contact on every search. Neither is worth it. The
guide tells marketers to segment on *that* a visitor searched and to read *what* in the activity log
and the dashboard.


## Addendum, 2026-08-23 (AN-3): there is an extension point, and we use it

The AN-2 addendum above is wrong on its central claim. Contact group conditions *can* be added by a
package. The mechanism is undocumented but entirely public API, and the owner supplied a working
reference implementation from another Xperience project (an `ContactIsInCounty` rule registered the
same way). Three pieces, all in `XpSearch.Core/ContactGroups/`:

1. **The rule row.** A `MacroRuleInfo` (`CMS.MacroEngine`) written through
   `IInfoProvider<MacroRuleInfo>` at module init, with `MacroRuleIsCustom = true` and
   `MacroRuleUsageLocation = MacroRuleUsageLocation.ContactGroupCondition` — a public, non-obsolete
   `[Flags]` enum in 31.8.0 whose members are `None`, `ContactGroupCondition`,
   `AutomationConditionStep` and `CustomerJourneysStage`. `MacroRuleText` carries the sentence the
   picker shows with `{parameter}` placeholders; `MacroRuleParameters` is a form definition, one
   `Kentico.Administration.TextInput` field named `text`, copied field-for-field from the system rule
   `CMSContactHasPerformedCustomActivityWithValue`. The rule is linked into a category through
   `MacroRuleMacroRuleCategoryInfo`; we use `WebActivity` (*Web activity*), the category all three
   system activity rules sit in.
2. **The evaluation.** A `MacroMethodContainer` registered with
   `[assembly: RegisterExtension(typeof(XpSearchContactMacroMethods), typeof(ContactInfo))]`, which is
   the documented way to add macro methods; `MacroRuleCondition` calls it as
   `Contact.XpSearchSearchedFor("{text}")`.
3. **The SQL fast path** — deliberately *not* shipped. See KNOWN-LIMITATIONS: the
   `IMacroRuleInstanceTranslator` interface is public and clean, but its only registration point,
   `MacroRuleMetadataContainer.RegisterMetadata`, is marked obsolete "will be removed in the next
   version" in 31.8.0. Groups therefore recalculate by evaluating the macro per contact, which is
   correct and is exactly what the reference implementation does (its metadata registration is
   commented out for the same reason).

Why the AN-2 conclusion missed it: the `cms.macrorule` object type is documented only as an
internal CI/CD object, and nothing in the digital-marketing documentation mentions adding condition
rules. The reason marketers cannot segment on a search value out of the box is narrower than "no
seam" — the system rule `CMSContactHasPerformedCustomActivityWithValue` does exactly what we need but
ships with `MacroRuleUsageLocation = AutomationConditionStep`, so it is offered in automation and not
in contact groups. We do not touch that row; we register our own three.

**Risk accepted:** `MacroRuleInfo` rows and the parameter form XML are undocumented. An upgrade can
change the form schema or the enum, and the failure mode is a rule that renders wrong in the picker
(the installer is idempotent, so a corrected definition ships as a normal library upgrade). The
installer is defensive about what it can be: it writes nothing when the row is unchanged, never
overwrites `MacroRuleEnabled` on a rule that already exists, and skips the category link if the
category is missing.

**Where it lives:** `XpSearch.Core`, next to `XpSearchActivityTypeInstaller` — not `XpSearch.Admin`.
The live site process recalculates contact groups too, so the macro methods must be loaded there.
