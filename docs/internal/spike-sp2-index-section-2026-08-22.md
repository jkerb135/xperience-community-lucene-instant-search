# Spike SP-2 - a per-index section under the Lucene integration's `IndexEditPage`

**Date:** 2026-08-22
**Status:** done, spike code deleted (`src/Search/Sp2Spike.cs` no longer exists)
**Host:** Dancing Goat sample at `F:\Personal\CommunityProjects\src`, XbK 31.8.0, `Kentico.Xperience.Lucene` 15.0.5
**Question:** can a `SECTION_LAYOUT` page with a left navigation be hung under the Lucene integration's
`IndexEditPage`, so that clicking an index in the "List of registered Lucene indices" leads to a sidebar
of per-index pages?

**Short answer: yes, at the registration and routing level. Not verified visually (see "What could not be observed").**

---

## What was built (throwaway, now deleted)

One file, `src/Search/Sp2Spike.cs`:

| Symbol | What it was |
| --- | --- |
| `Sp2Section : SecondaryMenuSectionPage` | registered under `parentType: typeof(IndexEditPage)`, slug `tuning`, `TemplateNames.SECTION_LAYOUT`, order 500 |
| `Sp2ChildA : ListingPage` | slug `a`, `TemplateNames.LISTING`, over `XpSearchRuleInfo`, with `[PageParameter(typeof(IntPageModelBinder))] public int IndexIdentifier` and a `QueryModifiers` filter on the resolved index name |
| `Sp2ChildB : ListingPage` | slug `b`, `TemplateNames.LISTING`, over `XpSearchSynonymInfo`, same bound parameter |
| `Sp2ListingExtender : PageExtender<IndexListingPage>` | replaced the row action with `AddEditRowAction<Sp2ChildA>()` and added `TableActions.AddLink<Sp2Section>(new AddLinkParameters("Tune") { Icon = Icons.Cogwheel })` |
| `Sp2SpikeModule : CMS.DataEngine.Module` | probe; dumped the admin UI tree, page attributes and generated links to stdout ten seconds after `ApplicationEvents.PostStart` |

`EditSectionPage<LuceneIndexItemInfo>` was **not** used - see "Which base class" below.

## What compiled

Everything above, after two corrections found by the compiler:

- `LuceneIndexItemInfo` lives in `Kentico.Xperience.Lucene.Core`, **not** `Kentico.Xperience.Lucene.Core.Indexing`.
- `Kentico.Xperience.Admin.Base.UITree` and `UITreeNode` are **internal**, despite being documented in
  `Kentico.Xperience.Admin.Base.xml`. The probe had to reach them by reflection. This is spike-only; the
  shipping library must not depend on them.
- `ActionConfiguration` has no public `CommandName` / `Url` members.

Solution build with the spike present: `0 Error(s)`. Host started clean (`Now listening on: http://localhost:27340`,
`Application started.`), no errors, failures or exceptions in the startup log other than the probe's own output.

---

## Evidence per question

All evidence is from the probe running **in-process** in the started host. Raw output is quoted verbatim.

### Q1 - does the section register and route?

Registered UI tree under the Lucene application (`UITree.Instance.Root`, walked by reflection):

```
- slug='lucene'  param=False template='@kentico/xperience-admin-base/SectionLayout' type=LuceneApplicationPage
    nav=True permissions=PermissionConfiguration(PermissionEvaluationIgnored=False, Permission=)
    app=ApplicationConfiguration(Identifier=Kentico.Xperience.Integrations.Lucene.Admin, IsDynamic=False)
  - slug='indexes' param=False template='@kentico/xperience-admin-base/Listing' type=IndexListingPage
      nav=True permissions=...Permission=View  app=<null>  extenders=[DancingGoat.Search.Sp2ListingExtender]
    - slug='create' param=False template='@kentico/xperience-admin-base/Edit' type=IndexCreatePage
        nav=True permissions=...Permission=Create app=<null> extenders=[]
    - slug=':IndexEditPage1' param=True template='@kentico/xperience-admin-base/Edit' type=IndexEditPage
        nav=True permissions=...Permission=Update app=<null> extenders=[]
      - slug='tuning' param=False template='@kentico/xperience-admin-base/SectionLayout' type=Sp2Section
          nav=False permissions=...Permission=  app=<null> extenders=[]
        - slug='a' param=False template='@kentico/xperience-admin-base/Listing' type=Sp2ChildA
            nav=True permissions=...Permission=View app=<null> extenders=[]
        - slug='b' param=False template='@kentico/xperience-admin-base/Listing' type=Sp2ChildB
            nav=True permissions=...Permission=View app=<null> extenders=[]
```

Link generation (`IPageLinkGenerator.GetPath<T>` throws `InvalidOperationException` when the page type is
not registered, so a returned path is proof of registration **and** of the resolved route shape):

```
First registered Lucene index id = 2
GetPath<Sp2Section>    = /lucene/indexes/2/tuning
GetPath<Sp2ChildA>     = /lucene/indexes/2/tuning/a
GetPath<Sp2ChildB>     = /lucene/indexes/2/tuning/b
GetPath<IndexEditPage> = /lucene/indexes/2
```

The parameter key for `PageParameterValues` is `typeof(IndexEditPage)` - the tree confirms `IndexEditPage`
is the parameterized node (`slug=':IndexEditPage1' param=True`) and it is the **nearest parameterized
ancestor** of both children, which is exactly what the parameterless `[PageParameter(typeof(IntPageModelBinder))]`
constructor binds to.

`nav=False` on `Sp2Section` is not a defect: the probe showed `SecondaryMenuSectionPage` itself carries
`[UINavigation]` (`Attributes on SecondaryMenuSectionPage (inherited): [UINavigationAttribute]`), i.e. the
base class opts the section root out of its *parent's* menu. Its two children are `nav=True` and are what
the secondary menu renders. This is the desired behaviour and needs no `[UINavigation]` from us.

### Q2 - the extender and the row action

**Registration: confirmed by the tree, not by inference.** The `IndexListingPage` node reports
`extenders=[DancingGoat.Search.Sp2ListingExtender]`. A `[assembly: PageExtender(typeof(...))]` on a
`PageExtender<IndexListingPage>` in the host assembly is picked up.

**Can the default edit row action be replaced or removed?** Yes, both.

```
ListingConfiguration.RowAction: type=ActionConfiguration, canRead=True, canWrite=True, setterPublic=True
IRowActionListingConfiguration.RowAction: canWrite=True
ListingConfiguration.TableActions: type=IList`1
```

- `RowAction` is a single `ActionConfiguration` with a **public setter** on both `ListingConfiguration`
  and the `IRowActionListingConfiguration` interface. `PageConfiguration.AddEditRowAction<TPage>()` called
  from an extender simply overwrites whatever `IndexListingPage.ConfigurePage` set, and `RowAction = null`
  removes it. Extenders run after the page's own `ConfigurePage`, so last writer wins - us.
- `TableActions` is a plain `IList<ActionConfiguration>`: `Remove`, `RemoveAt` and `Clear` are all available.

`AddEditRowAction<Sp2ChildA>()` is the right call for "clicking the index row goes to the section", because
that helper substitutes the row's object id into the nearest parameterized ancestor slug - here
`:IndexEditPage1` - producing `/lucene/indexes/{rowId}/tuning/a`, which the `GetPath` output above shows
is the correct shape. A separate `TableActions.AddLink<Sp2Section>(...)` compiles, but its
`PageParameterValues` is fixed at configuration time and has no access to the row, so it is the wrong
tool for a per-row link; prefer `AddEditRowAction<T>`.

### Q3 - permissions

**Our pages are governed by the *Lucene* application, not by `XpSearch.SearchTuning`.** Evidence: only the
application root node carries an `Application` configuration -
`Identifier=Kentico.Xperience.Integrations.Lucene.Admin` - and every descendant node (including
`Sp2Section`, `Sp2ChildA`, `Sp2ChildB`) reports `app=<null>`, i.e. it inherits the nearest ancestor
application. This matches the docs: "This permission must be from the set defined for the corresponding
application using the `UIPermission` attribute. Otherwise, you would be unable to assign this permission
to roles via Role management."
(<https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-permission-checks>)

Concretely:

- `LuceneApplicationPage` declares five `UIPermission`s (the probe counted
  `[UIPermissionAttribute x5]`): VIEW, CREATE, UPDATE, DELETE and `LuceneIndexPermissions.REBUILD`.
- Our two `ListingPage` children automatically evaluate VIEW (`Permission=View` on both nodes, supplied
  by the LISTING template, not by us). That permission **is** in the Lucene application's declared set,
  so a role granted "View" on the Lucene application can open them. This works out of the box.
- Any permission **not** in that set - notably anything declared on `SearchTuningApplication`
  (`XpSearch.SearchTuning`) - stops applying to a page moved under the Lucene app, and would not be
  assignable to roles for those pages. If we needed a bespoke permission there, we would have to add it
  with `[UIPermission("...")]` on a `PageExtender<LuceneApplicationPage>` (documented and supported), not
  by keeping it on `SearchTuningApplication`.
- Pages moved out of `XpSearch.SearchTuning` also disappear from that application's Role-management
  permission surface, so the existing VIEW/CREATE/UPDATE/DELETE grants on `XpSearch.SearchTuning` would no
  longer gate them.

### Q4 - other platform facts that bear on the plan

1. **The parent is an EDIT-template page with an UPDATE permission.** `IndexEditPage` reports
   `template='@kentico/xperience-admin-base/Edit'` and `Permission=Update`. So the URL segment that
   supplies our index id sits behind UPDATE on the Lucene application, while our own children only need
   VIEW. A view-only role can reach `/lucene/indexes/{id}/tuning/a` by direct link (the ancestor's
   permission is not re-evaluated for a descendant request), but cannot get there by clicking through the
   index edit form. Read-only tuning users are therefore reachable only via a row action that jumps
   straight to the section - which is what Q2's `AddEditRowAction<Sp2ChildA>()` does.
2. **`UITree` / `UITreeNode` are internal.** The docs describe them as public API; the compiler disagrees
   (`error CS0122: 'UITree' is inaccessible due to its protection level`). No shipping code may introspect
   the page tree. Registration correctness has to be asserted through `IPageLinkGenerator.GetPath<T>`,
   which is public and throws when a page is unregistered - that is the runnable check to keep.
3. **`SecondaryMenuSectionPage` already carries `[UINavigation]`.** Do not add `[UINavigation(false)]`
   ourselves and do not expect the section root to appear in the Lucene app's own left menu.
4. **The menu hides itself with a single child.** The Kentico docs state the navigation is not displayed
   for a level with only one page, so the section needs at least two children to be worth the indirection.
5. **`LuceneIndexItemInfo` is in `Kentico.Xperience.Lucene.Core`**, and `IInfoProvider<LuceneIndexItemInfo>`
   resolves fine from the host container - `Get(id)?.LuceneIndexItemIndexName` is all that is needed to
   turn the bound id into the index code name our tuning tables key on (`RuleIndexName` etc.).

### Which base class - `SecondaryMenuSectionPage` or `EditSectionPage<LuceneIndexItemInfo>`?

**`SecondaryMenuSectionPage`.** `EditSectionPage<TInfo>` also uses `SECTION_LAYOUT`, but its job is to
*introduce* a parameterized slug for `TInfo` (it exposes `ObjectId`, `ObjectType`, `GetInfoObject` and
sets the object's display name as the section caption). The parameterized slug we need already exists -
it is `IndexEditPage`'s. Hanging an `EditSectionPage<LuceneIndexItemInfo>` under `IndexEditPage` would add
a **second** id segment to the URL (`/lucene/indexes/2/<indexId>/...`) and force every link generator call
to supply both. `SecondaryMenuSectionPage` adds a static `tuning` segment and nothing else, which is
exactly the shape the `GetPath` output shows.

---

## Recommended page tree

```
LuceneApplicationPage                     (lucene)              - integration, SECTION_LAYOUT
└── IndexListingPage                      (indexes)             - integration, LISTING
    │   + XpSearch PageExtender<IndexListingPage>:
    │       PageConfiguration.AddEditRowAction<TuningRuleListing>()   // row click -> our section
    │       (optionally TableActions.AddLink<IndexEditPage-ish>("Configure") to keep the old edit reachable)
    └── IndexEditPage                     (:id)                 - integration, EDIT
        └── XpSearchIndexSection          (tuning)              - ours, SecondaryMenuSectionPage, SECTION_LAYOUT
            ├── TuningRuleListing         (rules)     order 100 - ours, LISTING,  filtered by index name
            ├── TuningSynonymListing      (synonyms)  order 200 - ours, LISTING,  filtered by index name
            └── ... (stopwords, field weights, query tester, analytics)
```

Extender approach: a single `PageExtender<IndexListingPage>` in `XpSearch.Admin`, annotated
`[assembly: PageExtender(typeof(...))]`, that overwrites `PageConfiguration.RowAction` via
`AddEditRowAction<TuningRuleListing>()`. Because extenders run after the extended page's own
`ConfigurePage`, this reliably wins over the integration's `AddEditRowAction<IndexEditPage>()`.

## What could not be observed

The admin SPA returns its HTML shell with HTTP 200 for **every** `/admin/...` path when unauthenticated -
verified against the running host for `/admin/lucene/indexes`, `/admin/api/page/lucene/indexes` and the
deliberately bogus `/admin/bogusapp/xyz`, all 200 with an identical `<!DOCTYPE html>` body. HTTP status is
therefore worthless as route evidence here, and no administrator credentials for the `comm_projects`
database were available in the repo or user secrets, so the authenticated page-metadata JSON could not be
fetched. Consequently:

- Whether the left navigation actually **renders** under an EDIT-template parent, and how the breadcrumb
  chain looks, is unverified. This is the main residual risk.
- `IndexIdentifier` binding was proven only structurally (`:IndexEditPage1` is the nearest parameterized
  ancestor, and `GetPath` produces `/lucene/indexes/2/tuning/a`), not by observing the bound value at
  request time; the `ConfigurePage` log line that would have shown it never fired.
- The extender's effect on the rendered row action was proven at the API level (public setter, extender
  registered on the node), not by clicking a row.

## Open risks

1. **Rendering under an EDIT parent is unproven.** Kentico's own documented example hangs a
   `SecondaryMenuSectionPage` under a LISTING page (`UserList`). Nothing in the docs says an EDIT parent is
   supported. Resolve this with five minutes in a logged-in browser before committing to the design.
2. **Permission model shifts to the Lucene application.** Anyone who relies on `XpSearch.SearchTuning`
   role grants loses them for moved pages. If bespoke tuning permissions are wanted, they must be added
   with `[UIPermission]` on a `PageExtender<LuceneApplicationPage>`.
3. **UPDATE-gated ancestor.** `IndexEditPage` requires UPDATE, so click-through from the index edit form
   is unavailable to view-only roles; the row action from the listing (VIEW) is the only viable entry.
4. **Hard dependency on integration internals.** `IndexEditPage`, `IndexListingPage` and the parameterized
   slug shape are `Kentico.Xperience.Lucene.Admin` implementation details. A future 16.x that renames or
   restructures them breaks our registration at startup, not at compile time in the consumer's app. Keep a
   `GetPath<T>` self-check so the failure is loud.
5. **Row-action hijack is a global side effect.** Replacing `RowAction` changes the integration's own
   listing for every user of the host, including people who only wanted to edit index configuration. Keep
   the original edit page reachable via an explicit `TableActions` link.
