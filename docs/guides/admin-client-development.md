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

Other scripts: `npm run typecheck` (`tsc --noEmit`, strict) and `npm run start` (webpack dev server on
port 3009, see *Serving* below).

### Layout

| Path | What it is |
|---|---|
| `Client/package.json` | The Kentico admin boilerplate's dependency set, versions pinned exactly. |
| `Client/webpack.config.js` | The boilerplate config with `orgName: "yourco"`, `projectName: "xperience-search-admin"`. |
| `Client/babel.config.json`, `Client/tsconfig.json` | Boilerplate, unchanged apart from `noUnusedLocals`. |
| `Client/src/entry.tsx` | Exports every component the admin application may load. |
| `Client/src/query-tester/QueryTesterTemplate.tsx` | Client template of the query tester. |
| `Client/src/analytics/AnalyticsDashboardTemplate.tsx` | Client template of the dashboard. |
| `Client/src/analytics/ReportTable.tsx`, `VolumeChart.tsx` | The dashboard's report card (a `Card` around the stock `Table`) and its inline-SVG chart. |
| `Client/src/theme.ts` | The three text treatments no component exposes, built from the package's `Colors` tokens. There is no stylesheet and no style loader. |

Both pages are built to the owner's design spec
(<https://claude.ai/design/p/d9cffec1-046f-46e2-b611-d162418351f9>) and may only use
`@kentico/xperience-admin-components`. Check a component and its prop names in
`node_modules/@kentico/xperience-admin-components/dist/entry.d.ts` before using it — that file is the
authority. See ADR-0020.

### The four names that must agree

A page's `templateName` is `@<orgName>/<projectName>/<ComponentName>`, and the admin application
appends `Template` to the component name. Change any one of these and all four must change together:

| Where | Value |
|---|---|
| `Client/webpack.config.js` | `orgName: "yourco"`, `projectName: "xperience-search-admin"` |
| `XpSearch.Admin.csproj` | `<AdminOrgName>yourco</AdminOrgName>`, `<AdminClientPath>`'s `<ProjectName>xperience-search-admin</ProjectName>` |
| `XpSearchAdminClientModule.cs` | `RegisterClientModule("yourco", "xperience-search-admin")` |
| `UIPage` registrations | `"@yourco/xperience-search-admin/QueryTester"`, `"@yourco/xperience-search-admin/AnalyticsDashboard"` |

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
# XpSearch.Admin.AdminResources.yourco.xperience.search.admin.entry.kxh.<hash>.js
```

The hash has to match the file `npm run build` wrote to `Client/dist`.

For front-end work, `Proxy` mode is faster — the host serves the templates from your webpack dev
server instead of the assembly. In the host application:

```json
"CMSAdminClientModuleSettings": {
  "yourco-xperience-search-admin": {
    "Mode": "Proxy",
    "Port": 3009
  }
}
```

Then run `npm run start` in `src/XpSearch.Admin/Client`. Remove the setting before shipping.

### Adding a page

1. Write the template in `Client/src/<area>/<Name>Template.tsx` and export it from `entry.tsx`.
2. Write the back end as `Page<TClientProperties>` with a `TemplateClientProperties` subclass, and
   register it with `[assembly: UIPage(..., "@yourco/xperience-search-admin/<Name>", order)]`.
3. Fetch data with `[PageCommand]` handlers and `usePageCommand` on the client. Give every command a
   `Permission` from the set the *owning application* declares — for a page inside
   `IndexTuningSection` that is the Lucene integration's application (`View`, `Create`, `Update`,
   `Delete`, `Rebuild`); for a page under `SearchTuningApplication` it is `View`, `Create`, `Delete`.
4. `npm run typecheck`, `npm run build`, `dotnet build src/XpSearch.Admin`.

#### Pages inside an index

Both shipped templates hang under `IndexTuningSection`, at
`/admin/lucene/indexes/tuning/{id}/<your-slug>`, so the index is not something the visitor picks:
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
