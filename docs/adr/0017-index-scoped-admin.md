# ADR-0017: the relevance-tuning admin pages live inside the Lucene integration's index

- **Status:** accepted (amended 2026-08-22 by CA-6, after the HW-7 host pass)
- **Date:** 2026-08-22
- **Spec reference:** §8.1, §9.3, §10.8 (amends the page tree of ADR-0014 and ADR-0016)

**Amendment, CA-6.** The section was first registered under the integration's `IndexEditPage`. The
platform rejects that, and the rejection is fatal to the whole administration — see *The rule the
platform enforces* below. The section now hangs under the index **listing**, behind a static `tuning`
segment, and contributes the index identifier itself. URLs changed accordingly.

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
that a `SecondaryMenuSectionPage` can carry the tuning pages as a left navigation, that
`[PageParameter(typeof(IntPageModelBinder), typeof(...))]` binds an ancestor's index id, and that a
`PageExtender<IndexListingPage>` can overwrite the listing's row action because
`ListingConfiguration.RowAction` has a public setter and extenders run last. Its further claim that
the section registers **under `IndexEditPage`** is contradicted by the HW-7 host pass and is
superseded; see below.

## The rule the platform enforces

A page rendered in the main content of its parent may not have a parent that uses the `EDIT`
template. Xperience validates this while building the UI tree and throws:

```
InvalidOperationException: Node 'IndexTuningSection' must use either 'SidePanel' or 'Dialog' page
location because its parent uses the '@kentico/xperience-admin-base/Edit' template. Update the
location via 'UIPageLocationAttribute'.
```

The tree is validated as a unit, so the failure is not local: **every** admin page in the host —
including Xperience's own and pages this package leaves standalone — becomes unresolvable. It is
invisible at startup (the tree is built lazily) and `GET /admin` still returns 200, because the admin
SPA serves its shell without touching the tree. Evidence: `docs/internal/host-pass-hw7-2026-08-22.md`
§4 and §6.1, an in-process `IPageLinkGenerator.GetPath` probe over every registered page.

Adding `[UIPageLocation(PageLocationEnum.SidePanel)]` would satisfy the validation but put the whole
sidebar — the query tester's two columns, the analytics charts — into the right-hand panel over the
index edit form. Kentico's documented shape hangs a `SecondaryMenuSectionPage` under a **LISTING**
page instead, so that is what this ADR now does.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep the flat Search tuning application, add an index filter | No dependency on integration internals; nothing moves | The index is still a field on every form, so the "wrong index" mistake stays; index configuration is still elsewhere |
| Own parameterized index section inside `SearchTuningApplication` (`/xpsearch-tuning/{id}/…`) | No dependency on `IndexEditPage`; permissions stay ours | A second index picker to build and keep in sync with the integration's listing; two places that list indexes |
| Hang a section under the integration's `IndexEditPage` | Reuses the identifier already in that page's URL, so the section only adds a static segment | **Rejected by the platform** — an EDIT-template parent admits only `SidePanel`/`Dialog` children, and the refusal breaks the entire admin UI tree |
| A second parameterized slug directly under `IndexListingPage`, beside `IndexEditPage`'s | One segment shorter | Two parameterized siblings under one parent is a shape Kentico's documentation never shows; unverifiable without another host pass |
| **Hang a section under the integration's `IndexListingPage`, behind a static `tuning` segment** | One index listing; the row click leads straight to tuning; the index's configuration and its tuning are one sidebar; the documented shape (listing → parameterized `SECTION_LAYOUT` section) | Hard dependency on `Kentico.Xperience.Lucene.Admin` page types; permissions move to the Lucene application; the row-action override is a global side effect; one extra pass-through page |
| `EditSectionPage<LuceneIndexItemInfo>` instead of `SecondaryMenuSectionPage` | Gives the section the index's display name as a caption | Binds the section to an object type of the integration's, for a caption; `SecondaryMenuSectionPage` carries the same parameterized slug without that coupling |

## Decision

Hang `IndexTuningSection : SecondaryMenuSectionPage` (`PageParameterConstants.PARAMETERIZED_SLUG`,
`SECTION_LAYOUT`) under `IndexTuningRoot`, a pass-through page that contributes the static `tuning`
segment under `Kentico.Xperience.Lucene.Admin.IndexListingPage`, and register every per-index page as
the section's child:

```
Lucene Search  (Kentico.Xperience.Integrations.Lucene.Admin)
└─ indexes                          IndexListingPage — row action replaced by our PageExtender
   ├─ {id}                          IndexEditPage — the integration's, untouched, still reachable by URL
   └─ tuning                        IndexTuningRoot — renders nothing, see below
      └─ {id}                       IndexTuningSection — ParameterDefaultValue = "1"
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

- **`IndexTuningRoot`** is a `SecondaryMenuSectionPage` with `SECTION_LAYOUT` and exactly one child.
  A `SECTION_LAYOUT` page with a single child shows no menu and displays that child, so it renders
  nothing of its own; it exists only to keep our parameterized slug from becoming a second
  parameterized child of the listing. `AddEditRowAction<IndexTuningSection>()` still works unchanged:
  the method appends the row's identifier to the target page's own parameterized slug, and no other
  slug on the path is parameterized, so it needs no explicit `PageParameterValues`.
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

- Spike SP-2, in-process probe on a running host (XbK 31.8.0, `Kentico.Xperience.Lucene` 15.0.5): the
  reflected `ListingConfiguration.RowAction` setter, and that a `SecondaryMenuSectionPage` carries a
  left navigation. Its `GetPath` result for a section under `IndexEditPage` is superseded by HW-7.
- HW-7 host pass, in-process `GetPath` probe over every registered page: the EDIT-parent registration
  throws for **all** of them (`docs/internal/host-pass-hw7-2026-08-22.md` §4).
- `dotnet build src/XpSearch.Admin` — 0 warnings, 0 errors (warnings are errors; XML docs required).
- `dotnet test tests/XpSearch.Admin.Tests` — 46 passed (34 before this unit). Tests cover
  `IndexScope.Resolve`/`Route`/`Matches`, that both client templates are handed a locked index, and
  that no page in this assembly renders in the main content of an EDIT-template parent.
- **Not yet verified on a host:** the re-parented tree. HW-8 has to re-run the HW-7 probe.

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
must be re-pointed at `/admin/lucene/indexes/tuning/{id}/…`, and `field-weights` is now `weights`,
`index-status` now `status`. Note the segment order: the static `tuning` segment comes **before** the
index identifier, because the identifier is contributed by our own section, not by the integration's
edit page.

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

**Unverified.** How the breadcrumb reads across the pass-through `tuning` segment, and whether the
sidebar renders in the order registered, is not observable headlessly — HW-8 has to look.
