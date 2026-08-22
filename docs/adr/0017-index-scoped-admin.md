# ADR-0017: the relevance-tuning admin pages live inside the Lucene integration's index

- **Status:** accepted
- **Date:** 2026-08-22
- **Spec reference:** §8.1, §9.3, §10.8 (amends the page tree of ADR-0014 and ADR-0016)

## Context

Every row the tuning tables hold is keyed on an index code name: `RuleIndexName`, `SynonymIndexName`,
`StopwordListIndexName`, `WeightIndexName`. The Search tuning application built in AD-1/AD-2 ignored
that and listed every index's rows together, with an **Index** drop-down on each form. Two costs
followed:

1. A marketer with three indexes had to read the Index column on every listing and pick the right
   index on every form. Picking the wrong one silently wrote a rule that never fires.
2. The index's own configuration lived in a different application (*Lucene Search*) from everything
   that shapes its results, so "what does this index do" was two places.

The owner decided on 2026-08-22 that only **API keys** and **Ingestion log** — the two pages that are
about systems pushing data in, not about one index — stay standalone; everything else becomes
per-index.

Spike SP-2 (`docs/internal/spike-sp2-index-section-2026-08-22.md`) established, in a running host,
that this is buildable: a `SecondaryMenuSectionPage` registered under the integration's
`IndexEditPage` registers and routes at `/lucene/indexes/{id}/tuning`, its children form the left
navigation, `[PageParameter(typeof(IntPageModelBinder), typeof(IndexEditPage))]` binds the index id,
and a `PageExtender<IndexListingPage>` can overwrite the listing's row action because
`ListingConfiguration.RowAction` has a public setter and extenders run last.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep the flat Search tuning application, add an index filter | No dependency on integration internals; nothing moves | The index is still a field on every form, so the "wrong index" mistake stays; index configuration is still elsewhere |
| Own parameterized index section inside `SearchTuningApplication` (`/xpsearch-tuning/{id}/…`) | No dependency on `IndexEditPage`; permissions stay ours | A second index picker to build and keep in sync with the integration's listing; two places that list indexes |
| **Hang a section under the integration's `IndexEditPage`** | One index listing; the row click leads straight to tuning; the index's configuration and its tuning are one sidebar | Hard dependency on `Kentico.Xperience.Lucene.Admin` page types; permissions move to the Lucene application; the row-action override is a global side effect |
| `EditSectionPage<LuceneIndexItemInfo>` instead of `SecondaryMenuSectionPage` | Gives the section the index's display name as a caption | Introduces a *second* id segment in the URL, because the parameterized slug we need already exists on `IndexEditPage` |

## Decision

Hang `IndexTuningSection : SecondaryMenuSectionPage` (slug `tuning`, `SECTION_LAYOUT`) under
`Kentico.Xperience.Lucene.Admin.IndexEditPage`, and register every per-index page as its child:

```
Lucene Search  (Kentico.Xperience.Integrations.Lucene.Admin)
└─ indexes                          IndexListingPage — row action replaced by our PageExtender
   └─ {id}                          IndexEditPage — the integration's, untouched
      └─ tuning                     IndexTuningSection
         ├─ settings      100       IndexSettingsPage : BaseIndexEditPage
         ├─ rules         200
         ├─ synonyms      300
         ├─ stopwords     400
         ├─ weights       500
         ├─ query-tester  600       React, index locked
         ├─ analytics     700       React, index locked (+ the zero-result rule create page)
         └─ status        800
Search ingestion  (XpSearch.SearchTuning — identifier and slug unchanged)
   ├─ api-keys
   └─ ingestion-log
```

Supporting decisions:

- **`IndexScope`** (`XpSearch.Admin/UIPages/IndexTuning.cs`) is the only place that turns the URL's
  identifier into an index code name (through `ILuceneConfigurationStorageService`), builds the
  `PageParameterValues` every in-section link needs, and decides whether a stored row belongs to the
  URL's index. `ILuceneIndexManager` was rejected for the lookup because `LuceneIndex` is sealed with
  an internal constructor and therefore cannot be faked in a unit test; `LuceneIndexModel` is a public
  POCO.
- **The index is not an input.** Listings filter on the resolved index name with a query modifier and
  drop the Index column; forms show the index read-only, take its value from the URL on submit, and
  refuse a row whose stored index differs from the URL's (`IndexScope.Matches`), so a rule opened
  through another index's URL is rejected rather than silently re-homed.
- **`IndexSettingsPage`** mirrors the integration's `IndexEditPage` (same model, same
  `ValidateAndProcess` submit) rather than re-parenting it, because a `UIPage` registration names one
  parent and the integration's is fixed.
- **The React templates** are told `indexLocked: true` and render the index as text instead of a
  `Select`; the page commands take the index from the URL and ignore what the client sends.
- **`SearchTuningApplication`** keeps its identifier `XpSearch.SearchTuning` and its slug, is renamed
  to *Search ingestion*, and drops the `UPDATE` permission it no longer evaluates.

## Evidence

- Spike SP-2, in-process probe on a running host (XbK 31.8.0, `Kentico.Xperience.Lucene` 15.0.5):
  registered UI tree, `IPageLinkGenerator.GetPath<T>` returning `/lucene/indexes/2/tuning/a`, and the
  reflected `ListingConfiguration.RowAction` setter. Full output in the spike note.
- `dotnet build src/XpSearch.Admin` — 0 warnings, 0 errors (warnings are errors; XML docs required).
- `dotnet test tests/XpSearch.Admin.Tests` — 45 passed (34 before this unit). New tests cover
  `IndexScope.Resolve`/`Route`/`Matches` and that both client templates are handed a locked index.

## Consequences

**Easier.** One index listing is the only place an index is chosen. Clicking a row lands on the
tuning sidebar. A form can no longer be saved against the wrong index. Adding a per-index page is one
`UIPage` registration under `IndexTuningSection`.

**Breaking, for roles.** The moved pages are governed by the **Lucene Search** application
(`Kentico.Xperience.Integrations.Lucene.Admin`), because a UI page inherits the nearest ancestor
application. Existing `XpSearch.SearchTuning` grants no longer gate them; a role that could tune
search must be granted *View*/*Create*/*Update*/*Delete* on Lucene Search instead. `XpSearch.SearchTuning`
grants now only cover API keys and the ingestion log. Any bespoke tuning permission would have to be
added with `[UIPermission]` on a `PageExtender<LuceneApplicationPage>`, not on our application.

**Breaking, for URLs.** `/admin/xpsearch-tuning/rules`, `/synonyms`, `/stopwords`, `/field-weights`,
`/query-tester`, `/analytics` and `/index-status` are gone. Bookmarks and links in host documentation
must be re-pointed at `/admin/lucene/indexes/{id}/tuning/…`, and `field-weights` is now `weights`,
`index-status` now `status`.

**Behaviour changes.** The analytics dashboard no longer has an "every index" view — it reports the
index you are in. The query tester's index selector is gone.

**A global side effect.** Our `PageExtender<IndexListingPage>` rewrites the row action of a page that
belongs to the integration, for every user of the host. Someone who only wanted the index
configuration form now lands on the tuning sidebar and reaches it as **Settings**.

**More expensive.** A hard dependency on `Kentico.Xperience.Lucene.Admin` implementation details
(`IndexEditPage`, `IndexListingPage`, `BaseIndexEditPage`, the shape of the parameterized slug). A
16.x that renames or restructures them breaks registration at host startup, not at compile time in
the consumer's app. `IndexSettingsPage` duplicates ~30 lines of the integration's edit page and has
to be re-checked when the integration's form changes.

**Unverified.** Kentico's documented example hangs a `SecondaryMenuSectionPage` under a LISTING page.
Whether the secondary menu *renders* under an EDIT-template parent, and how the breadcrumb reads, was
not observable headlessly — see `docs/internal/KNOWN-LIMITATIONS.md` and the HW-7 host pass.
