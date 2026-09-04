import { useState, type ReactElement } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  ButtonType,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  CellType,
  Checkbox,
  Colors,
  Cols,
  Column,
  ColumnContentType,
  Divider,
  DividerOrientation,
  Headline,
  HeadlineSize,
  Icon,
  Inline,
  Input,
  LayoutAlignment,
  MenuItem,
  NameToggleButtons,
  Row,
  Select,
  SidePanel,
  SidePanelSize,
  Spacing,
  Spinner,
  Stack,
  Table,
  Tag,
  useMediaBreakpoints,
} from '@kentico/xperience-admin-components';
import type { ComponentCell, IconName, TableColumn, TableRow } from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { column } from '../analytics/ReportTable';
import { flexRow, flexRowNoWrap, mono, muted, oneLine } from '../theme';

/*
 * Client template of the query tester (spec 8.4), rebuilt for QT-2 to the owner's prototype
 * docs/internal/design/QueryTester.dc.html: one list holding two rankings, a verdict, the pipeline
 * trail and a row detail panel that shows how a score was built. Registered as
 * "@xperience-community/xperience-search/QueryTester"; the back end is
 * XpSearch.Admin.UIPages.QueryTester.QueryTesterPage. See docs/adr/0028-query-tester-as-diff.md.
 */

interface ContactGroup {
  readonly codeName: string;
  readonly displayName: string;
}

interface QueryTesterProps {
  /** The index under test. It comes from the URL, so it is shown and never chosen. */
  readonly selectedIndexName: string;
  /** The content languages the index is configured for. Empty when the index indexes every language. */
  readonly languages: string[];
  /** The contact groups a run can be simulated as, so a group-scoped rule can be seen firing. */
  readonly contactGroups: ContactGroup[];
  /** Name of the index's draft or running experiment, whose variant B can be tried. Empty when there is none. */
  readonly experimentName: string;
}

type ResultChange = 'Unchanged' | 'MovedUp' | 'MovedDown' | 'Injected' | 'Removed';

/** The score one result had after one scoring stage of the pipeline (QT-2). */
interface ScoreStep {
  readonly stage: string;
  readonly score: number;
}

/** One tuning rule that touched a result. */
interface HitRule {
  readonly id: number;
  readonly name: string;
  readonly effect: string;
}

interface Hit {
  readonly id: string;
  readonly title: string;
  readonly url: string;
  readonly score: number;
  readonly position: number;
  readonly baseScore: number;
  readonly boosts: string[];
  readonly steps: ScoreStep[];
  readonly rules: HitRule[];
  readonly change: ResultChange;
}

interface Side {
  readonly hits: Hit[];
  readonly total: number;
  readonly tookMs: number;
  readonly queryExplanations: string[];
}

interface RunResult {
  readonly withRules: Side;
  readonly withoutRules: Side;
  readonly error: string;
}

interface RunData {
  readonly query: string;
  readonly language: string;
  readonly pageSize: number;
  /** Code name of the contact group to simulate; empty runs as the signed-in admin's own contact. */
  readonly contactGroup?: string;
  /** True to answer from the experiment's variant-B tuning instead of the live one. */
  readonly variantB?: boolean;
}

const Commands = {
  Run: 'Run',
  OpenStatus: 'OpenStatus',
  CreateRule: 'CreateRule',
  PinResult: 'PinResult',
  BuryResult: 'BuryResult',
  OpenRule: 'OpenRule',
};

const pageSizes = [10, 25, 50];
const anyLanguage = '';
const realVisitor = '';
const liveTuning = 'live';
const variantB = 'b';
const diffView = 'diff';
const sideBySideView = 'side';
const recentLimit = 5;

const changes: Record<ResultChange, { readonly label: string; readonly icon: IconName; readonly color: Colors }> = {
  Unchanged: { label: 'Unchanged', icon: 'xp-minus', color: Colors.BackgroundTagGrey },
  MovedUp: { label: 'Moved up', icon: 'xp-arrow-up', color: Colors.BackgroundTagSkyBlue },
  MovedDown: { label: 'Moved down', icon: 'xp-arrow-down', color: Colors.BackgroundTagYellow },
  Injected: { label: 'Added', icon: 'xp-plus', color: Colors.BackgroundTagNeonGreen },
  Removed: { label: 'Removed', icon: 'xp-ban-sign', color: Colors.BackgroundTagRose },
};

const effects: Record<string, string> = {
  boost: 'Boost rule',
  pin: 'Pin rule',
  bury: 'Bury rule',
  hide: 'Hide rule',
};

const round = (value: number): string => value.toFixed(3);

/** "English (en)" where the browser knows the code, the bare code where it does not. */
const languageLabel = (code: string): string => {
  const name = new Intl.DisplayNames(undefined, { type: 'language', fallback: 'code' }).of(code);

  return name === undefined || name === code ? code : `${name} (${code})`;
};

/** Recent queries live in the browser, per index, newest first (the SG-1 pattern). */
const recentKey = (index: string): string => `xpsearch.query-tester.recent.${index}`;

const readRecent = (index: string): string[] => {
  try {
    const stored: unknown = JSON.parse(window.localStorage.getItem(recentKey(index)) ?? '[]');

    return Array.isArray(stored) ? stored.filter((entry): entry is string => typeof entry === 'string').slice(0, recentLimit) : [];
  } catch {
    // A quota-less or blocked storage is not a reason to lose the page.
    return [];
  }
};

const writeRecent = (index: string, queries: string[]): void => {
  try {
    window.localStorage.setItem(recentKey(index), JSON.stringify(queries));
  } catch {
    // As above: the chips are a convenience, never state the page depends on.
  }
};

/** One row of the diff: the same document as both rankings hold it. */
interface DiffRow {
  readonly hit: Hit;
  /** Position with the tuning applied, or null when the tuning dropped it. */
  readonly tuned: number | null;
  /** Position without any tuning, or null when only the tuning has it. */
  readonly raw: number | null;
}

/** The second line of the score column: what the tuning did to this result's score. */
const delta = (row: DiffRow): string => {
  if (row.raw === null) {
    return 'not in raw';
  }

  if (row.tuned === null) {
    return 'not in tuned';
  }

  const difference = row.hit.score - row.hit.baseScore;

  return row.hit.change === 'Unchanged' || Math.abs(difference) < 0.0005
    ? `base ${round(row.hit.baseScore)}`
    : `${difference > 0 ? '+' : '−'}${round(Math.abs(difference))} vs base`;
};

/** How the row detail panel names the move. */
const summary = (row: DiffRow): string => {
  if (row.raw === null) {
    return `${changes[row.hit.change].label} · not in raw ranking → tuned #${row.tuned ?? 1}`;
  }

  if (row.tuned === null) {
    return `${changes[row.hit.change].label} · raw #${row.raw} → not in the tuned ranking`;
  }

  return `${changes[row.hit.change].label} · raw #${row.raw} → tuned #${row.tuned}`;
};

/*
 * A stock TableRow is a fixed 48px tall, so every cell of both tables is one line: Inline would wrap
 * the icon above the tag inside a narrow cell, and a second line would be clipped (ADR-0028).
 */
const ChangeTag = ({ change }: { readonly change: ResultChange }) => (
  <span style={flexRowNoWrap}>
    <Icon name={changes[change].icon} />
    <Tag label={changes[change].label} readOnly background={{ color: changes[change].color }} />
  </span>
);

/** ComponentCell renders <cell.component />, so a cell holds a component, not an element. */
const node = (columnName: string, render: () => ReactElement): ComponentCell => ({
  type: CellType.Component,
  columnName,
  component: render,
});

const dim = (unchanged: boolean, value: string): ReactElement =>
  unchanged ? <p style={muted}>{value}</p> : <span>{value}</span>;

/** The title alone; the URL is in the row panel's header and in the cell's tooltip. */
const Title = ({ hit, selected }: { readonly hit: Hit; readonly selected: boolean }) => (
  <strong
    style={selected ? { ...oneLine, color: Colors.TextHighEmphasis } : oneLine}
    title={hit.url === '' ? hit.title || hit.id : `${hit.title || hit.id} — ${hit.url}`}
  >
    {hit.title || hit.id}
  </strong>
);

/** The final score, then what the tuning did to it, on the same line. */
const Score = ({ row }: { readonly row: DiffRow }) => (
  <span style={{ ...flexRowNoWrap, columnGap: '6px' }}>
    <strong>{round(row.hit.score)}</strong>
    <span style={muted}>{delta(row)}</span>
  </span>
);

/** The verdict headline and body: what the tuning did to this query, in one sentence. */
const verdictOf = (rows: DiffRow[]): { readonly headline: string; readonly body: string } => {
  const tally: string[] = [];
  const count = (change: ResultChange) => rows.filter((row) => row.hit.change === change).length;

  ([
    ['MovedUp', 'moved up'],
    ['Injected', 'added'],
    ['MovedDown', 'moved down'],
    ['Removed', 'removed'],
  ] as [ResultChange, string][]).forEach(([change, word]) => {
    const total = count(change);

    if (total > 0) {
      tally.push(`${total} ${word}`);
    }
  });

  const moved = rows.filter((row) => row.hit.change !== 'Unchanged').length;

  return moved === 0
    ? {
        headline: 'Tuning made no difference to this query',
        body: 'Both rankings are identical. If this query matters, a pin or boost rule is the lever.',
      }
    : {
        headline: `Tuning changed ${moved} of ${rows.length} results`,
        body: `${tally.join(', ')}. Select a row to see how its score was built.`,
      };
};

export const QueryTesterTemplate = ({ selectedIndexName, languages, contactGroups, experimentName }: QueryTesterProps) => {
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(anyLanguage);
  const [pageSize, setPageSize] = useState(pageSizes[0]);
  const [contactGroup, setContactGroup] = useState(realVisitor);
  const [variant, setVariant] = useState(liveTuning);
  const [simulateOpen, setSimulateOpen] = useState(false);
  const [ran, setRan] = useState('');
  const [result, setResult] = useState<RunResult | undefined>(undefined);
  const [running, setRunning] = useState(false);
  const [view, setView] = useState(diffView);
  const [onlyChanges, setOnlyChanges] = useState(false);
  const [selected, setSelected] = useState('');
  const [stage, setStage] = useState(-1);
  const [recent, setRecent] = useState(() => readRecent(selectedIndexName));
  const { sm: narrow } = useMediaBreakpoints();

  const { execute: run } = usePageCommand<RunResult, RunData>(Commands.Run, {
    after: (response) => {
      setRunning(false);
      setResult(response);
    },
  });

  const { execute: openStatus } = usePageCommand<void, void>(Commands.OpenStatus);
  const { execute: createRule } = usePageCommand<void, { readonly query: string }>(Commands.CreateRule);
  const { execute: pinResult } = usePageCommand<void, { readonly query: string; readonly targetId: string; readonly position: number }>(
    Commands.PinResult,
  );
  const { execute: buryResult } = usePageCommand<void, { readonly query: string; readonly targetId: string }>(Commands.BuryResult);
  const { execute: openRule } = usePageCommand<void, { readonly ruleId: number; readonly variantB: boolean }>(Commands.OpenRule);

  const submit = (text: string, nextLanguage: string) => {
    const trimmed = text.trim();

    if (trimmed === '') {
      return;
    }

    const next = [trimmed, ...recent.filter((entry) => entry !== trimmed)].slice(0, recentLimit);

    setRecent(next);
    writeRecent(selectedIndexName, next);
    setQuery(text);
    setLanguage(nextLanguage);
    setRan(trimmed);
    setRunning(true);
    setSelected('');
    setStage(-1);
    void run({ query: text, language: nextLanguage, pageSize, contactGroup, variantB: variant === variantB });
  };

  const empty = query.trim() === '';
  const failed = !running && result !== undefined && result.error !== '';
  const loaded = !running && result !== undefined && result.error === '';
  const fallback = languages.find((code) => code !== language);

  const rawPositions = new Map<string, number>(
    (result?.withoutRules.hits ?? []).map((hit) => [hit.id, hit.position] as const),
  );
  const tunedPositions = new Map<string, number>(
    (result?.withRules.hits ?? []).map((hit) => [hit.id, hit.position] as const),
  );

  // One list, two rankings: every tuned hit, then the hits only the raw ranking still holds.
  const rows: DiffRow[] = [
    ...(result?.withRules.hits ?? []).map((hit) => ({ hit, tuned: hit.position, raw: rawPositions.get(hit.id) ?? null })),
    ...(result?.withoutRules.hits ?? [])
      .filter((hit) => hit.change === 'Removed')
      .map((hit) => ({ hit, tuned: tunedPositions.get(hit.id) ?? null, raw: hit.position })),
  ];

  const shown = view === diffView && onlyChanges ? rows.filter((row) => row.hit.change !== 'Unchanged') : rows;
  const selectedRow = rows.find((row) => row.hit.id === selected);
  const verdict = verdictOf(rows);
  const explanations = result?.withRules.queryExplanations ?? [];

  const pick = (identifier: unknown) => {
    const id = String(identifier);

    setSelected((current) => (current === id ? '' : id));
  };

  /*
   * The table is a grid of `auto` tracks whose cells carry `min-width: <units>x8px`,
   * `max-width: <units>x8px` and 16px of padding either side (content-box), so a column is exactly
   * `units x 8 + 32` wide when the two are equal - and an `auto` track grows to its content when they
   * are not, which is what put a 1026px row inside an 887px card. Every column is therefore pinned:
   * 86 units x 8 + 6 cells x 32 + the row's 2px border = 882px, inside the 887px of card content at a
   * 1366px viewport. Cells are single-line (the row is a fixed 48px), so long text ellipsizes.
   */
  const diffColumns: TableColumn[] = [
    column('tuned', 'Tuned #', { minWidth: 6, maxWidth: 6 }),
    column('raw', 'Raw #', { minWidth: 6, maxWidth: 6 }),
    column('change', 'Change', { minWidth: 19, maxWidth: 19, contentType: ColumnContentType.Component }),
    column('result', 'Result', { minWidth: 23, maxWidth: 23, contentType: ColumnContentType.Component }),
    column('score', 'Score', { minWidth: 16, maxWidth: 16, contentType: ColumnContentType.Component }),
    ...(narrow ? [] : [column('why', 'Why', { minWidth: 16, maxWidth: 16, contentType: ColumnContentType.Component })]),
  ];

  const diffRows: TableRow[] = shown.map((row) => ({
    identifier: row.hit.id,
    disabled: false,
    cells: [
      node('tuned', () => dim(row.hit.change === 'Unchanged', row.tuned === null ? '—' : String(row.tuned))),
      node('raw', () => dim(true, row.raw === null ? '—' : String(row.raw))),
      node('change', () => <ChangeTag change={row.hit.change} />),
      node('result', () => <Title hit={row.hit} selected={row.hit.id === selected} />),
      node('score', () => <Score row={row} />),
      ...(narrow
        ? []
        : [
            node('why', () => (
              <p style={{ ...muted, ...oneLine }} title={row.hit.boosts.join(' · ')}>
                {row.hit.boosts.join(' · ')}
              </p>
            )),
          ]),
    ],
  }));

  // Half a card is ~435px at 1366: 36 units + 4 cells x 32px + the border is 418px. Same arithmetic.
  const sideColumns = (position: string): TableColumn[] => [
    column('position', position, { minWidth: 4, maxWidth: 4 }),
    column('result', 'Result', { minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component }),
    column('change', 'Change', { minWidth: 12, maxWidth: 12, contentType: ColumnContentType.Component }),
    column('score', 'Score', { minWidth: 7, maxWidth: 7, contentType: ColumnContentType.Component }),
  ];

  const sideRows = (hits: Hit[], tuned: boolean): TableRow[] =>
    hits.map((hit) => ({
      identifier: hit.id,
      disabled: false,
      cells: [
        node('position', () => dim(hit.change === 'Unchanged', String(hit.position))),
        node('result', () => <Title hit={hit} selected={hit.id === selected} />),
        // The prototype's side-by-side marks a moved row with the tag alone; the icon belongs to the
        // diff table, where the column is wide enough for both.
        node('change', () =>
          hit.change === 'Unchanged' ? (
            <span />
          ) : (
            <Tag label={changes[hit.change].label} readOnly background={{ color: changes[hit.change].color }} />
          )),
        node('score', () => <strong>{round(tuned ? hit.score : hit.baseScore)}</strong>),
      ],
    }));

  return (
    <>
      <Stack spacing={Spacing.XL}>
        <Card
          headline={
            <div style={flexRow}>
              <Headline size={HeadlineSize.L}>Query tester</Headline>
              <p style={muted}>
                {`Index ${selectedIndexName} · ${variant === variantB ? `variant B of ${experimentName}` : 'live tuning'} · ${explanations.length} pipeline stage${explanations.length === 1 ? '' : 's'}`}
              </p>
            </div>
          }
        >
          <Stack spacing={Spacing.M}>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                submit(query, language);
              }}
            >
              <Row spacing={Spacing.L} alignY={LayoutAlignment.End}>
                <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                  <Input
                    label="Query"
                    markAsRequired
                    placeholder="e.g. espresso"
                    value={query}
                    onChange={(event) => setQuery(event.target.value)}
                  />
                </Column>
                <Column>
                  <Select label="Language" value={language} onChange={(value) => setLanguage(value ?? anyLanguage)}>
                    <MenuItem primaryLabel="Any language" value={anyLanguage} />
                    {languages.map((code) => (
                      <MenuItem key={code} primaryLabel={languageLabel(code)} value={code} />
                    ))}
                  </Select>
                </Column>
                <Column>
                  <Button
                    label="Run"
                    type={ButtonType.Submit}
                    color={ButtonColor.Primary}
                    size={ButtonSize.M}
                    inProgress={running}
                    disabled={empty}
                  />
                </Column>
              </Row>
            </form>

            <Row spacing={Spacing.M} alignY={LayoutAlignment.Center}>
              <Column cols={narrow ? Cols.Col12 : Cols.Col8}>
                <span style={flexRowNoWrap}>
                  <Button
                    label="Simulate as"
                    icon="xp-user"
                    color={ButtonColor.Tertiary}
                    size={ButtonSize.S}
                    active={simulateOpen}
                    onClick={() => setSimulateOpen(!simulateOpen)}
                  />
                  <Tag
                    label={contactGroups.find((group) => group.codeName === contactGroup)?.displayName ?? 'Real visitor (your contact)'}
                    readOnly
                    background={{ color: Colors.BackgroundTagSkyBlue }}
                  />
                  <Tag
                    label={variant === variantB ? `Variant B of ${experimentName}` : 'Live tuning (A)'}
                    readOnly
                    background={{ color: Colors.BackgroundTagSkyBlue }}
                  />
                </span>
              </Column>
              <Column cols={narrow ? Cols.Col12 : Cols.Col4}>
                {recent.length === 0 ? null : (
                  <Stack align={narrow ? LayoutAlignment.Start : LayoutAlignment.End}>
                    <span style={flexRowNoWrap}>
                      <p style={muted}>Recent:</p>
                      {recent.map((entry) => (
                        <Button
                          key={entry}
                          label={entry}
                          color={ButtonColor.Tertiary}
                          size={ButtonSize.S}
                          onClick={() => submit(entry, language)}
                        />
                      ))}
                    </span>
                  </Stack>
                )}
              </Column>
            </Row>

            {simulateOpen ? (
              <Stack spacing={Spacing.M}>
                <Divider orientation={DividerOrientation.Horizontal} />
                <Row spacing={Spacing.L} alignY={LayoutAlignment.End}>
                  <Column>
                    <Select label="Contact group" value={contactGroup} onChange={(value) => setContactGroup(value ?? realVisitor)}>
                      <MenuItem primaryLabel="Real visitor (your contact)" value={realVisitor} />
                      {contactGroups.map((group) => (
                        <MenuItem key={group.codeName} primaryLabel={group.displayName} value={group.codeName} />
                      ))}
                    </Select>
                  </Column>
                  <Column>
                    <Select label="Tuning" value={variant} onChange={(value) => setVariant(value ?? liveTuning)}>
                      <MenuItem primaryLabel="Live tuning (A)" value={liveTuning} />
                      {experimentName === '' ? null : (
                        <MenuItem primaryLabel={`Variant B of ${experimentName}`} value={variantB} />
                      )}
                    </Select>
                  </Column>
                  <Column>
                    <Select
                      label="Results per side"
                      value={String(pageSize)}
                      onChange={(value) => setPageSize(Number(value) || pageSizes[0])}
                    >
                      {pageSizes.map((size) => (
                        <MenuItem key={size} primaryLabel={String(size)} value={String(size)} />
                      ))}
                    </Select>
                  </Column>
                  <Column>
                    <p style={muted}>Simulation settings apply to the next run.</p>
                  </Column>
                </Row>
              </Stack>
            ) : null}
          </Stack>
        </Card>

        {running ? <Spinner /> : null}

        {failed ? (
          <Callout
            type={CalloutType.FriendlyWarning}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Friendly warning"
            headline="The query could not be run"
            actionButton={
              <Inline spacing={Spacing.M}>
                <Button
                  label="Open status"
                  color={ButtonColor.Secondary}
                  onClick={() => {
                    void openStatus();
                  }}
                />
                {fallback === undefined ? null : (
                  <Button label={`Try ${languageLabel(fallback)}`} color={ButtonColor.Tertiary} onClick={() => submit(query, fallback)} />
                )}
              </Inline>
            }
          >
            <p role="alert">{result.error}</p>
          </Callout>
        ) : null}

        {!running && result === undefined ? (
          <Callout
            type={CalloutType.QuickTip}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Quick tip"
            headline="What this page shows"
          >
            Running a query gives you one list holding two rankings: the results with the tuning of this index applied, marked
            against the same query run without any of it. Select a row to see how its score was built and which rules touched it.
          </Callout>
        ) : null}

        {loaded ? (
          <Callout
            type={CalloutType.QuickTip}
            placement={CalloutPlacementType.OnPaper}
            subheadline={`Verdict for ‘${ran}’`}
            headline={verdict.headline}
            actionButton={
              <Button
                label="Create a rule for this query"
                color={ButtonColor.Secondary}
                onClick={() => {
                  void createRule({ query: ran });
                }}
              />
            }
          >
            <div aria-live="polite">{verdict.body}</div>
          </Callout>
        ) : null}

        {loaded && explanations.length > 0 ? (
          <Card headline={<Headline size={HeadlineSize.S}>Pipeline</Headline>}>
            <Stack spacing={Spacing.S}>
              <Inline spacing={Spacing.XS}>
                <Tag label={ran} readOnly background={{ color: Colors.BackgroundTagDefault }} />
                {explanations.map((line, index) => (
                  <Inline key={line} spacing={Spacing.XS}>
                    <Icon name="xp-arrow-right" />
                    <Tag
                      label={line.length > 40 ? `${line.slice(0, 40)}…` : line}
                      tooltipText={line}
                      background={{ color: index === stage ? Colors.BackgroundTagSkyBlue : Colors.BackgroundTagGrey }}
                      onClick={() => setStage(index === stage ? -1 : index)}
                    />
                  </Inline>
                ))}
              </Inline>
              {stage >= 0 && stage < explanations.length ? <p style={mono}>{explanations[stage]}</p> : null}
            </Stack>
          </Card>
        ) : null}

        {loaded ? (
          <Card
            headline={
              <div style={flexRow}>
                <Headline size={HeadlineSize.S}>{`Results for ‘${ran}’`}</Headline>
                <p style={muted} aria-live="polite">
                  {`${result.withRules.total} tuned · ${result.withoutRules.total} raw · ${rows.filter((row) => row.hit.change !== 'Unchanged').length} changed · ${result.withRules.tookMs} ms / ${result.withoutRules.tookMs} ms`}
                </p>
              </div>
            }
          >
            <Stack spacing={Spacing.M}>
              <Row spacing={Spacing.L} alignY={LayoutAlignment.Center}>
                <Column>
                  {view === diffView ? (
                    <Checkbox
                      label="Only changes"
                      checked={onlyChanges}
                      onChange={(_event, checked) => setOnlyChanges(checked)}
                    />
                  ) : null}
                </Column>
                <Column>
                  <NameToggleButtons
                    selectedItemId={view}
                    items={[
                      { id: diffView, label: 'Diff' },
                      { id: sideBySideView, label: 'Side by side' },
                    ]}
                    onChange={(id) => setView(String(id))}
                  />
                </Column>
              </Row>

              {view === diffView ? (
                shown.length === 0 ? (
                  <p style={muted}>{rows.length === 0 ? 'No results.' : 'Nothing changed for this query.'}</p>
                ) : (
                  <Table columns={diffColumns} rows={diffRows} isHeaderVisible onRowClick={pick} />
                )
              ) : (
                <Row spacing={Spacing.L}>
                  <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                    <Stack spacing={Spacing.XS}>
                      <Headline size={HeadlineSize.S}>With tuning</Headline>
                      <p style={muted}>Rules, synonyms, stopwords, field weights</p>
                      <Table
                        columns={sideColumns('Tuned #')}
                        rows={sideRows(result.withRules.hits, true)}
                        isHeaderVisible
                        onRowClick={pick}
                      />
                    </Stack>
                  </Column>
                  <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                    <Stack spacing={Spacing.XS}>
                      <Headline size={HeadlineSize.S}>Without tuning</Headline>
                      <p style={muted}>Raw index, no rules applied</p>
                      <Table
                        columns={sideColumns('Raw #')}
                        rows={sideRows(result.withoutRules.hits, false)}
                        isHeaderVisible
                        onRowClick={pick}
                      />
                    </Stack>
                  </Column>
                </Row>
              )}
            </Stack>
          </Card>
        ) : null}
      </Stack>

      <SidePanel
        headline={selectedRow === undefined ? '' : selectedRow.hit.title || selectedRow.hit.id}
        size={narrow ? SidePanelSize.Full : SidePanelSize.Stackable}
        isVisible={selectedRow !== undefined}
        onClose={() => setSelected('')}
        footer={
          selectedRow === undefined ? undefined : (
            <Inline spacing={Spacing.M}>
              <Button
                label={`Bury for ‘${ran}’`}
                color={ButtonColor.Secondary}
                onClick={() => {
                  void buryResult({ query: ran, targetId: selectedRow.hit.id });
                }}
              />
              <Button
                label={`Pin for ‘${ran}’`}
                color={ButtonColor.Primary}
                onClick={() => {
                  void pinResult({ query: ran, targetId: selectedRow.hit.id, position: selectedRow.tuned ?? 1 });
                }}
              />
            </Inline>
          )
        }
      >
        {selectedRow === undefined ? null : (
          <Stack spacing={Spacing.L}>
            {selectedRow.hit.url === '' ? null : <p style={mono}>{selectedRow.hit.url}</p>}
            <Inline>
              <Tag label={summary(selectedRow)} readOnly background={{ color: changes[selectedRow.hit.change].color }} />
            </Inline>

            <Stack spacing={Spacing.S}>
              <Headline size={HeadlineSize.S}>How the score was built</Headline>
              {selectedRow.hit.steps.length === 0 ? (
                <p style={muted}>No breakdown was recorded for this result.</p>
              ) : (
                selectedRow.hit.steps.map((step, index) => (
                  <Row key={`${step.stage}-${index}`} spacing={Spacing.M}>
                    <Column cols={Cols.Col8}>
                      {index === selectedRow.hit.steps.length - 1 ? <strong>{step.stage}</strong> : <span>{step.stage}</span>}
                    </Column>
                    <Column cols={Cols.Col4}>
                      <Stack align={LayoutAlignment.End}>
                        {index === selectedRow.hit.steps.length - 1 ? (
                          <strong>{round(step.score)}</strong>
                        ) : (
                          <span>{round(step.score)}</span>
                        )}
                      </Stack>
                    </Column>
                  </Row>
                ))
              )}
            </Stack>

            <Stack spacing={Spacing.S}>
              <Headline size={HeadlineSize.S}>Rules that touched this result</Headline>
              {selectedRow.hit.rules.length === 0 ? (
                <p style={muted}>None. Only the query-level stages apply.</p>
              ) : (
                selectedRow.hit.rules.map((rule) => (
                  <Card key={`${rule.id}-${rule.effect}`}>
                    <Row spacing={Spacing.M} alignY={LayoutAlignment.Center}>
                      <Column cols={Cols.Col8}>
                        <Stack spacing={Spacing.XS}>
                          <strong>{rule.name}</strong>
                          <p style={muted}>{effects[rule.effect] ?? rule.effect}</p>
                        </Stack>
                      </Column>
                      <Column cols={Cols.Col4}>
                        <Stack align={LayoutAlignment.End}>
                          <Button
                            label="Open rule"
                            color={ButtonColor.Tertiary}
                            size={ButtonSize.S}
                            onClick={() => {
                              void openRule({ ruleId: rule.id, variantB: variant === variantB });
                            }}
                          />
                        </Stack>
                      </Column>
                    </Row>
                  </Card>
                ))
              )}
            </Stack>
          </Stack>
        )}
      </SidePanel>
    </>
  );
};
