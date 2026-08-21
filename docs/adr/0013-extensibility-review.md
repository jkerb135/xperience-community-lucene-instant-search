# ADR-0013: The worked example is a built sample, not a snippet

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §5.7, §5.9, §7, §11.3, §12

## Context

We asked a developer with no access to this source to build `myCompany.dropdownFacet` — a single-select
drop-down facet plus its Page Builder widget — from the published documentation and the four published
packages alone, and to log every point of friction. The result is
[`docs/internal/extensibility-friction-2026-08-21.md`](../internal/extensibility-friction-2026-08-21.md),
and the sample they produced is [`samples/CustomWidget.Dropdown`](../../samples/CustomWidget.Dropdown/).

The verdict was "yes, a competent Kentico agency developer can build this from the docs alone — but not
by following the worked example". Everything structural held: the behaviour API is honestly typed, the
C# base class compiled first try with `TreatWarningsAsErrors`, the two-registration model was clear, and
the mount bootstrap behaved exactly as documented. The failures were concentrated in two places.

**The flagship JavaScript example in `custom-widgets.md`.** It did not typecheck (three errors), emitted
`class="xps-dropdown__*"` — class names that appear in neither `MARKUP.md` nor either stylesheet, in a
product whose own `themes/scripts/check.mjs` treats a class name as semver-major — put no `xps` class on
its root (so no reset, no focus ring), derived element ids from `container.id`, which under Page Builder
is empty and therefore yields `id="-select"` duplicated on every instance, interpolated editor-supplied
text and taxonomy labels into HTML unescaped despite the package exporting `escapeHtml`, and got
single-select wrong: its handler closed over the first render's `renderOptions.items`, so two selections
without an intervening render left *both* values active. A developer who copied it shipped a broken
control; the friction log has the failing assertion.

**The published packages.** The npm tarball shipped `dist/` and nothing else, so `theming.md`'s
`<link href="/node_modules/@yourco/xperience-search/themes/shell.css">` was a 404 and `js-client.md`'s
"run it against the mock server" pointed at a path inside our repository. Nothing caught either,
because nothing in this repository ever consumed the tarball.

Both failures are the same failure: **prose and snippets have no build**.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Fix the snippet in place | One commit | The next edit re-breaks it; nothing would have caught the original four bugs either |
| Extract every guide snippet and typecheck it (doc tests) | Covers all guides | A harness, a build step and a fixture set for snippets that are mostly three lines and correct |
| Make the flagship example a real project, built from the packages, and paste it into the guide | The example is compiled, tested and consumed exactly as a customer consumes it; the packaging holes surface immediately | One sample to maintain; the guide has one long code block |
| Sample built via `ProjectReference` | Simplest to wire | Compiles against source that is not in any package — would not have caught the missing stylesheets, and is not what a customer does |

## Decision

**The worked example in `docs/guides/custom-widgets.md` *is*
`samples/CustomWidget.Dropdown/src/dropdownFacet.ts`, reproduced verbatim.** The guide says so, and
`samples/pack-and-build.mjs` fails the build if the fenced block and the file diverge by a byte.

**Samples build against packed packages, never project references.** `pack-and-build` runs `dotnet pack`
on Core/Widgets/Admin and `npm pack` on the client into a gitignored `samples/.feed/`, then restores,
builds, typechecks and tests the sample from that feed alone. A missing `files` entry, a dropped export
or a static web asset that stopped shipping fails there.

Five API additions came out of the exercise rather than prose, because five of the log's findings were
missing public surface:

- `widgetId(container, widget, part)` — the one implementation of `MARKUP.md` rule 4. The four built-ins
  that hand-rolled an id base now call it.
- `readMountConfig(config, spec)` — narrows `data-xps-config`, which is a trust boundary: the JSON is
  whatever an editor typed. A bad value throws naming the key; the bootstrap logs it once.
- The shared `xps-select` block (`__label`, `__control`, `--disabled`) — one themed `<select>` in the
  contract, rendered by `sortSelect` *and* available to a custom widget, replacing
  `xps-sort-select__label`/`__select`.
- `escapeHtml`, already exported, is now documented in `custom-widgets.md` and `widget-reference.md`.
- The npm package ships `themes/*.css`, a runnable `mock/server.mjs` (bin: `xpsearch-mock`) and a
  `README.md`.

## Evidence

- `src/XpSearch.Client/src/behaviors/facet-apply.test.ts` settles what the docs would not say.
  `withFacetList`'s `apply(value)` is `toggleFacet(attribute, value).search()`: it toggles **and**
  searches. The toggle is synchronous; the request is debounced, so the two `apply` calls of the
  single-select idiom coalesce into **one** request. A state change re-renders on a **microtask**, not
  synchronously — the friction log's §5 claim that no synchronous re-render happens between two changes
  is correct, and `custom-widgets.md`'s "controls update the moment they are clicked" was wrong as
  written. Docs changed; behaviour did not (coalescing renders is the point).
- `node samples/pack-and-build.mjs`: 6 vitest tests, 3 NUnit tests, `dotnet build` 0 warnings under
  `TreatWarningsAsErrors`, restored from `samples/.feed` only.
- `npm pack --dry-run` on the client: 46 files, including `themes/shell.css`, `themes/default.css`,
  `mock/server.mjs` and `README.md`, and excluding `demo/` and `scripts/`.
- `themes/ npm run check` stays green with `xps-select` in the fixtures, the CSS and `MARKUP.md`.

## Consequences

- The guide's example is long — the whole file, comments included — instead of a trimmed snippet. That
  is the trade: it is honest, and "40 lines" was never true once the control was correct (~80).
- Editing `dropdownFacet.ts` without re-pasting it into the guide fails `pack-and-build`. That is the
  enforcement, and it is deliberately annoying.
- `samples/Directory.Build.props` and `samples/Directory.Packages.props` are near-empty on purpose: a
  sample must build with a customer's defaults, so it opts out of the repository's shared build settings
  and central package management. Sample projects therefore declare package versions inline. That is the
  one place in this repository where inline versions are correct.
- Spec §5.7's illustrative "dropdown facet" listing keeps the pre-ADR-0010 connector names and the four
  bugs described above. It is a historical design document; `docs/guides/custom-widgets.md` plus the
  sample are the live surface, and the spec listing must not be copied.
- Publishing is still blocked by `"private": true` (Phase 8), so the JavaScript-only install path is a
  tarball by path. The tarball itself is complete — see
  [KNOWN-LIMITATIONS](../internal/KNOWN-LIMITATIONS.md).
- Two friction items are **disputed**, and the answers are now in the docs rather than in the reader's
  head: §6 (`apply()` semantics) was genuinely undocumented, but the reader's guess that `debounceMs`
  coalesces the pair is right; §11 (identifiers) is not a defect — the `[RegisterWidget]` identifier
  being Pascal-cased while `WidgetType` is camel-cased is Xperience's convention meeting ours, and only
  the *second* pair has to match. Both are stated explicitly now.
