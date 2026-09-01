# Unit IX-1 — Contributed fields in the schema, and an honest suggest-field default

PAUL plan 04-05, closing HW-14 gaps 4–5. Library unit, worktree branch `unit/ix-1` (already
created for you). Read `docs/internal/agent-primer.md` first. No contract change, no new
dependencies, no admin pages — this is Core options/schema/logging plus guides.

Background evidence:

- **Gap 4 (an API defect):** `IndexSchemaProvider.GetSchemaAsync`
  (`src/XpSearch.Core/Indexing/IndexSchemaProvider.cs:94-113`) assembles the schema from
  `BaseFields()` + each covered content type's detected fields + flattened link types —
  nothing else. A field written from the `ContributeAsync` hook therefore lands in the Lucene
  document but never in the schema, and hit attributes are projected FROM the schema, so the
  value is indexed yet invisible on the wire — silently. The guide section "Adding fields of
  your own" (`docs/guides/indexing-strategy.md:205`) teaches exactly this trap: its `Summary`
  example produces an attribute nobody will ever see. The sample host ships the workaround
  this unit obsoletes: `DancingGoatSearchFieldSource`, an `IContentTypeFieldSource` decorator
  registered before `AddXpSearch` (`src/Search/DancingGoatSearchIndexingStrategy.cs:239`,
  read-only reference — host is out of scope).
- **Gap 5:** `XpSearchIndexOptions.SuggestField` defaults to `title`
  (`src/XpSearch.Core/Options/XpSearchOptions.cs:54`), and `title` is the item display name —
  on real Kentico sites that is the web page item name, i.e. machine-ish slugs
  ("CoffeePlunger-p2e57tss"), so default document suggestions are ugly on ANY site until
  someone finds the setting. HW-14 hit this live.

## 1. `XpSearchIndexingOptions.AddField`

- New registration on `XpSearchIndexingOptions` (`src/XpSearch.Core/Indexing/XpSearchIndexingOptions.cs`):
  `AddField(string contentTypeName, SchemaField field)` (chainable, same shape as the
  existing `Exclude`/`Configure`/`FlattenLinkedItems` idiom) plus the read side the schema
  provider needs (mirror `FlattenedLinksOf`). Registering the same content type + field name
  twice: last wins or throw — pick one, pin it with a test, document it.
- `IndexSchemaProvider.GetSchemaAsync` appends a content type's contributed fields AFTER its
  detected and flattened fields, so a contributed field never shadows a real one
  (`IndexSchema` keeps the first definition — the comment at line 99 explains the rule;
  a collision therefore silently yields the detected field: state that in the guide).
- Contributed fields are EXEMPT from `Configure`/`Exclude` overrides: you authored the
  definition — change the `AddField` call, not an override of it. One sentence in the XML
  docs and the guide; pin with a test. (If you find `Apply` runs somewhere that makes the
  exemption awkward rather than natural, STOP and report instead of forcing either way.)
- Everything downstream reads `IIndexSchemaProvider`, so contributed fields should now appear
  in hit projection, the ingestion schema endpoint, and the admin attribute selectors with no
  further work — verify by test at the schema level and by grepping for other
  `IContentTypeFieldSource` consumers that would now double-count or miss them.

## 2. Close the silent-invisibility trap at the write site

`IndexingContext.AddFieldAsync` (the `ContributeAsync` helper) currently writes a field the
schema may not know. Add a guard: when the written field's name is not in the index schema,
log a warning naming the field, the content type and the `AddField` registration that fixes
it — once per field name, not per document. A rebuild of thousands of items must not produce
thousands of identical warnings. (If the schema is not reachable from the context/strategy at
that point without contortions, the warning may live where the strategy already holds the
schema — it does, it passes fields to mapping — but STOP and report if neither site can know
without new plumbing.)

## 3. Suggest-field default honesty (gap 5)

- Track whether `SuggestField` was configured: keep the public surface
  (`string SuggestField { get; set; }` = `title`) but flip an internal
  `SuggestFieldConfigured` in the setter — no API change.
- When `/suggest` serves an index in `Documents` or `Mixed` mode from an UNCONFIGURED suggest
  field, log one warning per index (not per request): document suggestions are reading the
  item display name, which on most Kentico sites is the code-ish item name — name the
  `SuggestField` option and the guide section. `DocumentSuggestService` already has the
  per-index options in hand; it does not currently take a logger — adding one is fine.
- Do NOT invent a smarter default: no heuristic can know the right field, and a wrong guess
  is worse than an honest warning.

## 4. Docs

- Rewrite "Adding fields of your own" (`docs/guides/indexing-strategy.md:205`): the honest
  two-step — declare the field once with `indexing.AddField(...)`, write its value per
  document in `ContributeAsync` — with the existing `Summary` example completed so it
  actually reaches the wire. Note the collision rule and the override exemption. The raw
  `document.Add(new StringField(...))` escape stays documented as exactly that: invisible to
  the schema and the wire, on purpose.
- Quick-start / suggest documentation: wherever document suggestions are introduced (grep the
  guides for `SuggestField`), a prominent line that real sites should set `SuggestField` to a
  human-readable field, with the Dancing Goat `ProductFieldName` example.
- CHANGELOG (Added, core). KNOWN-LIMITATIONS: remove/amend anything the trap's closure
  obsoletes — grep for ContributeAsync/field-source mentions.
- Host-pass checklist: append a new section (last is §S ending at item 100 — verify in YOUR
  worktree and number after whatever is last): contributed image/path fields on the wire via
  `AddField` once the host adopts it, the undeclared-field warning appearing in the event log
  when provoked, the suggest-field warning for an unconfigured index. Keep it short — most of
  this unit is unit-testable; only genuinely host-only observations belong there.

## 5. Verification

- C# suites green. New coverage: schema provider includes contributed fields per content
  type + after-detected ordering + collision behavior; AddField registration semantics;
  override exemption; write-site warning (fires once, names the fix, silent when declared);
  suggest-field warning (unconfigured Documents/Mixed fires once per index; configured or
  QuerySuggestions silent). JS untouched — `npm run build` + tests only if something forces a
  regen (it should not).
- Guides regenerated where generated (`docs:check` clean); CHANGELOG entry.
- Host follow-up is NOT yours (lead replaces `DancingGoatSearchFieldSource` with `AddField`
  calls and reruns the HW-14 parity checks); say in your report exactly what the host should
  change.
- Commit this spec file with the unit (copy from `docs/internal/units/IX-1.md` on main if
  your worktree predates it).

## Constraints

- Kentico docs MCP for any Xperience API question. No new dependencies, no contract change.
  Core must not gain Admin/Page Builder dependencies. Never touch
  `src/Components/Widgets/CardWidget/`. Host is out of scope entirely (read-only reference).
