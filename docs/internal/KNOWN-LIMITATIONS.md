# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

## `connectAutocomplete` and `connectHierarchicalMenu` in `XpSearch.Client/src/connectors.ts`

- **Simplified:** eight of the ten connectors spec 5.7 lists are published; these two are not exported at
  all. `connectAutocomplete` needs the `/suggest` semantics (query suggestions vs federated hits, and the
  stale-response and keyboard policy) that are still open decision 6 in the spec, and
  `connectHierarchicalMenu` needs hierarchical facet semantics, which depend on the faceting approach of
  ADR-0001. The transport half of autocomplete exists already: `SearchClient.suggest()`.
- **Ceiling:** the `autocomplete`, `hierarchicalMenu` and taxonomy-navigation widgets cannot be built
  yet, and a developer who needs either has to call `SearchClient.suggest()` and drive
  `helper.setQuery()` by hand.
- **Upgrade path:** add the two connector files once those decisions land; both are additive, so neither
  is a breaking change.

## `searchable` in `XpSearch.Client/src/widgets/refinementList.ts`

- **Simplified:** `searchable: true` renders a facet-search input that filters the values **already
  rendered**, in the browser, by case-insensitive substring. There is no facet-search endpoint in the
  JSON contract (spec §4.2 has no equivalent of Algolia's `searchForFacetValues`), so there is nothing
  server-side to call.
- **Ceiling:** a visitor cannot find a value that falls outside `limit`/`showMoreLimit`, or one the
  current query returned no documents for. On an attribute with hundreds of values the control looks
  like it is broken.
- **Upgrade path:** add a facet-search route to the contract (a coordinated contract change), give
  `connectRefinementList` a `searchForFacetValues` render-state member, and have the widget call it
  instead of filtering locally; the markup and the option name do not change.

## The loading template in `XpSearch.Client/src/widgets/hits.ts`

- **Simplified:** `templates.loading` (and the default skeleton rows) render only while a **first**
  search is in flight *and* a render happens during it — in practice, once the instance's stall
  threshold (`stalledSearchDelayMs`, default 200 ms) has elapsed. Later searches keep the previous
  results on screen with `aria-busy="true"` and the `--loading` modifier instead of blanking them.
- **Ceiling:** a fast first response never shows the skeletons at all (deliberate — no flash), and a
  page configured with `searchOnInitialLoad: false` renders an empty results area rather than a
  placeholder until the visitor searches.
- **Upgrade path:** if a project wants a placeholder before the first search, pass a
  `templates.empty` that reads well as an idle state, or give the instance a `status` render pass on
  `start()` so the widget can distinguish "about to search" from "idle".

## Widget renderers not shipped, in `XpSearch.Client/src/widgets/index.ts`

- **Simplified:** `autocomplete`, `rangeSlider`, `hierarchicalMenu` and `infiniteHits` have a markup
  contract in `themes/MARKUP.md` and a fixture, but no default renderer. Three of them have no
  connector either (see the entry above); `rangeSlider` has `connectRange` but inherits its
  hand-configured bounds.
- **Ceiling:** a project needing any of the four writes the renderer itself against the connector, or
  waits. `FIRST_PARTY_WIDGET_TYPES` still reserves the four names, so a `.xps-mount` naming one is a
  console error and a skipped widget, not a silent no-op.
- **Upgrade path:** add the renderer next to the others and put it in `DEFAULT_WIDGETS`; the markup,
  the CSS and the fixtures are already in place, so each is additive.

## `connectRange` in `XpSearch.Client/src/connectors/range.ts`

- **Simplified:** the control's bounds come from `widgetParams.min`/`max`, and `canRefine` is false
  without them, because the JSON contract carries no numeric facet statistics — there is nowhere for a
  server-computed min/max to arrive.
- **Ceiling:** a range slider over an unknown corpus has to be hand-configured, and its ends do not
  follow the current result set.
- **Upgrade path:** add facet statistics to `SearchResponse` (a contract change, so a coordinated event)
  and read them in the connector, keeping the params as an override.

## Default route mapping in `XpSearch.Client/src/routing.ts`

- **Simplified:** `defaultStateToRoute` owns the params `q`, `page` and `sort`, one param per facet
  attribute, and `<attribute>_<lt|lte|eq|gte|gt>` for numeric refinements. A facet attribute called `q`,
  `page`, `sort` or `price_lte` collides with the mapping and will not round-trip, and two instances with
  `routing: true` on one page fight over the same params.
- **Ceiling:** those attribute names, and multi-instance routing, need the
  `routing: { stateToRoute, routeToState }` escape hatch — public and tested, but hand-written.
- **Upgrade path:** add a documented `routing.prefix` option that namespaces every param (`s1_q`,
  `s1_tags`) when a second instance needs the URL too.

## `mock/server.ts` in `XpSearch.Client`

- **Simplified:** the mock matches query terms as lowercase substrings over title, body and tags, scores
  by term hits with a title bonus, and highlights by regex over the HTML-encoded snippet. It models the
  wire contract, not the Lucene pipeline: no analyzers, stemming, stopwords, synonyms or boost rules.
- **Ceiling:** relevance-ordering assertions written against it prove nothing about the real engine, and
  a UI tuned to its scores may look different against Lucene.
- **Upgrade path:** point the docs' examples and the widget tests at the real endpoint once
  `XpSearch.Core` serves it; keep the mock for offline development.

## `NoWarn NU5104` in `Directory.Build.props`

- **Simplified:** the packages are marked stable while depending, transitively, on the
  `Lucene.Net 4.8.0-beta00017` prerelease. `Kentico.Xperience.Lucene 15.0.5` pins that version, so there
  is no stable Lucene.Net to depend on; NU5104 ("a stable release of a package should not have a
  prerelease dependency") is suppressed for the whole library instead of being resolved.
- **Ceiling:** consumers see a prerelease package in their transitive dependency graph, and tooling that
  refuses prerelease dependencies (some corporate feed policies, `--no-prerelease` restore gates) will
  reject the package. The suppression is repo-wide for this library, so a *new*, genuinely wrong
  prerelease dependency would also go unnoticed.
- **Upgrade path:** drop the `NoWarn` when Kentico.Xperience.Lucene moves to a stable Lucene.Net release,
  and let the warning fail the build again.

## `Hit.Attributes` in `XpSearch.Core/Contract/Hit.cs`

- **Simplified:** `Hit` is generated from `contract/xpsearch-api.schema.json` like every other contract
  type, but quicktype's C# backend ignores the schema's `additionalProperties`, so the open half of the
  object — every non-reserved attribute a query retrieves — is hand-written as a `[JsonExtensionData]`
  property on a partial class next to the generated file. The TypeScript side needs no such help.
- **Ceiling:** one member of the contract exists in two places. If quicktype ever stops emitting `Hit` as
  a `partial class`, the hand-written half silently stops applying and hits lose their attributes; the
  `contract:check` script asserts against exactly that, plus the property names and the extension data
  attribute, so the failure is loud rather than silent. Reading an attribute in C# costs a dictionary
  lookup and a `JsonElement` unwrap.
- **Upgrade path:** delete `Contract/Hit.cs` and its assertions in `scripts/contract.mjs` if quicktype
  learns to emit `[JsonExtensionData]` for `additionalProperties`; otherwise leave it.

## `csharpEdits` in `XpSearch.Client/scripts/contract.mjs`

- **Simplified:** even with `--features attributes-only`, quicktype's C# output publishes types that are
  not part of the contract (the placeholder `XpSearchContract`, `DateOnlyConverter`, `TimeOnlyConverter`)
  and leaves the generated `EventTypeConverter` attached to nothing, so `EventType` would serialize as
  `0`/`1` instead of `"click"`/`"conversion"`. Rather than owning a C# emitter, the generator rewrites four
  anchor strings in the output: three `public` → `internal`, and one `[JsonConverter]` attribute plus a
  scoped `#pragma warning disable CS1591` on the enum.
- **Ceiling:** string surgery on generated code. A quicktype release that renames or reformats any of the
  four anchors breaks the edit — loudly, because each edit throws when its anchor is absent, and
  `Contract_Namespace_Exports_Only_The_Contract_Types` fails if a non-contract type reaches the public API.
  It also means the checked-in C# is not byte-identical to raw quicktype output.
- **Upgrade path:** drop an edit as soon as quicktype offers the behaviour directly (an option to suppress
  the helper converters, or `--features types-only`); or, if the list ever grows past a handful, generate
  the C# from the schema with a small emitter of our own instead.

## `check.mjs` in `themes/scripts/check.mjs`

- **Simplified:** the theme self-check tokenizes CSS with a regex (`([^{}]+)\{([^{}]*)\}`) and HTML
  with `class="…"` matching instead of parsing either. It sees flat declaration blocks, skips the
  at-rule wrappers it cannot nest into, and treats a colour as "a hex literal, a `rgb(`-family
  function, or one of ~20 named colours".
- **Ceiling:** a colour smuggled in through a nested at-rule's own prelude, a `@supports` block that
  re-opens braces inside a declaration value, a named colour outside the list (`rebeccapurple`), an
  `url()` data-URI containing a colour, or a class written into the DOM by JavaScript rather than a
  fixture, all pass unnoticed. The class-parity check compares literal strings, so it cannot know
  that `.xps-hits__item` in a fixture and `.xps-hits__item` in `MARKUP.md` mean the same element —
  only that both exist.
- **Upgrade path:** if the ruleset ever needs to reason about specificity or cascade layers, replace
  the tokenizer with `postcss` and keep the same five assertions; the checks are independent of how
  the file is parsed.

## `color-mix()` in `themes/src/default.css`

- **Simplified:** every tint and hover state (`.xps-highlight`, `.xps-chip`, `.xps-button:hover`,
  `.xps-autocomplete__option--active`, the panel shadow) is derived with
  `color-mix(in srgb, var(--xps-color-…) N%, …)` rather than being given its own custom property.
  That keeps the variable block exactly as spec §6 froze it — eight properties, no more.
- **Ceiling:** `color-mix()` shipped across browsers in 2023 (Chrome/Edge 111, Safari 16.2,
  Firefox 113). Because the mixes wrap a `var()`, an older browser makes the declaration invalid at
  computed-value time — it computes to `unset`, so no earlier declaration in the same rule can act
  as a fallback and the tint simply does not paint. Layout and text stay correct; the `<mark>`
  highlight and chip fills go plain. Documented, with the three-line workaround, in
  `docs/guides/theming.md`.
- **Upgrade path:** if a client needs a pre-2023 browser, add `--xps-color-highlight`,
  `--xps-color-hover` and `--xps-color-chip` to the variable block with flat defaults and use them
  directly — a spec §6 amendment, not a code change.

## `xps-autocomplete__panel` surface in `themes/src/shell.css`

- **Simplified:** shell positions the autocomplete popup but gives it no background, because the
  §6 rule "no colours beyond `currentColor`" leaves it nothing to paint with.
- **Ceiling:** a site that loads `shell.css` alone gets a see-through popup overlapping the page
  behind it until it sets a `background-color` on `.xps-autocomplete__panel`. Stated in the theming
  guide and in `themes/MARKUP.md`, but nothing enforces it.
- **Upgrade path:** none wanted — the alternative is shell shipping a colour, which is the rule
  that keeps it safe to load on any site.
