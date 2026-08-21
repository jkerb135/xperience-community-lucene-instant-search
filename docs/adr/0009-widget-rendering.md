# ADR-0009: Widget rendering — templating, focus and the accessibility gate

- **Status:** accepted
- **Date:** 2026-08-21
- **Spec reference:** §5.3, §5.4, §5.6, §5.7, §5.9, §9.1, §12

## Context

Spec §5.4 asks for "a tiny tagged-template `html` helper (no framework dependency, no virtual DOM)"
and default templates that are semantic, accessible and XSS-safe by default. Spec §5.7 forbids the
built-in widgets from using anything a third-party developer could not use. Spec §5.6 makes focus and
live-region behaviour non-negotiable, and §12 asks for an automated axe-core run. §5.9 caps core plus
the six default widgets at 20 KB gzip. Four decisions followed.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Template value:** plain string with a `__html` marker property | Smallest possible | A JSON payload carrying `__html` would be trusted as markup — a forgeable trust marker |
| **Template value:** `TemplateResult` class, `instanceof` for trust | Trust cannot be forged; nesting and arrays fall out naturally | ~40 bytes and a class in the public surface |
| **Template value:** DOM nodes (lit-style parts) | True diffing, focus preserved for free | A parts/diffing engine is the framework §5.4 rules out, and several KB |
| **Focus:** rebuild the widget on every render | One code path | Typing in the search box would reset the caret; pressing "show more" would drop focus |
| **Focus:** build once, patch afterwards | Correct for stateful controls, no diffing engine | Two code paths per widget (first render vs patch) |
| **Focus:** re-render, then restore focus by matching rendered text | One code path, survives list rebuilds | Only works where the control's text is stable |
| **Registration:** `registerWidgetType('hits', …)` side effect at import | Reuses the existing registry | `sideEffects: false` in `package.json` lets a bundler drop the module and silently break `.xps-mount` |
| **Registration:** a `DEFAULT_WIDGETS` table the bootstrap consults | Cannot be tree-shaken away; `registerWidgetType` still overrides | The bootstrap now imports the widgets, so the main entry always carries them |

## Decision

**`html` is a string builder with a class-shaped trust marker.** `` html`…` `` returns a
`TemplateResult` whose `value` is trusted HTML; every interpolation is escaped through
`escapeHtml` (`& < > " '`, so quoted attribute values are covered) unless it is already a
`TemplateResult`, an array of them, or produced by `html.raw(value)` — the single documented opt-out.
`null`, `undefined` and booleans render as empty. Trust is `instanceof TemplateResult`, not a
property, so a response body cannot forge it. `render(result, container)` is one `innerHTML`
assignment: no virtual DOM, as the spec requires. A template that returns a plain string has that
string escaped — the safe reading of an ambiguous case.

`highlight(field, hit)` returns `_highlights[field]` (HTML-encoded server-side before the tags were
inserted, §4.6) with `class="xps-highlight"` added to each `<mark>` so the markup contract is met
without asking every caller to configure `preTag`; it falls back to the escaped plain field.
`formatNumber` is `Intl.NumberFormat`.

**Focus is preserved per widget, not by a diffing engine.** Widgets that own stateful controls
(`searchBox`, `refinementList`, `sortBy`, `clearRefinements`, `toggleRefinement`) build their root
once and patch afterwards — the search input's `value` is only assigned when it differs from the
state, which is what keeps the caret where the user left it, and buttons that go inert are
`disabled` rather than removed. Widgets that are pure output rebuild wholesale; `pagination` and
`currentRefinements` rebuild through `renderKeepingFocus`, which re-focuses the control whose
rendered text matches the one that had focus. `hits` is a hybrid: its `role="status"` live region is
created once and only has its `textContent` replaced when the announcement changes, so a re-render
that does not change the count is not announced twice.

**axe-core runs in jsdom, restricted to the WCAG A/AA tags with `color-contrast` disabled.**
`color-contrast` needs computed colours and box geometry that jsdom does not have (and the widgets
ship no colours — the theme does, §6). Restricting the run to `wcag2a`/`wcag2aa`/`wcag21a`/`wcag21aa`
also excludes axe's page-level best-practice rules (`region`, `landmark-one-main`,
`page-has-heading-one`), which judge a document, not a widget. The test mounts all nine widgets
against the mock corpus in three states — results, refined, and no results — and asserts zero
violations. A browser run and a keyboard walkthrough remain the release gate (§12).

**The mount registry has a built-in fallback.** `bootstrap.ts` resolves `data-xps-widget` from the
`registerWidgetType` registry first and from a `DEFAULT_WIDGETS` table second, instead of the widgets
registering themselves as an import side effect that `"sideEffects": false` allows a bundler to drop.
`registerWidgetType('hits', …)` therefore still overrides a built-in, and the dotted-namespace rule
for third-party identifiers is unchanged.

## Consequences

- Custom templates are ordinary functions returning `TemplateResult | string | number | array`, with
  no build step and no framework. The cost is that a template cannot hold a DOM node reference — if
  a widget needs one, it is a connector plus a hand-written renderer, which is the supported path.
- All nine widgets fit the §5.9 budget with room to spare: **UMD, core + the `html` helper + all nine
  widgets, 12,256 B gzip against the 20,480 B ceiling** (ESM measured across `xpsearch.mjs`,
  `connectors.mjs` and the shared chunk: 12,931 B). The three Phase 2.5 widgets did not need a
  separate `./widgets-extra` entry point. `size-limit.json` now has one budget per format
  (`widgets-esm`, `widgets-umd`), both at 20 KB, because the default entry point is no longer
  core-only; the previous 10 KB core-only figure is not measurable from the shipped artefacts.
- Because the bootstrap imports the widgets, `import xpsearch from '@yourco/xperience-search'` pulls
  in all nine even if none is used. That is what the budget is measured against, and it is what makes
  a Page Builder page work with no author JavaScript at all. A consumer who wants only the connectors
  imports `@yourco/xperience-search/connectors`, which does not reach the widgets.
- Click tracking (§9.1) lives in `hits` as one delegated listener on the widget root: any `<a>` inside
  a `.xps-hits__item` sends `click` with the hit's `objectID` and its one-based position across pages.

## References

- WAI-ARIA APG, *Checkbox* pattern: <https://www.w3.org/WAI/ARIA/apg/patterns/checkbox/>
- WAI-ARIA APG, *Landmarks / search region*: <https://www.w3.org/WAI/ARIA/apg/practices/landmark-regions/>
- ARIA `status` role (live region semantics): <https://www.w3.org/TR/wai-aria-1.2/#status>
- axe-core rule descriptions, 4.13: <https://dequeuniversity.com/rules/axe/4.13>
- axe-core `run` options (`runOnly`, `rules`): <https://github.com/dequelabs/axe-core/blob/develop/doc/API.md#options-parameter>
