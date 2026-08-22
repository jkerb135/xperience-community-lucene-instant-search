# Host pass HW-4 — 2026-08-21

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, Xperience by Kentico 31.8.0, DB `comm_projects`,
http://localhost:27340). Library at `libraries/xperience-search`, `main` **723bb4e**, unchanged and
uncommitted by this pass (`git status --short` is empty; this note is untracked).

Scope: re-check the two library changes since HW-3 — **CA-4** (`_source` facet + ingestion health) and
**CR-2** (redirect rules) — on the real host, plus the HW-3 regression spot-checks.

**Verdict: CA-4 verified good. CR-2 is broken end to end in any default host configuration** — the
rule fires, but the response cache decorator drops `redirect` on its way to the wire (§5.1).

---

## 1. What changed in the host

**Nothing permanent.** `src/Program.cs` was patched for one diagnostic run with a single line
(`options.CacheTtl = TimeSpan.Zero; // HW4-TEMP`) inside the `AddXpSearch` options callback to isolate
§5.1, and the line was removed again (`grep -c HW4-TEMP src/Program.cs` → `0`, rebuilt clean
afterwards). No other host file was edited; `src\Components\Widgets\CardWidget\` was not touched.

`IIngestionQueue.PendingCount` → `FailedCount` needed no host fix: `grep -rn "PendingCount\|IIngestionQueue" src/`
returns nothing, the host never used that API.

Database left as found: the one `XpSearch_Rule` row inserted for §4 was deleted; all four tuning tables
are empty again (`rules|syns|stops|weights` → `0|0|0|0`).

---

## 2. Pre-build

```
cd libraries/xperience-search/src/XpSearch.Client && npm ci && npm run build
  → created dist/xpsearch.umd.js; packaged themes/shell.css, themes/default.css, mock/server.mjs
cd libraries/xperience-search/src/XpSearch.Admin/Client && npm ci && npm run build
  → webpack 5.109.2 compiled successfully in 832 ms
```

```
$ dotnet build CommProjects.sln
Build succeeded.
    2 Warning(s)      # both NU1902 AngleSharp, from the unrelated XbyK.Serps project
    0 Error(s)
```

The host was then started with `dotnet run --project src --no-build` after
`rm -rf src/App_Data/LuceneSearch/DancingGoatSample` and
`DELETE FROM XpSearch_ApiKey WHERE KeyName='dev-sample'`, so the index was rebuilt from empty on the new
document shape and a fresh dev key was issued. Startup log: 39 lines, no error/exception, the only
`warn` is the expected `DevIngestionKeySeeder` plaintext-key line, and `Rebuilding index
[DancingGoatSample]. 74 web page items queued` → 32 documents.

---

## 3. CA-4 — `_source` facet and ingestion health: **verified**

### 3.1 Counts and drill-down on Xperience content

```
$ curl -s -X POST /api/xpsearch/query -d '{"index":"DancingGoatSample","query":"","pageSize":1,"facets":["_source"]}'
total 32   facets {"_source": [{"count": 32, "label": "xperience", "value": "xperience"}]}

$ … "filters":{"facets":[{"attribute":"_source","values":["xperience"]}]}
filter _source=xperience total 32
```

This is exactly the HW-3 blocker (§5.1 of that note: empty bucket list, `total 0` on the filter) and it
is gone. `redirect` is present on the response as `null`, as the CR-2 contract requires.

### 3.2 With a pushed external document

```
$ curl -s -X POST …/admin/indexes/DancingGoatSample/documents -H "Authorization: Bearer $KEY" \
   -d '{"waitForIndex":true,"documents":[{"id":"pim-sku-88213","_source":"pim", … }]}'
{"errors":[],"failed":0,"indexed":1,"tookMs":65}

$ … facets ["_source"]
total 33   facets {"_source":[{"count":32,…"xperience"},{"count":1,…"pim"}]}
$ … filter _source=xperience  → total 32
$ … filter _source=pim        → total 1
```

Both provenances count and both drill down, for Xperience content and for pushed documents alike.

### 3.3 `clear?source=pim` and the health lag

```
$ curl -s -X POST -H "Authorization: Bearer $KEY" '…/clear?source=pim'
{"deleted":1,"taskId":"ea7611bca60b4dda9fcd215e9086ea2c","tookMs":17}

$ …/status          # immediately
{"documents":{"bySource":{"pim":1,"xperience":32},"total":33},"health":"healthy","index":"DancingGoatSample"}

$ …/status          # ~10 s later
{"documents":{"bySource":{"xperience":32},"total":32},"health":"healthy","index":"DancingGoatSample"}
```

HW-3 §5.2 is fixed: the count still lags (expected — the delete is queued), but `health` stays
`healthy` through the lag instead of flipping to `degraded`, and `docs/guides/ingestion.md:191-195` now
says `clear`/`delete` are asynchronous and `status` eventually consistent. `docs/guides/search-api.md:315`
now lists `_source` among the facetable attributes, matching observed behaviour.

---

## 4. CR-2 — redirect rules: rule fires, response loses it

No API creates rules, so one row was inserted directly and the host restarted (a raw insert bypasses the
object-type cache dependency the tuning cache uses):

```sql
INSERT INTO XpSearch_Rule
 (RuleGuid,RuleIndexName,RuleName,RuleEnabled,RuleConditionType,RulePattern,
  RuleConsequenceType,RuleTargetPosition,RuleBoostValue,RuleRedirectUrl,RulePriority)
VALUES (NEWID(),'DancingGoatSample','Support to contact',1,1,'support',4,0,1,'/contact',100);
-- RuleConditionType 1 = Exact, 4 = RuleConsequence.Redirect
```

### 4.1 Default host configuration (60 s search cache) — **fails**

```
$ curl -s -X POST /api/xpsearch/query -d '{"index":"DancingGoatSample","query":"support","pageSize":2}'
redirect None | total 0 | results 0
```

To rule out "the rule never matched", the row was switched to `RuleConditionType=3` (Always) and the
host restarted:

```
$ curl -s -X POST /api/xpsearch/query -d '{"index":"DancingGoatSample","query":"coffee","pageSize":1,"explain":true}'
redirect None   total 24
results[0].ranking = {'baseScore': 0.4801144301891327, 'boosts': ['rule:Support to contact'], 'position': 1}
```

The rule **is** selected and applied — `BoostRulesStage` only adds the `rule:<name>` explanation when the
consequence handler returned `true`, and for `Redirect` that happens only after `context.Redirect` was
set — yet the response carries `redirect: null`. See §5.1.

### 4.2 With the search cache disabled — **works**

Same rule, same host, only `options.CacheTtl = TimeSpan.Zero` added to `AddXpSearch` (temporary, since
reverted):

```
$ curl -s -X POST /api/xpsearch/query -d '{"index":"DancingGoatSample","query":"coffee","pageSize":1}'
cache OFF -> redirect: {'rule': 'Support to contact', 'url': '/contact'} total 24
```

Rule then set back to the specified `Contains` / `support`:

```
$ … {"query":"support","pageSize":2}
redirect {'rule': 'Support to contact', 'url': '/contact'} | total 0 | results 0
$ … {"query":"coffee","pageSize":2}
redirect None | total 24
```

- A matching query returns the destination **and** the normal payload; a non-matching one returns
  `"redirect": null`. Precedence, contract shape and `explain` all behave as `relevance-tuning.md` and
  `search-api.md` describe.
- `total 0` for `support` is sample data, not a defect: no Dancing Goat document contains the word. The
  "results are carried alongside the redirect" half of the contract is proven by the Always run above
  (redirect + 24 results in one response).

### 4.3 The `/search` page

```
$ curl -s -o search.html -w '%{http_code}\n' 'http://localhost:27340/search?q=support'
200
$ grep -o 'xps-widget="[a-zA-Z]*"' search.html | sort | uniq -c
      1 facetList   1 pagination   1 resultStats   1 results
      1 searchBox   1 sortSelect   1 suggestions
$ grep -c 'xpsearch.umd.js' search.html
1
```

The page renders with all seven widget mounts and the UMD bundle, with the redirect rule active. **The
navigation itself cannot be observed here** — following a redirect is client-side JS on the search box's
submit event, and this pass has no browser. Given §5.1, in the default host configuration the browser
would never see a `redirect` object to act on anyway.

---

## 5. Defects found in the library

### 5.1 The response cache drops `redirect`, so CR-2 never reaches a real client

`src/XpSearch.Core/Caching/CachedSearchPipeline.cs:59-70`:

```csharp
private static SearchResponse WithQueryId(SearchResponse response, string? queryId) =>
    new()
    {
        Results = response.Results,
        Facets = response.Facets,
        Page = response.Page,
        PageSize = response.PageSize,
        Total = response.Total,
        TotalPages = response.TotalPages,
        TookMs = response.TookMs,
        QueryId = string.IsNullOrWhiteSpace(queryId) ? Guid.NewGuid().ToString() : queryId
    };
```

`ProjectResponseStage.cs:64` sets `Redirect = context.Redirect` correctly, but every request whose index
is non-empty and whose `CacheTtl > 0` — i.e. the default, and the only path a shipped widget ever takes —
is re-projected through this hand-written copy, which has no `Redirect` line. The field is silently reset
to `null`. Evidence: §4.1 versus §4.2, same rule, same process, only `CacheTtl` differing.

Proposed fix (not applied): add `Redirect = response.Redirect,` to the initializer. Two notes for the
owner:

- This copy is a standing hazard — it enumerates the response's properties by hand, so every future
  contract field is opt-in and fails exactly this way. Worth either a `MemberwiseClone`-based copy
  (`var copy = (SearchResponse)response.Clone(); copy.QueryId = …`) or a unit test that asserts every
  public property of `SearchResponse` is either copied or deliberately overwritten. A generated-contract
  DTO makes the reflection version cheap.
- The CR-2 tests must be passing against the uncached pipeline only; a decorator-level test for the
  redirect (and for facets, which happen to be copied) would have caught this.

### 5.2 Unchanged from HW-3 §5.3 (note, not a defect)

`title` is still the web page item name (`CoffeePlunger-p2e57tss`) and `/suggest`'s `text` with it. As
designed, still worth a line in the widget/search-api guides.

HW-3 §5.1 and §5.2 are both fixed (§3 above).

---

## 6. Regression spot-checks (HW-3 §4), default configuration, rule active

```
$ curl -s -X POST /api/xpsearch/query -d '{"index":"DancingGoatSample","query":"coffee","pageSize":2,
    "facets":["ProductFieldCategory","ProductFieldTags","contentType","_source"],
    "highlight":{"fields":["ProductFieldName","ProductFieldDescription"]}}'
total 24
facets  ProductFieldCategory: Coffees 8, Brewers 7, Accessories 6, Grinders 3
        ProductFieldTags:     Bestsellers 2, "Hot tips"/HotTips 2
        contentType:          DancingGoat.ProductPage 24
        _source:              xperience 24
highlights[0] {"ProductFieldName":"<mark>Coffee</mark> Plunger",
               "ProductFieldDescription":" Eight cups of <mark>coffee</mark> in a single plunger. …"}

$ POST /api/xpsearch/suggest  → 200
$ POST /api/xpsearch/events   → 202
$ GET  /admin                 → 200
startup log (39 lines) → no error, no exception, no warning other than DevIngestionKeySeeder
```

Identical to HW-3 apart from the extra `_source` bucket. The host was stopped at the end of the pass.

---

## 7. Not verified, and why

- **The browser side of CR-2** — that the search box actually navigates on submit, that
  `followRedirects: false` opts out, and that `withResults` exposes `redirect`. No browser; and §5.1
  means the payload the JS reads is `null` today, so this cannot be exercised at all until that is
  fixed. Re-check after the fix.
- **`explain` + `redirect` in one uncached response.** The two halves were observed separately (§4.1
  gives the `rule:<name>` boost, §4.2 the redirect object); nothing suggests they interact.
- **Any admin UI** — no login. In particular the rule editor's Redirect dropdown and the Query tester's
  rendering of a redirect rule; this pass reached the feature through raw SQL instead.
- **A rule created through the admin UI invalidating the tuning cache without a restart** — every rule
  change here was a raw SQL write, which bypasses the object-type cache dependency by construction, so
  each one was followed by a host restart. The invalidation path itself is untested by this pass.
