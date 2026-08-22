# Host pass HW-5 — 2026-08-22

Host: `F:\Personal\CommunityProjects\src` (Dancing Goat, DB `comm_projects`, http://localhost:27340).
Library `libraries/xperience-search`, `main` **d868487** ("fix: keep the redirect (and every other member)
on a cached response"), unchanged by this pass (`git status --short` empty; this note untracked).

Scope: re-check CA-5 only — that a redirect survives a **cache hit** with the host's **default** cache
configuration. No host code changes, no library changes, no commits. `src/Program.cs` contains no
`CacheTtl` line (`grep -n "CacheTtl\|HW4-TEMP" src/Program.cs` → no match), so `AddXpSearch` ran with the
shipped search cache throughout. `src\Components\Widgets\CardWidget\` untouched.

**Verdict: PASS.** HW-4 §5.1 is fixed.

---

## 1. Build

```
$ powershell "Get-Process CommunityProjects | Stop-Process -Force"    # no stale process left
$ dotnet build CommProjects.sln
Build succeeded.
    6 Warning(s)     # 2x NU1902 AngleSharp (XbyK.Serps), 2x ASPDEPR006, 2x CS0618 — all pre-existing host warnings
    0 Error(s)
Time Elapsed 00:00:07.75
```

npm bundles were already built; `dotnet build` did not ask for them.

The fix under test, `src/XpSearch.Core/Caching/CachedSearchPipeline.cs:56` → `SearchResponse.WithQueryId`
(`Contract/SearchResponse.cs:12`), now clones (`MemberwiseClone`) instead of re-listing properties.

## 2. Rule setup (raw SQL, as in HW-4 — no API creates rules; each change followed by a restart)

`RuleCondition`: 0 = Contains, 1 = Exact, 2 = StartsWith, 3 = Always. `RuleConsequence`: 4 = Redirect.

```sql
INSERT INTO XpSearch_Rule
 (RuleGuid,RuleIndexName,RuleName,RuleEnabled,RuleConditionType,RulePattern,
  RuleConsequenceType,RuleTargetPosition,RuleBoostValue,RuleRedirectUrl,RulePriority)
VALUES (NEWID(),'DancingGoatSample','Support to contact',1,3,'support',4,0,1,'/contact',100);
-- ConditionType 3 = Always (phase A); later UPDATE ... SET RuleConditionType=0 (Contains) for phase B
```

Host started with `dotnet run --project src --no-build` (PID 19412, 00:02:32). Startup log 39 lines,
`Now listening on: http://localhost:27340`, `Application started.`, no error/exception. The index was
**not** rebuilt this pass — the HW-4 index in `src/App_Data/LuceneSearch/DancingGoatSample` was reused and
still returns the expected 24 hits for `coffee`.

## 3. Step 1 — Always rule, same query twice: redirect survives the cache hit

```
$ curl -s -X POST http://localhost:27340/api/xpsearch/query -H 'Content-Type: application/json' \
    -d '{"index":"DancingGoatSample","query":"coffee","explain":true}'      # run twice, back to back

--- coffee call 1 ---
redirect: {"rule": "Support to contact", "url": "/contact"}
total: 24 | results: 20 | queryId: ac4c6a70-d2f7-4d28-884e-dbd06794b37f | tookMs: 133
results[0]: id= d09cafef-980f-4eee-83f2-fa1ae34f94ca:en
            ranking= {"baseScore": 0.4801144301891327, "boosts": ["rule:Support to contact"], "position": 1}
--- coffee call 2 ---
redirect: {"rule": "Support to contact", "url": "/contact"}
total: 24 | results: 20 | queryId: e5b16696-5d7f-4034-bb1f-7ad8263bbfc0 | tookMs: 133
results[0]: id= d09cafef-980f-4eee-83f2-fa1ae34f94ca:en
            ranking= {"baseScore": 0.4801144301891327, "boosts": ["rule:Support to contact"], "position": 1}
```

Call 2 is the cache hit: identical `tookMs` (133, the stored value of the first execution) and identical
payload, but a fresh `queryId` — i.e. it came back through `WithQueryId` on the cached instance, exactly the
path that used to blank `redirect`. `total` is 24 (`results` is the 20 of page 1 of 2, default `pageSize`),
so redirect and results travel together, and `explain` and `redirect` coexist in one response.

Call 1 in full (results trimmed to the first hit):

```json
{
  "page": 1,
  "pageSize": 20,
  "queryId": "ac4c6a70-d2f7-4d28-884e-dbd06794b37f",
  "redirect": { "rule": "Support to contact", "url": "/contact" },
  "results": [
    {
      "attributes": {
        "title": "CoffeePlunger-p2e57tss",
        "contentType": "DancingGoat.ProductPage",
        "language": "en",
        "url": "/products/coffee-plunger",
        "_source": "xperience",
        "ProductFieldName": "Coffee Plunger",
        "ProductFieldDescription": " Eight cups of coffee in a single plunger. ...",
        "ProductFieldPrice": 29.9,
        "ProductFieldCategory": "Brewers",
        "ProductSKUCode": "ACC-PLU-STAND"
      },
      "id": "d09cafef-980f-4eee-83f2-fa1ae34f94ca:en",
      "ranking": { "baseScore": 0.4801144301891327, "boosts": ["rule:Support to contact"], "position": 1 },
      "score": 0.4801144301891327
    }
  ],
  "tookMs": 133,
  "total": 24,
  "totalPages": 2
}
```

## 4. Step 2 — Contains `support`: matching query redirects, non-matching one does not

```sql
UPDATE XpSearch_Rule SET RuleConditionType=0 WHERE RuleIndexName='DancingGoatSample';  -- Contains
```

Host restarted (PID 33464, 00:03:37), then each query issued twice (second = cache hit):

```
--- support call 1 ---   redirect: {"rule": "Support to contact", "url": "/contact"} | total: 0  | results: 0  | tookMs: 133
--- support call 2 ---   redirect: {"rule": "Support to contact", "url": "/contact"} | total: 0  | results: 0  | tookMs: 133
--- coffee  call 1 ---   redirect: null                                              | total: 24 | results: 20 | tookMs: 37
--- coffee  call 2 ---   redirect: null                                              | total: 24 | results: 20 | tookMs: 37
```

All four `queryId`s distinct (`7e5d1789...`, `bed6ef80...`, `16a8f88b...`, `f49c8f67...`). `total 0` for
`support` is sample data, not a defect — no Dancing Goat document contains the word (same as HW-4 §4.2). The
non-matching `coffee` returns `"redirect": null`, cached and uncached alike, so the fix carries the real
value rather than making the field non-null unconditionally.

## 5. Step 3 — cleanup

```
$ sqlcmd -S localhost -d comm_projects -E -C -Q "DELETE FROM XpSearch_Rule;"     (1 rows affected)
$ powershell "Stop-Process -Name CommunityProjects -Force"                       # then restart
--- support (no rules) ---  redirect: null | total: 0  | results: 0  | ranking[0]: None
--- coffee  (no rules) ---  redirect: null | total: 24 | results: 20
                            ranking[0]: {"baseScore": 0.4801144301891327, "boosts": [], "position": 1}
```

The `rule:Support to contact` boost is gone from `explain` as well, so the rule really is out of the
tuning cache. Startup log again 39 lines with no error/exception line.

Host stopped (`Get-Process CommunityProjects` → 0). Database left as found:

```
$ sqlcmd ... SELECT rules|synonyms|stopwordLists|fieldWeights
0|0|0|0
```

(The four tuning tables are `XpSearch_Rule`, `XpSearch_Synonym`, `XpSearch_StopwordList`,
`XpSearch_FieldWeight` — the last two names differ from the `XpSearch_Stopword` guess used in HW-4's prose.)

## 6. Not verified

- The **browser** half of CR-2 — that the search box navigates on `redirect`, that `followRedirects:false`
  opts out, that `withResults` exposes it. No browser in this pass; the payload the JS reads is now correct
  on the wire, which was the blocker, but the navigation itself is still unobserved.
- Admin UI (no login): the rule editor's Redirect dropdown and the Query tester.
- Tuning-cache invalidation without a restart: every rule change here was raw SQL, which bypasses the
  object-type cache dependency by construction.

---

**PASS — CA-5 verified on the real host with the default cache: `redirect` is present on both the first
response and the cached second response (24 results alongside it), `null` for a non-matching query, and the
database was returned to 0 rows in all four tuning tables.**
