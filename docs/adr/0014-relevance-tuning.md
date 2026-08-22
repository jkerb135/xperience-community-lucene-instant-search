# ADR-0014: Relevance tuning — storage, seam, cache and precedence

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §8.1, §8.2, §8.3, §8.5, §10.8

## Context

Phase 5 is the Algolia-parity feature: rules, synonyms, stopwords and field weights, edited by a
marketer in the Xperience administration and applied to every query. Four constraints shaped every
decision below.

1. **Core must keep working without the Admin package** (spec §2.2). A host that installs only
   `XpSearch.Core` gets search; the tuning stages must be inert, not absent.
2. **Rules are read on every query** (spec §8.5). A database round trip per search is not acceptable.
3. **No custom React** (spec §8.1) — the admin client toolchain is a later unit, and the built-in UI
   page templates are supposed to cover listings and editing.
4. **This is a package, not a project.** It ships as a DLL; there is no Modules application session in
   which a developer clicks a UI form together.

## Options considered

### Where tuning data lives

| Option | Pros | Cons |
|---|---|---|
| Custom module classes installed in code | What spec §8.2 asks for; CI/CD support; Xperience CRUD and cache dependencies for free | An installer has to run at startup |
| Settings keys / JSON blob in options | No installer | No listing UI, no per-row editing, no cache dependencies, not deployable per environment |
| Reuse the ingestion module's resource | One installer | Two installers writing one `ResourceInfo` race each other on first start |

### How the query pipeline reads it

| Option | Pros | Cons |
|---|---|---|
| `IRelevanceTuningSource` in Core, empty by default, replaced by Admin | Core runs alone; the stages are unit-testable against a fake | One more interface |
| Core depends on Admin | Direct | Inverts the package split of §2.2 |
| Admin registers its own stages | No Core seam | Duplicates the stage plumbing and makes the reserved slots meaningless |

### How the editing pages are built

| Option | Pros | Cons |
|---|---|---|
| `ListingPage` + `ModelEditPage<TModel>` | Both are built-in templates; the model's editing-component annotations live in code | Two mappings (model ↔ row) to keep in step |
| `InfoEditPage<TInfo>` | Binds straight to the object type | Renders a **UI form**, and UI forms are authored in the Modules application, not in code. A package cannot ship one without hand-writing undocumented `CMS_UIForm*` rows |
| Custom React pages | Full control | Out of scope for this unit, and unnecessary for CRUD |

### How the cache is invalidated

| Option | Pros | Cons |
|---|---|---|
| `IProgressiveCache` + `CacheDependencyBuilder.ForInfoObjects<T>().All()` | The platform touches the dummy keys itself when an object is saved or deleted; nothing to register, nothing to forget | Invalidates all indexes' entries, not just the changed one |
| `IInfoObjectEventHandler` per type, evicting by hand | Can be surgical | Eight handler registrations that reimplement what `TouchCacheDependencies` already does |

## Decision

**Storage.** Four module classes — `XpSearchRule`, `XpSearchSynonym`, `XpSearchFieldWeight` and
`XpSearchStopwordList` — installed by `XpSearchTuningModuleInstaller` under its **own** resource
`CMS.Integration.XpSearchTuning`, separate from the ingestion installer's
`CMS.Integration.XpSearchIngestion`. Each Info class carries `[assembly: RegisterObjectType]` and
`TouchCacheDependencies = true`.

**Stopwords.** Spec §8.1 lists a Stopwords page but §8.2 defines no class for it. Modelled as one row
per index with a single newline-separated text field (`XpSearchStopwordList`). A row per word would
have given a listing nobody wants to page through; the spec's own UI sketch is a single edit screen.

**Seam.** `XpSearch.Core.Tuning.IRelevanceTuningSource` with `GetRulesAsync`, `GetSynonymsAsync`,
`GetStopwordsAsync` and `GetFieldWeightsAsync`. `AddXpSearch()` registers `EmptyRelevanceTuningSource`;
`AddXpSearchAdmin()` replaces it with `InfoRelevanceTuningSource`.

**Stages.** The reserved slots of spec §4.4 are filled: `SynonymExpansionStage` (200) loads the whole
tuning set once per request and expands the query into slots of interchangeable terms;
`StopwordRemovalStage` (300); `BuildQueryStage` (400) multiplies the schema's field boosts by the
configured weights and ORs each slot's alternatives; `BoostRulesStage` (700) applies boost and filter
rules; `PinnedAndBuriedStage` (900) reorders after execution.

**Precedence.** Enabled rules whose schedule covers now and whose condition matches the normalized
query, ordered by `RulePriority` ascending then `RuleID` ascending. Boost and filter rules all apply,
in that order. For pin and bury, the **first rule to name a document wins**; later rules naming the
same document are ignored. A pinned document that the query did not match is loaded by id and
injected **only if it also matches every active filter** — the language, facet and numeric
refinements, which `SearchContext.ActiveFilters` accumulates for exactly this purpose.

**Cache.** `IProgressiveCache.LoadAsync`, keyed `xpsearch|tuning|<part>|<index>`, 30 minutes, with a
`CacheDependencyBuilder` dependency on all four object types. Saving anything in the application
touches the corresponding dummy key and the next query reloads.

**Admin → Ingestion.** Spec §10.8 puts API keys, index status and the ingestion log inside the Search
tuning application, and all three are views over data that `XpSearch.Ingestion` owns
(`XpSearchApiKeyInfo`, `IApiKeyService`, `IXpSearchIndexer`, `XpSearchIngestionLogInfo`). §2.2 did not
anticipate this edge. The alternative — an `IApiKeyAdminService`-style seam in Core that Ingestion
implements — would have to expose key creation, index status *and* the log shape through Core, which
is a larger public surface in the package that everyone installs, to avoid a reference in the package
almost nobody installs alone. So: **`XpSearch.Admin` references `XpSearch.Ingestion`**, and the
Ingestion package stays free of any admin dependency. A host that wants ingestion without the admin UI
still installs Ingestion alone.

## Evidence

- `tests/XpSearch.Core.Tests/TuningTests.cs` runs pin, bury, boost, filter, synonym expansion and
  stopword removal through the real Lucene fixture, including the "a pinned document must match the
  active filters" rule and the "no tuning changes nothing" baseline.
- `tests/XpSearch.Admin.Tests/ModuleInstallerTests.cs` pins the four form definitions to the columns
  of the §8.2 tables, the way the ingestion unit does.
- The `IFormItemCollectionProvider` route was confirmed against the Xperience documentation for
  model-based edit pages; `InfoEditPage` was ruled out after the object-types documentation confirmed
  UI forms are created in the Modules application.

## Consequences

**Easy.** A marketer tunes relevance with no deployment. A developer adds a fifth kind of tuning data
by adding a method to one interface and a page to one application. Core keeps running with the Admin
package uninstalled, and the tuning stages cost one cache read.

**Expensive.** Two mappings per entity (model → row, row → model) that the compiler cannot check;
they are exercised on a running instance, not in unit tests, because an Info object cannot be
constructed without Kentico's IoC container. Any change to a tuning object invalidates every index's
cached entry, which is the right trade at the volumes tuning data has.

**Foreclosed / deferred.**

- **Redirect rules were stored but not applied** while this unit shipped, because the owned JSON
  contract (ADR-0010) had no redirect member on the response and this unit was not allowed to add
  one. The field, the enum value and the drop-down option existed so no data was lost. **Resolved by
  unit CR-2 (2026-08-21):** `SearchResponse.redirect` is a required, nullable `{ url, rule }`;
  `BoostRulesStage` sets it from the first matching redirect rule in the precedence order above, and
  the search still runs, so the response carries results next to the destination. Following it is the
  client's decision — the shipped search box navigates only for a query the visitor submitted.
- **Index status is an edit page, not a listing.** The built-in listing template can only list a
  registered object type, and index status is derived from `ILuceneIndexManager` and the ingestion
  store. It is reported in a read-only text area, with the submit action wired to rebuild. A React
  listing would be better and belongs with the query tester unit.
- **The query tester (§8.4) and the analytics dashboard (§9.3)** need bespoke React and are not in
  this unit.
