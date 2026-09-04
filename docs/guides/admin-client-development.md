## Admin client module development

`XpSearch.Admin` ships two administration pages that are custom React templates rather than built-in
ones: the **Query tester** (spec §8.4) and the **Analytics dashboard** (spec §9.3). Their front end
lives in `src/XpSearch.Admin/Client` and is a standard Xperience admin client module, built with
webpack and embedded into `XpSearch.Admin.dll`.

If you never touch those two pages, you still need the bundle to build the project.

### Build it

```bash
cd src/XpSearch.Admin/Client
npm ci
npm run build          # writes Client/dist/entry.kxh.<hash>.js
cd ../../..
dotnet build src/XpSearch.Admin
```

`dotnet build src/XpSearch.Admin` fails with a clear message if `Client/dist` holds no bundle. The
output is gitignored, so this is the first thing to run on a fresh clone — the same rule as
`src/XpSearch.Widgets/Client` for `XpSearch.Widgets`.

Other scripts: `npm run typecheck` (`tsc --noEmit`, strict), `npm run test` (node's own runner over
`src/**/*.test.ts` — no framework, and the only place a `.test.ts` file may live; they are excluded
from `tsconfig.json` and never bundled) and `npm run start` (webpack dev server on port 3010, see
*Serving* below).

### Layout

| Path | What it is |
|---|---|
| `Client/package.json` | The Kentico admin boilerplate's dependency set, versions pinned exactly. |
| `Client/webpack.config.js` | The boilerplate config with `orgName: "xperience-community"`, `projectName: "xperience-search"`. |
| `Client/babel.config.json`, `Client/tsconfig.json` | Boilerplate, unchanged apart from `noUnusedLocals`. |
| `Client/src/entry.tsx` | Exports every component the admin application may load. |
| `Client/src/query-tester/QueryTesterTemplate.tsx` | Client template of the query tester. |
| `Client/src/analytics/AnalyticsDashboardTemplate.tsx` | Client template of the dashboard. |
| `Client/src/analytics/ReportTable.tsx`, `VolumeChart.tsx` | The dashboard's report card (a `Card` around the stock `Table`) and its inline-SVG chart. |
| `Client/src/status/`, `rule-builder/`, `experiments/` | Client templates of the index status page, the if/then rule builder and the experiment detail page. |
| `Client/src/<page>/<Page>.module.scss` | Each page's layout wrappers — see *Layout guidelines for custom pages* below. |
| `Client/src/theme.ts` | The text treatments no component exposes (`muted`, `figure`, `stateFigure`, `flexRow`), built from the package's `Colors` tokens. |

These pages are built to the owner's design spec
(<https://claude.ai/design/p/d9cffec1-046f-46e2-b611-d162418351f9>) and may only use
`@kentico/xperience-admin-components`. Check a component and its prop names in
`node_modules/@kentico/xperience-admin-components/dist/entry.d.ts` before using it — that file is the
authority. See ADR-0020.

### The four names that must agree

A page's `templateName` is `@<orgName>/<projectName>/<ComponentName>`, and the admin application
appends `Template` to the component name. Change any one of these and all four must change together:

| Where | Value |
|---|---|
| `Client/webpack.config.js` | `orgName: "xperience-community"`, `projectName: "xperience-search"` |
| `XpSearch.Admin.csproj` | `<AdminOrgName>xperience-community</AdminOrgName>`, `<AdminClientPath>`'s `<ProjectName>xperience-search</ProjectName>` |
| `XpSearchAdminClientModule.cs` | `RegisterClientModule("xperience-community", "xperience-search")` |
| `UIPage` registrations | `"@xperience-community/xperience-search/QueryTester"`, `"@xperience-community/xperience-search/AnalyticsDashboard"` |

So `QueryTesterTemplate` (exported from `entry.tsx`) backs `.../QueryTester`.

### Serving

The module is served in **Embedded** mode: the `Kentico.Xperience.Admin` targets turn everything
under `AdminClientPath` into embedded resources of `XpSearch.Admin.dll`, so the NuGet package carries
its own admin UI and a host application needs no configuration and no dev server. Embedded is the
default when a module has no `Mode` configured, so a host's `appsettings.json` can stay untouched.

To check that a build really embedded the bundle, read the assembly's manifest resource names - the
targets add the `EmbeddedResource` items *during* the build, so inspecting the project
(`dotnet msbuild -getItem:EmbeddedResource`) evaluates before they exist and reports nothing:

```powershell
[Reflection.Assembly]::LoadFrom("src/XpSearch.Admin/bin/Debug/net8.0/XpSearch.Admin.dll").GetManifestResourceNames()
# XpSearch.Admin.AdminResources.xperience_community.xperience.search.entry.kxh.<hash>.js
```

The hash has to match the file `npm run build` wrote to `Client/dist`.

For front-end work, `Proxy` mode is faster — the host serves the templates from your webpack dev
server instead of the assembly. In the host application:

```json
"CMSAdminClientModuleSettings": {
  "xperience-community-xperience-search": {
    "Mode": "Proxy",
    "Port": 3010
  }
}
```

Then run `npm run start` in `src/XpSearch.Admin/Client`. Remove the setting before shipping.

### Adding a page

1. Write the template in `Client/src/<area>/<Name>Template.tsx` and export it from `entry.tsx`.
2. Write the back end as `Page<TClientProperties>` with a `TemplateClientProperties` subclass, and
   register it with `[assembly: UIPage(..., "@xperience-community/xperience-search/<Name>", order)]`.
3. Fetch data with `[PageCommand]` handlers and `usePageCommand` on the client. Give every command a
   `Permission` from the set the *owning application* declares — for a page inside
   `IndexTuningSection` that is the Lucene integration's application (`View`, `Create`, `Update`,
   `Delete`, `Rebuild`); for a page under `SearchTuningApplication` it is `View`, `Create`, `Delete`.
4. `npm run typecheck`, `npm test`, `npm run build`, `dotnet build src/XpSearch.Admin`.

#### Pages inside an index

Both shipped templates hang under `IndexTuningSection`, at
`/admin/lucene/indexes/edit/{id}/<your-slug>`, so the index is not something the visitor picks:
`IndexTuningSection` contributes the `{id}` segment. The pattern is:

```csharp
[PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
public int IndexIdentifier { get; set; }

private string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

public override Task<MyClientProperties> ConfigureTemplateProperties(MyClientProperties properties)
{
    properties.IndexNames = [IndexName];
    properties.SelectedIndexName = IndexName;
    properties.IndexLocked = true;

    return Task.FromResult(properties);
}
```

`indexLocked` is the contract with the template: when it is `true` the template renders the index as
text instead of a `Select`. The command handlers ignore any index the client sends and use
`IndexName` from the URL, so a tampered payload cannot reach another index.

Constraints worth knowing before you start: dynamic `import()` is not supported in admin client
modules, and `react`, `react-dom`, `react-router`, `i18next` and `@hello-pangea/dnd` are shared at
runtime — never bundle your own copy.

### Reference

- Prepare your environment for admin development:
  <https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/prepare-your-environment-for-admin-development>
- UI pages:
  <https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages>
- UI page commands:
  <https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-commands>
- UI page permission checks:
  <https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-permission-checks>

## Stylesheets

`*.module.css` and `*.module.scss` are CSS modules: import the default export for the scoped
class-name map. Plain `*.css` / `*.scss` imports are global. Sass (`sass` + `sass-loader`) runs
before the same css-loader pipeline, so nesting, variables and partials all work; the rule-builder
styles (`src/rule-builder/RuleBuilderTemplate.module.scss`) are the in-tree example.

## Layout guidelines for custom pages

Every custom page in this module — the query tester, the analytics dashboard, the index status page,
the rule builder and the experiment detail page — follows the same six rules. The query tester
(`src/query-tester/QueryTesterTemplate.tsx` and its `.module.scss`) is the reference implementation;
copy its patterns rather than inventing new ones.

**1. Kentico components stay.** `Table`, `Pagination`, `Card`, `Callout`, `SidePanel`, `Tag`,
`Button`, `Input`, `Select`, `Checkbox`, `NameToggleButtons`, `Headline`, `Icon`, `Divider` and
`Stack` are never re-implemented. Write your own markup only where the package has no component for
the thing — a chart, a stacked bar, a drag handle, an iconed chip — and list each such region in the
page's ADR.

**2. Layout is done by wrappers.** Flex or CSS grid containers of your own, in a `.module.scss` next
to the template, set `flex-direction`, `gap`, `justify-content` and `align-items`; the stock
components go inside them. If you must override a stock component's geometry, hook its hashed class
prefix (`:global([class*="table-row___"])`) and record the override in
`docs/internal/KNOWN-LIMITATIONS.md`.

**3. One spacing rhythm, on the 8px grid.** Page sections and cards are **24px** apart
(`<Stack spacing={Spacing.XL}>` or `gap: 24px`). Sections inside a card are **16px** apart. Inline
groups use 8, 12 or 16px. Card padding is the stock 24px, and a page adds **no page-level padding of
its own** — the administration shell already pads it. Never 10, 14 or 20px.

**4. One page header pattern.** A flex row with `justify-content: space-between` and `align-items:
baseline` (`center` when the right side is a button): the title on the left — the card headline's
24/32 when the page opens with a card, `<Headline size={HeadlineSize.L}>` otherwise — with the muted
meta line (`Index … · …`) beside or directly under it, and the page's actions on the right.

**5. Tokens only.** Colours come from the package's `--color-*` custom properties in stylesheets and
from `Colors` in TypeScript. No literal hex, and **no `var(--x, #fallback)` fallbacks**: a fallback
silently hides a misspelled token name. Verify a name exists before using it —

```bash
grep -o -- '--color-[a-z-]*' \
  node_modules/@kentico/xperience-admin-components/dist/entry.js | sort -u
```

(`--color-border-selected`, for one, does not exist; the product accent is
`--color-product-selected`.) The package ships colour tokens only, so spacing and radii are literals
on the 8px grid (4 / 8 / 16px radii). Font sizes come from the ramp — 11, 12, 14, 16, 24 — with
weights 400 / 600 / 700, `"GT Walsheim", sans-serif` for headlines, tags and buttons, and Inter for
body text.

**6. Text treatments live in `src/theme.ts`** (`muted`, `figure`, `stateFigure`, `flexRow`). Add a
treatment there instead of writing an inline `style={{ … }}` literal in a template. An inline style
is for **data** only — the colour of a chart series or the width of a bar segment.

`npm run test` enforces rules 3 and 5 statically over every stylesheet in `src`
(`src/layout.test.ts`).

### `Row` inside a `Stack`

`Stack` spaces its children with `margin-top`, and `Row` sets a negative inline `margin-top` of its
own spacing to compensate the gutter padding its `Column`s add. A `Row` placed directly in a `Stack`
therefore cancels the stack's gap and its cards touch. Wrap it in a plain `<div>`:

```tsx
<Stack spacing={Spacing.XL}>
  <Card>…</Card>

  <div>
    <Row spacing={Spacing.L}>
      <Column>…</Column>
      <Column>…</Column>
    </Row>
  </div>
</Stack>
```

