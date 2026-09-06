# Unit UX-4 — Rule builder side panels rebuilt to the approved boards

Owner 2026-09-05: the panel canvas https://claude.ai/code/artifact/b6de8e81-c084-4220-8fd1-676973a206b6
is **approved**. The boards are in the repo at `docs/internal/design/rule-builder-panels/`
(`Main.dc.html` = condition panel, `ConditionExpression`, `ConditionEmpty`, `ActionPanel` (pin),
`ActionBoost`, `ActionText` (redirect / replace query / remove word / replace word),
`ActionCustomData`, `PickerStates`, `AttributeRows`; `canvas.json` holds the notes). Open them
in a browser; the `<style>` block carries every value.

Templates: `src/XpSearch.Admin/Client/src/rule-builder/ConditionPanel.tsx`, `ActionPanel.tsx`,
`ItemPicker.tsx`, `AttributeRows.tsx`, `RuleBuilderTemplate.module.scss`. The rules of QT-3 /
UX-3 apply unchanged (stock components stay — `SidePanel`, `Switch`, `Select`, `Input`,
`TextArea`, `Button`, `Tag`, `Spinner`, `Divider`, `Stack`; layout by flex wrappers; tokens only;
`src/layout.test.ts` must stay green; own markup only for the picker list). Reference
implementation for panel spacing: the query tester's `SidePanel` in `QueryTesterTemplate.tsx`.
Read `docs/internal/agent-primer.md`, ADR-0028, the guideline section of
`docs/guides/admin-client-development.md`, and `docs/internal/units/UX-3.md` §B.2.4 first.

## 1. Panel anatomy (both panels)
- Stock `SidePanel size=Stackable`; headline = the board's title (`Condition 1`,
  `1 · Pin an item` from `actionLabels`); **first body child = the one-sentence muted subtitle**
  (the component has no subtitle slot): condition `All parts you switch on must hold.`, action
  = the type's note from `actionLabels` (add a `subtitle` string per type there if missing).
- Body: `Stack spacing={Spacing.XL}` (24px) with a stock `Divider` between sections.
- Footer: `Discard` secondary + `Apply` primary, right-aligned, 12px apart (already so).

## 2. Condition panel (`Main`, `ConditionExpression`, `ConditionEmpty`)
- Three sections, each a stock `Switch` (label 14/16 bold) with its fields **indented 48px**
  (`.toggleFields` keeps the indent) — but the fields inside sit in **rows**, not a column:
  Query = one row (`The visitor's search` Select 200px + `Words to look for` Input `flex: 1`),
  then the `Match plurals & synonyms` Switch on its own line; Context = one row (`Contact group`
  Select `flex: 1` + `Language` Select 160px). Add a `.fieldRow` (flex, gap 8, align flex-end)
  wrapper; column gaps 16px.
- Filters: the attribute rows (§4); the row actions `Add row` (tertiary S, `xp-plus`) and
  `Edit as text` (quinary S) 8px apart; expression mode = `Expression` Input with its placeholder
  and explanation `Comma-separated attribute:value pairs; all of them must hold.` + `Back to rows`.
- Empty state (first open): only Query on; the words Input shows the `e.g. grinder` placeholder.

## 3. Action panel (`ActionPanel`, `ActionBoost`, `ActionText`, `ActionCustomData`)
- Pin: the picker (§5) → `Divider` → `Position` Input 120px with **explanationText
  `1 is the first result.`** (new; the validator already refuses < 1).
- Boost: picker (nothing chosen) → the muted line `…or boost everything matching:` → attribute
  rows → `Divider` → `Multiplier` Input 120px with explanationText `Above 0. 2 doubles the score.`
  (new).
- Hide / Bury / Filter results: picker or rows as today, no extra field.
- Redirect: `Send the visitor to` Input, placeholder **`e.g. /campaigns/grinder-week`** (add the
  `e.g.` prefix); Replace query: note + `Search instead for`; Remove word: note + `Word`;
  Replace word: `Replace` and **`Replace with`** (rename from `…with`) as two Inputs **16px
  apart** (a `Stack spacing={Spacing.L}` inside the section, not the panel's XL).
- Custom data: note + `JSON` `TextArea` (monospace, `minRows 4`) as today.

## 4. Attribute rows (`AttributeRows` board)
- Each row: `Attribute` Select `flex: 1` · `is` (muted 12/16, bottom-aligned) · `Value` Select
  (facetable: index values with counts as secondary labels) or Input (`Type the value`) `flex: 1`
  · `Remove` (quinary S, `xp-times`); only the first row labelled; rows 8px apart.
- Kept-from-saved-rule values show the `not facetable` / `not in the index` secondary labels as
  today. The no-rows helper line stays as is.

## 5. Item picker (`PickerStates` board)
- `Input` with the existing placeholder and a trailing search icon; the list = own markup
  (bordered `--color-divider-default`, radius 8, rows padding 8px 16px, title 14/20 + url 12/16
  muted, selected row `--color-background-selected` + 600); `N matches.` muted line; the
  `Selected: <title> <url>` line; `Details` (tertiary S) **only once an item is chosen** (hide it
  while nothing is chosen).
- States: loading = stock `Spinner size=S` + `Searching…` (add the Spinner); no results =
  `No matches. Try fewer words.`; error = the existing `role=alert` line in
  `--color-alert-text`; chosen item gone = raw id + yellow `Tag` `no longer in the index`;
  Details expanded = `Hide details` + `Stored result id: …` mono.

## 6. Button sizes
Remove / Details / Edit as text / Back to rows / Add row move from `ButtonSize.XS` to **S**
(32px), matching the approved page boards' Edit / Delete.

## 7. Deliverables and checks
- `RuleBuilderTemplate.module.scss` gains the panel wrappers (`.panelSubtitle`, `.fieldRow`,
  picker list rules already exist — align them to §5 values); `layout.test.ts` green.
- Behaviour, commands, drag-and-drop, validation (`wrongWith`), test ids unchanged except the
  copy changes named above (`Replace with`, the two explanation texts, the `e.g.` prefix, the
  Spinner, Details hidden until chosen). `npm test` (rule-builder `*.test.ts` included),
  `npm run typecheck`, `npm run build`, Admin suite.
- ADR-0020 Consequences: one line adding the panel boards' folder and the picker list as own
  markup. CHANGELOG `**Changed (admin):** rule builder panels rebuilt to the approved boards …`.
  `docs/internal/screenshot-manifest.md`: add PENDING rows for the condition panel and the pin
  action panel (`tuning--rule-condition-panel.png`, `tuning--rule-action-panel.png`).
- Do not start or stop the host (27340) or the dev server (3010).
- One commit: `feat(admin): rule builder side panels rebuilt to the approved boards (UX-4)`.
- Report: files, region → component per panel, decisions taken, suite/build lines, commit hash.
