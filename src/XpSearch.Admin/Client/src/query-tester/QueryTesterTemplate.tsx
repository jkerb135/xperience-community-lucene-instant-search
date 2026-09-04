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
  ColumnContentType,
  Divider,
  DividerOrientation,
  Icon,
  Inline,
  Input,
  MenuItem,
  NameToggleButtons,
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

import styles from './QueryTesterTemplate.module.scss';

/*
 * Client template of the query tester (spec 8.4), rebuilt for QT-2 to the owner's prototype
 * docs/internal/design/QueryTester.dc.html: one list holding two rankings, a verdict, the pipeline
 * trail and a row detail panel that shows how a score was built. QT-3a styled it to that prototype:
 * every region keeps its stock component and the layout lives in QueryTesterTemplate.module.scss.
 * Registered as "@xperience-community/xperience-search/QueryTester"; the back end is
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
 * The change marker is the one region the design system cannot render: Tag has no icon slot, so the
 * chip is our own span with the stock Icon inside it, styled to the Tag's geometry (ADR-0028).
 */
const ChangeChip = ({ change, label, className }: { readonly change: ResultChange; readonly label?: string; readonly className?: string }) => (
  <span
    className={className === undefined ? styles.chip : `${styles.chip} ${className}`}
    style={{ background: changes[change].color }}
  >
    <span className={styles.chipIcon}>
      <Icon name={changes[change].icon} />
    </span>
    {label ?? changes[change].label}
  </span>
);

/**
 * ComponentCell renders <cell.component />, so a cell holds a component, not an element. Every cell
 * carries the row's selected flag: Table only offers selection through checkboxes, so the open row
 * is marked from its cells (the module's `:has([data-row-selected])` rule on the row). The stock
 * cell inherits the shell's centred text, so each cell states its own alignment.
 */
const node = (columnName: string, selected: boolean, render: () => ReactElement): ComponentCell => ({
  type: CellType.Component,
  columnName,
  component: () => (
    <div
      className={`${styles.cell} ${columnName === 'score' ? styles.cellEnd : styles.cellStart}`}
      data-row-selected={selected ? 'true' : undefined}
    >
      {render()}
    </div>
  ),
});

/** Title over url, both ellipsized; the row grew to two lines for it. */
const Result = ({ hit }: { readonly hit: Hit }) => (
  <div className={styles.result}>
    <div className={`${styles.resultTitle} ${styles.oneLine}`} title={hit.title || hit.id}>
      {hit.title || hit.id}
    </div>
    {hit.url === '' ? null : (
      <div className={`${styles.resultUrl} ${styles.oneLine}`} title={hit.url}>
        {hit.url}
      </div>
    )}
  </div>
);

/** The final score with what the tuning did to it stacked underneath, both right-aligned. */
const Score = ({ row }: { readonly row: DiffRow }) => (
  <div className={styles.score}>
    <span className={styles.scoreValue}>{round(row.hit.score)}</span>
    <span className={styles.scoreDelta}>{delta(row)}</span>
  </div>
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
   * A Table cell carries `min-width: <units>x8px`, `max-width: <units>x8px` and 16px of padding
   * either side (content-box) inside a grid of `auto` tracks, so a column is exactly
   * `units x 8 + 32` wide and only stays put while the two widths are equal. The units below hold
   * the design's proportions (64 · 64 · 176 · 304 · 120 · 152) inside the 887px of card content a
   * 1366px viewport gives: 86 units x 8 + 6 cells x 32 + the row's 2px border = 882px.
   */
  const diffColumns: TableColumn[] = [
    column('tuned', 'Tuned', { minWidth: 4, maxWidth: 4 }),
    column('raw', 'Raw', { minWidth: 4, maxWidth: 4 }),
    column('change', 'Change', { minWidth: 18, maxWidth: 18, contentType: ColumnContentType.Component }),
    column('result', 'Result', { minWidth: 34, maxWidth: 34, contentType: ColumnContentType.Component }),
    column('score', 'Score', { minWidth: 11, maxWidth: 11, contentType: ColumnContentType.Component }),
    ...(narrow ? [] : [column('why', 'Why', { minWidth: 15, maxWidth: 15, contentType: ColumnContentType.Component })]),
  ];

  const diffRows: TableRow[] = shown.map((row) => {
    const open = row.hit.id === selected;
    const unchanged = row.hit.change === 'Unchanged';

    return {
      identifier: row.hit.id,
      disabled: false,
      cells: [
        node('tuned', open, () => (
          <span className={unchanged ? `${styles.rank} ${styles.dim}` : styles.rank}>{row.tuned === null ? '—' : row.tuned}</span>
        )),
        node('raw', open, () => <span className={styles.rankRaw}>{row.raw === null ? '—' : row.raw}</span>),
        node('change', open, () => <ChangeChip change={row.hit.change} />),
        node('result', open, () => <Result hit={row.hit} />),
        node('score', open, () => <Score row={row} />),
        ...(narrow
          ? []
          : [
              node('why', open, () => (
                <p className={styles.why} title={row.hit.boosts.join(' · ')}>
                  {row.hit.boosts.join(' · ')}
                </p>
              )),
            ]),
      ],
    };
  });

  // Half a card is ~435px at 1366: 36 units + 4 cells x 32px + the border is 418px. Same arithmetic.
  const sideColumns = (position: string): TableColumn[] => [
    column('position', position, { minWidth: 4, maxWidth: 4 }),
    column('result', 'Result', { minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component }),
    column('change', 'Change', { minWidth: 12, maxWidth: 12, contentType: ColumnContentType.Component }),
    column('score', 'Score', { minWidth: 7, maxWidth: 7, contentType: ColumnContentType.Component }),
  ];

  const sideRows = (hits: Hit[], tuned: boolean): TableRow[] =>
    hits.map((hit) => {
      const open = hit.id === selected;
      const unchanged = hit.change === 'Unchanged';

      return {
        identifier: hit.id,
        disabled: false,
        cells: [
          node('position', open, () => (
            <span className={unchanged ? `${styles.rank} ${styles.dim}` : styles.rank}>{hit.position}</span>
          )),
          node('result', open, () => <Result hit={hit} />),
          // The prototype's side-by-side marks a moved row with the chip alone, without the icon.
          node('change', open, () =>
            unchanged ? (
              <span />
            ) : (
              <Tag label={changes[hit.change].label} readOnly background={{ color: changes[hit.change].color }} />
            )),
          node('score', open, () => (
            <div className={styles.score}>
              <span className={styles.scoreValue}>{round(tuned ? hit.score : hit.baseScore)}</span>
            </div>
          )),
        ],
      };
    });

  return (
    <>
      <Stack spacing={Spacing.XL}>
        <div className={styles.card}>
          <Card
            headline={
              <div className={styles.cardHeader}>
                <span>Query tester</span>
                <p className={styles.muted}>
                  {`Index ${selectedIndexName} · ${variant === variantB ? `variant B of ${experimentName}` : 'live tuning'} · ${explanations.length} pipeline stage${explanations.length === 1 ? '' : 's'}`}
                </p>
              </div>
            }
          >
            <form
              className={styles.formRow}
              onSubmit={(event) => {
                event.preventDefault();
                submit(query, language);
              }}
            >
              <div className={styles.grow}>
                <Input
                  label="Query"
                  markAsRequired
                  placeholder="e.g. espresso"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                />
              </div>
              <Select label="Language" value={language} onChange={(value) => setLanguage(value ?? anyLanguage)}>
                <MenuItem primaryLabel="Any language" value={anyLanguage} />
                {languages.map((code) => (
                  <MenuItem key={code} primaryLabel={languageLabel(code)} value={code} />
                ))}
              </Select>
              <Button
                label="Run"
                type={ButtonType.Submit}
                color={ButtonColor.Primary}
                size={ButtonSize.M}
                inProgress={running}
                disabled={empty}
              />
            </form>

            <div className={styles.chipRow}>
              <Button
                label="Simulate as"
                icon="xp-user"
                color={ButtonColor.Tertiary}
                size={ButtonSize.S}
                active={simulateOpen}
                onClick={() => setSimulateOpen(!simulateOpen)}
              />
              <span className={styles.muted}>Applied:</span>
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
              {recent.length === 0 ? null : (
                <>
                  <span className={`${styles.muted} ${styles.spacer}`}>Recent:</span>
                  {recent.map((entry) => (
                    <Button
                      key={entry}
                      label={entry}
                      color={ButtonColor.Tertiary}
                      size={ButtonSize.S}
                      onClick={() => submit(entry, language)}
                    />
                  ))}
                </>
              )}
            </div>

            {simulateOpen ? (
              <div className={styles.drawer}>
                <Divider orientation={DividerOrientation.Horizontal} />
                <div className={styles.formRow}>
                  <Select label="Contact group" value={contactGroup} onChange={(value) => setContactGroup(value ?? realVisitor)}>
                    <MenuItem primaryLabel="Real visitor (your contact)" value={realVisitor} />
                    {contactGroups.map((group) => (
                      <MenuItem key={group.codeName} primaryLabel={group.displayName} value={group.codeName} />
                    ))}
                  </Select>
                  <Select label="Tuning" value={variant} onChange={(value) => setVariant(value ?? liveTuning)}>
                    <MenuItem primaryLabel="Live tuning (A)" value={liveTuning} />
                    {experimentName === '' ? null : (
                      <MenuItem primaryLabel={`Variant B of ${experimentName}`} value={variantB} />
                    )}
                  </Select>
                  <Select
                    label="Results per side"
                    value={String(pageSize)}
                    onChange={(value) => setPageSize(Number(value) || pageSizes[0])}
                  >
                    {pageSizes.map((size) => (
                      <MenuItem key={size} primaryLabel={String(size)} value={String(size)} />
                    ))}
                  </Select>
                  <p className={`${styles.muted} ${styles.drawerNote}`}>Simulation settings apply to the next run.</p>
                </div>
              </div>
            ) : null}
          </Card>
        </div>

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
          <div className={styles.verdict}>
            <Callout
              type={CalloutType.QuickTip}
              placement={CalloutPlacementType.OnPaper}
              subheadline={`Verdict for ‘${ran}’`}
              headline={verdict.headline}
            >
              <div className={styles.verdictRow}>
                <div aria-live="polite">{verdict.body}</div>
                <Button
                  label="Create a rule for this query"
                  color={ButtonColor.Secondary}
                  onClick={() => {
                    void createRule({ query: ran });
                  }}
                />
              </div>
            </Callout>
          </div>
        ) : null}

        {loaded && explanations.length > 0 ? (
          <div className={`${styles.card} ${styles.pipelineCard}`}>
            <Card>
              <div className={styles.pipelineRow}>
                <span className={`${styles.label} ${styles.pipelineLabel}`}>Pipeline</span>
                <Tag label={ran} readOnly background={{ color: Colors.TextDefaultOnLight }} />
                {explanations.map((line, index) => (
                  // Arrow and chip travel as one inline-flex box, so the arrow can never wrap away
                  // from its chip and is centred on it by construction.
                  <span className={styles.stage} key={line}>
                    <span className={styles.arrow}>
                      <Icon name="xp-arrow-right" />
                    </span>
                    <Tag
                      label={line.length > 40 ? `${line.slice(0, 40)}…` : line}
                      tooltipText={line}
                      background={{ color: index === stage ? Colors.BackgroundTagSkyBlue : Colors.BackgroundTagGrey }}
                      onClick={() => setStage(index === stage ? -1 : index)}
                    />
                  </span>
                ))}
                {stage >= 0 && stage < explanations.length ? (
                  <p className={`${styles.mono} ${styles.stageText}`}>{explanations[stage]}</p>
                ) : null}
              </div>
            </Card>
          </div>
        ) : null}

        {loaded ? (
          <div className={styles.card}>
            <Card
              headline={
                <div className={styles.resultsHeader}>
                  <span>{`Results for ‘${ran}’`}</span>
                  <p className={styles.stats} aria-live="polite">
                    <b>{result.withRules.total}</b>
                    {' tuned · '}
                    <b>{result.withoutRules.total}</b>
                    {' raw · '}
                    <b>{rows.filter((row) => row.hit.change !== 'Unchanged').length}</b>
                    {` changed · ${result.withRules.tookMs} ms / ${result.withoutRules.tookMs} ms`}
                  </p>
                  <div className={styles.viewCluster}>
                    {view === diffView ? (
                      <Checkbox
                        label="Only changes"
                        checked={onlyChanges}
                        onChange={(_event, checked) => setOnlyChanges(checked)}
                      />
                    ) : null}
                    <NameToggleButtons
                      selectedItemId={view}
                      items={[
                        { id: diffView, label: 'Diff' },
                        { id: sideBySideView, label: 'Side by side' },
                      ]}
                      onChange={(id) => setView(String(id))}
                    />
                  </div>
                </div>
              }
            >
              {view === diffView ? (
                shown.length === 0 ? (
                  <p className={styles.muted}>{rows.length === 0 ? 'No results.' : 'Nothing changed for this query.'}</p>
                ) : (
                  <div className={`${styles.results} ${styles.diffTable}`}>
                    <Table columns={diffColumns} rows={diffRows} isHeaderVisible onRowClick={pick} />
                  </div>
                )
              ) : (
                <div className={styles.sides}>
                  <div className={styles.side}>
                    <div className={styles.sideTitle}>With tuning</div>
                    <p className={styles.sideSubtitle}>Rules, synonyms, stopwords, field weights</p>
                    <div className={`${styles.results} ${styles.sideTable}`}>
                      <Table
                        columns={sideColumns('Tuned #')}
                        rows={sideRows(result.withRules.hits, true)}
                        isHeaderVisible
                        onRowClick={pick}
                      />
                    </div>
                  </div>
                  <div className={styles.side}>
                    <div className={styles.sideTitle}>Without tuning</div>
                    <p className={styles.sideSubtitle}>Raw index, no rules applied</p>
                    <div className={`${styles.results} ${styles.sideTable}`}>
                      <Table
                        columns={sideColumns('Raw #')}
                        rows={sideRows(result.withoutRules.hits, false)}
                        isHeaderVisible
                        onRowClick={pick}
                      />
                    </div>
                  </div>
                </div>
              )}
            </Card>
          </div>
        ) : null}
      </Stack>

      <SidePanel
        headline={
          selectedRow === undefined ? (
            ''
          ) : (
            <div>
              <div>{selectedRow.hit.title || selectedRow.hit.id}</div>
              {selectedRow.hit.url === '' ? null : <p className={styles.panelUrl}>{selectedRow.hit.url}</p>}
            </div>
          )
        }
        size={narrow ? SidePanelSize.Full : SidePanelSize.Stackable}
        isVisible={selectedRow !== undefined}
        onClose={() => setSelected('')}
        footer={
          selectedRow === undefined ? undefined : (
            <div className={styles.panelFooter}>
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
            </div>
          )
        }
      >
        {selectedRow === undefined ? null : (
          <div className={styles.panelBody}>
            <ChangeChip change={selectedRow.hit.change} label={summary(selectedRow)} className={styles.panelChip} />

            <div>
              <span className={styles.sectionLabel}>How the score was built</span>
              {selectedRow.hit.steps.length === 0 ? (
                <p className={styles.muted}>No breakdown was recorded for this result.</p>
              ) : (
                selectedRow.hit.steps.map((step, index) => {
                  const total = index === selectedRow.hit.steps.length - 1;

                  return (
                    <div key={`${step.stage}-${index}`} className={total ? `${styles.kvRow} ${styles.kvTotal}` : styles.kvRow}>
                      <span>{step.stage}</span>
                      <span>{round(step.score)}</span>
                    </div>
                  );
                })
              )}
            </div>

            <div>
              <span className={styles.sectionLabel}>Rules that touched this result</span>
              {selectedRow.hit.rules.length === 0 ? (
                <p className={styles.muted}>None. Only the query-level stages apply.</p>
              ) : (
                <div className={styles.ruleRows}>
                  {selectedRow.hit.rules.map((rule) => (
                    <div key={`${rule.id}-${rule.effect}`} className={styles.ruleRow}>
                      <div>
                        <div className={styles.ruleName}>{rule.name}</div>
                        <p className={styles.muted}>{effects[rule.effect] ?? rule.effect}</p>
                      </div>
                      <Button
                        label="Open rule"
                        color={ButtonColor.Tertiary}
                        size={ButtonSize.S}
                        onClick={() => {
                          void openRule({ ruleId: rule.id, variantB: variant === variantB });
                        }}
                      />
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
      </SidePanel>
    </>
  );
};
