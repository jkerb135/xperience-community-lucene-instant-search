# ADR-0016: The admin client module — one bundle, embedded, two custom pages

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §8.1, §8.4, §9.3

## Context

Spec §8.1 says to prefer built-in UI page templates and to write custom React only where a built-in
template genuinely cannot express the UI. Two pages fall on the far side of that line and the spec
names both as must-haves: the **Query tester** (§8.4 — one query, two rankings, per-hit score
explanations, "do not cut it") and the **Analytics dashboard** (§9.3 — six reports, a date range, a
chart, and a *Create rule* action that closes the loop from insight to fix).

Custom React means an admin client module: a Node project built by webpack, registered under an
organization and project name, and served to the administration application. That drags in decisions
about the toolchain, how the bundle reaches a consumer, how "without rules" is even executable, and
how a value gets from a dashboard row into a create form.

## Decision

**The boilerplate, renamed, and nothing else.** `src/XpSearch.Admin/Client` is the
`kentico-xperience-admin-sample` client project (`Kentico.Xperience.Templates` 31.8.0) with `orgName`
`yourco` and `projectName` `xperience-search-admin`, its dependency set pinned exactly. The only
deviations are `noUnusedLocals` in `tsconfig.json`, a `typecheck` script, and dropping the sample
component. No extra libraries: no chart library, no test runner, no CSS framework.

**Embedded, not proxied.** `AdminOrgName` + `AdminClientPath` in `XpSearch.Admin.csproj` make the
Kentico targets embed `Client/dist/**` into the assembly, which is the documented deployment mode and
the only one that works for a NuGet package — a consumer installs `YourCo.Xperience.Search.Admin`,
touches no `appsettings.json`, and the two pages load. `Proxy` mode stays available for our own
front-end work (`npm run start`, port 3009). `Client/dist` is gitignored and built by
`npm run build`; an MSBuild target fails the build with an actionable message when it is missing,
mirroring the `XpSearch.Widgets` asset target.

**"Without rules" is a second pipeline, not a second flag.** The tuning a search uses is loaded into
`SearchContext.Tuning` by `SynonymExpansionStage` from the DI-registered `IRelevanceTuningSource`;
nothing on `SearchRequest` can switch it off. `QueryTesterSearch` therefore composes a
`SearchPipeline` per side out of the *registered* stage instances, and for the "without rules" side
swaps that one stage for `new SynonymExpansionStage(new EmptyRelevanceTuningSource(), time)`. Every
other stage — query building, filters, execution, projection — is the one the live pipeline runs, so
the two columns differ in exactly one input. Two further consequences, both deliberate:

- The registered `ISearchPipeline` (the caching decorator) is bypassed. A tester that answers from a
  cache cannot show the effect of a rule a marketer saved ten seconds ago.
- `LogActivityStage` is dropped from both sides, so testing a query does not enter the aggregate
  query log and skew the dashboard next to it.

Core needed no new seam for any of this: `SearchPipeline`, `ISearchStage`, `ILuceneIndexAccessor`,
`IIndexSchemaProvider` and `EmptyRelevanceTuningSource` are all public.

**Query-level explanations come from a capture stage.** `ranking.boosts` on the wire is
`QueryExplanations + per-document entries` concatenated per hit, so the response alone cannot tell the
two apart. A private terminal stage inside `QueryTesterSearch` copies `context.QueryExplanations` out
of the context, and `QueryTesterDiff` skips that many entries per hit. The tester can therefore show
"how the query was rewritten" once and "what applied to this result" per row, with no Core change and
no string guessing.

**The diff is computed on the server and is a pure function.** `QueryTesterDiff.Compare` marks each
hit `Unchanged` / `MovedUp` / `MovedDown` / `Injected` / `Removed` by comparing positions by result
id, so the visual marking §8.4 asks for is unit-tested without an index or a browser, and the client
template stays a renderer. `ResultChange` carries `[JsonStringEnumConverter]` so the client switches
on names, not ordinals.

**The deep link is a parameterized slug carrying a base64url token.** A UI page can only be handed a
value through a parameterized URL slug, and AD-1's `RuleCreate` is registered under a static `create`
slug whose sibling parameterized slug is already taken by the rule edit section. So the pre-filled
form is a separate page, `ZeroResultRuleCreatePage : RuleCreate`, registered under the dashboard with
`PageParameterConstants.PARAMETERIZED_SLUG` and hidden from the navigation. A visitor's query is
arbitrary text and a slug is one URL segment, so index and query travel as one base64url token
(`RuleSeed`), which is exactly the character set a slug allows. `CreateRule` is a page command that
returns `NavigateTo(IPageLinkGenerator.GetPath<ZeroResultRuleCreatePage>(...))`. AD-1's own pages are
untouched.

**Design-system components for chrome, hand-written markup for the data.** *(Superseded by ADR-0020:
both pages were rebuilt to the owner's design spec and now use the component library throughout.)* `Button`, `Input`,
`Select`/`MenuItem`, `Headline` and `Spinner` come from `@kentico/xperience-admin-components`. The
report tables are plain semantic `<table>` markup: the design system's `Table` is built for listing
pages — virtualized, driven by column and cell descriptors — which buys nothing for six small reports
and complicates the per-row action. The volume chart is hand-written SVG with a `<details>` table
fallback, because the package exposes only `FunnelChart` and one series is not worth a chart library.
Both pages announce results through an `aria-live="polite"` region and keep every control labelled.

**Permissions are declared on the application.** `SearchTuningApplication` now declares `VIEW`,
`CREATE`, `UPDATE` and `DELETE` — the set its pages already evaluate implicitly through the built-in
templates — so `[UIEvaluatePermission]` and `[PageCommand(Permission = …)]` on the custom pages
reference permissions that Role management can actually grant. The tester and the dashboard require
`VIEW`; the *Create rule* command and the pre-filled create page require `CREATE`.

## Consequences

- Building `XpSearch.Admin` now requires Node: `npm ci && npm run build` in `src/XpSearch.Admin/Client`
  first, on CI as well. Documented in `docs/guides/admin-client-development.md`.
- Four names (webpack, csproj, `RegisterClientModule`, `UIPage` template names) must stay in sync;
  they are tabulated in that guide.
- The bundle is not in source control, so a package built from a clean clone without the npm step
  cannot exist — the build fails instead of shipping pages that silently fail to load.
- The tester's numbers can differ from a cached live search for as long as the search cache holds a
  stale entry. That is the intended reading: the tester shows what the index and the rules say now.
- Client-side testing is `tsc --noEmit` only; see KNOWN-LIMITATIONS.
